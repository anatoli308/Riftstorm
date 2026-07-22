using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Spells;
using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using Riftstorm.Gameplay.Combat.Spells.Visuals;
using Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime;
using Riftstorm.Management.SoundManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
[DisallowMultipleComponent]
    [RequireComponent(typeof(UnitStats))]
    /// <summary>
    /// Server-autoritative AI-Steuerung fuer NPC-Einheiten. Reiner Port der
    /// FLARE source-server-AI (<c>Server/src/AI/NpcAI.cpp</c>):
    /// 4-State-Machine <see cref="NpcAIState"/> (Idle/Combat/Evading/Dead),
    /// skalare Reichweiten-Checks (kein Frame-Box-Test, kein Mugen-Volumen),
    /// Schaden ueber <see cref="CombatFormulas.CalculateMeleeDamage"/>.
    ///
    /// <para>
    /// <b>Wichtig:</b> FLARE prueft Treffer rein per Distanz —
    /// <c>distance(self, target) &lt;= meleeRange + self.HitRadius + target.HitRadius</c>.
    /// Es gibt kein Geometrie-Overlap, keine Mugen-Clsn-Boxen. Die
    /// <see cref="TargetingHitbox"/> auf dem Prefab existiert ausschliesslich
    /// fuer Player-Click-Raycasting; AI ignoriert sie.
    /// </para>
    ///
    /// <para>
    /// Faction-Regel (aus <c>Shared/UnitDefines.h</c>):
    /// Hostile (3) aggrot automatisch, Neutral (2) und Friendly (1) nur per
    /// Retaliation. Auto-Aggro ohne Provokation laeuft also nur fuer
    /// <see cref="NpcTemplate.Faction"/> == 3.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UnitCombatVisuals))]
    public sealed class NpcController : NetworkBehaviour
    {
        // -------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------

        [Header("Referenzen (auto-resolved)")]
        [SerializeField] private UnitStats m_Stats;
        [SerializeField] private UnitCombatVisuals m_Visuals;
        [SerializeField] private FlareCharacter m_Character;

        [Header("AI-Reichweiten (Meter)")]
        [Tooltip("Source-Default DEFAULT_AGGRO_RANGE=5. Auto-Aggro-Suchradius fuer Hostile-Mobs.")]
        [SerializeField, Min(0f)] private float m_AggroRange = 5f;

        [Tooltip("Source-Default DEFAULT_MELEE_RANGE=3. Skalare Nahkampf-Reichweite.")]
        [SerializeField, Min(0f)] private float m_MeleeRange = 3f;

        [Tooltip("Riftstorm-Erweiterung: max. Reichweite fuer Ranged-Auto-Attacks (nur NPCs mit ranged_weapon_value>0). Das Original hat hierfuer kein Datenfeld.")]
        [SerializeField, Min(0f)] private float m_RangedRange = 20f;

        [Tooltip("Source-Default DEFAULT_LEASH_RANGE=50. Distanz vom Spawn-Punkt, ab der der NPC evadiert.")]
        [SerializeField, Min(0f)] private float m_LeashRange = 50f;

        [Tooltip("Wenn aktiv, ignoriert die Aggro-Suche Trigger-Collider auf Players nicht.")]
        [SerializeField] private bool m_IncludeTriggers = true;

        [Tooltip("LayerMask fuer den Aggro-Scan. ~0 = alle Layer.")]
        [SerializeField] private LayerMask m_TargetLayerMask = ~0;

        [Header("Bewegung")]
        [Tooltip("Multiplikator auf WalkSpeed im Evading-State. Source: EVADE_SPEED_MULTIPLIER=2.0f.")]
        [SerializeField, Min(1f)] private float m_EvadeSpeedMultiplier = 2.0f;

        [Tooltip("Toleranz (Meter) fuer 'am Home angekommen' im Evading-State.")]
        [SerializeField, Min(0.05f)] private float m_HomeArrivalDistance = 0.5f;

        [Header("Replication")]
        [Tooltip("SmoothDamp-Zeitkonstante fuer Positionsglaettung auf Remote-Clients.")]
        [SerializeField] private float m_RemoteSmoothTime = 0.1f;

        [Tooltip("Schwellenwert (Grad) ueber Octanten-Grenze, ab dem die Richtung umschaltet. " +
                 "Verhindert das Flackern zwischen Nachbar-Octanten durch SmoothDamp-Jitter. " +
                 "FLARE-Server haelt die Orientierung serverseitig stabil — wir replizieren das " +
                 "ueber NetworkVariable<byte> mit Hysterese statt per-Frame-Position-Delta.")]
        [SerializeField, Range(0f, 22.5f)] private float m_DirectionHysteresisDeg = 6f;

        [Header("Animationen")]
        [SerializeField] private string m_AnimStance = "stance";
        [SerializeField] private string m_AnimRun = "run";

        [Header("Debug")]
        [SerializeField] private bool m_ShowGizmos = true;

        // -------------------------------------------------------------------
        // Netzwerk-State
        // -------------------------------------------------------------------

        /// <summary>Server schreibt, jeder liest. Client-Glaettung haengt sich daran an.</summary>
        private readonly NetworkVariable<Vector3> m_ServerPosition =
            new(writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Server-authoritative FLARE-Direction (Riftstorm-Enum, 0=W..7=NW). Wird auf
        /// allen Peers gelesen, statt jeder Client den Octanten aus dem geglaetteten
        /// Position-Delta nachzurechnen. Verhindert das Spinnen, das bei SmoothDamp-
        /// Wobble entsteht. Default 2 = S.
        /// </summary>
        private readonly NetworkVariable<byte> m_ServerDirection =
            new(2, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Server entscheidet, ob die Einheit gerade laeuft (Run-Anim) oder steht
        /// (Stance-Anim). Verhindert, dass Remote-Clients durch SmoothDamp-Trail noch
        /// "moving" anzeigen, obwohl der Server schon idled.
        /// </summary>
        private readonly NetworkVariable<bool> m_ServerMoving =
            new(false, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Server-authoritative NPC-Rang (0=normal, 1=Elite, 2=Boss) aus
        /// <see cref="NpcTemplate.IsBoss"/>/<see cref="NpcTemplate.IsElite"/>. Repliziert,
        /// damit das lokale <see cref="UI.TargetFrameUI"/> den passenden Rahmen
        /// (Boss/Elite/Default) waehlen kann, ohne das <c>npc_template</c> clientseitig
        /// zu kennen.
        /// </summary>
        private readonly NetworkVariable<byte> m_ServerRank =
            new(0, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Server-authoritative <c>NetworkObjectId</c> des aktuellen Ziels dieses NPCs
        /// (0 = kein Ziel). Repliziert, damit das lokale <see cref="UI.TargetFrameUI"/>
        /// den Namen des NPC-Ziels ("Target-of-Target") anzeigen kann, ohne die
        /// Server-only Threat-/Target-Logik clientseitig zu kennen.
        /// </summary>
        private readonly NetworkVariable<ulong> m_ServerCurrentTargetId =
            new(0, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>Server-only gepufferter Rang aus <see cref="BindTemplate"/>.</summary>
        private byte m_RankValue;

        // -------------------------------------------------------------------
        // Server-only Felder
        // -------------------------------------------------------------------

        private NpcAIState m_State = NpcAIState.Idle;
        private UnitStats m_CurrentTarget;
        private float m_LastAttackTime = -999f;
        private bool m_ServerDead;

        // Server-only Impulse-State (KnockBack/PullTo/Charge/SlideFrom). Wird via
        // <see cref="ServerApplyImpulse"/> gesetzt; solange aktiv, ueberlagert die
        // Velocity die AI-Bewegung in <see cref="TickServer"/>. Clients folgen via
        // <see cref="m_ServerPosition"/>-Replikation &#8212; kein separates
        // ClientRpc, weil die Smooth-Interpolation der Remote-Clients ohnehin
        // jedem Server-Snapshot folgt.
        private Vector3 m_ImpulseVelocity;
        private float m_ImpulseSecondsRemaining;

        // Server-only Threat-Tabelle. Ersetzt das alte "closest hostile"-Picking
        // in UpdateCombat und ist die Quelle der Retaliation-Logik fuer
        // Neutral/Friendly. Wird in OnNetworkSpawn (Server) initialisiert und
        // ueber OnServerDamaged von UnitStats gespeist. Source-Pendant:
        // Server/src/AI/ThreatManager.h.
        private readonly ThreatManager m_Threat = new();
        private readonly List<ulong> m_ThreatUnitIdsBuffer = new(8);
        private readonly ulong[] m_TargetClientIds = new ulong[1];

        private int m_LocalObservedThreat;

        /// <summary>Client-lokaler Threat-Wert des eigenen Spielers auf diesem NPC.</summary>
        public int LocalObservedThreat => m_LocalObservedThreat;

        /// <summary>Feuert client-lokal, wenn sich der gespiegelt Threat-Wert aendert.</summary>
        public event System.Action<int> LocalObservedThreatChanged;

        /// <summary>
        /// Replizierte <c>NetworkObjectId</c> des aktuellen NPC-Ziels (0 = kein Ziel),
        /// client-lesbar fuer die Target-of-Target-Anzeige.
        /// </summary>
        public ulong CurrentTargetNetworkId => m_ServerCurrentTargetId.Value;

        /// <summary>Feuert auf allen Peers, sobald sich das replizierte NPC-Ziel aendert.</summary>
        public event System.Action<ulong> CurrentTargetChanged;

        /// <summary>True, wenn dieser NPC im Template als Boss markiert ist (repliziert, client-lesbar).</summary>
        public bool IsBoss => m_ServerRank.Value == 2;

        /// <summary>True, wenn dieser NPC im Template als Elite markiert ist (repliziert, client-lesbar).</summary>
        public bool IsElite => m_ServerRank.Value == 1;

        /// <summary>
        /// Server-only: legt externen Threat auf diese NPC-Unit an. Wird von
        /// <see cref="UnitStats"/> &uuml;ber das
        /// <see cref="ICombatUnit.AddIncomingThreat"/>-
        /// Bridging fuer den <see cref="SpellEffect.Threat"/>-
        /// SpellEffect aufgerufen.
        /// </summary>
        public void AddIncomingThreat(ulong sourceGuid, int amount)
        {
            if (!IsServer || amount == 0 || sourceGuid == 0UL) { return; }
            m_Threat.AddThreat(sourceGuid, amount);
            MirrorThreatToSource(sourceGuid);
        }

        // Runtime-Daten fuer die Template-Spell-Slots. Dynamisch dimensioniert
        // aus NpcTemplate.SpellSlots (beliebig viele Slots moeglich).
        private NpcSpellSlotRuntime[] m_SpellSlots = System.Array.Empty<NpcSpellSlotRuntime>();
        private int m_ActiveSpellSlotCount;

        // Primary-/Notfall-Spell (Port von Npc::m_primarySpellId). Eigener
        // Cooldown-Gate, da SpellCaster.Validate Cooldowns fuer Nicht-Spieler
        // ignoriert und der Primary keine eigene Interval/Cooldown-Spalte hat.
        private int m_PrimarySpellId;
        private float m_PrimaryNextReadyAt;

        // Sentinel fuer SelectSpellSlotToCast: Primary statt regulaerem Slot.
        private const int k_PrimarySlotSentinel = -2;

        // HP-Schwelle (Prozent), ab der der Primary als Notfall-Spell mit
        // hoechster Prioritaet zuendet. Port von NpcAI::selectSpellToCast
        // (healthPct <= 30).
        private const float k_EmergencyHealthPct = 30f;

        // Anti-Spam-GCD fuer den Primary, falls spell_template.cooldown == 0.
        // Verhindert Casts mit voller Server-Tickrate.
        private const float k_PrimaryGcdFallbackSec = 1.5f;

        // Basis-Auto-Attack-Spells (wie beim Spieler): Spell 81 "Melee Swing"
        // (Effekt MeleeAtk) und Spell 82 "Ranged Attack" (Effekt RangedAtk).
        // Beide haben keine Effekt-Daten ⇒ 100%-Waffenschaden-Pfad in
        // SpellExecutor/CombatFormulas. Dadurch erhalten NPC-Auto-Attacks
        // dieselbe Schul-Immunity-/Avoidance-Behandlung wie der Spieler
        // (u. a. Focused Evasion).
        private const int k_MeleeAutoAttackSpellId = 81;
        private const int k_RangedAutoAttackSpellId = 82;

        // -------------------------------------------------------------------
        // Cast-Time + GCD — Port der Spieler-Cast-Pipeline
        // (PlayerCombat.BeginCast/ServerCompleteCast). NPCs hatten bislang
        // keine Cast-Time (Instant-Execute) und keinen geteilten GCD
        // (SpellExecutor.StartCooldowns setzt GCD nur fuer IsPlayer).
        // -------------------------------------------------------------------

        // Aktiver Cast-Time-Spell. Waehrend des Casts steht der NPC still,
        // attackt nicht und startet keinen weiteren Spell — analog zum
        // Spieler-CastingState. Execute/Cooldown/GCD folgen erst bei Abschluss.
        private bool m_CastInProgress;
        private float m_CastEndsAt;
        private int m_CastSpellId;
        private int m_CastSlotIndex;
        private UnitStats m_CastTarget;

        // Client-seitige Overhead-Cast-Bar. Wird lazy zur Laufzeit ergaenzt
        // (kein Prefab-Eintrag) und ausschliesslich per ClientRpc gesteuert.
        private NpcCastBarView m_CastBarView;

        // Client-seitiger Cast-Snapshot fuer beobachtende HUDs (z. B. das
        // Target-Frame). Ausschliesslich von den Cast-Bar-ClientRpcs gesetzt;
        // der Fortschritt wird vom HUD lokal aus Start + Dauer interpoliert.
        private bool m_LocalCastActive;
        private int m_LocalCastSpellId;
        private float m_LocalCastStartUnscaled;
        private float m_LocalCastDurationSeconds;

        /// <summary>
        /// Feuert auf Clients (inkl. Host), wenn dieser NPC einen Cast-Time-Spell
        /// beginnt. Parameter: (spellId, Gesamtdauer in Sekunden). Fuer
        /// beobachtende HUDs wie das Target-Frame; der Fortschritt wird lokal
        /// interpoliert, es fliesst kein laufender Netzwerk-Traffic.
        /// </summary>
        public event System.Action<int, float> LocalCastStarted;

        /// <summary>
        /// Feuert auf Clients, wenn der laufende Cast endet (Abschluss, Abbruch
        /// oder Interrupt). Beobachtende HUDs blenden ihre Cast-Bar dann aus.
        /// </summary>
        public event System.Action LocalCastEnded;

        /// <summary>True, solange auf Clients ein Cast laeuft (Snapshot fuer spaetes Anbinden).</summary>
        public bool LocalCastActive => m_LocalCastActive;

        /// <summary>Katalog-ID des aktuell client-seitig laufenden Casts (<c>0</c>, wenn keiner).</summary>
        public int LocalCastSpellId => m_LocalCastSpellId;

        /// <summary>Startzeitpunkt des laufenden Casts in <see cref="Time.unscaledTime"/>.</summary>
        public float LocalCastStartUnscaled => m_LocalCastStartUnscaled;

        /// <summary>Gesamtdauer des laufenden Casts in Sekunden (mind. 0.01).</summary>
        public float LocalCastDurationSeconds => m_LocalCastDurationSeconds;

        // Globaler Cooldown (GCD), shared ueber alle Spells. Verhindert, dass
        // ein NPC mehrere Spells back-to-back im selben Tick zuendet — exakt
        // wie der Spieler (CooldownManager.GcdDurationMs).
        private float m_GcdReadyAt;

        // GCD-Dauer in Sekunden, 1:1 aus der Spieler-Konstante abgeleitet (1.5 s).
        private const float k_GcdSec = CooldownManager.GcdDurationMs / 1000f;

        // Template-Daten via BindTemplate (kein Mugen, kein ScriptableObject).
        private int m_Faction;
        private int m_WeaponValue = 10;
        private float m_MeleeCooldownSec = 2.0f;

        // Skill-Werte aus dem Template (melee_skill/ranged_skill). Fliessen als
        // Trefferchance-Bonus in die Hit-Formel (CombatFormulas.SkillPerHitPercent).
        private int m_MeleeSkill;
        private int m_RangedSkill;

        // Ranged-Auto-Attack-Daten (ranged_weapon_value/ranged_speed). Aktiv nur
        // wenn HasRangedWeapon. Cadence-Fallback 2 s analog Melee.
        private int m_RangedWeaponValue;
        private float m_RangedCooldownSec = 2.0f;

        // FLARE-Model-Name (via BindModelName) zum Aufloesen der Combat-Sounds
        // aus _sounds.json. Client-seitig gesetzt; leer => stille Wiedergabe.
        private string m_ModelName;

        // Lazy via ServiceLocator (pure Service). Cached den Sound-Lookup.
        private SoundManager m_SoundManager;

        // Lazy via ServiceLocator (pure Services). Visual-Kit-/Anim-/Particle-
        // Kataloge zum Aufloesen + Spawnen der Spell-Visuals (Travel/Impact/
        // Aura/Ground + Caster-Particles). Spiegelt die Loader-Felder aus
        // <see cref="Combat.PlayerCombat"/>, damit NPC-Casts dieselbe
        // FLARE-Visual-Pipeline wie Spieler-Casts rendern.
        private SpellVisualKitMappingCatalogLoader m_VisualKitMappingLoader;
        private SpellVisualKitDefinitionCatalogLoader m_VisualKitDefinitionLoader;
        private SpellAnimationCatalogLoader m_AnimationCatalogLoader;
        private ParticleSystemCatalogLoader m_ParticleCatalogLoader;

        /// <summary>
        /// Client-lokales Handle auf die aktuell laufenden Caster-Cast-Particles
        /// (gespawnt in <see cref="TryTriggerCasterParticles"/>). Wird beim
        /// naechsten Cast-Start, in <see cref="HideCastBarClientRpc"/> und in
        /// <see cref="PlayDieClientRpc"/> gestoppt, damit endlose PSystems
        /// (<c>lifetime = -1</c>) nicht bis zum 8s-Cap weiter glitzern.
        /// </summary>
        private GameObject m_ActiveCasterParticles;

        /// <summary>
        /// Fallback-Reichweite (Meter) fuer das gerichtete Skillshot-Visual,
        /// falls der Spell keine verwertbare Range traegt. Spiegelt die
        /// Konstante aus <see cref="PlayerCombat"/>.
        /// </summary>
        private const float k_FallbackSkillshotVisualRangeMeters = 12f;

        // Home-Position fuer Leash/Evade. Wird einmalig beim ersten Server-Tick gesetzt.
        private Vector3 m_HomePosition;
        private bool m_HomeInitialized;

        // Buffer fuer Aggro-Scan – statisch dimensioniert, kein Heap pro Frame.
        private static readonly Collider[] s_OverlapBuffer = new Collider[32];

        // Server-only: Wunsch-Blickrichtung des aktuellen States. Wird pro Tick von
        // UpdateCombat/UpdateEvading gesetzt und am Ende von TickServer in
        // m_ServerDirection (mit Hysterese) gepushed. Quelle ist die Intention
        // (Target-Position / Home-Position), NICHT die geglaettete Bewegung — exakt
        // wie source-server NpcAI::update das Sprite-Facing setzt.
        private Vector3 m_ServerFacingVec;
        private Vector3 m_ServerPrevPosition;
        private bool m_ServerPrevInitialized;

        // Anzahl aufeinanderfolgender Server-Ticks ohne Bewegung. Wird genutzt, um
        // m_ServerMoving asymmetrisch zu hysteresen: true ist immediate, false
        // braucht k_MovingStopGraceTicks Ticks ohne Bewegung. Verhindert das
        // run/stance-Strobing waehrend Combat, wenn der NPC zwischen Chase-Schritt
        // und In-Melee-Stillstand oszilliert (ein Schritt pro Tick reicht sonst
        // aus, um die Run-Anim 50–100 ms lang einzublenden, dann wieder Stance,
        // dann wieder Run — was visuell wie 'Drehen hin und her' wirkt).
        private int m_StoppedTickCount;

        // ~3 Ticks bei ServerTickRate 20 Hz = 150 ms Mindest-Stand vor Stance-Wechsel.
        // Knapp unterhalb der menschlichen Flicker-Wahrnehmungsschwelle (~100 ms).
        private const int k_MovingStopGraceTicks = 3;

        /// <summary>
        /// Laufzeitzustand eines NPC-Spell-Slots (Template + Timestamps in Sekunden).
        /// <para>
        /// <see cref="IntervalSec"/> gate't die Auswahl-Frequenz ("wie oft darf der
        /// Slot einen Cast-Versuch machen"), <see cref="CooldownSec"/> gate't den
        /// erfolgreichen Cast selbst. Beide Werte kommen aus
        /// <see cref="NpcTemplate.GetSpellSlot(int)"/>.
        /// </para>
        /// </summary>
        private struct NpcSpellSlotRuntime
        {
            public int SpellId;
            public float ChancePct;
            public float IntervalSec;
            public float CooldownSec;
            public float NextAttemptAt;
            public float NextReadyAt;
        }

        // -------------------------------------------------------------------
        // Visual-Tracking (alle Peers)
        // -------------------------------------------------------------------

        private Vector3 m_SmoothVelocity;

        // -------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------

        private void Awake()
        {
            if (m_Stats == null) m_Stats = GetComponent<UnitStats>();
            if (m_Visuals == null) m_Visuals = GetComponent<UnitCombatVisuals>();
            if (m_Character == null) m_Character = GetComponentInChildren<FlareCharacter>(includeInactive: true);
        }

        /// <summary>
        /// Wird vom <see cref="FlareNpcSpawner"/> aufgerufen, sobald der FLARE-Charakter
        /// asynchron aufgebaut ist. Vorher liefert <c>GetComponentInChildren</c> im
        /// <see cref="Awake"/> noch <c>null</c>.
        /// </summary>
        public void BindCharacter(FlareCharacter character)
        {
            m_Character = character;
            if (m_Visuals != null)
            {
                m_Visuals.BindCharacter(character);
            }
        }

        /// <summary>
        /// Hinterlegt den FLARE-Model-Namen (<see cref="NpcModel.Name"/>), ueber
        /// den die Combat-Sounds aus <c>_sounds.json</c> aufgeloest werden. Wird
        /// vom <see cref="FlareNpcSpawner"/> auf jedem Client gesetzt; ohne ihn
        /// bleibt die Sound-Wiedergabe ein stiller No-op.
        /// </summary>
        /// <param name="modelName">FLARE-Model-Name (Schluessel in <c>_sounds.json</c>).</param>
        public void BindModelName(string modelName)
        {
            m_ModelName = modelName;
        }

        /// <summary>
        /// Uebernimmt Combat-relevante Felder aus dem <see cref="NpcTemplate"/>
        /// (Faction, WeaponValue, MeleeSpeed). Muss vom Spawner VOR oder direkt nach
        /// <see cref="OnNetworkSpawn"/> gerufen werden, sonst laeuft der erste
        /// Aggro-Tick mit Default-Werten.
        /// </summary>
        public void BindTemplate(NpcTemplate tpl)
        {
            if (tpl == null)
            {
                return;
            }
            m_Faction = tpl.Faction;
            // weapon_value=-1 ist DB-Sentinel "Default". Source: DEFAULT_WEAPON_VALUE=10.
            m_WeaponValue = tpl.WeaponValue > 0 ? tpl.WeaponValue : 10;
            // melee_speed liegt im JSON in Millisekunden (z. B. 2000). Source:
            // attackTimer = npc->getMeleeSpeed() / 1000.0f. Sentinel/0 => 2 s.
            float meleeMs = tpl.MeleeSpeed > 0f ? tpl.MeleeSpeed : 2000f;
            m_MeleeCooldownSec = Mathf.Max(0.1f, meleeMs / 1000f);
            // Skill-Werte (melee_skill/ranged_skill) als Hit-Bonus-Quelle puffern.
            m_MeleeSkill = Mathf.Max(0, tpl.MeleeSkill);
            m_RangedSkill = Mathf.Max(0, tpl.RangedSkill);
            // Ranged-Auto-Attack: nur scharf, wenn ranged_weapon_value>0
            // (HasRangedWeapon). ranged_speed liegt analog melee_speed in ms vor;
            // Sentinel/0 ⇒ 2 s Cadence.
            m_RangedWeaponValue = Mathf.Max(0, tpl.RangedWeaponValue);
            float rangedMs = tpl.RangedSpeed > 0f ? tpl.RangedSpeed : 2000f;
            m_RangedCooldownSec = Mathf.Max(0.1f, rangedMs / 1000f);
            // Range-Felder aus Template uebernehmen, wenn vom JSON gesetzt (>0). Sentinel
            // <=0 ⇒ Inspector-Default behalten (entspricht Source-Constants
            // DEFAULT_AGGRO_RANGE=5, DEFAULT_MELEE_RANGE=3, DEFAULT_LEASH_RANGE=50).
            // Source bindet nur leash_range pro NPC; aggro/melee sind dort globale
            // constexpr. Riftstorm-Erweiterung: alle drei pro Template tunebar.
            if (tpl.AggroRange > 0f)
            {
                m_AggroRange = tpl.AggroRange;
            }
            if (tpl.MeleeRange > 0f)
            {
                m_MeleeRange = tpl.MeleeRange;
            }
            if (tpl.LeashRange > 0f)
            {
                m_LeashRange = tpl.LeashRange;
            }

            ConfigureSpellSlots(tpl);

            // Rang (Boss/Elite/Normal) puffern und replizieren, damit das lokale
            // TargetFrameUI den passenden Rahmen waehlen kann. Push ist spawn-sicher
            // (siehe PushRankToNetwork) und wird zusaetzlich in OnNetworkSpawn
            // nachgezogen, falls BindTemplate vor dem Spawn lief.
            m_RankValue = tpl.IsBoss ? (byte)2 : tpl.IsElite ? (byte)1 : (byte)0;
            PushRankToNetwork();

            // mechanic_immune_mask scharfschalten: der AuraManager liest die Maske
            // beim Apply eines CC-Effekts ueber UnitStats.MechanicImmuneMask.
            if (m_Stats != null)
            {
                m_Stats.SetMechanicImmuneMask(tpl.MechanicImmuneMask);
            }
            // move_speed wird NICHT hier gesetzt: UnitStats.WalkSpeed ist read-only und
            // ApplyBaseStats ist nach OnNetworkSpawn gesperrt. Der Override laeuft im
            // FlareNpcSpawner.ApplyStatsToUnitStats VOR dem Netcode-Spawn.
        }

        /// <summary>
        /// Schreibt den gepufferten <see cref="m_RankValue"/> spawn-sicher in die
        /// replizierte <see cref="m_ServerRank"/>. No-op auf Clients oder solange das
        /// NetworkObject noch nicht gespawnt ist (NetworkVariable-Schreibzugriff vor
        /// Spawn wuerde werfen).
        /// </summary>
        private void PushRankToNetwork()
        {
            if (IsServer && IsSpawned)
            {
                m_ServerRank.Value = m_RankValue;
            }
        }

        /// <summary>
        /// Uebernimmt die flachen SQL-Spell-Slots aus dem Template in einen
        /// kompakt dimensionierten Runtime-Array-Cache plus den Primary-Spell.
        /// JSON-Werte sind Millisekunden; im Tick arbeiten wir in Sekunden
        /// (Time.time). Die Array-Groesse folgt <see cref="NpcTemplate.SpellSlots"/>,
        /// d. h. beliebig viele Slots werden unterstuetzt.
        /// </summary>
        private void ConfigureSpellSlots(NpcTemplate tpl)
        {
            m_ActiveSpellSlotCount = 0;
            m_PrimaryNextReadyAt = 0f;

            // Primary-/Notfall-Spell uebernehmen und gegen den Katalog pruefen.
            m_PrimarySpellId = tpl.SpellPrimary > 0 ? tpl.SpellPrimary : 0;
            if (m_PrimarySpellId > 0 && !SpellCatalogLoader.TryGetTemplate(m_PrimarySpellId, out _))
            {
                Debug.LogWarning($"[NpcController] Unbekannter Primary-Spell {m_PrimarySpellId} von NPC '{tpl.Name}' ({tpl.Entry}).");
                m_PrimarySpellId = 0;
            }

            IReadOnlyList<NpcSpellSlotData> slots = tpl.SpellSlots;
            int count = slots.Count;
            if (m_SpellSlots.Length != count)
            {
                m_SpellSlots = count > 0 ? new NpcSpellSlotRuntime[count] : System.Array.Empty<NpcSpellSlotRuntime>();
            }

            for (int i = 0; i < count; i++)
            {
                NpcSpellSlotData data = slots[i];
                int id = data.Id;
                if (id > 0 && !SpellCatalogLoader.TryGetTemplate(id, out _))
                {
                    Debug.LogWarning($"[NpcController] Unbekannter Spell {id} in Slot {i + 1} von NPC '{tpl.Name}' ({tpl.Entry}).");
                    id = 0;
                }
                m_SpellSlots[i] = new NpcSpellSlotRuntime
                {
                    SpellId = id > 0 ? id : 0,
                    ChancePct = Mathf.Clamp(data.ChancePct, 0f, 100f),
                    IntervalSec = data.IntervalMs > 0f ? data.IntervalMs / 1000f : 0f,
                    CooldownSec = data.CooldownMs > 0f ? data.CooldownMs / 1000f : 0f,
                    NextAttemptAt = 0f,
                    NextReadyAt = 0f,
                };
                if (m_SpellSlots[i].SpellId > 0)
                {
                    m_ActiveSpellSlotCount++;
                }
            }
        }

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Auf allen Peers: Ziel-Aenderungen weiterreichen (Target-of-Target-UI).
            m_ServerCurrentTargetId.OnValueChanged += OnServerCurrentTargetIdChanged;

            if (m_Stats != null)
            {
                m_Stats.ClientDamageReceived += HandleClientDamageReceived;

                if (IsServer)
                {
                    m_Stats.OnServerDied += HandleServerDied;
                    m_Stats.OnServerDamaged += HandleServerDamaged;
                    m_ServerPosition.Value = transform.position;
                    m_HomePosition = transform.position;
                    m_HomeInitialized = true;
                    // Rang nachziehen, falls BindTemplate vor dem Spawn lief.
                    PushRankToNetwork();
                }
            }
        }

        /// <inheritdoc/>
        public override void OnNetworkDespawn()
        {
            m_ServerCurrentTargetId.OnValueChanged -= OnServerCurrentTargetIdChanged;

            if (m_Stats != null)
            {
                m_Stats.ClientDamageReceived -= HandleClientDamageReceived;
                if (IsServer)
                {
                    m_Stats.OnServerDied -= HandleServerDied;
                    m_Stats.OnServerDamaged -= HandleServerDamaged;
                }
            }
            if (IsServer)
            {
                m_Threat.Clear();
                ResetSpellRuntimeTimers();
            }
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// OnValueChanged-Bruecke der replizierten Ziel-Id: feuert das public
        /// <see cref="CurrentTargetChanged"/>-Event auf allen Peers.
        /// </summary>
        /// <param name="previous">Vorherige Ziel-<c>NetworkObjectId</c>.</param>
        /// <param name="current">Aktuelle Ziel-<c>NetworkObjectId</c> (0 = kein Ziel).</param>
        private void OnServerCurrentTargetIdChanged(ulong previous, ulong current)
        {
            CurrentTargetChanged?.Invoke(current);
        }

        /// <summary>
        /// Schreibt die <c>NetworkObjectId</c> des aktuellen Ziels in die replizierte
        /// <see cref="m_ServerCurrentTargetId"/> (0 = kein/ungueltiges Ziel). Nur der
        /// Server schreibt; der Wert aendert sich nur bei echtem Ziel-Wechsel.
        /// </summary>
        private void PushCurrentTargetId()
        {
            ulong targetId = IsValidTarget(m_CurrentTarget) ? m_CurrentTarget.NetworkObjectId : 0UL;
            if (m_ServerCurrentTargetId.Value != targetId)
            {
                m_ServerCurrentTargetId.Value = targetId;
            }
        }

        /// <summary>
        /// Setzt nur Runtime-Timestamps zurueck (keine Template-Daten).
        /// Wird bei Despawn/Death/Evade-Reset genutzt, damit NPCs nach einem
        /// harten Reset nicht mit stale Cooldown-Zeiten weiterlaufen.
        /// </summary>
        private void ResetSpellRuntimeTimers()
        {
            m_PrimaryNextReadyAt = 0f;
            m_GcdReadyAt = 0f;
            ClearActiveCastState();
            for (int i = 0; i < m_SpellSlots.Length; i++)
            {
                NpcSpellSlotRuntime slot = m_SpellSlots[i];
                slot.NextAttemptAt = 0f;
                slot.NextReadyAt = 0f;
                m_SpellSlots[i] = slot;
            }
        }

        // -------------------------------------------------------------------
        // Hauptloop
        // -------------------------------------------------------------------

        private void Update()
        {
            float dt = Time.deltaTime;

            if (IsServer)
            {
                TickServer(dt);
            }
            else
            {
                TickRemoteClient(dt);
            }

            UpdateVisuals();
        }

        // -------------------------------------------------------------------
        // Server-Tick — Port von NpcAI::update
        // -------------------------------------------------------------------

        private void TickServer(float dt)
        {
            if (m_ServerDead || m_Stats == null || m_Stats.IsDead)
            {
                m_ServerPosition.Value = transform.position;
                m_ServerMoving.Value = false;
                PushCurrentTargetId();
                return;
            }

            // Impulse-Override: externe Bewegung verdraengt jegliche AI-Logik fuer
            // die Dauer des Effekts. Der State-Switch unten wird komplett uebersprungen,
            // damit weder Pathing noch Combat-Approach das Knockback/Pull-Target
            // ueberschreiben. Replikation laeuft ueber <see cref="m_ServerPosition"/>.
            if (m_ImpulseSecondsRemaining > 0f)
            {
                float step = Mathf.Min(dt, m_ImpulseSecondsRemaining);
                transform.position += m_ImpulseVelocity * step;
                m_ImpulseSecondsRemaining -= step;
                if (m_ImpulseSecondsRemaining <= 0f)
                {
                    m_ImpulseSecondsRemaining = 0f;
                    m_ImpulseVelocity = Vector3.zero;
                }
                m_ServerPosition.Value = transform.position;
                m_ServerMoving.Value = true;
                return;
            }

            if (!m_HomeInitialized)
            {
                m_HomePosition = transform.position;
                m_HomeInitialized = true;
            }

            // Pre-Move-Position fuer Move/Idle-Klassifikation.
            if (!m_ServerPrevInitialized)
            {
                m_ServerPrevPosition = transform.position;
                m_ServerPrevInitialized = true;
            }
            Vector3 prePos = transform.position;
            m_ServerFacingVec = Vector3.zero;

            switch (m_State)
            {
                case NpcAIState.Idle:
                    UpdateIdle(dt);
                    break;
                case NpcAIState.Combat:
                    UpdateCombat(dt);
                    break;
                case NpcAIState.Evading:
                    UpdateEvading(dt);
                    break;
                case NpcAIState.Dead:
                    // no-op
                    break;
            }

            m_ServerPosition.Value = transform.position;
            PushCurrentTargetId();

            // Bewegungs- und Direction-Replikation pushen.
            Vector3 posDelta = transform.position - prePos;
            posDelta.y = 0f;
            // ~1cm/Frame Schwelle. Bei ServerTickRate=20 entspricht das ~0.2 m/s
            // minimaler Sichtbarkeitsgeschwindigkeit.
            const float k_MoveEpsilonSqr = 0.0001f;
            bool movedThisTick = posDelta.sqrMagnitude > k_MoveEpsilonSqr;

            // Asymmetrische Hysterese: Run-Anim startet sofort, Stance-Anim erst
            // nach k_MovingStopGraceTicks Ticks ohne Bewegung. Verhindert das
            // Strobing zwischen Chase-Schritt (run) und In-Melee-Stillstand
            // (stance) im Combat — die NetworkVariable wuerde sonst jeden Tick
            // flippen und ueber den Client als rasendes run/stance-Toggle
            // sichtbar werden (cf. Log-Sequenz 'dir 0→0 anim run→stance→run').
            if (movedThisTick)
            {
                m_StoppedTickCount = 0;
                if (!m_ServerMoving.Value)
                {
                    m_ServerMoving.Value = true;
                }
            }
            else
            {
                if (m_StoppedTickCount < k_MovingStopGraceTicks)
                {
                    m_StoppedTickCount++;
                }
                if (m_StoppedTickCount >= k_MovingStopGraceTicks && m_ServerMoving.Value)
                {
                    m_ServerMoving.Value = false;
                }
            }
            // Lokale Variable fuer den Rest des Ticks (Facing-Fallback unten).
            bool moving = movedThisTick;

            // Wenn der State keine Intention gesetzt hat (Idle-Wander oder externer
            // Push), nimm die tatsaechliche Bewegung als Facing-Fallback.
            if (m_ServerFacingVec.sqrMagnitude < 0.0001f && moving)
            {
                m_ServerFacingVec = posDelta;
            }
            UpdateServerDirection(m_ServerFacingVec);
            m_ServerPrevPosition = transform.position;
        }

        // -------------------------------------------------------------------
        // States
        // -------------------------------------------------------------------

        /// <summary>
        /// Port von <c>NpcAI::updateIdle</c>: nur Hostile-Faction sucht aktiv
        /// nach Targets. Neutral/Friendly bleiben Idle, bis sie per
        /// Retaliation in <see cref="HandleServerDamaged"/> Threat auf den
        /// Angreifer aufbauen und damit nach Combat wechseln.
        /// </summary>
        /// <remarks>
        /// Der Aggro-Scan setzt nicht mehr direkt <see cref="m_CurrentTarget"/>,
        /// sondern speist den gefundenen Spieler in den
        /// <see cref="ThreatManager"/> ein (initialer Threat = 1). Damit läuft
        /// das Target-Picking in <see cref="UpdateCombat"/> einheitlich über
        /// die Threat-Tabelle — und späterer Schaden anderer Spieler kann den
        /// Aggro-Pull ohne Sonderpfad überholen.
        /// </remarks>
        private void UpdateIdle(float dt)
        {
            if (!IsHostileFaction(m_Faction))
            {
                return;
            }

            UnitStats target = FindAggroTarget();
            if (target != null)
            {
                m_Threat.AddThreat(target.NetworkObjectId, 1);
                MirrorThreatToSource(target.NetworkObjectId);
                m_CurrentTarget = target;
                m_State = NpcAIState.Combat;
                PlayAggroClientRpc();
            }
        }

        /// <summary>
        /// Port von <c>NpcAI::updateCombat</c>: Leash-Check, in-Range =&gt; Attack,
        /// sonst auf Target zulaufen. Target-Verlust (tot / despawned) =&gt; Idle.
        /// </summary>
        /// <remarks>
        /// Target-Picking läuft jetzt über <see cref="ThreatManager"/>: das
        /// Top-Threat-Target wird pro Tick angefragt. Wenn die Threat-Tabelle
        /// leer wird (alle Einträge gepruned), kehrt der NPC zurück in Idle
        /// und setzt sein <see cref="m_CurrentTarget"/> zurück.
        /// </remarks>
        private void UpdateCombat(float dt)
        {
            UnitStats top = m_Threat.GetHighestThreat(NetworkManager.Singleton);
            if (top != null)
            {
                m_CurrentTarget = top;
            }
            else if (!IsValidTarget(m_CurrentTarget))
            {
                // Threat-Tabelle leer und kein gültiges Sticky-Target mehr.
                m_CurrentTarget = null;
                m_State = NpcAIState.Idle;
                return;
            }

            if (ShouldLeash())
            {
                // Source: beim Leash werden alle Threat-Einträge gedroppt,
                // damit der NPC nach dem Reset nicht sofort wieder vom
                // ursprünglichen Angreifer ge-pullt wird.
                ClearThreatAndNotify();
                m_CurrentTarget = null;
                m_State = NpcAIState.Evading;
                return;
            }

            // Riftstorm-Erweiterung gegenueber Source-Parity: solange ein
            // gueltiges Combat-Target existiert, schaut der NPC IMMER zum
            // Target — auch im Melee-Stand und waehrend eines Spell-Casts.
            // Damit verhaelt sich der NPC analog zum Spieler, der sich beim
            // Treffer/Attack-Aim zum Angreifer/Ziel ausrichtet. MoveTowardsEntity
            // ueberschreibt diesen Vektor beim Chase mit demselben Wert, der
            // Melee-Freeze (NpcAI.cpp Z.640-642) wird hier bewusst aufgeweicht,
            // damit das Sprite auch bei umrundenden Spielern dreht.
            if (IsValidTarget(m_CurrentTarget))
            {
                Vector3 toTarget = m_CurrentTarget.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    m_ServerFacingVec = toTarget;
                }
            }

            // CC-Gates: ein gestunnter NPC macht in diesem Tick gar nichts;
            // ein nur gerooteter / immobilisierter NPC darf weiter attackieren
            // und casten, kann sich aber nicht bewegen. Snare/Haste wirken
            // multiplikativ auf die Walk-Speed.
            bool stunned = m_Stats.IsStunned;
            bool immobilized = m_Stats.IsImmobilized;

            // Aktiver Cast-Time-Spell hat Vorrang: der NPC channelt, steht still
            // und attackt nicht (wie der Spieler im CastingState). Ein Stun
            // bricht den Cast ab. Solange gecastet wird, kein Move/Melee/neuer
            // Spell in diesem Tick.
            if (m_CastInProgress)
            {
                if (stunned)
                {
                    CancelActiveCast();
                }
                else
                {
                    TickActiveCast();
                    return;
                }
            }

            if (!stunned)
            {
                if (IsInMeleeRange(m_CurrentTarget))
                {
                    TryMeleeAttack(m_CurrentTarget);
                }
                else if (HasRangedWeapon && IsInRangedRange(m_CurrentTarget))
                {
                    // Ausserhalb Melee, aber in Ranged-Reichweite: stehen und
                    // schiessen (kein Kiting). Source hatte keine Ranged-AI; das
                    // ist eine Riftstorm-Erweiterung.
                    TryRangedAttack(m_CurrentTarget);
                }
                else if (!immobilized)
                {
                    float chaseSpeed = m_Stats.WalkSpeed * m_Stats.MoveSpeedMultiplier;
                    MoveTowardsEntity(m_CurrentTarget, dt, chaseSpeed);
                }

                int slotIndex = SelectSpellSlotToCast(m_CurrentTarget);
                if (slotIndex >= 0)
                {
                    BeginOrResolveSpellCast(slotIndex, m_CurrentTarget);
                }
            }
        }

        /// <summary>
        /// Port von <c>NpcAI::updateEvading</c>: mit doppelter Speed zum
        /// <see cref="m_HomePosition"/>, bei Ankunft Full-HP-Reset und zurueck
        /// in den Idle-State. Auren-Clear ist hier (noch) nicht implementiert.
        /// </summary>
        private void UpdateEvading(float dt)
        {
            // Stun/Root verhindern auch die Rueckkehr zum Home-Punkt.
            // Snare/Haste skalieren die Evade-Speed.
            if (m_Stats.IsImmobilized)
            {
                return;
            }
            float speed = m_Stats.WalkSpeed * m_EvadeSpeedMultiplier * m_Stats.MoveSpeedMultiplier;
            MoveTowardsPoint(m_HomePosition, dt, speed);

            if (Vector3.Distance(transform.position, m_HomePosition) <= m_HomeArrivalDistance)
            {
                transform.position = m_HomePosition;
                // Source: NpcAI::updateEvading setzt HP/Mana zurueck und clearst Auren.
                // Auren-Clear folgt mit dem Buff/Debuff-Pass.
                m_Stats.ServerResetHp();
                m_Stats.ServerResetMana();
                ResetSpellRuntimeTimers();
                m_State = NpcAIState.Idle;
            }
        }

        /// <summary>
        /// Port von <c>NpcAI::selectSpellToCast</c>. Reihenfolge:
        /// <list type="number">
        /// <item><b>Notfall-Primary</b>: HP <c>&lt;= 30 %</c> ⇒ Primary mit
        /// hoechster Prioritaet (sofern bereit).</item>
        /// <item><b>Regulaere Slots</b>: erster Slot, der Intervall/Cooldown/
        /// Chance/Castbarkeit erfuellt.</item>
        /// <item><b>Fallback-Primary</b>: kein Slot zog ⇒ Primary, falls bereit.</item>
        /// </list>
        /// Rueckgabe: Slot-Index, <see cref="k_PrimarySlotSentinel"/> fuer den
        /// Primary, oder <c>-1</c> (kein Cast in diesem Tick).
        /// </summary>
        private int SelectSpellSlotToCast(UnitStats target)
        {
            if (target == null || m_Stats == null)
            {
                return -1;
            }
            if (m_ActiveSpellSlotCount <= 0 && m_PrimarySpellId <= 0)
            {
                return -1;
            }

            float now = Time.time;

            // Globaler Cooldown: blockt JEDE Spell-Auswahl, solange der GCD
            // laeuft — analog zum Spieler, der waehrend des GCD keinen Spell
            // aktivieren kann. Melee/Chase laufen davon unbeeintraechtigt.
            if (now < m_GcdReadyAt)
            {
                return -1;
            }

            // (1) Notfall-Primary: bei niedriger HP vor allen Slots.
            if (CanCastPrimary(now, target) && IsInEmergencyHealth())
            {
                return k_PrimarySlotSentinel;
            }

            // (2) Regulaere Slots in Template-Reihenfolge.
            for (int i = 0; i < m_SpellSlots.Length; i++)
            {
                NpcSpellSlotRuntime slot = m_SpellSlots[i];
                if (slot.SpellId <= 0)
                {
                    continue;
                }
                if (slot.ChancePct <= 0f)
                {
                    continue;
                }
                if (slot.IntervalSec > 0f && now < slot.NextAttemptAt)
                {
                    continue;
                }

                if (slot.CooldownSec > 0f && now < slot.NextReadyAt)
                {
                    continue;
                }

                float roll = Random.Range(0f, 100f);
                if (roll > slot.ChancePct)
                {
                    continue;
                }

                if (!CanCastSpell(slot.SpellId, target))
                {
                    continue;
                }

                // Intervall erst bei tatsaechlich erfolgreicher Slot-Wahl
                // verbrauchen. Dadurch entspricht die Frequenz dem Designer-
                // intent (Chance + Conditions + Cooldown) statt bereits bei
                // reinen Fehlversuchen herunterzutakten.
                slot.NextAttemptAt = slot.IntervalSec > 0f ? now + slot.IntervalSec : now;
                m_SpellSlots[i] = slot;

                return i;
            }

            // (3) Fallback-Primary: kein Slot zog in diesem Tick.
            if (CanCastPrimary(now, target))
            {
                return k_PrimarySlotSentinel;
            }

            return -1;
        }

        /// <summary>
        /// True, wenn die aktuelle HP des NPC <c>&lt;= </c>
        /// <see cref="k_EmergencyHealthPct"/> Prozent betraegt. Port der
        /// Notfall-Bedingung aus <c>NpcAI::selectSpellToCast</c>.
        /// </summary>
        private bool IsInEmergencyHealth()
        {
            if (m_Stats == null)
            {
                return false;
            }
            int max = m_Stats.MaxHp;
            if (max <= 0)
            {
                return false;
            }
            float pct = m_Stats.CurrentHp * 100f / max;
            return pct <= k_EmergencyHealthPct;
        }

        /// <summary>
        /// Prueft, ob der Primary-/Notfall-Spell jetzt castbar ist: gesetzt,
        /// eigener Cooldown-Gate abgelaufen und voll castbar gegen das Target.
        /// </summary>
        private bool CanCastPrimary(float now, UnitStats target)
        {
            if (m_PrimarySpellId <= 0 || now < m_PrimaryNextReadyAt)
            {
                return false;
            }
            return CanCastSpell(m_PrimarySpellId, target);
        }

        /// <summary>
        /// Port von <c>NpcAI::canCastSpell</c>. Validiert Spell-Existenz plus
        /// komplette Castbarkeit gegen den aktuellen Target-Kontext. Reine
        /// Friendly-Spells werden vor der Validierung via
        /// <see cref="ResolveCastTarget"/> auf den NPC selbst umgelenkt, damit
        /// z. B. <c>Lesser Heal</c> nicht am feindlichen Aggro-Ziel scheitert.
        /// </summary>
        private bool CanCastSpell(int spellId, UnitStats target)
        {
            if (spellId <= 0 || m_Stats == null)
            {
                return false;
            }
            if (!SpellCatalogLoader.TryGetTemplate(spellId, out SpellTemplate spell) || spell == null)
            {
                return false;
            }
            UnitStats castTarget = ResolveCastTarget(spell, target);
            return SpellCaster.Validate(m_Stats, spell, castTarget) == CastResult.Success;
        }

        /// <summary>
        /// Bestimmt das tatsaechliche Cast-Ziel fuer einen NPC-Spell. Reine
        /// Friendly-Spells (kann nur Verbuendete targeten, nicht Gegner — z. B.
        /// <c>Lesser Heal</c>) wuerden gegen das feindliche Aggro-Ziel an
        /// <c>SpellCaster.CheckTarget</c> mit <c>TargetHostile</c> scheitern.
        /// Analog zum Smart-Self-Cast des Spielers
        /// (<see cref="PlayerCombat"/>) zielt der NPC solche Spells auf
        /// sich selbst. Alle anderen Spells behalten das uebergebene
        /// (feindliche) Aggro-Ziel. Idempotent: ein bereits aufgeloestes
        /// Self-Ziel bleibt Self.
        /// </summary>
        private UnitStats ResolveCastTarget(SpellTemplate spell, UnitStats hostileTarget)
        {
            if (spell == null || m_Stats == null)
            {
                return hostileTarget;
            }
            if (SpellUtils.CanTargetFriendly(spell) && !SpellUtils.CanTargetHostile(spell))
            {
                return m_Stats;
            }
            return hostileTarget;
        }

        /// <summary>
        /// Port von <c>NpcAI::performSpellCast</c>: instant Execute ueber die
        /// vorhandene Spell-Pipeline, danach Cooldown setzen und Cast-Anim
        /// replizieren. <paramref name="slotIndex"/> kann ein regulaerer Slot
        /// oder <see cref="k_PrimarySlotSentinel"/> (Primary-Spell) sein.
        /// </summary>
        private void TryPerformSpellCast(int slotIndex, UnitStats target, bool firePose = true)
        {
            if (target == null || m_Stats == null)
            {
                return;
            }

            if (slotIndex == k_PrimarySlotSentinel)
            {
                TryPerformPrimaryCast(target, firePose);
                return;
            }

            if (slotIndex < 0 || slotIndex >= m_SpellSlots.Length)
            {
                return;
            }

            NpcSpellSlotRuntime slot = m_SpellSlots[slotIndex];
            if (!SpellCatalogLoader.TryGetTemplate(slot.SpellId, out SpellTemplate spell) || spell == null)
            {
                return;
            }

            // Reine Friendly-Spells (z. B. Heal) auf den NPC selbst umlenken,
            // statt sie am feindlichen Aggro-Ziel scheitern zu lassen.
            UnitStats castTarget = ResolveCastTarget(spell, target);
            SpellExecutionResult result = SpellExecutor.Execute(m_Stats, spell, castTarget);
            if (result.Result != CastResult.Success)
            {
                return;
            }

            float spellCdSec = spell.Cooldown > 0 ? spell.Cooldown / 1000f : 0f;
            // Slot-Cooldown aus dem NPC-Template ist ein expliziter Override.
            // Wenn nicht gesetzt (<=0), faellt der Cast auf den Spell-Default
            // aus spell_template.cooldown zurueck.
            float cooldownSec = slot.CooldownSec > 0f ? slot.CooldownSec : spellCdSec;
            slot.NextReadyAt = cooldownSec > 0f ? Time.time + cooldownSec : Time.time;
            m_SpellSlots[slotIndex] = slot;

            // Globaler Cooldown wie beim Spieler: nach jedem erfolgreichen Cast
            // sperrt der GCD den naechsten Spell (cf. CooldownManager.StartGcd).
            m_GcdReadyAt = Time.time + k_GcdSec;

            // Travel-/Impact-/Aura-/Ground-Visual + Caster-Particles an alle
            // Peers fannen (server-autoritativ, Clients rendern nur).
            ServerFanSpellVisual(slot.SpellId, spell, castTarget);

            // Cast-Pose nur fannen, wenn nicht bereits beim Cast-Start gespielt
            // (Cast-Time-Spells fired die Pose in BeginOrResolveSpellCast).
            if (firePose)
            {
                PlayCastClientRpc(slot.SpellId);
            }
        }

        /// <summary>
        /// Fuehrt den Primary-/Notfall-Spell aus und setzt dessen eigenen
        /// Cooldown-Gate. Da der Primary keine Interval/Cooldown-Spalte hat und
        /// <c>SpellCaster.Validate</c> Cooldowns fuer Nicht-Spieler ignoriert,
        /// nutzen wir <c>spell_template.cooldown</c>; ist dieser <c>0</c>, greift
        /// ein <see cref="k_PrimaryGcdFallbackSec"/>-GCD als Anti-Spam-Schutz
        /// (verhindert Casts mit voller Tickrate). <paramref name="firePose"/>
        /// ist <c>false</c>, wenn die Cast-Pose bereits beim Cast-Start lief.
        /// </summary>
        private void TryPerformPrimaryCast(UnitStats target, bool firePose = true)
        {
            if (m_PrimarySpellId <= 0
                || !SpellCatalogLoader.TryGetTemplate(m_PrimarySpellId, out SpellTemplate spell)
                || spell == null)
            {
                return;
            }

            // Reine Friendly-Spells (Notfall-Heal) auf den NPC selbst umlenken.
            UnitStats castTarget = ResolveCastTarget(spell, target);
            SpellExecutionResult result = SpellExecutor.Execute(m_Stats, spell, castTarget);
            if (result.Result != CastResult.Success)
            {
                return;
            }

            float cooldownSec = spell.Cooldown > 0 ? spell.Cooldown / 1000f : k_PrimaryGcdFallbackSec;
            m_PrimaryNextReadyAt = Time.time + cooldownSec;

            // Globaler Cooldown wie beim Spieler.
            m_GcdReadyAt = Time.time + k_GcdSec;

            // Visual-Kit + Caster-Particles fannen (analog regulaerer Slot-Cast).
            ServerFanSpellVisual(m_PrimarySpellId, spell, castTarget);

            if (firePose)
            {
                PlayCastClientRpc(m_PrimarySpellId);
            }
        }

        /// <summary>
        /// Dispatcht einen ausgewaehlten Spell analog zu
        /// <see cref="PlayerCombat.BeginCast"/>: Cast-Time-Spells
        /// (<c>cast_time &gt; 0</c>) gehen in einen gehaltenen Cast (NPC steht
        /// still, channelt, loest erst nach Ablauf der Cast-Time auf),
        /// Instant-Spells (<c>cast_time &lt;= 0</c>) werden sofort aufgeloest.
        /// <paramref name="slotIndex"/> ist ein regulaerer Slot oder
        /// <see cref="k_PrimarySlotSentinel"/>.
        /// </summary>
        private void BeginOrResolveSpellCast(int slotIndex, UnitStats target)
        {
            if (target == null || m_Stats == null)
            {
                return;
            }

            int spellId = slotIndex == k_PrimarySlotSentinel
                ? m_PrimarySpellId
                : (slotIndex >= 0 && slotIndex < m_SpellSlots.Length ? m_SpellSlots[slotIndex].SpellId : 0);
            if (spellId <= 0)
            {
                return;
            }
            if (!SpellCatalogLoader.TryGetTemplate(spellId, out SpellTemplate spell) || spell == null)
            {
                return;
            }

            // Reine Friendly-Spells (Heal/Buff) channeln/zielen auf den NPC
            // selbst — Facing und m_CastTarget muessen daher das aufgeloeste
            // Ziel verwenden, nicht das feindliche Aggro-Ziel.
            UnitStats castTarget = ResolveCastTarget(spell, target);

            if (spell.CastTime > 0)
            {
                // Cast-Time-Spell: Cast-Pose SOFORT fannen (Channel-Animation),
                // Execute/Cooldown/GCD erst bei Abschluss in TickActiveCast.
                m_CastInProgress = true;
                m_CastSlotIndex = slotIndex;
                m_CastSpellId = spellId;
                m_CastTarget = castTarget;
                m_CastEndsAt = Time.time + spell.CastTime / 1000f;

                FaceTargetForCast(castTarget);
                PlayCastClientRpc(spellId);
                ShowCastBarClientRpc(spellId, spell.CastTime / 1000f);
                return;
            }

            // Instant-Cast: sofort aufloesen (setzt GCD, fired Pose).
            TryPerformSpellCast(slotIndex, castTarget);
        }

        /// <summary>
        /// Tickt einen aktiven Cast-Time-Spell: haelt das Facing auf das Ziel,
        /// bricht bei verlorenem/totem Ziel ab und loest den Spell nach Ablauf
        /// der Cast-Time auf (ohne erneute Cast-Pose). Wird nur aus
        /// <see cref="UpdateCombat"/> gerufen, solange
        /// <see cref="m_CastInProgress"/> gesetzt ist.
        /// </summary>
        private void TickActiveCast()
        {
            if (!IsValidTarget(m_CastTarget))
            {
                CancelActiveCast();
                return;
            }

            FaceTargetForCast(m_CastTarget);

            if (Time.time < m_CastEndsAt)
            {
                return; // noch am Channeln
            }

            // Cast abgeschlossen: Slot/Target sichern, State VOR dem Execute
            // zuruecksetzen (kein Re-Entry), dann aufloesen — Pose lief bereits
            // beim Cast-Start, daher firePose: false.
            int slotIndex = m_CastSlotIndex;
            UnitStats target = m_CastTarget;
            ClearActiveCastState();
            HideCastBarClientRpc();
            TryPerformSpellCast(slotIndex, target, firePose: false);
        }

        /// <summary>
        /// Bricht den aktiven Cast ab (Ziel verloren, Stun, harter Reset). Es
        /// werden weder Spell-Effekt noch GCD ausgeloest — ein abgebrochener
        /// Cast verhaelt sich wie ein nie gestarteter (vgl. Spieler-Interrupt).
        /// </summary>
        private void CancelActiveCast()
        {
            ClearActiveCastState();
            HideCastBarClientRpc();
        }

        /// <summary>
        /// Bricht einen laufenden Cast von au&#223;en ab (z. B. durch einen
        /// Spieler-Interrupt-Effekt). L&#228;uft server-autoritativ &#252;ber
        /// <see cref="UnitStats"/>. Liefert <c>true</c>,
        /// wenn tats&#228;chlich ein aktiver Cast abgebrochen wurde &#8212; ein
        /// abgebrochener Cast feuert weder Spell-Effekt noch GCD.
        /// </summary>
        /// <returns><c>true</c>, wenn ein laufender Cast unterbrochen wurde.</returns>
        public bool ServerInterruptCast()
        {
            if (!IsServer || !m_CastInProgress)
            {
                return false;
            }
            CancelActiveCast();
            return true;
        }

        /// <summary>Setzt alle Cast-Tracking-Felder zurueck.</summary>
        private void ClearActiveCastState()
        {
            m_CastInProgress = false;
            m_CastEndsAt = 0f;
            m_CastSpellId = 0;
            m_CastSlotIndex = -1;
            m_CastTarget = null;
        }

        /// <summary>
        /// Richtet die server-seitige Wunsch-Blickrichtung auf das Cast-Ziel aus
        /// (XZ), damit gerichtete VFX/Projectile-Spawns einen sinnvollen Forward
        /// haben — analog zu <c>PlayerCombat.FaceCurrentTarget</c>.
        /// </summary>
        private void FaceTargetForCast(UnitStats target)
        {
            if (target == null)
            {
                return;
            }
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                m_ServerFacingVec = toTarget;
            }
        }

        // -------------------------------------------------------------------
        // Helpers — Port der NpcAI::* Helfer
        // -------------------------------------------------------------------

        /// <summary>
        /// Port von <c>NpcAI::findAggroTarget</c>: closest hostile player im
        /// Aggro-Radius. Source nutzt eine Entity-Map; wir scannen per
        /// <see cref="Physics.OverlapSphereNonAlloc"/>, was funktional
        /// aequivalent ist, solange Players Collider haben.
        /// </summary>
        private UnitStats FindAggroTarget()
        {
            QueryTriggerInteraction triggerMode = m_IncludeTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            int hits = Physics.OverlapSphereNonAlloc(
                transform.position,
                m_AggroRange,
                s_OverlapBuffer,
                m_TargetLayerMask,
                triggerMode);

            UnitStats closest = null;
            float closestSqr = float.PositiveInfinity;
            Vector3 myPos = transform.position;

            for (int i = 0; i < hits; i++)
            {
                Collider col = s_OverlapBuffer[i];
                s_OverlapBuffer[i] = null;
                if (col == null)
                {
                    continue;
                }

                UnitStats stats = col.GetComponentInParent<UnitStats>();
                if (!IsValidTarget(stats))
                {
                    continue;
                }
                if (stats == m_Stats)
                {
                    continue;
                }
                if (!IsHostileTo(stats))
                {
                    continue;
                }

                float sqr = (stats.transform.position - myPos).sqrMagnitude;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    closest = stats;
                }
            }

            return closest;
        }

        /// <summary>
        /// Port von <c>Entity::isInRange</c> + <c>NpcAI::isInMeleeRange</c>:
        /// rein skalare Distanz. Wir addieren die HitRadii auf beiden Seiten,
        /// damit grosse Modelle sich nicht "ineinanderschieben" muessen, bevor
        /// der Schlag laendet.
        /// </summary>
        private bool IsInMeleeRange(UnitStats target)
        {
            if (target == null)
            {
                return false;
            }
            float effective = m_MeleeRange + m_Stats.HitRadius + target.HitRadius;
            float distSqr = (target.transform.position - transform.position).sqrMagnitude;
            return distSqr <= effective * effective;
        }

        /// <summary>
        /// True, wenn dieser NPC eine Fernkampfwaffe traegt (<c>ranged_weapon_value &gt; 0</c>)
        /// und damit Ranged-Auto-Attacks ausfuehren darf.
        /// </summary>
        private bool HasRangedWeapon => m_RangedWeaponValue > 0;

        /// <summary>
        /// Skalare Reichweiten-Pruefung fuer Ranged-Auto-Attacks. Analog
        /// <see cref="IsInMeleeRange"/>, aber mit <see cref="m_RangedRange"/>.
        /// </summary>
        private bool IsInRangedRange(UnitStats target)
        {
            if (target == null)
            {
                return false;
            }
            float effective = m_RangedRange + m_Stats.HitRadius + target.HitRadius;
            float distSqr = (target.transform.position - transform.position).sqrMagnitude;
            return distSqr <= effective * effective;
        }

        /// <summary>
        /// Port von <c>NpcAI::moveTowardsEntity</c>: einen Schritt Richtung
        /// Target, aber kurz vor Melee-Reichweite stoppen, damit der NPC nicht
        /// ueber den Spieler hinwegrutscht und im naechsten Tick zurueckdreht.
        /// </summary>
        private void MoveTowardsEntity(UnitStats target, float dt, float speed)
        {
            Vector3 selfPos = transform.position;
            Vector3 targetPos = target.transform.position;
            Vector3 diff = targetPos - selfPos;
            diff.y = 0f;
            float dist = diff.magnitude;
            if (dist <= 0.001f)
            {
                return;
            }

            float stopDistance = Mathf.Max(0.1f,
                m_MeleeRange + m_Stats.HitRadius + target.HitRadius - 0.1f);
            float step = Mathf.Max(0f, speed) * dt;
            float moveDist = Mathf.Min(step, Mathf.Max(0f, dist - stopDistance));
            if (moveDist <= 0f)
            {
                return;
            }

            Vector3 dirNorm = diff / dist;
            transform.position = selfPos + dirNorm * moveDist;
            // Source-Parity: orientation wird NUR gesetzt, wenn der NPC tatsaechlich
            // einen Schritt gemacht hat (NpcAI.cpp Z.640-642 in moveTowards). Damit
            // friert die Blickrichtung im Melee-Stand korrekt ein und dreht sich nicht,
            // wenn der Spieler um den NPC herumlaeuft.
            m_ServerFacingVec = diff;
        }

        /// <summary>
        /// Port von <c>NpcAI::returnHome</c>: gerader Lauf Richtung
        /// Home-Position ohne Stop-Distance.
        /// </summary>
        private void MoveTowardsPoint(Vector3 point, float dt, float speed)
        {
            Vector3 selfPos = transform.position;
            Vector3 diff = point - selfPos;
            diff.y = 0f;
            float dist = diff.magnitude;
            if (dist <= 0.001f)
            {
                return;
            }
            float step = Mathf.Min(Mathf.Max(0f, speed) * dt, dist);
            if (step <= 0f)
            {
                return;
            }
            transform.position = selfPos + (diff / dist) * step;
            // Source-Parity: orientation nur bei tatsaechlicher Bewegung. Verhindert
            // dass das Sprite am Home-Punkt (Evade-Ende) noch zappelt.
            m_ServerFacingVec = diff;
        }

        /// <summary>Port von <c>NpcAI::shouldLeash</c>: nur Distanz vom Home.</summary>
        private bool ShouldLeash()
        {
            if (!m_HomeInitialized || m_LeashRange <= 0f)
            {
                return false;
            }
            float distSqr = (transform.position - m_HomePosition).sqrMagnitude;
            return distSqr > m_LeashRange * m_LeashRange;
        }

        /// <summary>Port von <c>NpcAI::isValidTarget</c>: nicht null, nicht tot.</summary>
        private bool IsValidTarget(UnitStats target)
        {
            return target != null && !target.IsDead;
        }

        /// <summary>
        /// Port der <c>UnitDefines::Faction</c>-Logik: Hostile aggrot alles
        /// ausser sich selbst, Neutral/Friendly nur Retaliation. NPC-NPC-
        /// Friendly-Fire ist vorerst aus (analog Source-Default).
        /// </summary>
        /// TODO: Faction Enum usw. implementieren und hier nutzen, statt hardcoded.
        private bool IsHostileTo(UnitStats other)
        {
            if (other == null)
            {
                return false;
            }
            if (other.GetComponent<NpcController>() != null)
            {
                return false;
            }
            return IsHostileFaction(m_Faction);
        }

        private static bool IsHostileFaction(int faction)
        {
            // Shared/UnitDefines.h: PlayerDefault=0, Friendly=1, Neutral=2, Hostile=3, PvP=4
            // TODO: UnitFaction.cs implementieren und hier nutzen, statt hardcoded.
            return faction == 3;
        }

        // -------------------------------------------------------------------
        // Attack
        // -------------------------------------------------------------------

        /// <summary>
        /// NPC-Melee-Auto-Attack. Cooldown ueber <see cref="m_MeleeCooldownSec"/>,
        /// danach werden die Effekte von Spell 81 ("Melee Swing") ueber
        /// <see cref="SpellExecutor.ApplyAllEffectsAtImpact"/> angewendet. Der
        /// Schaden laeuft dadurch durch <see cref="SpellExecutor"/>/<see cref="CombatFormulas"/>
        /// (100%-Waffenschaden, Armor + Dodge/Parry/Block sowie School-Immunity
        /// wie Focused Evasion) — exakt wie der Spieler-Auto-Attack.
        /// <para>
        /// Bewusst <b>kein</b> <see cref="SpellCaster.Validate"/>: Die NPC-AI hat
        /// die Reichweite bereits ueber <see cref="IsInMeleeRange"/> (NPC-eigene
        /// <see cref="m_MeleeRange"/>) gegated. Die spieler-zentrische
        /// Spell-Range-/Equipment-Validierung (Spell 81 traegt nur 2&#160;m
        /// Source-Range, Spell 82 verlangt eine Ranged-Waffe) wuerde den
        /// Auto-Attack sonst faelschlich blocken. Der eigene Attack-Timer bleibt
        /// unabhaengig vom Spell-GCD, genau wie die Auto-Attack-Cadence des Spielers.
        /// </para>
        /// </summary>
        private void TryMeleeAttack(UnitStats target)
        {
            float now = Time.time;
            if (now - m_LastAttackTime < m_MeleeCooldownSec)
            {
                return;
            }
            if (m_Visuals != null && m_Visuals.IsBusy)
            {
                return;
            }
            if (m_Stats == null
                || !SpellCatalogLoader.TryGetTemplate(k_MeleeAutoAttackSpellId, out SpellTemplate spell)
                || spell == null)
            {
                return;
            }

            SpellExecutor.ApplyAllEffectsAtImpact(m_Stats, spell, target, CastDestination.None);
            m_LastAttackTime = now;
            PlaySwingClientRpc(default);
        }

        /// <summary>
        /// NPC-Ranged-Auto-Attack (Riftstorm-Erweiterung; das Original besitzt
        /// keine Ranged-AI). Teilt sich den Attack-Cooldown-Timer mit dem
        /// Melee-Swing, nutzt aber <see cref="m_RangedCooldownSec"/> als Cadence
        /// und wendet die Effekte von Spell 82 ("Ranged Attack") ueber
        /// <see cref="SpellExecutor.ApplyAllEffectsAtImpact"/> an — dieselbe
        /// Schadenspipeline wie der Spieler. Der RangedAtk-Effekt skaliert mit
        /// dem Ranged-Waffenschaden des NPCs (siehe <see cref="UnitStats.SetWeaponDamage"/>).
        /// <para>
        /// Bewusst <b>kein</b> <see cref="SpellCaster.Validate"/>: Die NPC-AI hat
        /// die Reichweite bereits ueber <see cref="IsInRangedRange"/> (NPC-eigene
        /// <see cref="m_RangedRange"/>) gegated, und Spell 82 verlangt in der
        /// Source eine equippte Ranged-Waffe (<c>BaseRangedWeaponDamage&gt;0</c>),
        /// die NPCs nie als Item-Stat fuehren — die regulaere Validierung wuerde
        /// den Schuss daher faelschlich als <c>NoRangedWeapon</c> blocken.
        /// </para>
        /// </summary>
        private void TryRangedAttack(UnitStats target)
        {
            float now = Time.time;
            if (now - m_LastAttackTime < m_RangedCooldownSec)
            {
                return;
            }
            if (m_Visuals != null && m_Visuals.IsBusy)
            {
                return;
            }
            if (m_Stats == null
                || !SpellCatalogLoader.TryGetTemplate(k_RangedAutoAttackSpellId, out SpellTemplate spell)
                || spell == null)
            {
                return;
            }

            SpellExecutor.ApplyAllEffectsAtImpact(m_Stats, spell, target, CastDestination.None);
            m_LastAttackTime = now;
            PlaySwingClientRpc(default);
        }

        // -------------------------------------------------------------------
        // Damage / Threat
        // -------------------------------------------------------------------

        /// <summary>
        /// Server-Hook: speist den <see cref="ThreatManager"/> mit jedem auf
        /// uns angewendeten Schaden und löst bei Neutral/Friendly-NPCs die
        /// Retaliation aus (Idle → Combat). Source-Pendant: NpcAI::onDamaged
        /// + ThreatManager::addThreat.
        /// </summary>
        private void HandleServerDamaged(UnitStats attacker, int damage, HitResult result)
        {
            if (attacker == null || damage <= 0 || attacker == m_Stats)
            {
                return;
            }
            m_Threat.AddThreat(attacker.NetworkObjectId, damage);
            MirrorThreatToSource(attacker.NetworkObjectId);

            // Retaliation: Neutral/Friendly wechseln aus Idle in Combat,
            // sobald sie zum ersten Mal Schaden bekommen. Hostile-NPCs sind
            // entweder schon in Combat (durch Aggro-Scan) oder folgen hier
            // ebenfalls dem Wechsel — letzteres ist günstig für Pull-aus-
            // Aggro-Range-Szenarien (z. B. Bogenschuss von ausserhalb).
            if (m_State == NpcAIState.Idle && !m_ServerDead)
            {
                m_State = NpcAIState.Combat;
            }
        }

        // -------------------------------------------------------------------
        // Death
        // -------------------------------------------------------------------

        private void HandleServerDied()
        {
            m_ServerDead = true;
            m_CurrentTarget = null;
            ClearThreatAndNotify();
            ResetSpellRuntimeTimers();
            m_State = NpcAIState.Dead;
            PlayDieClientRpc();
        }

        /// <summary>
        /// Server-only: hebt den Dead-State eines NPCs ohne Home-Teleport auf.
        /// Wird von spellbasierten Wiederbelebungen genutzt.
        /// </summary>
        public void ServerHandleExternalRevive()
        {
            if (!IsServer)
            {
                return;
            }

            m_ServerDead = false;
            m_CurrentTarget = null;
            ClearThreatAndNotify();
            ResetSpellRuntimeTimers();
            m_State = NpcAIState.Idle;
            m_ServerPosition.Value = transform.position;
            m_ServerMoving.Value = false;
            m_ImpulseSecondsRemaining = 0f;
            m_ImpulseVelocity = Vector3.zero;
            PlayRespawnClientRpc();
        }

        private void ClearThreatAndNotify()
        {
            if (!IsServer)
            {
                m_Threat.Clear();
                return;
            }

            m_Threat.CopyUnitIds(m_ThreatUnitIdsBuffer);
            m_Threat.Clear();

            for (int i = 0; i < m_ThreatUnitIdsBuffer.Count; i++)
            {
                SendThreatToSource(m_ThreatUnitIdsBuffer[i], 0);
            }

            m_ThreatUnitIdsBuffer.Clear();
        }

        private void MirrorThreatToSource(ulong sourceGuid)
        {
            SendThreatToSource(sourceGuid, m_Threat.GetThreat(sourceGuid));
        }

        private void SendThreatToSource(ulong sourceGuid, int threatValue)
        {
            if (!IsServer || sourceGuid == 0UL || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(sourceGuid, out NetworkObject sourceObject)
                || sourceObject == null)
            {
                return;
            }
            if (!sourceObject.IsPlayerObject)
            {
                return;
            }

            m_TargetClientIds[0] = sourceObject.OwnerClientId;
            ClientRpcParams clientRpcParams = new()
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = m_TargetClientIds,
                },
            };
            SyncLocalThreatClientRpc(threatValue, clientRpcParams);
        }

        // -------------------------------------------------------------------
        // Externe Bewegung (Teleport / Impulse) &#8212; vom SpellExecutor genutzt
        // -------------------------------------------------------------------

        /// <summary>
        /// Server-only: harte Reposition der NPC-Unit. Synchronisiert ueber
        /// <see cref="m_ServerPosition"/>. Bricht zusaetzlich einen laufenden
        /// Impulse ab, weil ein Teleport semantisch dominanter ist (z. B.
        /// Blink-to-Caster ueberschreibt einen vorherigen KnockBack).
        /// </summary>
        public void ServerTeleportTo(Vector3 position)
        {
            if (!IsServer)
            {
                return;
            }
            transform.position = position;
            m_ServerPosition.Value = position;
            m_ImpulseSecondsRemaining = 0f;
            m_ImpulseVelocity = Vector3.zero;
        }

        /// <summary>
        /// Server-only: forcierte Bewegung ueber <paramref name="durationSec"/>
        /// Sekunden mit <c>direction.normalized * meters / durationSec</c> m/s.
        /// Waehrend der Dauer pausiert die AI-Bewegung in <see cref="TickServer"/>;
        /// Combat-Logik (Threat, Spell-Cooldowns) laeuft weiter und greift wieder,
        /// sobald die Dauer abgelaufen ist.
        /// </summary>
        public void ServerApplyImpulse(Vector3 direction, float meters, float durationSec)
        {
            if (!IsServer)
            {
                return;
            }
            if (durationSec <= 0f || meters == 0f)
            {
                return;
            }
            Vector3 dir = direction;
            dir.y = 0f;
            float sqr = dir.sqrMagnitude;
            if (sqr < 1e-6f)
            {
                return;
            }
            dir /= Mathf.Sqrt(sqr);
            m_ImpulseVelocity = dir * (meters / durationSec);
            m_ImpulseSecondsRemaining = durationSec;
        }

        // -------------------------------------------------------------------
        // Remote-Tick
        // -------------------------------------------------------------------

        private void TickRemoteClient(float dt)
        {
            Vector3 target = m_ServerPosition.Value;
            Vector3 current = transform.position;
            transform.position = Vector3.SmoothDamp(
                current,
                target,
                ref m_SmoothVelocity,
                m_RemoteSmoothTime);
        }

        // -------------------------------------------------------------------
        // Visuals (alle Peers)
        // -------------------------------------------------------------------

        private void UpdateVisuals()
        {
            if (m_Character == null)
            {
                return;
            }

            if (m_Visuals != null && m_Visuals.IsBusy)
            {
                return;
            }
            if (m_ServerDead)
            {
                return;
            }

            // Direction + Moving werden vom Server replizert. Jeder Peer rendert
            // damit dieselbe Octanten-Wahl — kein Octanten-Flackern durch lokales
            // SmoothDamp-Jitter mehr.
            int dir = m_ServerDirection.Value & 7;
            string anim = m_ServerMoving.Value ? m_AnimRun : m_AnimStance;
            m_Character.SetDirection(dir);
            m_Character.Play(anim);
        }

        /// <summary>
        /// Server-only. Pusht die FLARE-Direction in die NetworkVariable, aber nur,
        /// wenn der Winkel die aktuelle Octanten-Mitte um mehr als 22.5° +
        /// <see cref="m_DirectionHysteresisDeg"/> verlaesst. So flippt die Richtung
        /// nicht bei jedem Wackler an der Octanten-Grenze.
        /// </summary>
        private void UpdateServerDirection(Vector3 facingVec)
        {
            facingVec.y = 0f;
            // <2cm Vektor ⇒ Rauschen, Richtung behalten.
            if (facingVec.sqrMagnitude < 0.0004f)
            {
                return;
            }

            int rawDir = ComputeFlareDirection(facingVec);
            byte currentDir = m_ServerDirection.Value;
            if (rawDir == currentDir)
            {
                return;
            }

            // Hysterese: Octanten-Wechsel nur, wenn der Winkel mindestens
            // (22.5° + Hysterese) ueber die Mitte des aktuellen Octanten hinaus
            // gewandert ist. Octanten-Mitte = ((dir - 4) * 45°), Inverse von
            // ComputeFlareDirection.
            float angleDeg = Mathf.Atan2(-facingVec.z, facingVec.x) * Mathf.Rad2Deg;
            float currentCenterDeg = (currentDir - 4) * 45f;
            float deviation = Mathf.Abs(Mathf.DeltaAngle(angleDeg, currentCenterDeg));
            if (deviation < 22.5f + m_DirectionHysteresisDeg)
            {
                return;
            }

            m_ServerDirection.Value = (byte)rawDir;
        }

        /// <summary>
        /// Bildet einen XZ-Bewegungsvektor auf FLARE's 8-Octant-Direction-Index ab
        /// (0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW). Z wird invertiert, weil
        /// FLARE seine Y-Achse aus der Top-Down-Sicht ableitet (Sued = +Z in Unity).
        /// Achtung: dieser Enum-Wert ist Riftstorm-spezifisch und unterscheidet
        /// sich vom source-server-Enum (S=0..SE=7). Atlas-Layout ist hier
        /// massgebend, NICHT die source-Reihenfolge.
        /// </summary>
        private static int ComputeFlareDirection(Vector3 diff)
        {
            float angleDeg = Mathf.Atan2(-diff.z, diff.x) * Mathf.Rad2Deg;
            int octant = Mathf.RoundToInt(angleDeg / 45f);
            return (octant + 4) & 7;
        }

        // -------------------------------------------------------------------
        // ClientRpcs
        // -------------------------------------------------------------------

        [ClientRpc]
        private void PlaySwingClientRpc(FixedString32Bytes animationName)
        {
            if (m_Visuals == null)
            {
                return;
            }
            if (animationName.Length > 0)
            {
                m_Visuals.PlaySwing(animationName.ToString());
            }
            else
            {
                m_Visuals.PlaySwing();
            }
            TryPlayNpcSound("attack");
        }

        [ClientRpc]
        private void PlayCastClientRpc(int spellEntry)
        {
            if (m_Visuals != null)
            {
                m_Visuals.PlayCast();
            }
            // Caster-Particles + Caster-Sound bei Cast-START auf allen Peers
            // (analog Combat.PlayerCombat.BeginCastClientRpc).
            TryTriggerCasterParticles(spellEntry);
            TryTriggerCasterSound(spellEntry);
        }

        /// <summary>
        /// Server-seitiger Fan-Out des Spell-Visuals nach erfolgreichem
        /// <see cref="SpellExecutor.Execute(ICombatUnit,SpellTemplate,ICombatUnit)"/>.
        /// Berechnet Source-/Target-<c>NetworkObjectId</c> und &#8212; fuer
        /// Skillshots &#8212; einen gerichteten Flug-Endpunkt (Forward-Fallback,
        /// identisch zur Spieler-Logik in
        /// <c>Combat.PlayerCombat.ServerCompleteCast</c>) und ruft das
        /// <see cref="PlaySpellVisualClientRpc"/> auf allen Peers (inkl. Host).
        /// NPCs casten ausschliesslich auf Unit-Ziele (keine Boden-Destination),
        /// daher gibt es keinen stationaeren Ground-AoE-Punkt
        /// (<paramref name="castTarget"/>-Transform traegt das Visual).
        /// </summary>
        /// <param name="spellEntry">Katalog-ID des gecasteten Spells.</param>
        /// <param name="spell">Aufgeloestes Spell-Template (fuer Skillshot/Range).</param>
        /// <param name="castTarget">Aufgeloestes Cast-Ziel (kann der NPC selbst sein).</param>
        private void ServerFanSpellVisual(int spellEntry, SpellTemplate spell, UnitStats castTarget)
        {
            if (!IsServer || spell == null || m_Stats == null)
            {
                return;
            }

            ulong sourceNetId = NetworkObject != null ? NetworkObject.NetworkObjectId : 0UL;
            bool selfCast = ReferenceEquals(castTarget, m_Stats);
            ulong targetNetId = (!selfCast && castTarget != null) ? castTarget.NetworkObjectId : 0UL;

            Vector3 groundPoint = Vector3.zero;
            bool hasGroundPoint = false;
            if (spell.IsSkillshot)
            {
                // Skillshots haben kein Unit-Ziel-Transform fuer das Visual:
                // gerichteter Flug-Endpunkt = Caster-Position + Richtung x Range.
                // Richtung primaer zum Ziel, sonst aktuelle Server-Blickrichtung.
                Vector3 origin = m_Stats.transform.position;
                Vector3 dir = castTarget != null
                    ? castTarget.transform.position - origin
                    : m_ServerFacingVec;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) { dir = m_ServerFacingVec; dir.y = 0f; }
                if (dir.sqrMagnitude < 0.0001f) { dir = Vector3.forward; }
                dir.Normalize();
                float rangeMeters = SpellUtils.RangeToMeters(spell.Range);
                if (rangeMeters <= 0f) { rangeMeters = k_FallbackSkillshotVisualRangeMeters; }
                groundPoint = origin + dir * rangeMeters;
                hasGroundPoint = true;
            }

            PlaySpellVisualClientRpc(spellEntry, sourceNetId, targetNetId, groundPoint, hasGroundPoint, 0);
        }

        /// <summary>
        /// Client-seitiges Rendern des Spell-Visuals (Travel/Impact/Aura/Ground)
        /// fuer einen NPC-Cast. Spiegelt
        /// <c>Combat.PlayerCombat.PlaySpellCastClientRpc</c>: resolved Visual-Kit
        /// + Anim-Katalog, leitet die Travel-Speed aus der Spell-Geschwindigkeit
        /// ab und spawnt &#252;ber <see cref="SpellVisualSpawner"/> entweder ein
        /// gerichtetes Skillshot-Visual (<paramref name="hasGroundPoint"/>) oder
        /// ein ziel-gebundenes Visual. Die Caster-Cast-Pose/-Particles laufen
        /// bereits in <see cref="PlayCastClientRpc"/> bei Cast-START.
        /// </summary>
        [ClientRpc]
        private void PlaySpellVisualClientRpc(int spellEntry, ulong sourceNetId, ulong targetNetId, Vector3 groundPoint, bool hasGroundPoint, int groundDurationMs)
        {
            m_VisualKitMappingLoader ??= ServiceLocator.Get<SpellVisualKitMappingCatalogLoader>();
            m_VisualKitDefinitionLoader ??= ServiceLocator.Get<SpellVisualKitDefinitionCatalogLoader>();
            m_AnimationCatalogLoader ??= ServiceLocator.Get<SpellAnimationCatalogLoader>();
            m_ParticleCatalogLoader ??= ServiceLocator.Get<ParticleSystemCatalogLoader>();

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

            SpellVisualDefinition kit = SpellVisualResolver.Resolve(spellEntry, mappings, defs);
            if (kit == null)
            {
                return;
            }

            // Travel-Speed aus der Spell-Geschwindigkeit ableiten (Source-Pixel/
            // Frame -> m/s), damit Client-Travel und Server-Projektil zeitlich
            // konsistent laufen. Fallback auf die Resolver-Konstante.
            SpellTemplate castSpell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (castSpell != null && castSpell.Speed > 0f)
            {
                float travelMps = SpellUtils.ProjectileSpeedToMps(castSpell.Speed);
                if (travelMps > 0f) { kit.TravelSpeed = travelMps; }
            }

            ParticleSystemCatalog particles = m_ParticleCatalogLoader?.GetCached();

            Transform targetTransform = targetNetId != 0UL && targetNetId != sourceNetId
                ? ResolveNetworkTransform(targetNetId)
                : null;

            if (castSpell != null && castSpell.IsSkillshot && hasGroundPoint)
            {
                SpellVisualSpawner.SpawnDirectional(kit, anims, sourceTransform, groundPoint, particles);
            }
            else
            {
                SpellVisualSpawner.Spawn(kit, anims, sourceTransform, targetTransform, particles);
            }

            if (hasGroundPoint && (castSpell == null || !castSpell.IsSkillshot) && kit.Ground.HasAny)
            {
                float lifetime = groundDurationMs > 0 ? groundDurationMs * 0.001f : 0f;
                SpellVisualSpawner.SpawnGround(kit.Ground, anims, groundPoint, lifetime, particles);
            }
        }

        /// <summary>
        /// Loest auf diesem Client das Caster-Partikelsystem des Spells aus.
        /// Resolved <c>spellEntry</c> &#8594; <see cref="SpellVisualKitMapping.CastingKit"/>
        /// &#8594; <see cref="SpellVisualKitDefinition.Psystem"/> &#8594;
        /// <see cref="ParticleSystemCatalog"/> und spawnt das System ueber
        /// <see cref="CasterParticleSpawner"/> am NPC. Stilles No-Op, falls
        /// Mapping/Kit/PSystem-Name fehlt. Spiegelt
        /// <c>Combat.PlayerCombat.TryTriggerCasterParticles</c>.
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
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            m_ActiveCasterParticles = CasterParticleSpawner.Spawn(def, transform, worldYOffset: 0f);
        }

        /// <summary>
        /// Loest auf diesem Client den Caster-Sound des Spells aus. Resolved
        /// <c>spellEntry</c> &#8594; <see cref="SpellVisualKitMapping.CastingKit"/>
        /// &#8594; <see cref="SpellVisualKitDefinition.Sound"/> &#8594;
        /// <see cref="SoundManager.GetClip"/> und spielt einen 3D-One-Shot am NPC.
        /// Stilles No-Op falls Mapping/Kit/Sound-Name fehlt. Spiegelt
        /// <c>Combat.PlayerCombat.TryTriggerCasterSound</c>.
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
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }

        /// <summary>
        /// Loest eine gespawnte <see cref="NetworkObject"/>-Id zu ihrem Transform
        /// auf, oder <c>null</c> wenn es auf diesem Client nicht (mehr) existiert.
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

        /// <summary>
        /// Startet die Overhead-Cast-Bar auf allen Clients (inkl. Host) f&#252;r
        /// einen Cast-Time-Spell. Der Fortschritt wird client-seitig lokal aus
        /// der &#252;bergebenen Dauer interpoliert &#8212; es flie&#223;t kein
        /// laufender Netzwerk-Traffic.
        /// </summary>
        /// <param name="spellId">Katalog-ID des gecasteten Spells.</param>
        /// <param name="durationSeconds">Gesamte Cast-Zeit in Sekunden.</param>
        [ClientRpc]
        private void ShowCastBarClientRpc(int spellId, float durationSeconds)
        {
            m_LocalCastActive = true;
            m_LocalCastSpellId = spellId;
            m_LocalCastStartUnscaled = Time.unscaledTime;
            m_LocalCastDurationSeconds = Mathf.Max(0.01f, durationSeconds);
            EnsureCastBarView().Begin(spellId, durationSeconds);
            LocalCastStarted?.Invoke(spellId, m_LocalCastDurationSeconds);
        }

        /// <summary>
        /// Blendet die Overhead-Cast-Bar auf allen Clients aus (Abschluss,
        /// Abbruch oder Interrupt).
        /// </summary>
        [ClientRpc]
        private void HideCastBarClientRpc()
        {
            m_LocalCastActive = false;
            m_LocalCastSpellId = 0;
            if (m_CastBarView != null)
            {
                m_CastBarView.End();
            }
            // Endlose Caster-PSystems (lifetime = -1) beim Cast-Ende stoppen,
            // damit sie nicht bis zum 8s-Cap weiterglitzern.
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            LocalCastEnded?.Invoke();
        }

        /// <summary>
        /// Liefert die client-seitige <see cref="NpcCastBarView"/> und erg&#228;nzt
        /// sie bei Bedarf zur Laufzeit (kein Prefab-Eintrag n&#246;tig).
        /// </summary>
        private NpcCastBarView EnsureCastBarView()
        {
            if (m_CastBarView == null)
            {
                m_CastBarView = gameObject.GetComponent<NpcCastBarView>();
                if (m_CastBarView == null)
                {
                    m_CastBarView = gameObject.AddComponent<NpcCastBarView>();
                }
            }
            return m_CastBarView;
        }

        [ClientRpc]
        private void PlayDieClientRpc()
        {
            if (m_Visuals != null)
            {
                m_Visuals.PlayDie();
            }
            // Laufende Caster-Particles beim Tod sauber stoppen.
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            TryPlayNpcSound("die");
        }

        [ClientRpc]
        private void PlayAggroClientRpc()
        {
            TryPlayNpcSound("aggro");
        }

        [ClientRpc]
        private void SyncLocalThreatClientRpc(int threatValue, ClientRpcParams _ = default)
        {
            if (m_LocalObservedThreat == threatValue)
            {
                return;
            }

            m_LocalObservedThreat = threatValue;
            try
            {
                LocalObservedThreatChanged?.Invoke(threatValue);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        [ClientRpc]
        private void PlayRespawnClientRpc()
        {
            if (m_Visuals != null)
            {
                m_Visuals.ResetForRespawn();
            }
        }

        // -------------------------------------------------------------------
        // Hit-Reaktion
        // -------------------------------------------------------------------

        private void HandleClientDamageReceived(int amount, HitResult result)
        {
            if (m_Visuals == null)
            {
                return;
            }
            if (result == HitResult.Miss || result == HitResult.Dodge)
            {
                return;
            }
            m_Visuals.PlayHit();
            TryPlayNpcSound("damage");
        }

        /// <summary>
        /// Spielt – falls vorhanden – einen zufaelligen Combat-Sound fuer das
        /// gebundene Model (<see cref="m_ModelName"/>) und das uebergebene
        /// <paramref name="evt"/> (attack/damage/die/aggro) raeumlich an der
        /// NPC-Position ab. Stiller No-op, wenn kein Model gebunden ist, der
        /// <see cref="SoundManager"/> fehlt, kein Eintrag existiert oder der
        /// Clip nicht geladen werden kann. Client-seitige Presentation – wird
        /// ausschliesslich aus ClientRpcs / Client-Hooks gerufen.
        /// </summary>
        /// <param name="evt">Event-Schluessel aus <c>_sounds.json</c>.</param>
        private void TryPlayNpcSound(string evt)
        {
            if (string.IsNullOrEmpty(m_ModelName))
            {
                return;
            }
            if (!NpcSoundCatalogLoader.TryGetRandomSound(m_ModelName, evt, out string soundFile))
            {
                return;
            }

            m_SoundManager ??= ServiceLocator.Get<SoundManager>();
            if (m_SoundManager == null)
            {
                return;
            }

            AudioClip clip = m_SoundManager.GetClip(soundFile);
            if (clip == null)
            {
                return;
            }
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }

        // -------------------------------------------------------------------
        // Read-only Properties (Debug / Gizmos)
        // -------------------------------------------------------------------

        /// <summary>Konfigurierter Aggro-Radius in Metern. Read-only fuer Debug-Visuals.</summary>
        public float AggroRange => m_AggroRange;

        /// <summary>Konfigurierte Melee-Reichweite in Metern.</summary>
        public float MeleeRange => m_MeleeRange;

        /// <summary>Konfigurierte Leash-Range in Metern.</summary>
        public float LeashRange => m_LeashRange;

        /// <summary><c>true</c>, wenn der Server-Tick ein aktives Target haelt.</summary>
        public bool HasServerTarget => m_CurrentTarget != null;

        /// <summary>Aktueller AI-State (server-only valid).</summary>
        public NpcAIState State => m_State;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!m_ShowGizmos)
            {
                return;
            }
            Vector3 c = transform.position;

            Gizmos.color = new Color(0f, 1f, 0f, 0.9f);
            Gizmos.DrawWireSphere(c, m_AggroRange);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(c, m_LeashRange);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 c = transform.position;
            float self = m_Stats != null ? m_Stats.HitRadius : 0f;
            float tgt = m_CurrentTarget != null ? m_CurrentTarget.HitRadius : 0f;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(c, m_MeleeRange + self + tgt);

            if (Application.isPlaying && IsServer && m_CurrentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(c, m_CurrentTarget.transform.position);
            }
        }
#endif
    }

    /// <summary>
    /// Server-AI-State-Machine, 1:1 Port der Enum aus
    /// <c>Server/src/AI/NpcAI.h</c>.
    /// </summary>
    public enum NpcAIState
    {
        Idle = 0,
        Combat = 1,
        Evading = 2,
        Dead = 3,
    }
}
