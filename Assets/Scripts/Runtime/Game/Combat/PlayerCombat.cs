using System.Threading;
using Riftstorm.Game.Combat.CombatStates;
using Riftstorm.Game.Input;
using Riftstorm.Game.Items;
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
[DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCombatVisuals))]
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
    [RequireComponent(typeof(UnitStats))]
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

        [Tooltip("Default-Offhand (Buckler/Shield/Torch/...), die der Server jedem neu gespawnten Spieler equippt. " +
                 "Leer = kein Offhand. Wird ignoriert, falls die Default-Waffe TwoHanded ist.")]
        [SerializeField] private string m_DefaultOffhandId = "buckler";

        [Tooltip("Default-Ranged-Waffe (Bow/Crossbow/Gun), die der Server jedem neu gespawnten Spieler in den " +
                 "Ranged-Slot legt. Leer = kein Bogen ausgeruestet (Aimed Shot / Multi-Shot etc. werden dann " +
                 "von SpellCaster.CheckEquipment mit NoRangedWeapon abgelehnt).")]
        [SerializeField] private string m_DefaultRangedId = string.Empty;

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

        /// <summary>
        /// Server-autoritative Ranged-Waffe (Bow/Crossbow/Gun). Leer = kein
        /// Bogen ausgeruestet — Ranged-Spells (required_equipment=12) werden
        /// dann von <c>SpellCaster.CheckEquipment</c> mit
        /// <c>CastResult.NoRangedWeapon</c> abgelehnt.
        /// </summary>
        private readonly NetworkVariable<FixedString64Bytes> m_CurrentRangedId = new(
            default,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        // -------------------------------------------------------------------------
        // Equip-Lese-API (fuer PlayerEquipmentVisuals, HUD, Range-Indicator)
        // -------------------------------------------------------------------------

        /// <summary>Aktuell ausgeruestete Waffe (Id aus <c>combat/weapons.json</c>). Leer = unbewaffnet.</summary>
        public string CurrentWeaponId => m_CurrentWeaponId.Value.ToString();

        /// <summary>Aktuell ausgeruestete Offhand (Id aus <c>combat/offhand_items.json</c>). Leer = keine Offhand.</summary>
        public string CurrentOffhandId => m_CurrentOffhandId.Value.ToString();

        /// <summary>Aktuell ausgeruestete Ranged-Waffe (Id aus <c>combat/weapons.json</c>). Leer = kein Bogen.</summary>
        public string CurrentRangedId => m_CurrentRangedId.Value.ToString();

        /// <summary>
        /// Feuert auf jedem Peer (Server + alle Clients), sobald die
        /// server-autoritative Waffe sich aendert. Payload: <c>(oldId, newId)</c>,
        /// beide leer wenn ungesetzt. Wird vom <see cref="PlayerEquipmentVisuals"/>
        /// abonniert, um den FLARE-MainHand-Layer-Atlas zu tauschen.
        /// </summary>
        public event System.Action<string, string> WeaponChanged;

        /// <summary>
        /// Feuert auf jedem Peer, sobald die server-autoritative Offhand sich
        /// aendert. Payload: <c>(oldId, newId)</c>; leerer <c>newId</c> bedeutet
        /// Offhand wurde geleert (Unequip oder Verdraengung durch TwoHanded-Waffe).
        /// </summary>
        public event System.Action<string, string> OffhandChanged;

        /// <summary>
        /// Feuert auf jedem Peer, sobald die server-autoritative Ranged-Waffe
        /// sich aendert. Payload: <c>(oldId, newId)</c>; leerer <c>newId</c>
        /// bedeutet der Ranged-Slot wurde geleert (Unequip oder weil das Item
        /// keine Ranged-Waffe ist).
        /// </summary>
        public event System.Action<string, string> RangedChanged;

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
        private OffhandCatalogLoader m_OffhandCatalogLoader;
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

            // Audio-Bridge fuer SpellVisualSpawner registrieren (Gameplay-
            // Assembly darf SoundManager/ServiceLocator nicht direkt sehen).
            SpellVisualAudioHook.ClipResolver ??= ResolveSpellVisualClip;
        }

        /// <summary>
        /// Resolver fuer <see cref="SpellVisualAudioHook.ClipResolver"/>. Mappt
        /// einen Sound-Dateinamen (inkl. Extension) auf einen <see cref="AudioClip"/>
        /// via <see cref="SoundManager"/>. No-Op (<c>null</c>) wenn der Manager
        /// noch nicht im ServiceLocator registriert ist.
        /// </summary>
        private AudioClip ResolveSpellVisualClip(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }
            m_SoundManager ??= ServiceLocator.Get<SoundManager>();
            return m_SoundManager != null ? m_SoundManager.GetClip(fileName) : null;
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

            // Server bestimmt die initiale Offhand. Wird beim Equip einer
            // TwoHanded-Default-Waffe spaeter durch RequestEquipWeaponServerRpc
            // geleert; hier setzen wir den Buckler-Default bedingungslos und
            // ueberlassen die 2H-Verdraengung dem zentralen Equip-Pfad.
            if (IsServer && m_CurrentOffhandId.Value.Length == 0 && !string.IsNullOrEmpty(m_DefaultOffhandId))
            {
                WeaponDefinition defaultWeapon = ResolveCurrentWeapon();
                if (defaultWeapon == null || !defaultWeapon.IsTwoHanded)
                {
                    m_CurrentOffhandId.Value = new FixedString64Bytes(m_DefaultOffhandId);
                }
            }

            // Default-Ranged-Slot: leer ist explizit erlaubt (kein Bogen-Default).
            // Server validiert keine Ranged-Kategorie hier, weil m_DefaultRangedId
            // im Inspector vom Designer gesetzt wird; falsche Eintraege fallen
            // beim ersten ResolveRangedWeapon() einfach auf null zurueck.
            if (IsServer && m_CurrentRangedId.Value.Length == 0 && !string.IsNullOrEmpty(m_DefaultRangedId))
            {
                m_CurrentRangedId.Value = new FixedString64Bytes(m_DefaultRangedId);
            }

            // Jeder Peer haengt sich an die Equip-NetworkVariables und leitet
            // Aenderungen als Plain-string-Events weiter — Source-treu trennt das
            // Equip-Daten (NetVar) von Equip-Visuals (PlayerEquipmentVisuals) und
            // erspart der UI direkten NetVar-Zugriff.
            m_CurrentWeaponId.OnValueChanged += OnNetWeaponChanged;
            m_CurrentOffhandId.OnValueChanged += OnNetOffhandChanged;
            m_CurrentRangedId.OnValueChanged += OnNetRangedChanged;

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
            m_CurrentWeaponId.OnValueChanged -= OnNetWeaponChanged;
            m_CurrentOffhandId.OnValueChanged -= OnNetOffhandChanged;
            m_CurrentRangedId.OnValueChanged -= OnNetRangedChanged;

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

        private void OnNetWeaponChanged(FixedString64Bytes oldValue, FixedString64Bytes newValue)
        {
            WeaponChanged?.Invoke(oldValue.ToString(), newValue.ToString());
        }

        private void OnNetOffhandChanged(FixedString64Bytes oldValue, FixedString64Bytes newValue)
        {
            OffhandChanged?.Invoke(oldValue.ToString(), newValue.ToString());
        }

        private void OnNetRangedChanged(FixedString64Bytes oldValue, FixedString64Bytes newValue)
        {
            RangedChanged?.Invoke(oldValue.ToString(), newValue.ToString());
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

            // Stun-Gate: ein gestunnter Spieler kann keine Auto-Attack starten.
            // Roots erlauben Auto-Attack (FLARE-Konvention), daher hier
            // explizit IsStunned, NICHT IsImmobilized.
            if (m_Stats != null && m_Stats.IsStunned)
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
            // Mid-Windup-Stun: wenn der Angreifer waehrend des Windups gestunnt
            // wird, cancelt das den Hit-Resolve und der AttackingState faellt
            // zurueck in Idle. Roots blocken Auto-Attack NICHT.
            if (m_Stats != null && m_Stats.IsStunned)
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
            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null)
            {
                Debug.LogWarning("[PlayerCombat] WeaponCatalog not loaded — attack ignored.");
                return null;
            }
            // Equipped Waffe bevorzugt; sonst Fallback auf "unarmed" (Faust-Kampf).
            // Der "unarmed"-Eintrag liegt in Assets/StreamingAssets/combat/weapons.json
            // und stellt sicher, dass Spieler ohne Item bewaffnung ueberhaupt
            // zuschlagen koennen (Range 1.2, BaseDamage 4, cd 0.6s).
            if (m_CurrentWeaponId.Value.Length > 0)
            {
                WeaponDefinition equipped = catalog.Get(m_CurrentWeaponId.Value.ToString());
                if (equipped != null) { return equipped; }
            }
            return catalog.Get("unarmed");
        }

        /// <summary>
        /// Aktuell ausgeruestete <see cref="WeaponDefinition"/> oder <c>null</c>,
        /// wenn keine Waffe gesetzt ist bzw. der WeaponCatalog noch nicht
        /// geladen wurde. Vom HUD genutzt, um den effektiven Angriffsschaden
        /// (<c>weapon.BaseDamage + WeaponDamage + STR/2</c>) zu rendern.
        /// </summary>
        public WeaponDefinition CurrentWeapon => ResolveCurrentWeapon();

        /// <summary>
        /// Aufloesung der Ranged-Waffe (Bow/Crossbow/Gun) aus dem dedizierten
        /// Ranged-Slot. Anders als <see cref="ResolveCurrentWeapon"/> gibt es
        /// hier KEINEN <c>unarmed</c>-Fallback: ohne ausgeruesteten Bogen ist
        /// das Ergebnis <c>null</c>, damit <c>UnitStats.BaseRangedWeaponDamage</c>
        /// auf 0 faellt und <c>SpellCaster.CheckEquipment</c> Ranged-Spells
        /// (<c>required_equipment=12</c>) blockt. Zusaetzlich wird ein im
        /// Slot liegendes Nicht-Ranged-Modell (defensive Validierung, sollte
        /// vom Equip-Pfad bereits abgefangen sein) auf <c>null</c> gemappt.
        /// </summary>
        private WeaponDefinition ResolveRangedWeapon()
        {
            if (m_CurrentRangedId.Value.Length == 0) { return null; }
            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null) { return null; }
            WeaponDefinition equipped = catalog.Get(m_CurrentRangedId.Value.ToString());
            if (equipped == null || !equipped.IsRanged) { return null; }
            return equipped;
        }

        /// <summary>
        /// Aktuell ausgeruestete Ranged-Waffe (Bow/Crossbow/Gun) oder <c>null</c>.
        /// Wird von <see cref="UnitStats.BaseRangedWeaponDamage"/> gelesen und
        /// von <see cref="ResolveWeaponFor"/>, um bei Ranged-Spells die
        /// passende Waffe zu liefern.
        /// </summary>
        public WeaponDefinition CurrentRangedWeapon => ResolveRangedWeapon();

        /// <summary>
        /// Spell-aware Waffen-Aufloesung: Spells mit <c>required_equipment=12</c>
        /// (Ranged) bekommen die Bogen-Definition aus dem Ranged-Slot — falls
        /// keine vorhanden ist, <c>null</c> (kein <c>unarmed</c>-Fallback, weil
        /// <c>SpellCaster.CheckEquipment</c> den Cast vorher bereits ablehnt).
        /// Alle anderen Spells (Melee, Magie, Heal, Aura, ...) ziehen die
        /// Main-Hand-Auflfoesung mit <c>unarmed</c>-Fallback, damit z. B.
        /// Sinister Strike auch ohne Schwert eine Faust-Animation spielt.
        /// </summary>
        public WeaponDefinition ResolveWeaponFor(SpellTemplate spell)
        {
            if (spell != null && spell.RequiredEquipment == 12L)
            {
                return ResolveRangedWeapon();
            }
            return ResolveCurrentWeapon();
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
        // Weapon-Swap (Console-Command /weapon, später Loadout/Inventory)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg fuer einen Waffenwechsel. Wird aktuell vom
        /// <see cref="UI.Console.Commands.WeaponCommand"/> aufgerufen, spaeter vom
        /// Loadout-/Inventory-System. Lokale Sanity-Checks (Spawn + Owner), die
        /// autoritative Katalog-Validierung und das Setzen des
        /// <c>NetworkVariable</c> passieren serverseitig in
        /// <see cref="RequestEquipWeaponServerRpc"/>.
        /// </summary>
        /// <param name="weaponId">Id aus <c>combat/weapons.json</c> (z.B. <c>"shortsword"</c>).</param>
        public void TryRequestEquipWeapon(string weaponId)
        {
            if (!IsSpawned || !IsOwner || string.IsNullOrWhiteSpace(weaponId))
            {
                return;
            }
            RequestEquipWeaponServerRpc(new FixedString64Bytes(weaponId));
        }

        [ServerRpc]
        private void RequestEquipWeaponServerRpc(FixedString64Bytes weaponId, ServerRpcParams _ = default)
        {
            if (weaponId.Length == 0)
            {
                return;
            }

            // Katalog-Validierung — unbekannte Ids werden verworfen, sonst wuerde
            // ResolveCurrentWeapon dauerhaft null liefern und Attacks/Range-Indicator
            // tot bleiben.
            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null)
            {
                Debug.LogWarning("[PlayerCombat] WeaponCatalog not loaded — equip ignored.");
                return;
            }
            if (!catalog.TryGet(weaponId.ToString(), out WeaponDefinition weaponDef))
            {
                Debug.LogWarning($"[PlayerCombat] Unknown weapon id '{weaponId}' — equip ignored.");
                return;
            }

            // Laufenden Swing/Recovery abbrechen, damit der neue Cooldown direkt
            // aus der frisch ausgeruesteten Waffe greift, sobald der naechste
            // Attack-Request eingeht.
            if (m_CurrentState == AttackingState)
            {
                ChangeState(IdleState);
                NotifyAttackCanceledClientRpc();
            }

            m_CurrentWeaponId.Value = weaponId;

            // Source-Parity: TwoHanded-Waffen blockieren den Offhand-Slot. Schild/
            // Buckler/Torch werden beim Equip eines Greatswords/Bows automatisch
            // entfernt; das NetVar-Event fuer Offhand feuert anschliessend und
            // raeumt die OffHand-FLARE-Schicht clientseitig leer.
            if (weaponDef.IsTwoHanded && m_CurrentOffhandId.Value.Length > 0)
            {
                m_CurrentOffhandId.Value = default;
            }
        }

        // -------------------------------------------------------------------------
        // Offhand-Swap (Console-Command /offhand, später Loadout/Inventory)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg fuer einen Offhand-Wechsel. Akzeptiert <c>"none"</c>,
        /// <c>"clear"</c> oder Leerstring zum Ausziehen. Lokale Sanity-Checks
        /// (Spawn + Owner), autoritative Katalog-Validierung serverseitig in
        /// <see cref="RequestEquipOffhandServerRpc"/>.
        /// </summary>
        /// <param name="offhandId">Id aus <c>combat/offhand_items.json</c> (z. B. <c>"buckler"</c>) oder <c>"none"</c>.</param>
        public void TryRequestEquipOffhand(string offhandId)
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            string normalized = offhandId == null ? string.Empty : offhandId.Trim();
            if (normalized.Equals("none", System.StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("clear", System.StringComparison.OrdinalIgnoreCase))
            {
                normalized = string.Empty;
            }
            RequestEquipOffhandServerRpc(new FixedString64Bytes(normalized));
        }

        [ServerRpc]
        private void RequestEquipOffhandServerRpc(FixedString64Bytes offhandId, ServerRpcParams _ = default)
        {
            // Leere Id = ausziehen. Direkt setzen, ohne Katalog-Lookup.
            if (offhandId.Length == 0)
            {
                if (m_CurrentOffhandId.Value.Length > 0)
                {
                    m_CurrentOffhandId.Value = default;
                }
                return;
            }

            // TwoHanded-Waffe blockiert Offhand komplett — Source-Parity zum
            // Original (kein Buckler-Equip moeglich, solange Zweihaender gezogen).
            WeaponDefinition currentWeapon = ResolveCurrentWeapon();
            if (currentWeapon != null && currentWeapon.IsTwoHanded)
            {
                Debug.LogWarning($"[PlayerCombat] Cannot equip offhand '{offhandId}' while two-handed weapon '{currentWeapon.Id}' is wielded.");
                return;
            }

            m_OffhandCatalogLoader ??= ServiceLocator.Get<OffhandCatalogLoader>();
            OffhandCatalog catalog = m_OffhandCatalogLoader?.GetCached();
            if (catalog == null)
            {
                Debug.LogWarning("[PlayerCombat] OffhandCatalog not loaded — equip ignored.");
                return;
            }
            if (!catalog.TryGet(offhandId.ToString(), out OffhandDefinition _))
            {
                Debug.LogWarning($"[PlayerCombat] Unknown offhand id '{offhandId}' — equip ignored.");
                return;
            }

            m_CurrentOffhandId.Value = offhandId;
        }

        // -------------------------------------------------------------------------
        // Bridge fuer PlayerEquipment (Phase 16B)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Server-Bridge fuer <see cref="PlayerEquipment"/>: setzt die Waffe
        /// anhand eines <see cref="ItemTemplate"/>-Entries. Aufloesung erfolgt
        /// ueber <see cref="ItemTemplate.Model"/> → WeaponCatalog. Bei
        /// <paramref name="templateId"/> &lt;= 0 wird die Waffe geleert.
        /// </summary>
        /// <remarks>
        /// Wird ausschliesslich von <see cref="PlayerEquipment"/> aufgerufen,
        /// nachdem dort der NetworkList-Slot serverseitig geschrieben wurde.
        /// Logik (Katalog-Validierung, Attack-Cancel, Zweihaender-Offhand-Clear)
        /// ist bewusst eine 1:1-Kopie von <c>RequestEquipWeaponServerRpc</c> —
        /// kein Refactor in dieser Phase, damit der bestehende Console-Pfad
        /// stabil bleibt.
        /// </remarks>
        internal void Server_ApplyWeaponFromTemplate(int templateId)
        {
            if (!IsServer)
            {
                return;
            }

            // Leerer Slot: Waffe ausziehen — kein Default-Fallback hier, damit
            // PlayerEquipment die alleinige Quelle der Wahrheit bleibt sobald
            // ein Item drin liegt; Default greift weiter nur beim Spawn.
            if (templateId <= 0)
            {
                if (m_CurrentWeaponId.Value.Length > 0)
                {
                    m_CurrentWeaponId.Value = default;
                }
                return;
            }

            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null
                || string.IsNullOrEmpty(template.Model) || template.Model == "0")
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyWeaponFromTemplate: Template {templateId} hat kein gueltiges Model.");
                return;
            }

            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null || !catalog.TryGet(template.Model, out WeaponDefinition weaponDef))
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyWeaponFromTemplate: Model '{template.Model}' nicht im WeaponCatalog.");
                return;
            }

            if (m_CurrentState == AttackingState)
            {
                ChangeState(IdleState);
                NotifyAttackCanceledClientRpc();
            }

            m_CurrentWeaponId.Value = new FixedString64Bytes(template.Model);

            if (weaponDef.IsTwoHanded && m_CurrentOffhandId.Value.Length > 0)
            {
                m_CurrentOffhandId.Value = default;
            }
        }

        /// <summary>
        /// Server-Bridge fuer <see cref="PlayerEquipment"/>: setzt die
        /// Ranged-Waffe anhand eines <see cref="ItemTemplate"/>-Entries.
        /// Aufloesung ueber <see cref="ItemTemplate.Model"/> → WeaponCatalog
        /// (Bows liegen in <c>combat/weapons.json</c>, nicht im OffhandCatalog).
        /// Bei <paramref name="templateId"/> &lt;= 0 wird der Ranged-Slot
        /// geleert. Defensive Validierung: nur Modelle mit <c>IsRanged==true</c>
        /// werden akzeptiert, damit ein Longsword nicht durch einen falsch
        /// gemappten EquipType=Ranged-Item-Eintrag in den Ranged-Slot rutscht.
        /// </summary>
        internal void Server_ApplyRangedFromTemplate(int templateId)
        {
            if (!IsServer)
            {
                return;
            }

            if (templateId <= 0)
            {
                if (m_CurrentRangedId.Value.Length > 0)
                {
                    m_CurrentRangedId.Value = default;
                }
                return;
            }

            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null
                || string.IsNullOrEmpty(template.Model) || template.Model == "0")
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyRangedFromTemplate: Template {templateId} hat kein gueltiges Model.");
                return;
            }

            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null || !catalog.TryGet(template.Model, out WeaponDefinition weaponDef))
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyRangedFromTemplate: Model '{template.Model}' nicht im WeaponCatalog.");
                return;
            }

            if (!weaponDef.IsRanged)
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyRangedFromTemplate: Model '{template.Model}' ist keine Ranged-Waffe (Type={weaponDef.Type}).");
                return;
            }

            m_CurrentRangedId.Value = new FixedString64Bytes(template.Model);
        }

        /// <summary>
        /// Server-Bridge fuer <see cref="PlayerEquipment"/>: setzt die Offhand
        /// anhand eines <see cref="ItemTemplate"/>-Entries. Aufloesung ueber
        /// <see cref="ItemTemplate.Model"/> → OffhandCatalog. Bei
        /// <paramref name="templateId"/> &lt;= 0 wird die Offhand geleert.
        /// </summary>
        internal void Server_ApplyOffhandFromTemplate(int templateId)
        {
            if (!IsServer)
            {
                return;
            }

            if (templateId <= 0)
            {
                if (m_CurrentOffhandId.Value.Length > 0)
                {
                    m_CurrentOffhandId.Value = default;
                }
                return;
            }

            // Zweihaender blockt Offhand — Source-Parity. PlayerEquipment
            // raeumt vor einem 2H-Equip die Offhand-Slots leer; hier ist der
            // Check nur die letzte Sicherung gegen Out-of-Order-Bridge-Calls.
            WeaponDefinition currentWeapon = ResolveCurrentWeapon();
            if (currentWeapon != null && currentWeapon.IsTwoHanded)
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyOffhandFromTemplate: Zweihaender '{currentWeapon.Id}' blockiert Offhand.");
                return;
            }

            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null
                || string.IsNullOrEmpty(template.Model) || template.Model == "0")
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyOffhandFromTemplate: Template {templateId} hat kein gueltiges Model.");
                return;
            }

            m_OffhandCatalogLoader ??= ServiceLocator.Get<OffhandCatalogLoader>();
            OffhandCatalog catalog = m_OffhandCatalogLoader?.GetCached();
            if (catalog == null || !catalog.TryGet(template.Model, out OffhandDefinition _))
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyOffhandFromTemplate: Model '{template.Model}' nicht im OffhandCatalog.");
                return;
            }

            m_CurrentOffhandId.Value = new FixedString64Bytes(template.Model);
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

            // Rohe TargetId weiterreichen — auch wenn das Ziel inzwischen tot ist.
            // TryGetCurrentTarget filtert tote Targets raus; das wuerde clientseitig
            // zu targetId=0 fuehren und serverseitig zur Selbst-Fallback-Logik
            // verleiten ("Can't target self." statt "Target is dead."). Die echte
            // Validierung passiert ohnehin serverseitig in SpellCaster.Validate.
            ulong targetId = m_TargetSelection != null
                ? m_TargetSelection.CurrentTargetId
                : 0UL;

            RequestCastSpellServerRpc(spellEntry, targetId, Vector3.zero, false);
        }

        /// <summary>
        /// Variante mit explizitem Boden-Zielpunkt fuer Ground-Target-Spells
        /// (Blink/Boden-AoE). Wird vom Client-seitigen Ground-Target-Picker
        /// aufgerufen, sobald der Spieler eine Reticle-Position bestaetigt hat
        /// (LMB). Range/LoS werden serverseitig validiert; ein zu weit gesetzter
        /// Punkt wird im SpellExecutor auf <see cref="SpellTemplate.Range"/>
        /// geclampt.
        /// </summary>
        /// <param name="spellEntry">Numerischer Entry aus <c>spells/_templates.json</c>.</param>
        /// <param name="worldDestination">Welt-Koordinate des Boden-Zielpunkts.</param>
        public void TryRequestCastSpellAtGround(int spellEntry, Vector3 worldDestination)
        {
            if (!IsSpawned || !IsOwner || spellEntry <= 0)
            {
                return;
            }
            if (m_Stats != null && m_Stats.IsDead)
            {
                return;
            }

            // Rohe TargetId weiterreichen — siehe Kommentar in TryRequestCastSpellAtSelection.
            ulong targetId = m_TargetSelection != null
                ? m_TargetSelection.CurrentTargetId
                : 0UL;

            RequestCastSpellServerRpc(spellEntry, targetId, worldDestination, true);
        }

        /// <summary>
        /// Server-autoritativer Eingang. Resolvt Spell-Template und Primärziel,
        /// delegiert die Annahme-/Ablehnungs-Entscheidung an den aktuellen
        /// Combat-State (Idle akzeptiert, Attacking/Casting/Dead verwerfen).
        /// </summary>
        [ServerRpc]
        private void RequestCastSpellServerRpc(
            int spellEntry,
            ulong targetNetworkObjectId,
            Vector3 castDestination,
            bool hasCastDestination,
            ServerRpcParams rpcParams = default)
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

            ICombatUnit primaryTarget = null;
            if (targetNetworkObjectId != 0UL
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj)
                && targetObj != null)
            {
                if (targetObj.TryGetComponent<UnitStats>(out var targetStats))
                {
                    primaryTarget = targetStats;
                }
            }

            // Self-only Spells (z. B. Buff auf sich selbst) brauchen keinen externen
            // Target-Pick — hier den Caster als Primaerziel einsetzen. Fuer alle
            // anderen Spells bleibt primaryTarget==null, damit SpellCaster.CheckTarget
            // sauber "No target" / "Target is dead" / "Invalid target" zurueckgibt
            // statt das Self-Fallback faelschlich "Can't target self." zu triggern.
            if (primaryTarget == null && SpellUtils.IsSelfOnly(spell))
            {
                primaryTarget = m_Stats;
            }

            // Smart-Self-Cast: Wenn der Spieler einen Buff/Friendly-Spell wirkt
            // waehrend ein Gegner als Ziel ausgewaehlt ist, soll der Cast nicht
            // scheitern. Stattdessen wird der Caster selbst als Ziel verwendet
            // (WoW-Verhalten "Auto-self-cast on harm-target"). Greift nur fuer
            // reine Friendly-Spells (kein Hostile-Targeting moeglich) — Spells,
            // die sowohl Friendly als auch Hostile targeten koennen (z.B. Dispel),
            // bleiben am ausgewaehlten Ziel.
            if (primaryTarget != null
                && primaryTarget != (ICombatUnit)m_Stats
                && SpellUtils.CanTargetFriendly(spell)
                && !SpellUtils.CanTargetHostile(spell))
            {
                ICombatUnit casterUnit = m_Stats;
                if (casterUnit != null && primaryTarget.FactionId != casterUnit.FactionId)
                {
                    primaryTarget = m_Stats;
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

            CastDestination destination = hasCastDestination
                ? CastDestination.At(castDestination)
                : CastDestination.None;

            m_CurrentState.OnCastRequested(spellEntry, spell, targetNetworkObjectId, primaryTarget, destination);
        }

        /// <summary>
        /// Server-Pfad zum Einleiten eines Casts. Wird vom <see cref="PlayerCombatIdleState"/>
        /// aufgerufen. Instant-Casts (<c>CastTime &lt;= 0</c>) werden sofort über
        /// <see cref="ServerCompleteCast"/> abgewickelt — der Spieler bleibt im
        /// Idle-State. Cast-Time-Spells transitionieren in den <see cref="CastingState"/>,
        /// der das Awaitable-Gate hält und am Ende ebenfalls <see cref="ServerCompleteCast"/>
        /// aufruft.
        /// </summary>
        internal void BeginCast(int spellEntry, SpellTemplate spell, ulong targetNetId, ICombatUnit primaryTarget, CastDestination destination)
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
                ServerCompleteCast(spellEntry, spell, targetNetId, primaryTarget, destination);
                return;
            }

            // Owner-HUD &#252;ber den startenden Cast informieren — VOR der
            // State-Transition, damit die CastBar parallel zum Server-Timer
            // l&#228;uft. Sekunden statt Millisekunden, weil das HUD direkt
            // gegen <see cref="Time.unscaledTime"/> interpoliert.
            BeginCastClientRpc(spellEntry, spell.CastTime / 1000f);

            CastingState.ConfigureFromCast(spellEntry, spell, targetNetId, primaryTarget, destination);
            ChangeState(CastingState);
        }

        /// <summary>
        /// Server-Pfad zum Abschluss eines Casts (sowohl für Instant- als auch
        /// für Cast-Time-Spells). Führt <see cref="SpellExecutor.Execute"/> aus
        /// und fanned bei Erfolg das Visual via <see cref="PlaySpellCastClientRpc"/>
        /// an alle Peers (inkl. Host) aus. <paramref name="destination"/> wird
        /// fuer Boden-Zielpunkte (Blink, Boden-AoE) durchgereicht.
        /// </summary>
        internal void ServerCompleteCast(int spellEntry, SpellTemplate spell, ulong targetNetId, ICombatUnit primaryTarget, CastDestination destination)
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

            SpellExecutionResult result = SpellExecutor.Execute(caster, spell, primaryTarget, destination);
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

            // Cooldowns sind server-autoritativ (vgl. <see cref="SpellExecutor.StartCooldowns"/>).
            // Den startenden Cooldown + GCD an den Owner-Client mirroren, damit
            // <see cref="UI.ActionBarHUD"/> die Sweep-Anzeige rendern kann
            // (Cooldown-Replikation pro Cast statt Full-State-Sync).
            int gcdMs = (spell.Attributes & SpellAttributes.Triggered) == 0
                ? CooldownManager.GcdDurationMs
                : 0;
            int cooldownMs = spell.Cooldown > 0 ? spell.Cooldown : 0;
            if (cooldownMs > 0 || gcdMs > 0)
            {
                ClientRpcParams ownerOnly = new()
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[] { OwnerClientId },
                    },
                };
                NotifyCooldownStartedClientRpc(spellEntry, cooldownMs, gcdMs, ownerOnly);
            }

            ulong sourceNetId = NetworkObject != null ? NetworkObject.NetworkObjectId : 0UL;
            ulong resolvedTargetNetId = ReferenceEquals(primaryTarget, caster) ? 0UL : targetNetId;

            // Ground-Visual (FLARE go_kit) braucht die Cast-Destination + die
            // resolvierte Auren-Dauer, damit z. B. der Ice-Patch bei Spell 30
            // genau so lange am Boden liegt wie der Root-Aura. Wenn der Cast
            // keine Destination hat (Single-Target, Self), faellt der Spawner
            // auf One-Shot zurueck.
            Vector3 groundPoint = destination.HasValue ? destination.Position : Vector3.zero;
            int groundDurationMs = destination.HasValue ? SpellUtils.CalculateDuration(spell, caster) : 0;
            PlaySpellCastClientRpc(spellEntry, sourceNetId, resolvedTargetNetId, groundPoint, destination.HasValue, groundDurationMs);
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
            TryShowRangedForCast(spellEntry);

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
            TryHideRangedAfterCast();
            if (!IsOwner)
            {
                return;
            }
            OwnerCastEnded?.Invoke(completed);
        }

        /// <summary>
        /// Blendet die Ranged-Waffe (Bow/Crossbow/Gun) fuer die Dauer eines
        /// Shoot-Casts auf der FLARE-Schicht <c>"Ranged"</c> ein. Wird auf
        /// jedem Peer aus <see cref="BeginCastClientRpc"/> aufgerufen und ist
        /// strikt an <c>SpellTemplate.RequiredEquipment == 12L</c> gebunden —
        /// alle anderen Spells lassen MainHand/OffHand unangetastet.
        /// MainHand + OffHand bleiben in jedem Frame sichtbar (das Visual ist
        /// kein Stance-Override, sondern eine zusaetzliche Schicht), siehe
        /// <see cref="PlayerEquipmentVisuals.ShowRangedForCast"/>.
        /// </summary>
        private void TryShowRangedForCast(int spellEntry)
        {
            SpellTemplate spell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (spell == null || spell.RequiredEquipment != 12L)
            {
                return;
            }
            if (!TryGetComponent<PlayerEquipmentVisuals>(out var visuals))
            {
                return;
            }
            visuals.ShowRangedForCast(CurrentRangedId);
        }

        /// <summary>
        /// Entfernt die Ranged-Waffe vom FLARE-Layer am Cast-Ende. Idempotent —
        /// wird unkonditional aus <see cref="EndCastClientRpc"/> aufgerufen,
        /// damit auch abgebrochene Casts (Move-cancel, Death-cancel) die Bogen-
        /// Schicht wieder leeren.
        /// </summary>
        private void TryHideRangedAfterCast()
        {
            if (!TryGetComponent<PlayerEquipmentVisuals>(out var visuals))
            {
                return;
            }
            visuals.HideRangedAfterCast();
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
        /// Mirror des frisch gestarteten Spell-Cooldowns + GCD an den Owner-Client.
        /// Server hat die Cooldowns ueber <see cref="SpellExecutor.StartCooldowns"/>
        /// bereits server-autoritativ angelegt &#8212; dieser RPC schiebt dieselben
        /// Daten an den Owner, damit <see cref="UI.ActionBarHUD"/> auch auf
        /// remote Clients den Sweep + Remaining-Sekunden anzeigt.
        /// Cooldowns sind idempotent (<see cref="CooldownManager.StartCooldown"/>
        /// ueberschreibt), daher ist Self-Host (Server == Owner) harmlos.
        /// </summary>
        [ClientRpc]
        private void NotifyCooldownStartedClientRpc(int spellEntry, int cooldownMs, int gcdMs, ClientRpcParams _ = default)
        {
            if (!IsOwner || m_Stats == null)
            {
                return;
            }
            CooldownManager cd = ((ICombatUnit)m_Stats).Cooldowns;
            if (cd == null)
            {
                return;
            }
            if (cooldownMs > 0)
            {
                cd.StartCooldown(spellEntry, cooldownMs);
            }
            if (gcdMs > 0)
            {
                cd.StartGcd(gcdMs);
            }
        }

        /// <summary>
        /// Client-seitiger Visual-Handler. Loest aus den Source-Tabellen
        /// <c>spell_visual_kit</c> (per-Spell Kit-IDs, <c>_visuals.json</c>) und
        /// <c>spell_visual</c> (Kit-Definitionen, <c>_visual_kits.json</c>) eine
        /// Phasen-Anim auf (Casting, Travel, Impact, Aura) und spawnt den
        /// <see cref="WorldSpellAnimation"/> ueber <see cref="SpellVisualSpawner"/>.
        /// Wenn der Cast eine Boden-Destination hat
        /// (<paramref name="hasGroundPoint"/> = true) und das Visual-Kit ein
        /// <c>go_kit</c> definiert, wird zusaetzlich eine Ground-Animation
        /// an <paramref name="groundPoint"/> platziert (FLARE-style Eis-Patch
        /// bei Spell 30 / Ice Blast). <paramref name="groundDurationMs"/>
        /// entspricht der server-seitig resolvten Auren-Dauer; bei 0 laeuft
        /// die Anim als One-Shot.
        /// </summary>
        [ClientRpc]
        private void PlaySpellCastClientRpc(int spellEntry, ulong sourceNetId, ulong targetNetId, Vector3 groundPoint, bool hasGroundPoint, int groundDurationMs)
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

            // Ground-Visual (FLARE go_kit) — nur wenn der Server eine Cast-
            // Destination mitgeschickt hat UND das Visual-Kit eine Ground-Phase
            // aufgeloest hat. Beispiel: Spell 30 / Ice Blast hat go_kit=35
            // (nova_001.sa) und liefert eine TargetsGround-Destination.
            if (hasGroundPoint && kit.Ground.HasAny)
            {
                float lifetime = groundDurationMs > 0 ? groundDurationMs * 0.001f : 0f;
                SpellVisualSpawner.SpawnGround(kit.Ground, anims, groundPoint, lifetime);
            }
        }

        /// <summary>
        /// Löst auf diesem Client die Cast-Pose des Casters (FLARE <c>[cast]</c>)
        /// aus. Wird sowohl für Cast-Time- als auch für Instant-Casts aus
        /// <see cref="BeginCastClientRpc"/> aufgerufen — also bei Cast-START,
        /// nicht erst bei Cast-Resolve.
        /// <para>
        /// Routing nach Spell-Typ + aktuell ausgeruesteter Waffe:
        /// <list type="bullet">
        ///   <item><description><see cref="SpellEffect.WeaponDamage"/>-Spells nutzen die
        ///     Attack-Pose der equippten Waffe — Ranged-Waffe ⇒ <see cref="UnitCombatVisuals.PlayShoot"/>
        ///     (Aimed Shot), Melee-Waffe ⇒ <see cref="UnitCombatVisuals.PlaySwing"/>
        ///     (Sinister Strike). So liest der Spieler den Spell visuell als
        ///     verstaerkten Auto-Attack statt als Magier-Cast.</description></item>
        ///   <item><description>Alle anderen Spells (SchoolDamage, Heal, Aura, ...) fallen
        ///     auf die generische Cast-Pose (<see cref="UnitCombatVisuals.PlayCast"/>)
        ///     zurueck.</description></item>
        /// </list>
        /// FLARE-Charaktere haben nur eine generische Cast-Animation; der
        /// <c>unit_cast_animation</c>-Index im Visual-Kit-Mapping ist daher
        /// visuell bedeutungslos und wird hier bewusst nicht konsumiert.
        /// </para>
        /// </summary>
        private void TryTriggerCasterPose(int spellEntry)
        {
            UnitCombatVisuals visuals = m_Visuals != null
                ? m_Visuals
                : GetComponent<UnitCombatVisuals>();
            if (visuals == null)
            {
                visuals = GetComponentInChildren<UnitCombatVisuals>();
            }
            if (visuals == null)
            {
                return;
            }

            // Spell-Effekte abfragen, um zu entscheiden, ob es ein
            // Waffen-Spell ist (Pose folgt der Waffe) oder ein klassischer
            // Cast (generische Cast-Pose).
            SpellTemplate spell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (spell != null && SpellUsesWeaponAttack(spell))
            {
                // Spell-aware Auflfoesung: Ranged-Spells nutzen die Bogen-Waffe
                // aus dem Ranged-Slot (CurrentRangedWeapon), alle anderen die
                // Main-Hand. Damit spielt Aimed Shot zuverlaessig Shoot, auch
                // wenn die Main-Hand ein Longsword fuehrt.
                WeaponDefinition weapon = ResolveWeaponFor(spell);
                if (weapon != null)
                {
                    if (weapon.IsRanged) { visuals.PlayShoot(); }
                    else { visuals.PlaySwing(); }
                    return;
                }
            }
            visuals.PlayCast();
        }

        /// <summary>True, sobald der Spell mindestens einen aktiven Effekt-Slot
        /// vom Typ <see cref="SpellEffect.WeaponDamage"/> hat. Solche Spells
        /// skalieren mit dem Waffenschaden und sollen die Attack-Pose der
        /// equippten Waffe statt der generischen Cast-Pose spielen.</summary>
        private static bool SpellUsesWeaponAttack(SpellTemplate spell)
        {
            for (int slot = 1; slot <= 3; slot++)
            {
                SpellTemplateEffect eff = spell.GetEffect(slot);
                if (eff.IsActive && eff.Effect == SpellEffect.WeaponDamage)
                {
                    return true;
                }
            }
            return false;
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
