using Riftstorm.Game.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Input
{
    /// <summary>
    /// LoL-Style MOBA-Steuerung. Uebersetzt einen einzelnen RMB-Klick
    /// (<see cref="PlayerInputController.MoveCommandPressed"/>) in einen
    /// persistenten Bewegungs-Intent (Move-to-Point oder Follow-Target) und
    /// liefert pro Frame einen normalisierten 2D-Move-Vektor — dieselbe
    /// Vertragsfläche wie der frühere WASD-Input.
    ///
    /// <para>
    /// <b>Architektur</b>: Owner-Client only. Klick → Maus-Raycast →
    /// trifft NetworkObject mit <see cref="UnitStats"/> (≠ self, lebendig) ⇒
    /// <see cref="CommandIntent.FollowTarget"/> + Lock-Request an Server.
    /// Trifft nur Boden ⇒ <see cref="CommandIntent.MoveToPoint"/>.
    /// Pro Frame wird ein XZ-Delta zur Destination berechnet und in einen
    /// 2D-Vektor (x = world-X, y = world-Z) ueberfuehrt. <see cref="PlayerMovement"/>
    /// konsumiert das wie zuvor WASD und behaelt seine Prediction-/Reconciliation-
    /// Pipeline unveraendert.
    /// </para>
    /// <para>
    /// <b>Stop-Logik</b>: MoveToPoint → Stop bei <c>m_ArrivalRadius</c>.
    /// FollowTarget → Stop, sobald innerhalb von <c>m_FollowStopRadius</c> (LoL:
    /// "in Reichweite, bleibe stehen und schlage zu"). Auto-Attack-Trigger lebt
    /// in <see cref="PlayerCombat"/> und wird separat verdrahtet — dieser
    /// Controller liefert nur die Bewegung.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobaCommandController : MonoBehaviour
    {
        private const float k_HeldMoveRepeatSeconds = 0.05f;
        private const float k_HeldMoveMinCursorDeltaPixels = 6f;

        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [Header("Refs")]
        [SerializeField] private NetworkObject m_OwnerNetworkObject;
        [SerializeField] private PlayerInputController m_Input;
        [SerializeField] private TargetSelection m_TargetSelection;
        [SerializeField] private UnitStats m_SelfStats;
        [SerializeField] private PlayerCombat m_Combat;
        [Tooltip("Optional. Bei Prefabs leer lassen — Unity erlaubt keine Scene-Camera-Referenz im Prefab. " +
                 "Bleibt das Feld leer, faellt der Code zur Laufzeit auf Camera.main zurueck.")]
        [SerializeField] private Camera m_Camera;

        [Header("Raycast")]
        [SerializeField] private float m_MaxRayDistance = 200f;
        [Tooltip("Layer, die der RMB-Raycast trifft. Boden + Unit-Layer einschliessen.")]
        [SerializeField] private LayerMask m_RaycastMask = ~0;

        [Header("Bewegung")]
        [Tooltip("Wie nah der Spieler an einen Move-to-Point heran muss, bevor er stoppt (Meter).")]
        [SerializeField] private float m_ArrivalRadius = 0.15f;
        [Tooltip("Stop-Radius beim Follow-Target ALS FALLBACK. Wird nur verwendet, wenn keine Waffe " +
                 "aktiv ist oder PlayerCombat fehlt. Im Normalfall stoppt der Spieler bei " +
                 "weapon.Range + target.HitRadius (LoL-Style Auto-Attack-Reichweite).")]
        [SerializeField] private float m_FollowStopRadius = 1.5f;

        // -------------------------------------------------------------------------
        // Intent
        // -------------------------------------------------------------------------

        /// <summary>Aktuelle Klick-Absicht. Treibt den per-Frame Move-Vektor.</summary>
        private enum CommandIntent
        {
            /// <summary>Kein aktives Move-Command — Spieler steht.</summary>
            Idle,
            /// <summary>Bewege zu <c>m_DestinationXZ</c>, stoppe bei <c>m_ArrivalRadius</c>.</summary>
            MoveToPoint,
            /// <summary>Folge <c>m_FollowTargetId</c>, stoppe bei <c>m_FollowStopRadius</c>.</summary>
            FollowTarget,
        }

        private CommandIntent m_Intent = CommandIntent.Idle;
        private Vector3 m_DestinationXZ;
        private ulong m_FollowTargetId = TargetSelection.NoTarget;
        private bool m_SuppressHeldMoveUntilRelease;
        private bool m_WasMoveHeldLastFrame;
        private float m_NextHeldMoveTime;
        private Vector2 m_LastHeldMoveScreenPos;

        /// <summary>Vorallokierter Puffer fuer <see cref="Physics.RaycastNonAlloc"/>, damit der Klick-Handler keine Allokationen verursacht.</summary>
        private readonly RaycastHit[] m_HitBuffer = new RaycastHit[16];

        // -------------------------------------------------------------------------
        // Public Surface (identisch zur frueheren WASD-Schnittstelle)
        // -------------------------------------------------------------------------

        /// <summary>Aktueller normalisierter Bewegungsvektor (x = Welt-X, y = Welt-Z).</summary>
        public Vector2 MoveDirection { get; private set; }

        /// <summary>True, sobald ein Bewegungs-Intent aktiv Distanz liefert.</summary>
        public bool IsMoving { get; private set; }

        /// <summary>Aktuelles Klick-Ziel (0 = keins). Owner-lokal, nicht repliziert.</summary>
        public ulong FollowTargetId => m_FollowTargetId;

        /// <summary>
        /// Bricht den aktuellen Bewegungs-Intent sofort ab und unterdrueckt
        /// weitere RMB-Hold-Updates, bis die Taste losgelassen wurde.
        /// </summary>
        public void InterruptMovementForCast()
        {
            ResetIntent();
            MoveDirection = Vector2.zero;
            IsMoving = false;
            m_SuppressHeldMoveUntilRelease = true;
        }

        /// <summary>
        /// Hebt die Cast-bedingte Hold-Sperre wieder auf. Wenn RMB weiterhin
        /// gehalten wird, wird der Move-Intent im naechsten Update frisch
        /// aufgebaut; wenn nicht, bleibt der Controller einfach idle.
        /// </summary>
        public void ResumeHeldMovementAfterCast()
        {
            m_SuppressHeldMoveUntilRelease = false;
            m_WasMoveHeldLastFrame = false;
            m_NextHeldMoveTime = 0f;
        }

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        private void Awake()
        {
            if (m_OwnerNetworkObject == null)
            {
                m_OwnerNetworkObject = GetComponentInParent<NetworkObject>();
            }
            if (m_Input == null)
            {
                m_Input = GetComponentInParent<PlayerInputController>();
            }
            if (m_TargetSelection == null)
            {
                m_TargetSelection = GetComponentInParent<TargetSelection>();
            }
            if (m_SelfStats == null)
            {
                m_SelfStats = GetComponentInParent<UnitStats>();
            }
            if (m_Combat == null)
            {
                m_Combat = GetComponentInParent<PlayerCombat>();
            }
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }
        }

        private void OnEnable()
        {
            if (m_Input != null)
            {
                m_Input.MoveCommandPressed += OnMoveCommandPressed;
                m_Input.ClearTargetPressed += OnClearTargetPressed;
                m_Input.WeaponModeTogglePressed += OnWeaponModeTogglePressed;
            }
        }

        private void OnDisable()
        {
            if (m_Input != null)
            {
                m_Input.MoveCommandPressed -= OnMoveCommandPressed;
                m_Input.ClearTargetPressed -= OnClearTargetPressed;
                m_Input.WeaponModeTogglePressed -= OnWeaponModeTogglePressed;
            }
            ResetIntent();
        }

        // -------------------------------------------------------------------------
        // Input-Handler
        // -------------------------------------------------------------------------

        /// <summary>
        /// RMB-Klick: bestimmt aus der aktuellen Maus-Position einen neuen Bewegungs-Intent.
        /// </summary>
        private void OnMoveCommandPressed()
        {
            if (m_OwnerNetworkObject == null || !m_OwnerNetworkObject.IsOwner)
            {
                return;
            }
            // LoL-Style Attack-Cancel wird unten ZIELABHAENGIG gefeuert:
            //  - RMB auf Boden / neues Ziel  -> Cancel (Spieler will weg/wechseln)
            //  - RMB auf bereits gelocktes Ziel -> KEIN Cancel (Auto-Attack laeuft
            //    weiter, Spam resettet den Windup nicht).
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null)
                {
                    return;
                }
            }
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 screen = mouse.position.ReadValue();
            Ray ray = m_Camera.ScreenPointToRay(screen);

            // Alle Hits sammeln und den eigenen Collider ueberspringen — sonst blockt der
            // Spieler-Capsule jeden Klick, der von oben durch ihn hindurch zielt, und der
            // MoveToPoint landet auf der eigenen Position (=> ArrivalRadius greift sofort,
            // Charakter bewegt sich nicht).
            int hitCount = Physics.RaycastNonAlloc(ray, m_HitBuffer, m_MaxRayDistance, m_RaycastMask, QueryTriggerInteraction.Collide);
            RaycastHit best = default;
            bool hasBest = false;
            float bestDist = float.PositiveInfinity;
            UnitStats bestStats = null;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = m_HitBuffer[i];
                UnitStats stats = candidate.collider.GetComponentInParent<UnitStats>();
                if (stats != null && stats == m_SelfStats)
                {
                    continue;
                }
                if (candidate.distance < bestDist)
                {
                    bestDist = candidate.distance;
                    best = candidate;
                    bestStats = stats;
                    hasBest = true;
                }
            }

            // 1) Trifft der Strahl einen Gegner? (UnitStats, lebendig, nicht self)
            if (hasBest && bestStats != null && !bestStats.IsDead)
            {
                NetworkObject targetNet = bestStats.GetComponentInParent<NetworkObject>();
                if (targetNet != null)
                {
                    ulong newFollowId = targetNet.NetworkObjectId;
                    // LoL: nur cancel, wenn das Ziel WIRKLICH wechselt. Spamklicks auf
                    // dasselbe bereits gelockte Ziel duerfen den Windup nicht resetten.
                    bool sameLockedTarget = m_TargetSelection != null
                        && m_TargetSelection.CurrentTargetId == newFollowId;
                    if (!sameLockedTarget && m_Combat != null)
                    {
                        m_Combat.RequestCancelAttack();
                    }
                    m_Intent = CommandIntent.FollowTarget;
                    m_FollowTargetId = newFollowId;
                    // LoL-Verhalten: RMB-auf-Gegner acquired auch den Lock visuell.
                    if (m_TargetSelection != null && m_TargetSelection.CurrentTargetId != m_FollowTargetId)
                    {
                        m_TargetSelection.RequestSelectTargetServerRpc(m_FollowTargetId);
                    }
                    return;
                }
            }

            // 2) Andernfalls: Move-to-Point. Bevorzugt der naechste Nicht-Self-Hit
            //    (z. B. Boden-Mesh); fehlt der, fallen wir auf die horizontale Ebene
            //    in Spielerhoehe zurueck, damit ein Klick direkt durch die eigene
            //    Figur trotzdem ein gueltiges Ziel produziert.
            Vector3 destination;
            if (hasBest)
            {
                destination = best.point;
            }
            else
            {
                Plane ground = new(Vector3.up, new Vector3(0f, transform.position.y, 0f));
                if (!ground.Raycast(ray, out float enter))
                {
                    return;
                }
                destination = ray.GetPoint(enter);
            }

            // Klick ins Leere -> Move-Befehl. JETZT cancelt der RMB die Attacke,
            // damit der Spieler nicht im Auto-Attack festklebt.
            if (m_Combat != null)
            {
                m_Combat.RequestCancelAttack();
            }
            m_Intent = CommandIntent.MoveToPoint;
            m_FollowTargetId = TargetSelection.NoTarget;
            // Y festhalten — die Bewegung laeuft auf der XZ-Ebene des Owners.
            m_DestinationXZ = new Vector3(destination.x, transform.position.y, destination.z);
        }

        /// <summary>
        /// Escape: laufenden Bewegungs-Intent abbrechen (LoL-Style "stop").
        /// </summary>
        private void OnClearTargetPressed()
        {
            ResetIntent();
        }

        /// <summary>
        /// Taste 'T': schaltet serverseitig zwischen Melee- und Ranged-Auto-Attack
        /// um. Wirksam nur, wenn ein Bogen ausgeruestet ist (Server-Gate in
        /// <see cref="PlayerCombat.RequestToggleWeaponMode"/>); sonst No-Op.
        /// </summary>
        private void OnWeaponModeTogglePressed()
        {
            if (m_OwnerNetworkObject == null || !m_OwnerNetworkObject.IsOwner)
            {
                return;
            }
            if (m_Combat != null)
            {
                m_Combat.RequestToggleWeaponMode();
            }
        }

        // -------------------------------------------------------------------------
        // Per-Frame Move-Vektor
        // -------------------------------------------------------------------------

        private void Update()
        {
            if (m_OwnerNetworkObject == null || !m_OwnerNetworkObject.IsOwner)
            {
                MoveDirection = Vector2.zero;
                IsMoving = false;
                return;
            }
            if (m_SelfStats != null && m_SelfStats.IsDead)
            {
                ResetIntent();
                MoveDirection = Vector2.zero;
                IsMoving = false;
                return;
            }

            ProcessHeldMoveCommand();

            switch (m_Intent)
            {
                case CommandIntent.MoveToPoint:
                    UpdateMoveToPoint();
                    return;

                case CommandIntent.FollowTarget:
                    UpdateFollowTarget();
                    return;

                default:
                    MoveDirection = Vector2.zero;
                    IsMoving = false;
                    return;
            }
        }

        private void ProcessHeldMoveCommand()
        {
            bool isHeld = m_Input != null && m_Input.IsMoveCommandHeld;
            if (!isHeld)
            {
                m_WasMoveHeldLastFrame = false;
                m_SuppressHeldMoveUntilRelease = false;
                return;
            }

            if (m_SuppressHeldMoveUntilRelease)
            {
                m_WasMoveHeldLastFrame = true;
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 screenPos = mouse.position.ReadValue();
            bool firstHeldFrame = !m_WasMoveHeldLastFrame;
            bool movedEnough = (screenPos - m_LastHeldMoveScreenPos).sqrMagnitude
                >= k_HeldMoveMinCursorDeltaPixels * k_HeldMoveMinCursorDeltaPixels;
            bool repeatDue = Time.unscaledTime >= m_NextHeldMoveTime;
            if (firstHeldFrame || movedEnough || repeatDue)
            {
                OnMoveCommandPressed();
                m_LastHeldMoveScreenPos = screenPos;
                m_NextHeldMoveTime = Time.unscaledTime + k_HeldMoveRepeatSeconds;
            }

            m_WasMoveHeldLastFrame = true;
        }

        private void UpdateMoveToPoint()
        {
            Vector3 self = transform.position;
            float dx = m_DestinationXZ.x - self.x;
            float dz = m_DestinationXZ.z - self.z;
            float sqr = dx * dx + dz * dz;
            if (sqr <= m_ArrivalRadius * m_ArrivalRadius)
            {
                ResetIntent();
                MoveDirection = Vector2.zero;
                IsMoving = false;
                return;
            }
            float inv = 1f / Mathf.Sqrt(sqr);
            MoveDirection = new Vector2(dx * inv, dz * inv);
            IsMoving = true;
        }

        private void UpdateFollowTarget()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(m_FollowTargetId, out NetworkObject targetNet) || targetNet == null)
            {
                ResetIntent();
                MoveDirection = Vector2.zero;
                IsMoving = false;
                return;
            }
            UnitStats targetStats = targetNet.GetComponent<UnitStats>();
            if (targetStats == null || targetStats.IsDead)
            {
                ResetIntent();
                MoveDirection = Vector2.zero;
                IsMoving = false;
                return;
            }

            Vector3 self = transform.position;
            Vector3 targetPos = targetNet.transform.position;
            float dx = targetPos.x - self.x;
            float dz = targetPos.z - self.z;
            float sqr = dx * dx + dz * dz;

            // LoL-Style Stop-Radius: ENG auf weapon.Range (ohne HitRadius). Der Server
            // erlaubt im ServerResolveMeleeHit zusaetzlich +victim.HitRadius als Slack.
            // Damit ist garantiert: sobald der Client stoppt und feuert, ist der Server
            // (trotz Owner-Prediction + Target-Interpolation-Lag) sicher noch in Reichweite.
            // Fallback auf m_FollowStopRadius, solange Combat/Waffe nicht verfuegbar sind.
            float weaponRange = m_Combat != null ? m_Combat.CurrentWeaponRange : 0f;
            float reach = weaponRange > 0f ? weaponRange : m_FollowStopRadius;
            if (sqr <= reach * reach)
            {
                // In Reichweite: nicht weiter laufen — Intent BLEIBT FollowTarget,
                // damit der Spieler bei Ziel-Bewegung wieder nachzieht.
                MoveDirection = Vector2.zero;
                IsMoving = false;
                // Auto-Attack triggern. PlayerCombat selbst gated gegen RPC-Spam
                // (Prediction-Window) und gegen den Waffen-Cooldown (State-Machine).
                if (m_Combat != null)
                {
                    m_Combat.TryRequestAutoAttack();
                }
                return;
            }
            float inv = 1f / Mathf.Sqrt(sqr);
            MoveDirection = new Vector2(dx * inv, dz * inv);
            IsMoving = true;
        }

        private void ResetIntent()
        {
            m_Intent = CommandIntent.Idle;
            m_FollowTargetId = TargetSelection.NoTarget;
            m_WasMoveHeldLastFrame = false;
            m_NextHeldMoveTime = 0f;
        }
    }
}
