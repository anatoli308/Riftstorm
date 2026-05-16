using System.Threading;
using Riftstorm.Game.Combat.CombatStates;
using Riftstorm.Game.Input;
using Riftstorm.Game.Movement;
using Riftstorm.Gameplay.Combat;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Tolik.Riftstorm.Runtime.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Autoritative Combat-Statemachine pro Spieler.
    ///
    /// <para>
    /// Owner-Client: hört auf <see cref="PlayerInputController.AttackPressed"/> und
    /// schickt eine <see cref="RequestAttackServerRpc"/> an den Server. Es wird
    /// keinerlei lokale Animationslogik gestartet — alles läuft über den Server.
    /// </para>
    /// <para>
    /// Server: validiert (Waffe vorhanden, State akzeptiert Anfrage), startet den
    /// <see cref="PlayerCombatAttackingState"/> mit dem Cooldown der Waffe und
    /// fächert die Animation per <see cref="PlayAttackClientRpc"/> an alle Clients
    /// aus. Schaden/Hit-Resolution folgt in einer späteren Phase.
    /// </para>
    /// <para>
    /// Visuals laufen lokal pro Client über <see cref="PlayerCombatVisuals"/>. Die
    /// Statemachine selbst ist input-getrieben, nicht polling-getrieben (vgl.
    /// Projektregeln: No Polling, No Coroutines).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCombatVisuals))]
    public sealed class PlayerCombat : NetworkStateMachine<PlayerCombatState, PlayerCombat>
    {
        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [SerializeField] private PlayerCombatVisuals m_Visuals;
        [SerializeField] private PlayerInputController m_Input;
        [SerializeField] private PlayerTargetingInput m_Targeting;
        [SerializeField] private TargetSelection m_TargetSelection;
        [SerializeField] private UnitStats m_Stats;
        [SerializeField] private PlayerMovement m_Movement;

        [Tooltip("Default-Waffe, mit der der Server jeden neu gespawnten Spieler ausrüstet, " +
                 "solange das Loadout-System noch nicht greift.")]
        [SerializeField] private string m_DefaultWeaponId = "longsword";

        [Header("Respawn")]
        [Tooltip("Sekunden zwischen Tod und automatischem Respawn am initialen Spawn-Punkt.")]
        [SerializeField] private float m_RespawnDelaySeconds = 5f;

        // -------------------------------------------------------------------------
        // Netzwerk-State
        // -------------------------------------------------------------------------

        /// <summary>Server-autoritative Waffe (FixedString, damit es in NetworkVariable passt).</summary>
        private readonly NetworkVariable<FixedString64Bytes> m_CurrentWeaponId = new(
            default,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>Server-autoritative Offhand. Leer = kein Offhand.</summary>
        private readonly NetworkVariable<FixedString64Bytes> m_CurrentOffhandId = new(
            default,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        // -------------------------------------------------------------------------
        // States (öffentlich lesbar, damit States gegenseitig referenzieren können)
        // -------------------------------------------------------------------------

        public PlayerCombatIdleState IdleState { get; private set; }
        public PlayerCombatAttackingState AttackingState { get; private set; }
        public PlayerCombatDeadState DeadState { get; private set; }

        // -------------------------------------------------------------------------
        // Caches
        // -------------------------------------------------------------------------

        private WeaponCatalogLoader m_WeaponCatalogLoader;

        /// <summary>Server-only: initiale Welt-Position bei Spawn, dorthin wird respawnt.</summary>
        private Vector3 m_ServerSpawnPosition;

        /// <summary>Server-only: bricht den laufenden Respawn-Timer ab, wenn das Objekt despawnt.</summary>
        private CancellationTokenSource m_RespawnCts;

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        private void Awake()
        {
            if (m_Visuals == null)
            {
                m_Visuals = GetComponent<PlayerCombatVisuals>();
            }
            if (m_Input == null)
            {
                m_Input = GetComponentInParent<PlayerInputController>();
            }
            if (m_Stats == null)
            {
                m_Stats = GetComponent<UnitStats>();
            }
            if (m_TargetSelection == null)
            {
                m_TargetSelection = GetComponent<TargetSelection>();
            }
            if (m_Movement == null)
            {
                m_Movement = GetComponent<PlayerMovement>();
            }
            if (m_Targeting == null)
            {
                m_Targeting = GetComponentInChildren<PlayerTargetingInput>(includeInactive: true);
                if (m_Targeting == null)
                {
                    m_Targeting = GetComponentInParent<PlayerTargetingInput>();
                }
            }

            IdleState = new PlayerCombatIdleState();
            AttackingState = new PlayerCombatAttackingState();
            DeadState = new PlayerCombatDeadState();

            InitializeStates(new PlayerCombatState[] { IdleState, AttackingState, DeadState }, IdleState);
        }

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Owner-Client koppelt seinen Input an die Attack-ServerRpc.
            if (IsOwner && m_Input != null)
            {
                m_Input.AttackPressed += OnLocalAttackPressed;
            }

            // Server bestimmt die initiale Waffe (Loadout kommt später).
            if (IsServer && m_CurrentWeaponId.Value.Length == 0 && !string.IsNullOrEmpty(m_DefaultWeaponId))
            {
                m_CurrentWeaponId.Value = new FixedString64Bytes(m_DefaultWeaponId);
            }

            // Server abonniert das Todes-Event, um State-Wechsel + Client-Fanout der
            // Death-Animation auszulösen (Source-treu: kein Polling, eventbasiert).
            if (IsServer && m_Stats != null)
            {
                m_Stats.OnServerDied += OnServerDied;
            }

            // Server merkt sich den initialen Spawn-Punkt für späteren Respawn-Teleport.
            if (IsServer)
            {
                m_ServerSpawnPosition = transform.position;
            }
        }

        /// <inheritdoc/>
        public override void OnNetworkDespawn()
        {
            if (m_Input != null)
            {
                m_Input.AttackPressed -= OnLocalAttackPressed;
            }
            if (IsServer && m_Stats != null)
            {
                m_Stats.OnServerDied -= OnServerDied;
            }
            if (m_RespawnCts != null)
            {
                m_RespawnCts.Cancel();
                m_RespawnCts.Dispose();
                m_RespawnCts = null;
            }
            base.OnNetworkDespawn();
        }

        // -------------------------------------------------------------------------
        // Bewegungs-Gate (von <see cref="Movement.PlayerMovement"/> server-seitig konsultiert)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Server-Sicht: <c>true</c>, sobald der Combat-State Bewegung verbietet
        /// (während eines Angriffs-Cooldowns oder nach dem Tod). Wird von
        /// <see cref="Movement.PlayerMovement.SubmitCommandServerRpc"/> autoritativ
        /// genutzt, um Move-Inputs zu verwerfen — verhindert Move-while-Attacking-Cheats.
        /// </summary>
        public bool IsServerMovementLocked =>
            IsServer && (m_CurrentState == AttackingState || m_CurrentState == DeadState);

        // -------------------------------------------------------------------------
        // Server-Death-Pipeline
        // -------------------------------------------------------------------------

        private void OnServerDied()
        {
            if (!IsServer)
            {
                return;
            }

            // 1) Visual-Fanout auf alle Peers (inkl. Host), bevor die State-Transition
            //    irgendwelche Side-Effects auslöst.
            PlayDeathClientRpc();

            // 2) State-Hook: jeder Combat-State entscheidet selbst, ob er nach DeadState
            //    wechselt (Idle/Attacking → DeadState; DeadState ignoriert es).
            m_CurrentState?.OnDeath();

            // 3) Respawn nach Verzögerung planen (Awaitable, kein Polling).
            //    Vorherigen Timer abbrechen, falls einer aktiv ist.
            if (m_RespawnCts != null)
            {
                m_RespawnCts.Cancel();
                m_RespawnCts.Dispose();
            }
            m_RespawnCts = new CancellationTokenSource();
            _ = ScheduleRespawnAsync(m_RespawnCts.Token);
        }

        [ClientRpc]
        private void PlayDeathClientRpc(ClientRpcParams _ = default)
        {
            if (m_Visuals != null)
            {
                m_Visuals.PlayDie();
            }
        }

        /// <summary>
        /// Server-only: wartet <see cref="m_RespawnDelaySeconds"/> Sekunden und führt dann
        /// einen vollständigen Respawn aus (HP/Mana reset, Teleport zum Spawn, State zurück
        /// auf Idle, Visual-Fanout). Bricht sauber ab, wenn das Objekt despawnt.
        /// </summary>
        private async Awaitable ScheduleRespawnAsync(CancellationToken token)
        {
            try
            {
                float delay = Mathf.Max(0f, m_RespawnDelaySeconds);
                if (delay > 0f)
                {
                    await Awaitable.WaitForSecondsAsync(delay, token);
                }
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            if (!IsServer || !IsSpawned)
            {
                return;
            }

            ServerRespawn();
        }

        /// <summary>
        /// Server-only: führt den eigentlichen Respawn aus. Reihenfolge ist wichtig —
        /// erst Stats zurücksetzen, dann teleportieren, dann State-Hook, dann Visual-Fanout,
        /// damit Clients in einem konsistenten Zustand das Reset sehen.
        /// </summary>
        private void ServerRespawn()
        {
            if (!IsServer)
            {
                return;
            }

            // 1) Stats zurücksetzen (replizierte NetworkVariables → alle Clients sehen Full-HP).
            if (m_Stats != null)
            {
                m_Stats.ServerResetHp();
                m_Stats.ServerResetMana();
            }

            // 2) Harte Reposition zum initialen Spawn-Punkt (mit Owner-Prediction-Reset).
            if (m_Movement != null)
            {
                m_Movement.ServerTeleportTo(m_ServerSpawnPosition);
            }
            else
            {
                transform.position = m_ServerSpawnPosition;
            }

            // 3) State-Hook: DeadState → IdleState (andere States ignorieren OnRespawn).
            m_CurrentState?.OnRespawn();

            // 4) Visual-Fanout: alle Peers (inkl. Host) verlassen den Dead-Visual-Latch.
            PlayRespawnClientRpc();
        }

        [ClientRpc]
        private void PlayRespawnClientRpc(ClientRpcParams _ = default)
        {
            if (m_Visuals != null)
            {
                m_Visuals.ResetForRespawn();
            }
        }

        // -------------------------------------------------------------------------
        // Bindings (vom Bootstrap aufgerufen)
        // -------------------------------------------------------------------------

        /// <summary>Vom Bootstrap aufgerufen, sobald die Visuals-Komponente konstruiert ist.</summary>
        public void BindVisuals(PlayerCombatVisuals visuals)
        {
            m_Visuals = visuals;
        }

        /// <summary>Vom Bootstrap aufgerufen, sobald der Input-Controller verfügbar ist.</summary>
        public void BindInput(PlayerInputController input)
        {
            // Falls OnNetworkSpawn bereits gelaufen ist und wir Owner sind, jetzt nachhängen.
            if (m_Input == input)
            {
                return;
            }

            if (IsSpawned && IsOwner && m_Input != null)
            {
                m_Input.AttackPressed -= OnLocalAttackPressed;
            }

            m_Input = input;

            if (IsSpawned && IsOwner && m_Input != null)
            {
                m_Input.AttackPressed += OnLocalAttackPressed;
            }
        }

        // -------------------------------------------------------------------------
        // Owner-Input → Server
        // -------------------------------------------------------------------------

        private void OnLocalAttackPressed()
        {
            if (!IsSpawned)
            {
                return;
            }
            // Owner liest den aktuell &#252;berfahrenen Mauszielkandidaten und reicht ihn mit der
            // Attack-Anfrage durch — der Server validiert anschlie&#223;end Existenz, Lebendigkeit
            // und Reichweite (analog zum SoF-Server, der target-id + range pr&#252;ft).
            ulong hovered = m_Targeting != null ? m_Targeting.CurrentHoveredTargetId : TargetSelection.NoTarget;
            RequestAttackServerRpc(hovered);
        }

        [ServerRpc]
        private void RequestAttackServerRpc(ulong requestedTargetId, ServerRpcParams _ = default)
        {
            WeaponDefinition weapon = ResolveCurrentWeapon();
            if (weapon == null)
            {
                return;
            }

            // Ziel-Update vom Client &#252;bernehmen (0 = nichts &#252;berfahren &#8594; bestehendes Ziel behalten,
            // damit ein verfehlter Klick nicht das gelockte Ziel l&#246;scht). Validierung erfolgt
            // anschlie&#223;end im Hit-Resolve.
            if (m_TargetSelection != null && requestedTargetId != TargetSelection.NoTarget)
            {
                m_TargetSelection.ServerSetTarget(requestedTargetId);
            }

            m_CurrentState.OnAttackRequested(weapon);
        }

        // -------------------------------------------------------------------------
        // Server → States
        // -------------------------------------------------------------------------

        /// <summary>
        /// Server-Pfad zum Einleiten einer Attacke. Wird vom Idle-State aufgerufen,
        /// sobald eine valide Attack-Anfrage eingegangen ist.
        /// </summary>
        internal void BeginAttack(WeaponDefinition weapon)
        {
            if (!IsServer)
            {
                return;
            }

            // Server dreht den Angreifer Richtung Ziel (XZ-Ebene), damit Visuals & sp&#228;tere
            // FX-Spawner einen sinnvollen Forward-Vektor haben.
            FaceCurrentTarget();

            // 1) An alle Clients (inkl. Host) zum Visual-Trigger fan-out.
            PlayAttackClientRpc(weapon.AttackAnim);

            // 2) Cooldown-State konfigurieren und Transition.
            AttackingState.ConfigureFromWeapon(weapon);
            ChangeState(AttackingState);
        }

        private void FaceCurrentTarget()
        {
            if (m_TargetSelection == null || !m_TargetSelection.TryGetCurrentTarget(out NetworkObject no, out _))
            {
                return;
            }
            Vector3 to = no.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f)
            {
                return;
            }
            transform.rotation = Quaternion.LookRotation(to, Vector3.up);
        }

        // -------------------------------------------------------------------------
        // Server → Clients (Visual-Fanout)
        // -------------------------------------------------------------------------

        [ClientRpc]
        private void PlayAttackClientRpc(CombatAnim anim, ClientRpcParams _ = default)
        {
            if (m_Visuals == null)
            {
                return;
            }
            switch (anim)
            {
                case CombatAnim.Swing:
                    m_Visuals.PlaySwing();
                    break;
                case CombatAnim.Shoot:
                    m_Visuals.PlayShoot();
                    break;
                case CombatAnim.Cast:
                    m_Visuals.PlayCast();
                    break;
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// True, wenn <paramref name="state"/> aktuell der aktive State ist.
        /// Wird vom Attacking-State benutzt, um Stale-Transitions zu vermeiden.
        /// </summary>
        internal bool IsCurrentState(PlayerCombatState state)
        {
            return m_CurrentState == state;
        }

        /// <summary>
        /// Server-only Damage-Resolve. Wird vom Attacking-State zum
        /// HitResolveProgress-Frame aufgerufen. Source-treuer Single-Target-
        /// Flow: das aktuell gelockte Ziel aus <see cref="TargetSelection"/>
        /// auflösen, gegen <c>weapon.Range</c> auf 2D-Distanz pr&#252;fen, dann
        /// Schaden &#252;ber <see cref="CombatFormulas"/> berechnen und via
        /// <see cref="IDamageable.ApplyDamage"/> anwenden.
        /// </summary>
        internal void ServerResolveMeleeHit(WeaponDefinition weapon)
        {
            if (!IsServer || weapon == null)
            {
                return;
            }
            if (m_Stats == null || m_Stats.IsDead)
            {
                return;
            }
            if (m_TargetSelection == null)
            {
                return;
            }
            if (!m_TargetSelection.TryGetCurrentTarget(out NetworkObject targetObject, out UnitStats victimStats))
            {
                return;
            }
            if (victimStats == null || victimStats.IsDead)
            {
                return;
            }

            // 2D-Distanzpr&#252;fung (XZ) — identisch zum SoF-Quellcode (sqrt(dx²+dy²)).
            Vector3 d = targetObject.transform.position - transform.position;
            d.y = 0f;
            float distSqr = d.sqrMagnitude;
            if (distSqr > weapon.Range * weapon.Range)
            {
                return;
            }

            // Front-Arc-Gate — nur sinnvoll, wenn Arc echt einschränkt (<360°)
            // und das Ziel nicht direkt im eigenen Pivot steht (sonst NaN).
            if (weapon.FrontArcDeg < 360f && distSqr > 0.0001f)
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    fwd.Normalize();
                    Vector3 toTarget = d / Mathf.Sqrt(distSqr);
                    float cosHalf = Mathf.Cos(weapon.FrontArcDeg * 0.5f * Mathf.Deg2Rad);
                    if (Vector3.Dot(fwd, toTarget) < cosHalf)
                    {
                        return;
                    }
                }
            }

            // Self-Hit / Allies sp&#228;ter via Hostility-Flag (PvP/Faction) — Phase 4 reicht Single-Target reicht.
            if (victimStats == m_Stats)
            {
                return;
            }

            DamageInfo info = CombatFormulas.CalculateMeleeDamage(m_Stats, victimStats, weapon);
            ((IDamageable)victimStats).ApplyDamage(info);
        }

        /// <summary>
        /// Server-only: prüft, ob das aktuell gelockte Ziel noch ein
        /// reguläres Angriffsziel ist (existiert, lebt, in Range). Wird
        /// vom <see cref="CombatStates.PlayerCombatAttackingState"/> vor
        /// dem Hit-Resolve genutzt, um den Cooldown zu canceln, sobald das
        /// Ziel weg ist (Tod, ServerClearTarget, Despawn, Wegrennen).
        /// </summary>
        internal bool ServerIsTargetStillValid(WeaponDefinition weapon)
        {
            if (!IsServer || weapon == null || m_TargetSelection == null)
            {
                return false;
            }
            if (!m_TargetSelection.TryGetCurrentTarget(out NetworkObject targetObject, out UnitStats victimStats))
            {
                return false;
            }
            if (victimStats == null || victimStats.IsDead || victimStats == m_Stats)
            {
                return false;
            }
            Vector3 d = targetObject.transform.position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= weapon.Range * weapon.Range;
        }

        private WeaponDefinition ResolveCurrentWeapon()
        {
            if (m_CurrentWeaponId.Value.Length == 0)
            {
                return null;
            }
            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null)
            {
                Debug.LogWarning("[PlayerCombat] WeaponCatalog not loaded — attack ignored.");
                return null;
            }
            return catalog.Get(m_CurrentWeaponId.Value.ToString());
        }
    }
}
