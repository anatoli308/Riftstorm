using System;
using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Server-autoritative AI-Steuerung fuer NPC-Einheiten. Verantwortlich fuer
    /// Target-Akquise (Aggro), Bewegung in Reichweite, Attacken mit Cooldown und
    /// Reaktion auf Death-Events. Jeder Client repliziert die Position via
    /// <see cref="m_ServerPosition"/> und glaettet sie per <c>SmoothDamp</c>.
    ///
    /// <para>
    /// Bewusst <b>kein Coroutine/Timer-Polling</b>: Aggro-Scan und Cooldown laufen
    /// am Server-Tick in <c>Update</c>, weil Riftstorm fixed-rate-server-driven
    /// ist. Animationen werden NICHT serialisiert &#8212; einmalige Aktionen
    /// (Swing, Hit, Die) verteilt der Server per <c>ClientRpc</c>, idle/run
    /// leiten die Clients lokal aus dem Bewegungs-Delta ab.
    /// </para>
    ///
    /// <para>
    /// Erwarteter Component-Stack auf demselben GameObject:
    /// <list type="bullet">
    ///   <item><see cref="UnityEngine.Collider"/> &#8212; HitRadius-passender Collider, damit
    ///   andere NPCs/Spieler diesen NPC per <c>OverlapSphere</c> als Target finden koennen.</item>
    ///   <item><see cref="NetworkObject"/> &#8212; Netcode-Identity.</item>
    ///   <item><see cref="UnitStats"/> &#8212; Single Source of Truth fuer HP/Speed/Range/etc.</item>
    ///   <item><see cref="NpcIdentity"/> &#8212; <c>INameSource</c>-Bruecke fuer Nametag.</item>
    ///   <item><see cref="UnitCombatVisuals"/> &#8212; State-Machine fuer Swing/Hit/Die.</item>
    ///   <item><see cref="FlareCharacter"/> &#8212; Sprite-Direction + Animation-Playback.</item>
    /// </list>
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnitStats))]
    public sealed class NpcController : NetworkBehaviour
    {
        [Header("Referenzen (auto-resolved)")]
        [SerializeField] private UnitStats m_Stats;
        [SerializeField] private UnitCombatVisuals m_Visuals;
        [SerializeField] private FlareCharacter m_Character;
        [SerializeField] private MugenHitboxRuntime m_HitboxRuntime;

        [Header("Aggro & Targeting")]
        [Tooltip("Radius in Metern, innerhalb dessen der NPC ein Target sucht.")]
        [SerializeField] private float m_AggroRadius = 8f;

        [Tooltip("Radius in Metern, ab dem ein bereits akquiriertes Target verloren wird.")]
        [SerializeField] private float m_DeaggroRadius = 12f;

        [Tooltip("LayerMask, auf der nach Targets gesucht wird. Wenn 0/leer, wird auf allen Layern gesucht.")]
        [SerializeField] private LayerMask m_TargetLayerMask = ~0;

        [Header("Combat")]
        [Tooltip("Sekunden zwischen zwei Attacken.")]
        [SerializeField] private float m_AttackCooldown = 1.0f;

        [Tooltip("Roher Schaden pro Treffer. Spaeter durch Stats/Formel ersetzbar.")]
        [SerializeField] private int m_AttackDamage = 5;

        [Header("Replication")]
        [Tooltip("SmoothDamp-Zeitkonstante fuer die Positionsglaettung auf Remote-Clients.")]
        [SerializeField] private float m_RemoteSmoothTime = 0.1f;

        [Header("Animationen")]
        [SerializeField] private string m_AnimStance = "stance";
        [SerializeField] private string m_AnimRun = "run";

        [Header("Debug")]
        [Tooltip("Wenn aktiv, beruecksichtigt der Aggro-Scan auch Trigger-Collider.")]
        [SerializeField] private bool m_IncludeTriggers = true;

        [Tooltip("Zeigt Aggro-/Deaggro-/Attack-Range im Scene-View auch ohne Selektion.")]
        [SerializeField] private bool m_ShowGizmos = true;

        /// <summary>Server schreibt, jeder liest. Client-Glaettung haengt sich daran an.</summary>
        private readonly NetworkVariable<Vector3> m_ServerPosition =
            new(writePerm: NetworkVariableWritePermission.Server);

        // Server-only Felder
        private UnitStats m_CurrentTarget;
        private float m_LastAttackTime = -999f;
        private bool m_ServerDead;

        // Buffer fuer Aggro-Scan &#8212; bewusst statisch dimensioniert, kein Heap pro Frame.
        private static readonly Collider[] s_OverlapBuffer = new Collider[32];

        // Dedup-Buffer für MUGEN-Multi-Box-AoE pro Tick. Reicht für die paar Targets,
        // die ein einzelner Attack-Frame realistisch erwischen kann.
        private static readonly UnitStats[] s_HitDedupe = new UnitStats[16];

        // Visual-Tracking (alle Peers)
        private Vector3 m_PrevVisualPosition;
        private Vector3 m_SmoothVelocity;
        private bool m_VisualsInitialized;

        // MUGEN-Skill-Pool fuer Auto-Attacks (server-only, einmalig befuellt).
        private MugenCharacterStats m_MugenStats;
        private MugenSkillData[] m_BasicAttackPool = Array.Empty<MugenSkillData>();

        private void Awake()
        {
            if (m_Stats == null) m_Stats = GetComponent<UnitStats>();
            if (m_Visuals == null) m_Visuals = GetComponent<UnitCombatVisuals>();
            if (m_Character == null) m_Character = GetComponentInChildren<FlareCharacter>(includeInactive: true);
            if (m_HitboxRuntime == null) m_HitboxRuntime = GetComponent<MugenHitboxRuntime>();
        }

        /// <summary>
        /// Wird von <see cref="MugenNpcSpawner"/> aufgerufen, sobald der FLARE-Charakter
        /// asynchron aufgebaut ist. Vorher liefert <c>GetComponentInChildren</c> im
        /// <see cref="Awake"/> noch <c>null</c> &#8212; analog zu <see cref="Riftstorm.Game.Movement.PlayerMovement.BindVisuals"/>.
        /// </summary>
        public void BindCharacter(FlareCharacter character)
        {
            m_Character = character;
            if (m_Visuals != null)
            {
                m_Visuals.BindCharacter(character);
            }
            // MugenNpcSpawner hängt den Hitbox-Runtime erst nach dem async Atlas-Load an,
            // also hier erneut auflösen falls Awake zu früh war.
            if (m_HitboxRuntime == null)
            {
                m_HitboxRuntime = GetComponent<MugenHitboxRuntime>();
            }
            // Bewegungs-Delta neu kalibrieren, sonst feuert der erste Frame ggf. einen
            // Run-Sprung aus 0,0,0 zur aktuellen Position.
            m_PrevVisualPosition = transform.position;
            m_VisualsInitialized = true;
        }

        /// <summary>
        /// Bindet den geladenen MUGEN-Stat-Block (mit <c>Skills</c>-Array) an den
        /// Controller. Wird vom <see cref="MugenNpcSpawner"/> nach erfolgreichem
        /// JSON-Load aufgerufen. Baut den Auto-Attack-Pool einmalig aus den
        /// Stand-Normal-Attack-Skills auf; ist <paramref name="stats"/> oder das
        /// Skills-Array leer, faellt <see cref="TryAttack"/> auf den Skalar-
        /// <see cref="m_AttackDamage"/> zurueck.
        /// </summary>
        public void BindMugenStats(MugenCharacterStats stats)
        {
            m_MugenStats = stats;
            m_BasicAttackPool = stats != null
                ? stats.GetBasicAttackPool()
                : Array.Empty<MugenSkillData>();
        }

        /// <summary>Konfigurierter Aggro-Radius in Metern. Read-only fuer Debug-Visuals.</summary>
        public float AggroRadius => m_AggroRadius;

        /// <summary>Konfigurierter Deaggro-Radius in Metern. Read-only fuer Debug-Visuals.</summary>
        public float DeaggroRadius => m_DeaggroRadius;

        /// <summary>
        /// Effektive Attack-Reichweite des NPCs zum eigenen Hit-Radius. Target-spezifischer
        /// Anteil (Target-HitRadius) wird zur Laufzeit dynamisch addiert und ist deshalb
        /// nicht Teil dieser Property. Liefert <c>0</c>, solange noch keine Stats geladen sind.
        /// </summary>
        public float AttackRangeWithSelfHitRadius =>
            m_Stats != null ? m_Stats.AttackRange + m_Stats.HitRadius : 0f;

        /// <summary><c>true</c>, wenn der Server-Tick ein aktives Target haelt.</summary>
        public bool HasServerTarget => m_CurrentTarget != null;

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (m_Stats != null)
            {
                // Hit-Reaktion laeuft auf jedem Peer (Server-Host inklusive),
                // weil ClientDamageReceived sowohl client- als auch server-seitig
                // gefeuert wird, sobald ApplyDamage durchschlaegt.
                m_Stats.ClientDamageReceived += HandleClientDamageReceived;

                if (IsServer)
                {
                    m_Stats.OnServerDied += HandleServerDied;
                    m_ServerPosition.Value = transform.position;
                }
            }

            m_PrevVisualPosition = transform.position;
            m_VisualsInitialized = true;
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

        // ----- Server-Tick ---------------------------------------------------

        private void TickServer(float dt)
        {
            if (m_ServerDead || m_Stats == null || m_Stats.IsDead)
            {
                return;
            }

            UpdateTargetAcquisition();

            if (m_CurrentTarget != null)
            {
                Vector3 selfPos = transform.position;
                Vector3 targetPos = m_CurrentTarget.transform.position;
                Vector3 diff = targetPos - selfPos;
                diff.y = 0f;
                float dist = diff.magnitude;

                float effectiveRange = m_Stats.AttackRange + m_CurrentTarget.HitRadius + m_Stats.HitRadius;

                if (dist <= effectiveRange)
                {
                    // In Reichweite &#8212; nicht bewegen, evtl. attackieren.
                    TryAttack(m_CurrentTarget);
                }
                else
                {
                    // Auf Target zulaufen.
                    Vector3 step = diff.normalized * (m_Stats.WalkSpeed * dt);
                    transform.position = selfPos + step;
                }
            }

            m_ServerPosition.Value = transform.position;
        }

        private void UpdateTargetAcquisition()
        {
            // Deaggro pruefen, bevor neu gesucht wird.
            if (m_CurrentTarget != null)
            {
                if (m_CurrentTarget.IsDead)
                {
                    m_CurrentTarget = null;
                }
                else
                {
                    float distSqr = (m_CurrentTarget.transform.position - transform.position).sqrMagnitude;
                    if (distSqr > m_DeaggroRadius * m_DeaggroRadius)
                    {
                        m_CurrentTarget = null;
                    }
                }
            }

            if (m_CurrentTarget != null)
            {
                return;
            }

            QueryTriggerInteraction triggerMode = m_IncludeTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            int hits = Physics.OverlapSphereNonAlloc(
                transform.position,
                m_AggroRadius,
                s_OverlapBuffer,
                m_TargetLayerMask,
                triggerMode);

            UnitStats closest = null;
            float closestSqr = float.PositiveInfinity;
            Vector3 myPos = transform.position;

            for (int i = 0; i < hits; i++)
            {
                Collider col = s_OverlapBuffer[i];
                if (col == null)
                {
                    continue;
                }
                s_OverlapBuffer[i] = null;

                UnitStats stats = col.GetComponentInParent<UnitStats>();
                if (stats == null || stats == m_Stats || stats.IsDead)
                {
                    continue;
                }
                // Nur Targets ausserhalb der eigenen NPC-Familie. Reicht fuer
                // Phase B aus, weil aktuell nur Spieler und neutrale NPCs existieren.
                // Spaeter durch Faction-Filter ersetzbar.
                if (stats.GetComponent<NpcController>() != null)
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

            m_CurrentTarget = closest;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Zeichnet Aggro- und Deaggro-Radius im Scene-View. Wenn das Objekt
        /// selektiert ist, zusaetzlich die effektive Attack-Range (gelb) und eine
        /// Linie zum aktuellen Target (rot, server-only). Steuerbar ueber
        /// <see cref="m_ShowGizmos"/>.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!m_ShowGizmos)
            {
                return;
            }
            Vector3 c = transform.position;

            Gizmos.color = new Color(0f, 1f, 0f, 0.9f);
            Gizmos.DrawWireSphere(c, m_AggroRadius);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);
            Gizmos.DrawWireSphere(c, m_DeaggroRadius);
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || !IsServer)
            {
                return;
            }
            Vector3 c = transform.position;

            if (m_Stats != null)
            {
                float effRange = m_Stats.AttackRange + m_Stats.HitRadius +
                    (m_CurrentTarget != null ? m_CurrentTarget.HitRadius : 0f);
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(c, effRange);
            }

            if (m_CurrentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(c, m_CurrentTarget.transform.position);
            }
        }
