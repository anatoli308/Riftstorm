using System.Threading;
using Riftstorm.Game.Combat.CombatStates;
using Riftstorm.Game.Input;
using Riftstorm.Game.Movement;
using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using Riftstorm.Gameplay.Combat.Spells.Visuals;
using Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime;
using Riftstorm.Management.SoundManagement;
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
    /// Owner-Client: hat KEIN eigenes Attack-Binding mehr. Angriffe werden
    /// ausschliesslich vom <see cref="MobaCommandController"/> via
    /// <see cref="TryRequestAutoAttack"/> ausgeloest (LoL-Style: RMB auf Gegner
    /// laeuft in Waffenreichweite und greift dort automatisch an). LMB ist reine
    /// Selektion und wird von <see cref="PlayerTargetingInput"/> abgewickelt.
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
        public PlayerCombatCastingState CastingState { get; private set; }
        public PlayerCombatDeadState DeadState { get; private set; }

        // -------------------------------------------------------------------------
        // Caches
        // -------------------------------------------------------------------------

        private WeaponCatalogLoader m_WeaponCatalogLoader;
        private SpellVisualKitMappingCatalogLoader m_VisualKitMappingLoader;
        private SpellVisualKitDefinitionCatalogLoader m_VisualKitDefinitionLoader;
        private SpellAnimationCatalogLoader m_AnimationCatalogLoader;
        private ParticleSystemCatalogLoader m_ParticleCatalogLoader;
        private SoundManager m_SoundManager;

        /// <summary>
        /// Client-lokales Handle auf die aktuell laufenden Cast-Particles
        /// (gespawnt in <see cref="TryTriggerCasterParticles"/>). Wird in
        /// <see cref="EndCastClientRpc"/> gestoppt, damit endlose PSystems
        /// (<c>lifetime = -1</c>, z. B. casting_shadow / casting_holy) nicht
        /// bis zum harten 8s-Cap am Boden weiter glitzern.
        /// </summary>
        private GameObject m_ActiveCasterParticles;

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
            CastingState = new PlayerCombatCastingState();
            DeadState = new PlayerCombatDeadState();

            InitializeStates(new PlayerCombatState[] { IdleState, AttackingState, CastingState, DeadState }, IdleState);
        }

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Owner-Client hat KEIN eigenes Attack-Binding (LoL-Style): Angriffe
            // kommen ausschliesslich aus MobaCommandController.TryRequestAutoAttack.

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

            // Jeder Peer (Server + Clients) abonniert das Damage-Fanout-Event, um die
            // Hit-Reaction-Anim lokal zu spielen — UnitStats sendet das via ClientRpc
            // auf allen Maschinen, der Filter auf 'amount > 0' verhindert, dass
            // Miss/Dodge/Parry/Resist/Immune/Absorb eine Treffer-Anim auslösen.
            if (m_Stats != null)
            {
                m_Stats.ClientDamageReceived += OnClientDamageReceived;
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
            if (IsServer && m_Stats != null)
            {
                m_Stats.OnServerDied -= OnServerDied;
            }
            if (m_Stats != null)
            {
                m_Stats.ClientDamageReceived -= OnClientDamageReceived;
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
            IsServer && (m_CurrentState == AttackingState || m_CurrentState == CastingState || m_CurrentState == DeadState);

        /// <summary>
        /// Server-Sicht: <c>true</c>, wenn der Spieler aktuell im
        /// <see cref="CastingState"/> ist. Wird vom <see cref="Movement.PlayerMovement"/>
        /// genutzt, um beim ersten Move-Command des Owners w&#228;hrend eines Casts
        /// den Cast autoritativ zu unterbrechen (Move-cancels-Cast, LoL-Style).
        /// </summary>
        public bool IsServerCasting => IsServer && m_CurrentState == CastingState;

        /// <summary>
        /// Server-Sicht: das aktuell gecastete <see cref="SpellTemplate"/>, oder
        /// <c>null</c>, wenn der Spieler nicht im <see cref="CastingState"/> ist.
        /// Wird vom <see cref="Movement.PlayerMovement"/> konsultiert, um Spells
        /// mit <see cref="SpellAttributes.CanMoveWhileCasting"/> vom Move-cancels-Cast
        /// auszunehmen.
        /// </summary>
        public SpellTemplate CurrentCastSpell =>
            IsServer && m_CurrentState == CastingState ? CastingState.CurrentSpell : null;

        // -------------------------------------------------------------------------
        // CastBar-Events (Owner-only, gefeuert aus den CastBar-ClientRpcs)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Event: feuert auf dem Owner-Client, sobald der Server einen
        /// Cast-Time-Spell startet. Payload: <c>spellEntry</c> + Dauer in Sekunden.
        /// Wird vom HUD (<see cref="UI.CastBarHUD"/>) abonniert.
        /// </summary>
        public event System.Action<int, float> OwnerCastStarted;

        /// <summary>
        /// Owner-Event: feuert auf dem Owner-Client, sobald ein laufender Cast
        /// beendet wird. <c>completed = true</c> bei erfolgreichem Abschluss,
        /// <c>false</c> bei Unterbrechung (Bewegung, Tod, expliziter Cancel).
        /// </summary>
        public event System.Action<bool> OwnerCastEnded;

        /// <summary>
        /// Owner-Event: feuert auf dem Owner-Client, sobald der Server einen
        /// Cast-Wunsch ablehnt (OnCooldown, OutOfRange, NotEnoughMana,
        /// TargetFriendly, CasterCasting, ...). Payload: numerischer
        /// <see cref="CastResult"/>-Code &#8212; die UI mapped ihn ueber
        /// <see cref="CastResultStrings.Get"/> auf einen sichtbaren String
        /// (analog FLARE <c>CombatMessenger</c>). Nicht fuer <see cref="CastResult.Success"/>.
        /// </summary>
        public event System.Action<CastResult> OwnerCastFailed;

        /// <summary>
        /// Owner-Vorhersage: <c>true</c> zwischen dem lokalen Attack-Input und dem
        /// Eintreffen des <see cref="PlayAttackClientRpc"/> vom Server. Verhindert das
        /// charakteristische Reconciliation-Ruckeln, wenn der Owner während des einen
        /// Roundtrips weiterpredictet, der Server aber bereits clampt. Wird vom
        /// <see cref="Movement.PlayerMovement"/> zusätzlich zu
        /// <see cref="PlayerCombatVisuals.IsBusy"/> konsultiert. Hartes Sicherheits-Timeout
        /// für den Fall, dass der Server die Attack verwirft (kein Ziel/Waffe) und gar
        /// kein ClientRpc folgt.
        /// </summary>
        public bool IsOwnerPredictingAttack => Time.unscaledTime < m_OwnerPredictedAttackUntil;

        private float m_OwnerPredictedAttackUntil;

        /// <summary>Maximales Vorhersagefenster (Sekunden), wenn keine Server-Antwort kommt.</summary>
        private const float k_OwnerAttackPredictionWindow = 0.5f;

        // -------------------------------------------------------------------------
        // Server-Death-Pipeline
        // -------------------------------------------------------------------------

        private void OnServerDied()
        {
            if (!IsServer)
            {
                return;
            }

            // 0) Falls der Spieler gerade gecastet hat, dem Owner-HUD den
            //    Cast-Abbruch melden, BEVOR die State-Transition den
            //    CastingState verl&#228;sst (sonst h&#228;tte das HUD keine
            //    Chance mehr, die CastBar zu schlie&#223;en).
            if (m_CurrentState == CastingState)
            {
                EndCastClientRpc(false);
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
        /// Client-side handler für das <see cref="UnitStats.ClientDamageReceived"/>-Event.
        /// Wird auf jedem Peer (Server + alle Clients) aufgerufen, sobald UnitStats den
        /// Damage-Fanout für diesen Spieler auslöst. Spielt nur dann eine Hit-Reaction,
        /// wenn tatsächlich Schaden geflossen ist — Misses/Dodges/Resists triggern keine
        /// Anim (dafür wäre ein separates Reaction-Event vorgesehen).
        /// </summary>
        private void OnClientDamageReceived(int amount, HitResult result)
        {
            if (amount <= 0)
            {
                return;
            }
            if (m_Visuals != null)
            {
                m_Visuals.PlayHit();
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
            // Combat selbst horcht nicht mehr auf den Input — wir merken uns die Referenz
            // nur, weil andere Komponenten ggf. ueber Combat darauf zugreifen.
            m_Input = input;
        }

        // -------------------------------------------------------------------------
        // Owner-Input → Server
        // -------------------------------------------------------------------------

        /// <summary>
        /// LoL-Style Auto-Attack-Einstieg fuer den <see cref="MobaCommandController"/>.
        /// Wird per Frame aus dem Follow-Update aufgerufen, sobald der Spieler in
        /// Waffenreichweite zum gelockten Ziel steht. Idempotent gegenueber dem
        /// Prediction-Window — solange der Owner einen Angriff vorhersagt, wird
        /// kein weiterer RPC abgeschickt. Cooldown-Gating und Ziel-/Waffenvalidierung
        /// laufen auf dem Server in <see cref="RequestAttackServerRpc"/> + State-Machine.
        /// </summary>
        public void TryRequestAutoAttack()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            if (m_Stats != null && m_Stats.IsDead)
            {
                return;
            }
            if (m_TargetSelection == null || m_TargetSelection.CurrentTargetId == TargetSelection.NoTarget)
            {
                return;
            }
            // Bereits ein Attack-RPC unterwegs? Dann nichts tun — das verhindert RPC-Spam
            // pro Frame im Follow-Update und ueberlaesst die naechste Anfrage dem Ablauf
            // des Vorhersagefensters (deckt sich mit dem Waffen-Cooldown auf dem Server).
            if (IsOwnerPredictingAttack)
            {
                return;
            }

            // Lokale Movement-Vorhersage aktivieren (siehe PlayerMovement.TickOwner).
            m_OwnerPredictedAttackUntil = Time.unscaledTime + k_OwnerAttackPredictionWindow;
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

            // Kein Auto-Set des Ziels mehr — der Server nutzt ausschliesslich das bereits
            // per RequestSelectTargetServerRpc gelockte Ziel. Klick-zum-Selektieren laeuft
            // jetzt ueber PlayerTargetingInput, nicht ueber den Attack-Pfad.
            m_CurrentState.OnAttackRequested(weapon);
        }

        // -------------------------------------------------------------------------
        // Attack-Cancel (LoL-Style "Move-cancels-Attack")
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg zum Abbrechen einer laufenden Attacke. Wird vom
        /// <see cref="MobaCommandController"/> bei jedem neuen RMB-Move-Command
        /// aufgerufen, damit der Spieler — wie in League of Legends — während
        /// des Auto-Attack-Windups oder -Recovers durch Klicken irgendwo hin
        /// die Attacke abbricht und sich sofort bewegen kann. Lokal wird die
        /// Owner-Prediction-Sperre sofort gelöst, damit das Movement keinen
        /// Frame Verzögerung hat; der Server hebt parallel den
        /// <see cref="IsServerMovementLocked"/>-Lock auf und verhindert den
        /// Damage-Resolve, falls der noch nicht stattgefunden hat.
        /// Idempotent: ohne aktive Attacke ist es ein No-Op.
        /// </summary>
        public void RequestCancelAttack()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            // Owner-lokal: Movement-Prediction sofort freigeben, damit MoveDirection
            // schon im selben Frame greift, ohne auf das Server-ClientRpc zu warten.
            m_OwnerPredictedAttackUntil = 0f;
            RequestCancelAttackServerRpc();
        }

        [ServerRpc]
        private void RequestCancelAttackServerRpc(ServerRpcParams _ = default)
        {
            if (m_CurrentState != AttackingState)
            {
                return;
            }
            // ChangeState → Exit() canceled das CTS im AttackingState, damit der
            // noch nicht aufgelöste Hit nicht mehr landet (sauberer Cancel im Windup)
            // bzw. der Recovery-Teil sofort endet (sauberer Cancel im Wind-down).
            ChangeState(IdleState);
            // Visual-Fanout: Owner-Prediction überall zurücksetzen, falls noch
            // restliche ClientRpc-Latenz hängt. Anim selbst läuft kurz aus —
            // das ist okay (LoL macht es genauso: Anim klingt aus, Gameplay frei).
            NotifyAttackCanceledClientRpc();
        }

        [ClientRpc]
        private void NotifyAttackCanceledClientRpc(ClientRpcParams _ = default)
        {
            if (IsOwner)
            {
                m_OwnerPredictedAttackUntil = 0f;
            }
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
            //    Cooldown mitsenden, damit der Owner seinen Movement-Lock für
            //    exakt die gleiche Dauer wie der Server hält (kein Reconciliation-Snap,
            //    wenn die visuelle Anim kürzer ist als der Cooldown).
            PlayAttackClientRpc(weapon.AttackAnim, weapon.AttackCooldown);

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
        private void PlayAttackClientRpc(CombatAnim anim, float cooldown, ClientRpcParams _ = default)
        {
            // Server hat die Attack bestätigt: Owner-Vorhersage exakt auf den Server-
            // Cooldown verlängern. Damit ist der Owner-Movement-Lock und der
            // Server-Movement-Lock (IsServerMovementLocked) deckungsgleich —
            // egal wie lang/kurz die visuelle Attack-Anim ist.
            if (IsOwner)
            {
                m_OwnerPredictedAttackUntil = Time.unscaledTime + Mathf.Max(0.05f, cooldown);
            }

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

            // Vor dem FrontArc-Check noch einmal Richtung Ziel drehen: das Ziel
            // konnte sich w&#228;hrend Windup bewegen, sonst l&#228;uft der Treffer ggf.
            // ins Leere obwohl der Spieler "auf" dem Ziel war.
            FaceCurrentTarget();

            // 2D-Distanzprüfung (XZ) — identisch zum SoF-Quellcode (sqrt(dx²+dy²)),
            // erweitert um den HitRadius des Opfers: Treffer landet, sobald die Waffen-
            // reichweite die Körperhülle des Ziels erreicht (nicht erst dessen Mittelpunkt).
            // Damit deckt der Owner-lokale AttackRangeIndicator die echte Reichweite ab,
            // ohne dass der Spieler "in den Gegner hineinlaufen" muss.
            Vector3 d = targetObject.transform.position - transform.position;
            d.y = 0f;
            float distSqr = d.sqrMagnitude;
            float reach = weapon.Range + victimStats.HitRadius;
            if (distSqr > reach * reach)
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
            // Range-Check muss EXAKT zu ServerResolveMeleeHit passen (weapon.Range + HitRadius),
            // sonst cancelt der AttackingState den Cooldown waehrend das Resolve-Frame noch
            // einen Hit landen wuerde.
            float reach = weapon.Range + victimStats.HitRadius;
            return d.sqrMagnitude <= reach * reach;
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

        /// <summary>
        /// Range (Unity-Worldunits) der aktuell ausgerüsteten Waffe; 0, wenn keine Waffe gesetzt
        /// ist oder der Katalog noch nicht geladen wurde. Wird vom lokalen
        /// <see cref="AttackRangeIndicator"/> für die Ground-Kreis-Darstellung gelesen.
        /// </summary>
        public float CurrentWeaponRange
        {
            get
            {
                WeaponDefinition weapon = ResolveCurrentWeapon();
                return weapon != null ? weapon.Range : 0f;
            }
        }

        // -------------------------------------------------------------------------
        // Spell-Cast (HUD/ActionBar → Server → SpellExecutor)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg, um einen Spell zu casten. HUD/ActionBar ruft das auf,
        /// sobald der Spieler eine Ability-Hotkey betätigt. Lokal nur Sanity-Checks
        /// (lebt, hat Spawn, hat Owner-Rechte, valides Target gewählt) — die
        /// autoritative Validierung passiert serverseitig in
        /// <see cref="RequestCastSpellServerRpc"/> über <see cref="SpellExecutor"/>.
        /// Target-Auswahl folgt der bestehenden <see cref="TargetSelection"/>-Logik;
        /// für Self-Casts darf das Target leer sein.
        /// </summary>
        /// <param name="spellEntry">Numerischer Entry aus <c>spells/_templates.json</c> (z. B. <c>133</c> für Fireball).</param>
        public void TryRequestCastSpell(int spellEntry)
        {
            if (!IsSpawned || !IsOwner || spellEntry <= 0)
            {
                return;
            }
            if (m_Stats != null && m_Stats.IsDead)
            {
                return;
            }

            ulong targetId = 0UL;
            if (m_TargetSelection != null
                && m_TargetSelection.CurrentTargetId != TargetSelection.NoTarget
                && m_TargetSelection.TryGetCurrentTarget(out NetworkObject targetObject, out _)
                && targetObject != null)
            {
                targetId = targetObject.NetworkObjectId;
            }

            RequestCastSpellServerRpc(spellEntry, targetId);
        }

        /// <summary>
        /// Server-autoritativer Eingang. Resolvt Spell-Template und Primärziel,
        /// delegiert die Annahme-/Ablehnungs-Entscheidung an den aktuellen
        /// Combat-State (Idle akzeptiert, Attacking/Casting/Dead verwerfen).
        /// </summary>
        [ServerRpc]
        private void RequestCastSpellServerRpc(int spellEntry, ulong targetNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            if (m_Stats == null || m_Stats.IsDead)
            {
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;

            SpellTemplate spell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (spell == null)
            {
                Debug.LogWarning($"[PlayerCombat] Unbekannter Spell-Entry '{spellEntry}'.");
                ServerNotifyCastFailed(senderClientId, CastResult.UnknownSpell);
                return;
            }

            ICombatUnit primaryTarget = m_Stats;
            if (targetNetworkObjectId != 0UL
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj)
                && targetObj != null)
            {
                UnitStats targetStats = targetObj.GetComponent<UnitStats>();
                if (targetStats != null)
                {
                    primaryTarget = targetStats;
                }
            }

            // Wenn der Spieler bereits castet oder swingt, lehnt der State silent ab
            // (Base.OnCastRequested ist no-op in Casting/Attacking/Dead). Damit der
            // Owner trotzdem "Du castest bereits." sieht, hier vor der State-Dispatch
            // explizit reporten.
            if (m_CurrentState != IdleState)
            {
                ServerNotifyCastFailed(senderClientId, CastResult.CasterCasting);
                return;
            }

            // Frueh-Validate: Resourcen/Cooldown/Target/Range/Faction werden hier
            // bereits geprueft, damit der Owner sofort eine Fehlermeldung bekommt
            // (kein stilles Verschlucken bei Spell auf Cooldown). Bei Cast-Time-
            // Spells laeuft am Resolve-Ende noch ein zweites Validate in
            // SpellExecutor.Execute &#8212; das kann nochmal scheitern, wenn das Ziel
            // waehrend des Casts aus der Range laeuft (WoW-Verhalten).
            CastResult preValidate = SpellCaster.Validate(m_Stats, spell, primaryTarget);
            if (preValidate != CastResult.Success)
            {
                ServerNotifyCastFailed(senderClientId, preValidate);
                return;
            }

            m_CurrentState.OnCastRequested(spellEntry, spell, targetNetworkObjectId, primaryTarget);
        }

        /// <summary>
        /// Server-Pfad zum Einleiten eines Casts. Wird vom <see cref="PlayerCombatIdleState"/>
        /// aufgerufen. Instant-Casts (<c>CastTime &lt;= 0</c>) werden sofort über
        /// <see cref="ServerCompleteCast"/> abgewickelt — der Spieler bleibt im
        /// Idle-State. Cast-Time-Spells transitionieren in den <see cref="CastingState"/>,
        /// der das Awaitable-Gate hält und am Ende ebenfalls <see cref="ServerCompleteCast"/>
        /// aufruft.
        /// </summary>
        internal void BeginCast(int spellEntry, SpellTemplate spell, ulong targetNetId, ICombatUnit primaryTarget)
        {
            if (!IsServer || spell == null)
            {
                return;
            }

            // Caster Richtung Ziel ausrichten (XZ), bevor wir den Cast-Timer starten —
            // damit gerichtete VFX/Projectile-Spawns einen sinnvollen Forward haben.
            FaceCurrentTarget();

            if (spell.CastTime <= 0)
            {
                // Instant-Cast: keine State-Transition nötig, aber dennoch die
                // Cast-Pose über BeginCastClientRpc fanned (castSeconds=0 ->
                // CastBar bleibt zu, Pose feuert trotzdem auf allen Peers).
                BeginCastClientRpc(spellEntry, 0f);
                ServerCompleteCast(spellEntry, spell, targetNetId, primaryTarget);
                return;
            }

            // Owner-HUD &#252;ber den startenden Cast informieren — VOR der
            // State-Transition, damit die CastBar parallel zum Server-Timer
            // l&#228;uft. Sekunden statt Millisekunden, weil das HUD direkt
            // gegen <see cref="Time.unscaledTime"/> interpoliert.
            BeginCastClientRpc(spellEntry, spell.CastTime / 1000f);

            CastingState.ConfigureFromCast(spellEntry, spell, targetNetId, primaryTarget);
            ChangeState(CastingState);
        }

        /// <summary>
        /// Server-Pfad zum Abschluss eines Casts (sowohl für Instant- als auch
        /// für Cast-Time-Spells). Führt <see cref="SpellExecutor.Execute"/> aus
        /// und fanned bei Erfolg das Visual via <see cref="PlaySpellCastClientRpc"/>
        /// an alle Peers (inkl. Host) aus.
        /// </summary>
        internal void ServerCompleteCast(int spellEntry, SpellTemplate spell, ulong targetNetId, ICombatUnit primaryTarget)
        {
            if (!IsServer || spell == null)
            {
                return;
            }

            ICombatUnit caster = m_Stats;
            if (caster == null)
            {
                return;
            }

            SpellExecutionResult result = SpellExecutor.Execute(caster, spell, primaryTarget);
            if (result.Result != CastResult.Success)
            {
                // CastBar auch bei serverseitig abgelehnter Execute schlie&#223;en
                // (z. B. fehlende Mana / out-of-range zum Zeitpunkt des Resolves).
                EndCastClientRpc(false);
                ServerNotifyCastFailed(OwnerClientId, result.Result);
                return;
            }

            // Erfolgreich abgeschlossen — Owner-HUD die CastBar als completed
            // schlie&#223;en lassen.
            EndCastClientRpc(true);

            ulong sourceNetId = NetworkObject != null ? NetworkObject.NetworkObjectId : 0UL;
            ulong resolvedTargetNetId = ReferenceEquals(primaryTarget, caster) ? 0UL : targetNetId;
            PlaySpellCastClientRpc(spellEntry, sourceNetId, resolvedTargetNetId);
        }

        /// <summary>
        /// Server-Pfad zum harten Abbrechen eines laufenden Casts. Wird vom
        /// <see cref="Movement.PlayerMovement.SubmitCommandServerRpc"/> aufgerufen,
        /// sobald der Owner w&#228;hrend des Casts ein Move-Input absetzt
        /// (LoL/WoW-Style "Move-cancels-Cast"). Idempotent: au&#223;erhalb von
        /// <see cref="CastingState"/> ist es ein No-Op. Die State-Transition
        /// nach <see cref="IdleState"/> ruft <see cref="PlayerCombatCastingState.Exit"/>
        /// auf, das den Awaitable-CastTimer canceled — Resource-/Cooldown-Aufwand
        /// ist noch nicht gefallen (passiert erst in <see cref="ServerCompleteCast"/>),
        /// also kein Refund n&#246;tig.
        /// </summary>
        public void ServerInterruptCast()
        {
            if (!IsServer || m_CurrentState != CastingState)
            {
                return;
            }
            ChangeState(IdleState);
            EndCastClientRpc(false);
        }

        // -------------------------------------------------------------------------
        // CastBar-ClientRpcs (Owner-only Event-Fanout)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Wird vom Server gefeuert, sobald ein Cast startet. Triggert auf
        /// ALLEN Peers die Caster-Cast-Pose (FLARE <c>[cast]</c>), damit die
        /// Animation während der gesamten Cast-Zeit zu sehen ist und nicht
        /// erst nach Cast-Ende kurz aufblitzt. Quelle für das Pose-Gate:
        /// <c>spell_visual_kit.unit_cast_animation</c> per Spell-Entry. Da das
        /// Source-Index→State-Mapping nicht recoverable ist, gilt: jeder
        /// Nicht-Null-Index → generische "cast"-Pose. So feuert die Pose auch
        /// für Spells, deren Visual-Kit kein spranim hat (z. B. Spell 133 /
        /// Kit 154 → nur psystem + sound).
        ///
        /// Das <see cref="OwnerCastStarted"/>-Event (CastBar-HUD) bleibt
        /// Owner-only und feuert nur bei echtem Cast-Time-Spell
        /// (<paramref name="castSeconds"/> &gt; 0). Instant-Casts werden mit
        /// <c>castSeconds = 0</c> hier durchgeschleust, damit die Pose
        /// trotzdem auf allen Peers zu sehen ist.
        /// </summary>
        [ClientRpc]
        private void BeginCastClientRpc(int spellEntry, float castSeconds, ClientRpcParams _ = default)
        {
            TryTriggerCasterPose(spellEntry);
            TryTriggerCasterParticles(spellEntry);
            TryTriggerCasterSound(spellEntry);

            if (!IsOwner || castSeconds <= 0f)
            {
                return;
            }
            OwnerCastStarted?.Invoke(spellEntry, Mathf.Max(0.01f, castSeconds));
        }

        /// <summary>
        /// Wird vom Server gefeuert, sobald ein laufender Cast beendet wird —
        /// entweder erfolgreich (<paramref name="completed"/> = <c>true</c>) oder
        /// unterbrochen (Bewegung, Tod, Execute-Fail). Owner-only Event-Fanout.
        /// </summary>
        [ClientRpc]
        private void EndCastClientRpc(bool completed, ClientRpcParams _ = default)
        {
            // Cast-Particles laufen auf allen Peers (gespawnt in BeginCastClientRpc
            // via TryTriggerCasterParticles), also muss der Stop VOR dem Owner-Check
            // stehen — sonst wuerden Remote-Peers das endlose PSystem bis zum harten
            // 8s-Cap weiter sehen.
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            if (!IsOwner)
            {
                return;
            }
            OwnerCastEnded?.Invoke(completed);
        }

        /// <summary>
        /// Server-Helper: schickt einen <see cref="CastResult"/>-Fehler nur an den
        /// Cast-anfordernden Client (Owner). HUD (<see cref="UI.CastFailedToastHUD"/>)
        /// rendert daraus &uuml;ber <see cref="CastResultStrings.Get"/> einen
        /// kurzlebigen Screen-Toast. Self-Host (IsServer &amp;&amp; IsOwner) wird
        /// trivial mitversorgt &#8212; ClientRpc liefert auch an den lokalen Client.
        /// </summary>
        internal void ServerNotifyCastFailed(ulong targetClientId, CastResult result)
        {
            if (!IsServer || result == CastResult.Success)
            {
                return;
            }
            ClientRpcParams targetOnly = new()
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { targetClientId },
                },
            };
            NotifyCastFailedClientRpc((byte)result, targetOnly);
        }

        /// <summary>
        /// Owner-only Fanout der Cast-Fehlerursache. Wird vom HUD &uuml;ber
        /// <see cref="OwnerCastFailed"/> konsumiert &#8212; analog zu
        /// <see cref="OwnerCastStarted"/>/<see cref="OwnerCastEnded"/>.
        /// </summary>
        [ClientRpc]
        private void NotifyCastFailedClientRpc(byte resultCode, ClientRpcParams _ = default)
        {
            if (!IsOwner)
            {
                return;
            }
            OwnerCastFailed?.Invoke((CastResult)resultCode);
        }

        /// <summary>
        /// Client-seitiger Visual-Handler. Loest aus den Source-Tabellen
        /// <c>spell_visual_kit</c> (per-Spell Kit-IDs, <c>_visuals.json</c>) und
        /// <c>spell_visual</c> (Kit-Definitionen, <c>_visual_kits.json</c>) eine
        /// Phasen-Anim auf (Casting, Travel, Impact, Aura) und spawnt den
        /// <see cref="WorldSpellAnimation"/> ueber <see cref="SpellVisualSpawner"/>.
        /// </summary>
        [ClientRpc]
        private void PlaySpellCastClientRpc(int spellEntry, ulong sourceNetId, ulong targetNetId)
        {
            m_VisualKitMappingLoader ??= ServiceLocator.Get<SpellVisualKitMappingCatalogLoader>();
            m_VisualKitDefinitionLoader ??= ServiceLocator.Get<SpellVisualKitDefinitionCatalogLoader>();
            m_AnimationCatalogLoader ??= ServiceLocator.Get<SpellAnimationCatalogLoader>();

            SpellVisualKitMappingCatalog mappings = m_VisualKitMappingLoader?.GetCached();
            SpellVisualKitDefinitionCatalog defs = m_VisualKitDefinitionLoader?.GetCached();
            SpellAnimationCatalog anims = m_AnimationCatalogLoader?.GetCached();
            if (mappings == null || defs == null || anims == null)
            {
                return;
            }

            Transform sourceTransform = ResolveNetworkTransform(sourceNetId);
            if (sourceTransform == null)
            {
                return;
            }

            // Caster-Cast-Pose wird bereits in BeginCastClientRpc bei Cast-START
            // auf allen Peers ausgelöst (sowohl Cast-Time- als auch Instant-Casts).
            // Hier nur noch der Spawner-Pfad für Travel/Impact/Aura-Visuals.

            SpellVisualDefinition kit = SpellVisualResolver.Resolve(spellEntry, mappings, defs);
            if (kit == null)
            {
                return;
            }

            Transform targetTransform = targetNetId != 0UL && targetNetId != sourceNetId
                ? ResolveNetworkTransform(targetNetId)
                : null;

            SpellVisualSpawner.Spawn(kit, anims, sourceTransform, targetTransform);
        }

        /// <summary>
        /// Löst auf diesem Client die Cast-Pose des Casters (FLARE <c>[cast]</c>)
        /// aus, sofern das Visual-Kit-Mapping des Spells eine Cast-Animation
        /// vorgibt (<c>unit_cast_animation != 0</c>). Wird sowohl für Cast-Time-
        /// als auch für Instant-Casts aus <see cref="BeginCastClientRpc"/>
        /// aufgerufen — also bei Cast-START, nicht erst bei Cast-Resolve.
        /// Pose-Trigger läuft via <see cref="UnitCombatVisuals.PlayCast"/> auf
        /// dem PlayerCombat-GameObject (PlayerCombatVisuals sitzt per
        /// <see cref="RequireComponent"/> garantiert daneben).
        /// <c>uca_speed</c> wird bewusst ignoriert, bis
        /// <see cref="Sprites.FlareCharacter.Play"/> eine Speed-Übergabe
        /// unterstützt.
        /// </summary>
        private void TryTriggerCasterPose(int spellEntry)
        {
            m_VisualKitMappingLoader ??= ServiceLocator.Get<SpellVisualKitMappingCatalogLoader>();
            SpellVisualKitMappingCatalog mappings = m_VisualKitMappingLoader?.GetCached();
            if (mappings == null)
            {
                return;
            }
            if (!mappings.TryGet(spellEntry, out SpellVisualKitMapping mapping)
                || mapping == null
                || mapping.UnitCastAnimation == 0)
            {
                return;
            }
            UnitCombatVisuals visuals = m_Visuals != null
                ? m_Visuals
                : GetComponent<UnitCombatVisuals>();
            if (visuals == null)
            {
                visuals = GetComponentInChildren<UnitCombatVisuals>();
            }
            if (visuals != null)
            {
                visuals.PlayCast();
            }
        }

        /// <summary>
        /// Loest auf diesem Client das Caster-Partikelsystem des Spells aus.
        /// Resolved <c>spellEntry</c> → <see cref="SpellVisualKitMapping.CastingKit"/>
        /// → <see cref="SpellVisualKitDefinition.Psystem"/> (z. B. <c>casting_holy.psi</c>)
        /// → <see cref="ParticleSystemCatalog"/> und spawnt das System ueber
        /// <see cref="CasterParticleSpawner"/>. Wird parallel zur Cast-Pose aus
        /// <see cref="BeginCastClientRpc"/> bei Cast-START auf allen Peers gefeuert
        /// (sowohl Cast-Time- als auch Instant-Casts). Stilles No-Op, falls
        /// Mapping/Kit/PSystem-Name nicht im Katalog vorhanden.
        /// </summary>
        private void TryTriggerCasterParticles(int spellEntry)
        {
            m_VisualKitMappingLoader ??= ServiceLocator.Get<SpellVisualKitMappingCatalogLoader>();
            m_VisualKitDefinitionLoader ??= ServiceLocator.Get<SpellVisualKitDefinitionCatalogLoader>();
            m_ParticleCatalogLoader ??= ServiceLocator.Get<ParticleSystemCatalogLoader>();

            SpellVisualKitMappingCatalog mappings = m_VisualKitMappingLoader?.GetCached();
            SpellVisualKitDefinitionCatalog defs = m_VisualKitDefinitionLoader?.GetCached();
            ParticleSystemCatalog particles = m_ParticleCatalogLoader?.GetCached();
            if (mappings == null || defs == null || particles == null)
            {
                return;
            }

            if (!mappings.TryGet(spellEntry, out SpellVisualKitMapping mapping)
                || mapping == null
                || mapping.CastingKit == 0)
            {
                return;
            }
            if (!defs.TryGet(mapping.CastingKit, out SpellVisualKitDefinition kit)
                || kit == null
                || string.IsNullOrEmpty(kit.Psystem))
            {
                return;
            }
            string psName = ParticleSystemCatalog.StripPsi(kit.Psystem);
            if (!particles.TryGet(psName, out ParticleSystemDefinition def) || def == null)
            {
                return;
            }
            // Falls aus irgendeinem Grund noch ein altes Cast-PSystem laeuft
            // (z. B. zweiter Cast-Start ohne dazwischenliegendes Cast-End),
            // sauber stoppen bevor wir das neue spawnen.
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            m_ActiveCasterParticles = CasterParticleSpawner.Spawn(def, transform, worldYOffset: 0f);
        }

        /// <summary>
        /// Loest auf diesem Client den Caster-Sound des Spells aus.
        /// Resolved <c>spellEntry</c> → <see cref="SpellVisualKitMapping.CastingKit"/>
        /// → <see cref="SpellVisualKitDefinition.Sound"/> (Dateiname inkl. Extension,
        /// z. B. <c>"skill_heal.wav"</c>) → <see cref="SoundManager.GetClip"/>. Wird
        /// parallel zu Cast-Pose / Cast-Particles aus <see cref="BeginCastClientRpc"/>
        /// bei Cast-START auf allen Peers gefeuert. Stilles No-Op falls Mapping/Kit/
        /// Sound-Name nicht im Index vorhanden.
        /// </summary>
        private void TryTriggerCasterSound(int spellEntry)
        {
            m_VisualKitMappingLoader ??= ServiceLocator.Get<SpellVisualKitMappingCatalogLoader>();
            m_VisualKitDefinitionLoader ??= ServiceLocator.Get<SpellVisualKitDefinitionCatalogLoader>();
            m_SoundManager ??= ServiceLocator.Get<SoundManager>();

            SpellVisualKitMappingCatalog mappings = m_VisualKitMappingLoader?.GetCached();
            SpellVisualKitDefinitionCatalog defs = m_VisualKitDefinitionLoader?.GetCached();
            if (mappings == null || defs == null || m_SoundManager == null)
            {
                return;
            }

            if (!mappings.TryGet(spellEntry, out SpellVisualKitMapping mapping)
                || mapping == null
                || mapping.CastingKit == 0)
            {
                return;
            }
            if (!defs.TryGet(mapping.CastingKit, out SpellVisualKitDefinition kit)
                || kit == null
                || string.IsNullOrEmpty(kit.Sound))
            {
                return;
            }
            AudioClip clip = m_SoundManager.GetClip(kit.Sound);
            if (clip == null)
            {
                return;
            }
            // 3D one-shot am Caster; PlayClipAtPoint zerstoert das temporäre GO automatisch
            // nach Clip-Ende. Event-Frequenz (Cast-Start) ist niedrig genug fuer dieses Pattern.
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }

        /// <summary>
        /// Resolves a spawned <see cref="NetworkObject"/> by id to its transform,
        /// oder <c>null</c> wenn es auf diesem Client nicht (mehr) existiert.
        /// </summary>
        private Transform ResolveNetworkTransform(ulong netId)
        {
            if (netId == 0UL || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return null;
            }
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(netId, out NetworkObject no) || no == null)
            {
                return null;
            }
            return no.transform;
        }
    }
}
