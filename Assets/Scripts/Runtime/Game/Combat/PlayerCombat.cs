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
    [RequireComponent(typeof(TargetSelection))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(MobaCommandController))]
    [RequireComponent(typeof(UnitCombatVisuals))]
    public sealed partial class PlayerCombat : NetworkStateMachine<PlayerCombatState, PlayerCombat>
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
        [SerializeField] private MobaCommandController m_MoveCommands;

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

        /// <summary>
        /// Server-autoritativer Auto-Attack-Modus: <c>false</c> = Melee
        /// (Main-Hand, Entry 81 "Melee Swing"), <c>true</c> = Ranged (Bogen-Slot,
        /// Entry 82 "Ranged Attack"). Owner togglet per <see cref="RequestToggleWeaponMode"/>;
        /// der Server gated den Wechsel darauf, dass tatsaechlich eine Ranged-Waffe
        /// ausgeruestet ist (sonst bleibt der Modus zwingend Melee). Jeder Peer
        /// liest den Wert, damit der Owner-lokale Follow-Stop-Radius
        /// (<see cref="CurrentWeaponRange"/>) zur aktiven Waffe passt.
        /// </summary>
        private readonly NetworkVariable<bool> m_RangedModeActive = new(
            false,
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
        /// True, wenn der Ranged-Auto-Attack-Modus aktiv UND ein Bogen ausgeruestet
        /// ist. Wird vom HUD genutzt, um Melee/Ranged-Indikator zu rendern. Ohne
        /// ausgeruesteten Bogen ist das Ergebnis immer <c>false</c> (Auto-Fallback
        /// auf Melee), auch wenn das NetworkVariable noch <c>true</c> stehen sollte.
        /// </summary>
        public bool IsRangedModeActive => m_RangedModeActive.Value && ResolveRangedWeapon() != null;

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

        /// <summary>
        /// Feuert auf jedem Peer, sobald der Auto-Attack-Modus (Melee/Ranged)
        /// sich aendert. Payload: <c>true</c> = Ranged aktiv. Wird vom HUD
        /// abonniert, um den Melee/Ranged-Indikator umzuschalten.
        /// </summary>
        public event System.Action<bool> WeaponModeChanged;

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
            if (m_MoveCommands == null)
            {
                m_MoveCommands = GetComponent<MobaCommandController>();
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
            m_RangedModeActive.OnValueChanged += OnNetWeaponModeChanged;

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
            m_RangedModeActive.OnValueChanged -= OnNetWeaponModeChanged;

            if (IsServer && m_Stats != null)
            {
                m_Stats.OnServerDied -= OnServerDied;
            }
            if (m_Stats != null)
            {
                m_Stats.ClientDamageReceived -= OnClientDamageReceived;
            }
            CancelRespawnTimer();
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
            // Server-Safety: wird der Bogen-Slot geleert, faellt der Auto-Attack-
            // Modus zwingend auf Melee zurueck — sonst stuende der Modus auf Ranged,
            // waehrend ResolveAutoAttackWeapon mangels Bogen ohnehin Melee liefert.
            if (IsServer && newValue.Length == 0 && m_RangedModeActive.Value)
            {
                m_RangedModeActive.Value = false;
            }
            RangedChanged?.Invoke(oldValue.ToString(), newValue.ToString());
        }

        private void OnNetWeaponModeChanged(bool oldValue, bool newValue)
        {
            WeaponModeChanged?.Invoke(newValue);
        }

        // -------------------------------------------------------------------------
        // Bewegungs-Gate (von <see cref="Movement.PlayerMovement"/> server-seitig konsultiert)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Server-Sicht: <c>true</c>, sobald der Combat-State Bewegung verbietet
        /// (während eines Angriffs-Cooldowns oder nach dem Tod). Wird von
        /// <see cref="PlayerMovement.SubmitCommandServerRpc"/> autoritativ
        /// genutzt, um Move-Inputs zu verwerfen — verhindert Move-while-Attacking-Cheats.
        /// </summary>
        public bool IsServerMovementLocked =>
            IsServer && (m_CurrentState == AttackingState || m_CurrentState == CastingState || m_CurrentState == DeadState);

        /// <summary>
        /// Server-Sicht: <c>true</c>, wenn der Spieler aktuell im
        /// <see cref="CastingState"/> ist. Wird vom <see cref="PlayerMovement"/>
        /// genutzt, um beim ersten Move-Command des Owners w&#228;hrend eines Casts
        /// den Cast autoritativ zu unterbrechen (Move-cancels-Cast, LoL-Style).
        /// </summary>
        public bool IsServerCasting => IsServer && m_CurrentState == CastingState;

        /// <summary>
        /// Server-Sicht: das aktuell gecastete <see cref="SpellTemplate"/>, oder
        /// <c>null</c>, wenn der Spieler nicht im <see cref="CastingState"/> ist.
        /// Wird vom <see cref="PlayerMovement"/> konsultiert, um Spells
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
        /// <see cref="PlayerMovement"/> zusätzlich zu
        /// <see cref="PlayerCombatVisuals.IsBusy"/> konsultiert. Hartes Sicherheits-Timeout
        /// für den Fall, dass der Server die Attack verwirft (kein Ziel/Waffe) und gar
        /// kein ClientRpc folgt.
        /// </summary>
        public bool IsOwnerPredictingAttack => Time.unscaledTime < m_OwnerPredictedAttackUntil;

        private float m_OwnerPredictedAttackUntil;

        /// <summary>Maximales Vorhersagefenster (Sekunden), wenn keine Server-Antwort kommt.</summary>
        private const float k_OwnerAttackPredictionWindow = 0.5f;

        /// <summary>
        /// Spell-Entry der Basis-Melee-Auto-Attack (<c>Melee Swing</c>) aus
        /// <c>spells/_templates.json</c>. Der Rechtsklick-Auto-Attack castet diesen
        /// Spell statt einer eigenen Hitscan-Logik, damit GCD, Cast-Pose (Swing)
        /// und Schadensaufloesung ueber die regulaere Spell-Pipeline laufen.
        /// </summary>
        private const int k_MeleeAutoAttackSpell = 81;

        /// <summary>
        /// Spell-Entry der Basis-Ranged-Auto-Attack (<c>Ranged Attack</c>) aus
        /// <c>spells/_templates.json</c>. Wird im Ranged-Modus statt
        /// <see cref="k_MeleeAutoAttackSpell"/> gecastet; loest GCD, Shoot-Pose und
        /// die Bogen-Cast-Visuals (RequiredEquipment 12) ueber die Spell-Pipeline aus.
        /// </summary>
        private const int k_RangedAutoAttackSpell = 82;

        /// <summary>
        /// Fallback-Reichweite (Meter) fuer den Client-Flug-Endpunkt eines
        /// Skillshots, wenn der Spell kein gueltiges Range-Feld traegt. Spiegelt
        /// <c>ServerProjectile.k_FallbackSkillshotRangeMeters</c>, damit Visual
        /// und server-seitiges Projektil dieselbe Distanz nutzen.
        /// </summary>
        private const float k_FallbackSkillshotVisualRangeMeters = 15f;

    }
}