#endif

        private void TryAttack(UnitStats target)
        {
            float now = Time.time;
            if (now - m_LastAttackTime < m_AttackCooldown)
            {
                return;
            }
            if (m_Visuals != null && m_Visuals.IsBusy)
            {
                // Aktuell laufende Aktion (z. B. Hit-Stagger) nicht ueberschreiben.
                return;
            }

            m_LastAttackTime = now;

            // B.5.0: Random-Pick aus dem MUGEN Stand-Normal-Attack-Pool. Faellt
            // auf den Inspector-Skalar <see cref="m_AttackDamage"/> zurueck, wenn
            // BindMugenStats nicht gelaufen ist oder der Pool leer ist.
            MugenSkillData picked = null;
            if (m_BasicAttackPool != null && m_BasicAttackPool.Length > 0)
            {
                picked = m_BasicAttackPool[UnityEngine.Random.Range(0, m_BasicAttackPool.Length)];
            }

            int damage = picked != null && picked.Damage > 0 ? picked.Damage : m_AttackDamage;
            DamageInfo info = new()
            {
                BaseDamage = damage,
                FinalDamage = damage,
                Absorbed = 0,
                HitResult = HitResult.Hit,
                Overkill = 0,
                KilledTarget = false,
            };

            // Animationsnamen fuer den Visual-RPC. FixedString32Bytes bleibt
            // alloc-frei in der Replikation; Empty -> Client spielt Default-Swing.
            FixedString32Bytes animName = default;
            if (picked != null && !string.IsNullOrEmpty(picked.AnimAlias))
            {
                animName = picked.AnimAlias;
            }

            // Pfad A: MUGEN-Hitbox-Volumen vorhanden -> Multi-Target AoE via Physics.OverlapBox
            // pro Frame. Führt zur server-autoritativen Pixel-genauen MUGEN-Logik.
            int volumeHits = TryApplyMugenAttackVolumes(in info);
            if (volumeHits >= 0)
            {
                PlaySwingClientRpc(animName);
                return;
            }

            // Pfad B (Fallback): Skalar gegen das aktuelle Aggro-Target.
            target.ApplyDamage(in info);
            PlaySwingClientRpc(animName);
        }

        /// <summary>
        /// Versucht, den aktuellen MUGEN-Frame zu lesen und alle dort definierten
        /// Clsn1-Volumen via <see cref="Physics.OverlapBoxNonAlloc(Vector3, Vector3, Collider[], Quaternion, int, QueryTriggerInteraction)"/>
        /// zu testen. Liefert die Anzahl getroffener Einheiten oder <c>-1</c>, wenn der
        /// aktuelle Frame keine Attack-Boxen hat (dann nimmt der Caller den Skalar-Fallback).
        /// </summary>
        /// <remarks>
        /// Self-Hit und Faction-Filter wie im Aggro-Scan: andere NPCs werden ignoriert,
        /// bis das Faction-System steht. Doppel-Hits aus mehreren Boxen pro Frame werden
        /// über linearen Scan deduped (kleine n &lt;= s_OverlapBuffer.Length).
        /// </remarks>
        private int TryApplyMugenAttackVolumes(in DamageInfo info)
        {
            if (m_HitboxRuntime == null || !m_HitboxRuntime.RefreshFromCurrentFrame())
            {
                return -1;
            }
            IReadOnlyList<MugenWorldBox> boxes = m_HitboxRuntime.AttackBoxes;
            if (boxes == null || boxes.Count == 0)
            {
                return -1;
            }

            QueryTriggerInteraction triggerMode = m_IncludeTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            // Kleine Heap-freie Dedup-Liste über den existierenden Static-Buffer hinaus.
            // s_HitDedupe ist auf 16 Targets dimensioniert &#8212; genügt für MUGEN-Multi-Hit.
            int unique = 0;
            for (int b = 0; b < boxes.Count; b++)
            {
                MugenWorldBox box = boxes[b];
                int hits = Physics.OverlapBoxNonAlloc(
                    box.Center,
                    box.HalfExtents,
                    s_OverlapBuffer,
                    box.Rotation,
                    m_TargetLayerMask,
                    triggerMode);
                for (int i = 0; i < hits; i++)
                {
                    Collider col = s_OverlapBuffer[i];
                    s_OverlapBuffer[i] = null;
                    if (col == null)
                    {
                        continue;
                    }
                    UnitStats stats = col.GetComponentInParent<UnitStats>();
                    if (stats == null || stats == m_Stats || stats.IsDead)
                    {
                        continue;
                    }
                    if (stats.GetComponent<NpcController>() != null)
                    {
                        // Vorläufiger Friendly-Fire-Filter: keine NPC-NPC-Treffer.
                        continue;
                    }
                    bool dup = false;
                    for (int u = 0; u < unique; u++)
                    {
                        if (s_HitDedupe[u] == stats)
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (dup)
                    {
                        continue;
                    }
                    if (unique < s_HitDedupe.Length)
                    {
                        s_HitDedupe[unique++] = stats;
                    }
                    stats.ApplyDamage(in info);
                }
            }
            // Buffer leeren, damit der nächste Frame keine Stale-References sieht.
            for (int u = 0; u < unique; u++)
            {
                s_HitDedupe[u] = null;
            }
            return unique;
        }

        private void HandleServerDied()
        {
            m_ServerDead = true;
            m_CurrentTarget = null;
            PlayDieClientRpc();
        }

        // ----- Remote-Tick ---------------------------------------------------

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

        // ----- Visuals (alle Peers) ------------------------------------------

        private void UpdateVisuals()
        {
            if (m_Character == null)
            {
                // Headless-Server / NPC ohne Sprite &#8212; keine Visuals.
                return;
            }

            Vector3 pos = transform.position;
            if (!m_VisualsInitialized)
            {
                m_PrevVisualPosition = pos;
                m_VisualsInitialized = true;
                return;
            }

            Vector3 diff = pos - m_PrevVisualPosition;
            diff.y = 0f;
            m_PrevVisualPosition = pos;

            // Nicht ueberschreiben, wenn eine Swing/Hit/Die-Aktion laeuft.
            if (m_Visuals != null && m_Visuals.IsBusy)
            {
                return;
            }
            if (m_ServerDead)
            {
                return;
            }

            const float k_MoveEpsilonSqr = 0.0001f; // ~1cm pro Frame
            bool moving = diff.sqrMagnitude > k_MoveEpsilonSqr;

            if (moving)
            {
                int dir = ComputeFlareDirection(diff);
                m_Character.SetDirection(dir);
                m_Character.Play(m_AnimRun);
            }
            else
            {
                m_Character.Play(m_AnimStance);
            }
        }

        /// <summary>
        /// Bildet einen XZ-Bewegungsvektor auf FLARE's 8-Octant-Direction-Index ab
        /// (0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW). Z wird invertiert, weil
        /// FLARE seine Y-Achse aus der Top-Down-Sicht ableitet (Sued = +Z in Unity).
        /// </summary>
        private static int ComputeFlareDirection(Vector3 diff)
        {
            float angleDeg = Mathf.Atan2(-diff.z, diff.x) * Mathf.Rad2Deg;
            int octant = Mathf.RoundToInt(angleDeg / 45f);
            return (octant + 4) & 7;
        }

        // ----- ClientRpcs ----------------------------------------------------

        [ClientRpc]
        private void PlaySwingClientRpc(FixedString32Bytes animationName)
        {
            if (m_Visuals != null)
            {
                if (animationName.Length > 0)
                {
                    m_Visuals.PlaySwing(animationName.ToString());
                }
                else
                {
                    m_Visuals.PlaySwing();
                }
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

        // ----- Hit-Reaktion --------------------------------------------------

        private void HandleClientDamageReceived(int amount, HitResult result)
        {
            // Nur eine kurze Hit-Stagger-Animation. Den eigentlichen HP-Abzug
            // verwaltet UnitStats; FloatingCombatText haengt sich ebenfalls dort an.
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
    }
}
