using Riftstorm.Game.Combat;
using Riftstorm.Game.Spells;
using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
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
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnitStats))]
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

        // -------------------------------------------------------------------
        // Server-only Felder
        // -------------------------------------------------------------------

        private NpcAIState m_State = NpcAIState.Idle;
        private UnitStats m_CurrentTarget;
        private float m_LastAttackTime = -999f;
        private bool m_ServerDead;

        // Server-only Threat-Tabelle. Ersetzt das alte "closest hostile"-Picking
        // in UpdateCombat und ist die Quelle der Retaliation-Logik fuer
        // Neutral/Friendly. Wird in OnNetworkSpawn (Server) initialisiert und
        // ueber OnServerDamaged von UnitStats gespeist. Source-Pendant:
        // Server/src/AI/ThreatManager.h.
        private readonly ThreatManager m_Threat = new();

        // Runtime-Daten fuer die vier Template-Spell-Slots.
        private readonly NpcSpellSlotRuntime[] m_SpellSlots = new NpcSpellSlotRuntime[4];
        private int m_ActiveSpellSlotCount;

        // Template-Daten via BindTemplate (kein Mugen, kein ScriptableObject).
        private int m_Faction;
        private int m_WeaponValue = 10;
        private float m_MeleeCooldownSec = 2.0f;

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
            // move_speed wird NICHT hier gesetzt: UnitStats.WalkSpeed ist read-only und
            // ApplyBaseStats ist nach OnNetworkSpawn gesperrt. Der Override laeuft im
            // FlareNpcSpawner.ApplyStatsToUnitStats VOR dem Netcode-Spawn.
        }

        /// <summary>
        /// Uebernimmt die vier flachen SQL-Spell-Slots aus dem Template in
        /// einen kompakten Runtime-Array-Cache. JSON-Werte sind Millisekunden;
        /// im Tick arbeiten wir in Sekunden (Time.time).
        /// </summary>
        private void ConfigureSpellSlots(NpcTemplate tpl)
        {
            m_ActiveSpellSlotCount = 0;
            for (int i = 0; i < m_SpellSlots.Length; i++)
            {
                (int id, float chance, float interval, float cooldown) = tpl.GetSpellSlot(i + 1);
                if (id > 0 && !SpellCatalogLoader.TryGetTemplate(id, out _))
                {
                    Debug.LogWarning($"[NpcController] Unbekannter Spell {id} in Slot {i + 1} von NPC '{tpl.Name}' ({tpl.Entry}).");
                    id = 0;
                }
                m_SpellSlots[i] = new NpcSpellSlotRuntime
                {
                    SpellId = id > 0 ? id : 0,
                    ChancePct = Mathf.Clamp(chance, 0f, 100f),
                    IntervalSec = interval > 0f ? interval / 1000f : 0f,
                    CooldownSec = cooldown > 0f ? cooldown / 1000f : 0f,
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
                }
            }
        }

        /// <inheritdoc/>
        public override void OnNetworkDespawn()
        {
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
        /// Setzt nur Runtime-Timestamps zurueck (keine Template-Daten).
        /// Wird bei Despawn/Death/Evade-Reset genutzt, damit NPCs nach einem
        /// harten Reset nicht mit stale Cooldown-Zeiten weiterlaufen.
        /// </summary>
        private void ResetSpellRuntimeTimers()
        {
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
                m_CurrentTarget = target;
                m_State = NpcAIState.Combat;
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
                m_Threat.Clear();
                m_CurrentTarget = null;
                m_State = NpcAIState.Evading;
                return;
            }

            if (IsInMeleeRange(m_CurrentTarget))
            {
                TryMeleeAttack(m_CurrentTarget);
            }
            else
            {
                MoveTowardsEntity(m_CurrentTarget, dt, m_Stats.WalkSpeed);
            }

            int slotIndex = SelectSpellSlotToCast(m_CurrentTarget);
            if (slotIndex >= 0)
            {
                TryPerformSpellCast(slotIndex, m_CurrentTarget);
            }
        }

        /// <summary>
        /// Port von <c>NpcAI::updateEvading</c>: mit doppelter Speed zum
        /// <see cref="m_HomePosition"/>, bei Ankunft Full-HP-Reset und zurueck
        /// in den Idle-State. Auren-Clear ist hier (noch) nicht implementiert.
        /// </summary>
        private void UpdateEvading(float dt)
        {
            float speed = m_Stats.WalkSpeed * m_EvadeSpeedMultiplier;
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
        /// Port von <c>NpcAI::selectSpellToCast</c>. Iteriert die vier
        /// Template-Slots, prueft Intervall/Cooldown/Chance und liefert den
        /// ersten castbaren Slot zurueck. -1 = kein Cast in diesem Tick.
        /// </summary>
        private int SelectSpellSlotToCast(UnitStats target)
        {
            if (target == null || m_Stats == null)
            {
                return -1;
            }
            if (m_ActiveSpellSlotCount <= 0)
            {
                return -1;
            }

            float now = Time.time;
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

            return -1;
        }

        /// <summary>
        /// Port von <c>NpcAI::canCastSpell</c>. Validiert Spell-Existenz plus
        /// komplette Castbarkeit gegen den aktuellen Target-Kontext.
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
            return SpellCaster.Validate(m_Stats, spell, target) == CastResult.Success;
        }

        /// <summary>
        /// Port von <c>NpcAI::performSpellCast</c>: instant Execute ueber die
        /// vorhandene Spell-Pipeline, danach Slot-Cooldown setzen und Cast-Anim
        /// replizieren.
        /// </summary>
        private void TryPerformSpellCast(int slotIndex, UnitStats target)
        {
            if (slotIndex < 0 || slotIndex >= m_SpellSlots.Length || target == null || m_Stats == null)
            {
                return;
            }

            NpcSpellSlotRuntime slot = m_SpellSlots[slotIndex];
            if (!SpellCatalogLoader.TryGetTemplate(slot.SpellId, out SpellTemplate spell) || spell == null)
            {
                return;
            }

            SpellExecutionResult result = SpellExecutor.Execute(m_Stats, spell, target);
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

            PlayCastClientRpc();
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
            return faction == 3;
        }

        // -------------------------------------------------------------------
        // Attack
        // -------------------------------------------------------------------

        /// <summary>
        /// Port von <c>NpcAI::performMeleeAttack</c>: Cooldown ueber
        /// <see cref="m_MeleeCooldownSec"/>, Damage-Roll via
        /// <see cref="CombatFormulas.CalculateMeleeDamage"/> mit einer
        /// transienten <see cref="WeaponDefinition"/> aus
        /// <c>template.weapon_value</c>.
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
            m_LastAttackTime = now;

            WeaponDefinition weapon = new()
            {
                BaseDamage = m_WeaponValue,
                Range = m_MeleeRange,
                AttackCooldown = m_MeleeCooldownSec,
            };

            DamageInfo info = CombatFormulas.CalculateMeleeDamage(m_Stats, target, weapon);
            // Attacker durchreichen, damit der Schaden Threat auf dem Target
            // erzeugt (relevant fuer Player-Pets / Npc-vs-Npc; aktuell ohne
            // Effekt fuer reine Player-Targets, aber API-konsistent).
            target.ApplyDamage(m_Stats, in info);
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
            m_Threat.Clear();
            ResetSpellRuntimeTimers();
            m_State = NpcAIState.Dead;
            PlayDieClientRpc();
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
        }

        [ClientRpc]
        private void PlayCastClientRpc()
        {
            if (m_Visuals != null)
            {
                m_Visuals.PlayCast();
            }
        }

        [ClientRpc]
        private void PlayDieClientRpc()
        {
            if (m_Visuals != null)
            {
                m_Visuals.PlayDie();
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
