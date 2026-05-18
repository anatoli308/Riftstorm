using Riftstorm.Game.Combat;
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
            // leash_range aus Template uebernehmen, wenn vom JSON gesetzt (>0).
            // Source: NpcAI verwendet getLeashRange() pro NPC, Default 50.
            if (tpl.LeashRange > 0f)
            {
                m_LeashRange = tpl.LeashRange;
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
                }
            }
            base.OnNetworkDespawn();
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
            bool moving = posDelta.sqrMagnitude > k_MoveEpsilonSqr;
            if (m_ServerMoving.Value != moving)
            {
                m_ServerMoving.Value = moving;
            }

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
        /// Retaliation in <see cref="HandleClientDamageReceived"/> auf Combat
        /// geschaltet werden (TODO: Attacker-Identitaet im Damage-Event).
        /// </summary>
        private void UpdateIdle(float dt)
        {
            if (!IsHostileFaction(m_Faction))
            {
                return;
            }

            UnitStats target = FindAggroTarget();
            if (target != null)
            {
                m_CurrentTarget = target;
                m_State = NpcAIState.Combat;
            }
        }

        /// <summary>
        /// Port von <c>NpcAI::updateCombat</c>: Leash-Check, in-Range =&gt; Attack,
        /// sonst auf Target zulaufen. Target-Verlust (tot / despawned) =&gt; Idle.
        /// </summary>
        private void UpdateCombat(float dt)
        {
            if (!IsValidTarget(m_CurrentTarget))
            {
                m_CurrentTarget = null;
                m_State = NpcAIState.Idle;
                return;
            }

            if (ShouldLeash())
            {
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
                m_State = NpcAIState.Idle;
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
            target.ApplyDamage(in info);

            PlaySwingClientRpc(default);
        }

        // -------------------------------------------------------------------
        // Death
        // -------------------------------------------------------------------

        private void HandleServerDied()
        {
            m_ServerDead = true;
            m_CurrentTarget = null;
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
            m_Character.SetDirection(dir);
            m_Character.Play(m_ServerMoving.Value ? m_AnimRun : m_AnimStance);
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
