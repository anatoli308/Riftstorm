using Riftstorm.Game.Combat.CombatStates;
using Riftstorm.Game.Input;
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

        [Tooltip("Default-Waffe, mit der der Server jeden neu gespawnten Spieler ausrüstet, " +
                 "solange das Loadout-System noch nicht greift.")]
        [SerializeField] private string m_DefaultWeaponId = "longsword";

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
        }

        /// <inheritdoc/>
        public override void OnNetworkDespawn()
        {
            if (m_Input != null)
            {
                m_Input.AttackPressed -= OnLocalAttackPressed;
            }
            base.OnNetworkDespawn();
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
            RequestAttackServerRpc();
        }

        [ServerRpc]
        private void RequestAttackServerRpc(ServerRpcParams _ = default)
        {
            WeaponDefinition weapon = ResolveCurrentWeapon();
            if (weapon == null)
            {
                return;
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

            // 1) An alle Clients (inkl. Host) zum Visual-Trigger fan-out.
            PlayAttackClientRpc(weapon.AttackAnim);

            // 2) Cooldown-State konfigurieren und Transition.
            AttackingState.ConfigureFromWeapon(weapon);
            ChangeState(AttackingState);
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
