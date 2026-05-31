using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Riftstorm.Game.UI;
using Tolik.Riftstorm.Runtime.Gameplay.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Input
{
    /// <summary>
    /// Owner-Client-seitige Ziel-Eingabe. Source-treue Trennung zwischen
    /// "Hover" (Maus-Vorschau, rein lokal, ohne Visual) und "Locked Target"
    /// (server-autoritativ in <see cref="TargetSelection"/>):
    ///
    /// <list type="bullet">
    /// <item><b>Hover</b> — pro Frame Maus-Ray gegen Welt. Liefert
    /// <see cref="CurrentHoveredTargetId"/> und schaltet auf der ueberfahrenen
    /// Einheit ein <see cref="HoverHighlight"/> ein (roter Sprite-Tint, lokal).
    /// Aendert das server-autoritative Lock NICHT.</item>
    /// <item><b>Lock</b> — vom Server in <see cref="TargetSelection.CurrentTargetId"/>
    /// gehalten. Tab-Cycle und Klick gehen ueber ServerRpc. Der lokale Owner
    /// hoert auf <see cref="TargetSelection.CurrentTargetIdChanged"/> und schaltet
    /// den <see cref="HitboxIndicator"/> des jeweils gelockten Ziels ein.</item>
    /// </list>
    ///
    /// <para>
    /// Diese Trennung entspricht dem SoF/SpellCaster-Quellcode: der Caster haelt
    /// einen <c>Entity* target</c>; der Hover-Visual-Effekt entspricht dem
    /// <c>brightenPct</c>-Boost in <c>ClientGameObj.cpp</c> bei <c>isMousedOver()</c>.
    /// </para>
    /// <para>Kein Polling-Cooldown, keine Coroutines — alles event- bzw. ray-getrieben.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerTargetingInput : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private NetworkObject m_OwnerNetworkObject;
        [SerializeField] private PlayerInputController m_Input;
        [SerializeField] private TargetSelection m_TargetSelection;
        [Tooltip("Optional. Bei Prefabs leer lassen \u2014 Unity erlaubt keine Scene-Camera-Referenz im Prefab " +
                 "(\"Type mismatch\"). Bleibt das Feld leer, f\u00e4llt der Code zur Laufzeit auf Camera.main zur\u00fcck.")]
        [SerializeField] private Camera m_Camera;

        [Header("Raycast")]
        [SerializeField] private float m_MaxRayDistance = 200f;
        [SerializeField] private LayerMask m_TargetMask = ~0;

        [Header("Tab-Target")]
        [Tooltip("Maximale 2D-Reichweite (XZ) f\u00fcr Tab-Cycling. Au\u00dferhalb liegende Units werden ignoriert.")]
        [SerializeField] private float m_TabTargetRange = 30f;

        private readonly List<UnitStats> m_CycleBuffer = new(32);

        // -------------------------------------------------------------------------
        // Lock-Visual State
        // -------------------------------------------------------------------------

        private ulong m_VisualLockedId = TargetSelection.NoTarget;
        private HitboxIndicator m_VisualLockedIndicator;

        // Hover-Visual: rot eingefaerbter Sprite-Tint auf der gerade ueberfahrenen Einheit.
        // Wird beim Wechsel CurrentHoveredTargetId -> Neuer Wert pro Frame umgeschaltet.
        // Source-Aequivalent zu ClientGameObj.cpp:82-86 (brightenPct on isMousedOver).
        private HoverHighlight m_HoverVisual;

        /// <summary>
        /// NetworkObject-Id des aktuell ueberfahrenen Ziels (0 = keins). Wird
        /// pro Frame aus dem Maus-Raycast bestimmt und treibt sowohl das lokale
        /// <see cref="HoverHighlight"/>-Visual als auch die Klick-zum-Selektieren-
        /// Logik in <see cref="OnAttackPressed"/>.
        /// </summary>
        public ulong CurrentHoveredTargetId { get; private set; } = TargetSelection.NoTarget;

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
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }
        }

        private void OnEnable()
        {
            if (m_Input != null)
            {
                m_Input.NextTargetPressed += OnNextTargetPressed;
                m_Input.AttackPressed += OnAttackPressed;
                m_Input.ClearTargetPressed += OnClearTargetPressed;
            }
            if (m_TargetSelection != null)
            {
                m_TargetSelection.CurrentTargetIdChanged += OnLockChanged;
                // Initialer Sync \u2014 falls das Server-Lock schon vor unserem OnEnable existierte.
                if (m_TargetSelection.CurrentTargetId != m_VisualLockedId)
                {
                    OnLockChanged(m_VisualLockedId, m_TargetSelection.CurrentTargetId);
                }
            }
            // Sofortiges Anzeigen des Default-Cursors beim Spielstart. Ohne
            // diesen Aufruf wuerde der Cursor erst beim ersten Hover-Wechsel
            // (ApplyHover) gesetzt \u2014 vorher zeigt Windows weiter den System-Cursor.
            Riftstorm.Game.UI.CursorService.Reload();
        }

        private void OnDisable()
        {
            if (m_Input != null)
            {
                m_Input.NextTargetPressed -= OnNextTargetPressed;
                m_Input.AttackPressed -= OnAttackPressed;
                m_Input.ClearTargetPressed -= OnClearTargetPressed;
            }
            if (m_TargetSelection != null)
            {
                m_TargetSelection.CurrentTargetIdChanged -= OnLockChanged;
            }
            ClearLockVisual();
            CurrentHoveredTargetId = TargetSelection.NoTarget;
            ApplyHover(null);
        }

        private void OnDestroy() => ClearLockVisual();

        // -------------------------------------------------------------------------
        // Maus-Hover (pure Vorschau-Id, KEIN Visual)
        // -------------------------------------------------------------------------

        private void Update()
        {
            if (m_OwnerNetworkObject == null || !m_OwnerNetworkObject.IsOwner)
            {
                ClearHover();
                return;
            }
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
                ClearHover();
                return;
            }

            Vector2 mousePos = mouse.position.ReadValue();
            Ray ray = m_Camera.ScreenPointToRay(mousePos);

            if (!Physics.Raycast(ray, out RaycastHit hit, m_MaxRayDistance, m_TargetMask, QueryTriggerInteraction.Collide))
            {
                ClearHover();
                return;
            }

            NetworkObject hitNo = hit.collider.GetComponentInParent<NetworkObject>();
            if (hitNo == null || hitNo == m_OwnerNetworkObject)
            {
                ClearHover();
                return;
            }

            if (!hitNo.TryGetComponent<UnitStats>(out var hitStats))
            {
                hitStats = hitNo.GetComponentInChildren<UnitStats>();
            }
            if (hitStats == null || hitStats.IsDead)
            {
                ClearHover();
                return;
            }

            CurrentHoveredTargetId = hitNo.NetworkObjectId;
            if (!hitNo.TryGetComponent<HoverHighlight>(out var nextHover))
            {
                nextHover = hitNo.GetComponentInChildren<HoverHighlight>();
            }
            ApplyHover(nextHover);
        }

        /// <summary>Setzt Hover-Id und -Visual in einem Schritt zurueck.</summary>
        private void ClearHover()
        {
            CurrentHoveredTargetId = TargetSelection.NoTarget;
            ApplyHover(null);
        }

        /// <summary>
        /// Schaltet das Hover-Highlight idempotent auf <paramref name="next"/> um.
        /// Wenn sich nichts aendert, passiert nichts. Sonst wird der alte HoverHighlight
        /// abgeschaltet und der neue eingeschaltet — ein roter Tint zieht ueber den Sprite.
        /// </summary>
        private void ApplyHover(HoverHighlight next)
        {
            if (ReferenceEquals(m_HoverVisual, next))
            {
                return;
            }
            if (m_HoverVisual != null)
            {
                m_HoverVisual.SetHovered(false);
            }
            m_HoverVisual = next;
            if (m_HoverVisual != null)
            {
                m_HoverVisual.SetHovered(true);
            }
            // Hardware-Cursor parallel zum Hover-Tint umschalten: Attack-Cursor,
            // sobald eine gehoverte Einheit existiert, sonst Default. Nur fuer
            // den Owner relevant — diese Methode wird ausschliesslich aus dem
            // Owner-Update-Pfad heraus aufgerufen.
            CursorService.SetAttack(m_HoverVisual != null);
        }

        // -------------------------------------------------------------------------
        // Tab-Target Cycling (Owner-Client, event-driven)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Wechselt zum naechsten lebenden <see cref="UnitStats"/>-Ziel in 2D-Reichweite
        /// (XZ). Sendet das Ergebnis ueber <see cref="TargetSelection.RequestSelectTargetServerRpc"/>
        /// an den Server — der Lock-Indicator wird ueber das anschliessende
        /// <see cref="TargetSelection.CurrentTargetIdChanged"/>-Event gesetzt.
        /// </summary>
        private void OnNextTargetPressed()
        {
            if (m_OwnerNetworkObject == null || !m_OwnerNetworkObject.IsOwner)
            {
                return;
            }
            if (m_TargetSelection == null)
            {
                return;
            }

            m_CycleBuffer.Clear();
            Vector3 origin = m_OwnerNetworkObject.transform.position;
            float maxSqr = m_TabTargetRange * m_TabTargetRange;

            UnitStats[] all = Object.FindObjectsByType<UnitStats>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                UnitStats candidate = all[i];
                if (candidate == null || candidate.IsDead)
                {
                    continue;
                }
                if (!candidate.TryGetComponent<NetworkObject>(out var candidateNo))
                {
                    candidateNo = candidate.GetComponentInParent<NetworkObject>();
                }
                if (candidateNo == null || candidateNo == m_OwnerNetworkObject)
                {
                    continue;
                }
                Vector3 d = candidate.transform.position - origin;
                d.y = 0f;
                if (d.sqrMagnitude > maxSqr)
                {
                    continue;
                }
                m_CycleBuffer.Add(candidate);
            }

            if (m_CycleBuffer.Count == 0)
            {
                m_TargetSelection.RequestSelectTargetServerRpc(TargetSelection.NoTarget);
                return;
            }

            // Aufsteigend nach 2D-Distanz sortieren.
            m_CycleBuffer.Sort((a, b) =>
            {
                Vector3 da = a.transform.position - origin; da.y = 0f;
                Vector3 db = b.transform.position - origin; db.y = 0f;
                return da.sqrMagnitude.CompareTo(db.sqrMagnitude);
            });

            // Aktuellen Index ueber das LOCK (server target) suchen \u2014 nicht ueber Hover.
            ulong currentLockId = m_TargetSelection.CurrentTargetId;
            int currentIndex = -1;
            if (currentLockId != TargetSelection.NoTarget)
            {
                for (int i = 0; i < m_CycleBuffer.Count; i++)
                {
                    if (!m_CycleBuffer[i].TryGetComponent<NetworkObject>(out var no))
                    {
                        no = m_CycleBuffer[i].GetComponentInParent<NetworkObject>();
                    }
                    if (no != null && no.NetworkObjectId == currentLockId)
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }

            int nextIndex = (currentIndex + 1) % m_CycleBuffer.Count;
            UnitStats next = m_CycleBuffer[nextIndex];
            if (!next.TryGetComponent<NetworkObject>(out var nextNo))
            {
                nextNo = next.GetComponentInParent<NetworkObject>();
            }
            if (nextNo == null)
            {
                return;
            }

            m_TargetSelection.RequestSelectTargetServerRpc(nextNo.NetworkObjectId);
        }

        // -------------------------------------------------------------------------
        // Klick-to-Select / Klick-ins-Leere = Clear
        // -------------------------------------------------------------------------

        /// <summary>
        /// Reagiert auf den linken Maus-Klick (Attack-Action). Liegt unter dem
        /// Cursor ein gültiges anderes Ziel → wird es per ServerRpc gelockt.
        /// Klick ins Leere (kein Hover-Target) released das aktuelle Lock —
        /// LoL-Verhalten: LMB-Klick auf Boden/leeren Bereich = Deselect.
        /// ESC bleibt der dedizierte Shortcut (siehe
        /// <see cref="OnClearTargetPressed"/>). Der eigentliche Angriff wird
        /// parallel von <see cref="PlayerCombat"/> ausgelöst (gleiche
        /// AttackPressed-Quelle); beide Hörer arbeiten unabhängig.
        /// </summary>
        private void OnAttackPressed()
        {
            if (m_OwnerNetworkObject == null || !m_OwnerNetworkObject.IsOwner)
            {
                return;
            }
            if (m_TargetSelection == null)
            {
                return;
            }

            // Klick ins Leere: Lock freigeben (LoL-Style Deselect).
            if (CurrentHoveredTargetId == TargetSelection.NoTarget)
            {
                if (m_TargetSelection.CurrentTargetId != TargetSelection.NoTarget)
                {
                    m_TargetSelection.RequestSelectTargetServerRpc(TargetSelection.NoTarget);
                }
                return;
            }

            // Nur auf echtes neues Ziel wechseln.
            if (CurrentHoveredTargetId != m_TargetSelection.CurrentTargetId)
            {
                m_TargetSelection.RequestSelectTargetServerRpc(CurrentHoveredTargetId);
            }
        }

        /// <summary>
        /// ESC-Handler: gibt das aktuelle Lock-Target wieder frei.
        /// </summary>
        private void OnClearTargetPressed()
        {
            if (m_OwnerNetworkObject == null || !m_OwnerNetworkObject.IsOwner)
            {
                return;
            }
            if (m_TargetSelection == null)
            {
                return;
            }
            if (m_TargetSelection.CurrentTargetId == TargetSelection.NoTarget)
            {
                return;
            }
            m_TargetSelection.RequestSelectTargetServerRpc(TargetSelection.NoTarget);
        }

        // -------------------------------------------------------------------------
        // Lock-Visual (per TargetSelection.CurrentTargetIdChanged)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Schaltet den HitboxIndicator des alten Lock-Ziels ab und den des
        /// neuen ein. Nur der lokale Owner zeigt den Indicator (jeder Spieler
        /// sieht nur sein eigenes Lock).
        /// </summary>
        private void OnLockChanged(ulong previous, ulong current)
        {
            if (m_OwnerNetworkObject == null || !m_OwnerNetworkObject.IsOwner)
            {
                return;
            }

            ClearLockVisual();

            if (current == TargetSelection.NoTarget)
            {
                return;
            }
            if (NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(current, out NetworkObject no) ||
                no == null)
            {
                return;
            }

            HitboxIndicator indicator = no.GetComponentInChildren<HitboxIndicator>();
            if (indicator == null)
            {
                return;
            }

            indicator.Show();
            m_VisualLockedId = current;
            m_VisualLockedIndicator = indicator;
        }

        private void ClearLockVisual()
        {
            if (m_VisualLockedIndicator != null)
            {
                m_VisualLockedIndicator.Hide();
                m_VisualLockedIndicator = null;
            }
            m_VisualLockedId = TargetSelection.NoTarget;
        }
    }
}
