using System;
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
    public sealed partial class NpcController : NetworkBehaviour
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

        // Server-only: einmaliger Callback fuer das Impuls-Ende (Impact/Landung),
        // vom <see cref="SpellExecutor"/> registriert (Charge-Stun bei Ankunft).
        private Action m_OnImpulseComplete;

        /// <summary>True, solange eine externe Bewegung (Impuls) laeuft.</summary>
        public bool IsImpulseActive => m_ImpulseSecondsRemaining > 0f;
        // Server-only Fear-Fluchtrichtung (normalisiert). Wird pro Tick aus der
        // Position des Fear-Verursachers aktualisiert und bleibt als Fallback
        // erhalten, falls der Caster despawnt &#8212; damit der gefuerchtete NPC
        // nicht auf der Stelle zappelt, sondern seine Fluchtrichtung haelt.
        private Vector3 m_FleeDirection;

        // Vorausblick-Distanz (Meter) fuer den Fear-Fluchtpunkt: gross genug,
        // dass MoveTowardsPoint jeden Tick einen vollen Schritt liefert.
        private const float k_FleeLookaheadMeters = 20f;

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
                    // Impact-Callback (server-only, einmalig): Charge-Debuffs
                    // greifen jetzt bei der Landung mit voller Dauer.
                    if (m_OnImpulseComplete != null)
                    {
                        Action onComplete = m_OnImpulseComplete;
                        m_OnImpulseComplete = null;
                        onComplete.Invoke();
                    }
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

            // Fear hat Vorrang vor der regulaeren State-Machine: ein gefuerchteter
            // NPC flieht vom Verursacher, statt zu attackieren/casten. Der
            // Combat-/Threat-State bleibt erhalten, sodass der Kampf nach Ablauf
            // des Fears normal weiterlaeuft.
            if (m_Stats.IsFeared)
            {
                UpdateFeared(dt);
            }
            else
            {
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
