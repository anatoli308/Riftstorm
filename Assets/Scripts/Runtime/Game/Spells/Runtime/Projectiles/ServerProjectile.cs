using Riftstorm.Game.Combat;
using UnityEngine;

namespace Riftstorm.Game.Spells.Projectiles
{
    /// <summary>
    /// Server-seitige Projectile-Simulation fuer Single-Target-Spells mit
    /// <c>Speed &gt; 0</c>. Verzoegert die SpellEffect-Anwendung um die
    /// Travel-Zeit (<c>Distanz / Speed</c>), so dass der serverseitige
    /// Schaden zeitlich zum Client-Visual passt (Travel-Phase aus
    /// <c>WorldSpellAnimation</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Server-only: keine <c>NetworkObject</c>-Komponente. Der Client kennt
    /// das Projectile nicht; die visuelle Travel-Phase laeuft separat ueber
    /// <c>PlaySpellCastClientRpc</c> → <c>SpellVisualSpawner</c>. Damage-
    /// Timing und Visual-Timing leben auf zwei Kanaelen, die naehrungsweise
    /// uebereinstimmen — fuer Singleplayer-Wahrnehmung ausreichend, kein
    /// Anspruch auf frame-genaue Synchronitaet.
    /// </para>
    /// <para>
    /// Lebenszyklus: per <see cref="Spawn"/> direkt von
    /// <c>SpellExecutor.Execute</c> erzeugt. Folgt dem Ziel ("homing")
    /// und detoniert, sobald entweder
    /// <list type="bullet">
    ///   <item><description>die Ziel-Distanz unterschritten ist (Hit),</description></item>
    ///   <item><description>oder die maximale Flugzeit ueberschritten ist
    ///   (Fail-Safe — Ziel ausser Reichweite gerannt).</description></item>
    /// </list>
    /// Stirbt das Ziel oder ist <c>null</c> vor dem Impact, wird der
    /// Projectile-GameObject ohne Hit zerstoert.
    /// </para>
    /// <para>
    /// Kein Pooling — wenige gleichzeitige Projectiles pro Match erwartet
    /// (Spielerzahl x Cast-Frequenz). Bei Performance-Bedarf spaeter
    /// nachruesten (siehe Performance-First-Philosophy).
    /// </para>
    /// </remarks>
    public sealed class ServerProjectile : MonoBehaviour
    {
        const float k_HitRadiusMeters = 0.4f;
        const float k_FallbackFlightSeconds = 5f;
        const float k_SkillshotHitRadiusMeters = 0.5f;
        const float k_FallbackSkillshotRangeMeters = 15f;

        // Reusable Physics-Query-Buffer fuer den Skillshot-Hit-Test —
        // vermeidet Per-Frame-Allocs in OverlapSphereNonAlloc.
        static readonly Collider[] s_SkillshotHitBuffer = new Collider[16];

        ICombatUnit m_Caster;
        ICombatUnit m_Target;
        SpellTemplate m_Spell;
        CastDestination m_Destination;
        float m_SpeedMps;
        float m_MaxFlightSeconds;
        float m_ElapsedSeconds;
        bool m_Detonated;

        // -- Directional / Skillshot-Modus --
        bool m_Directional;
        Vector3 m_Direction;
        Vector3 m_Origin;
        float m_MaxRangeMeters;
        bool m_WantHostile;
        bool m_WantFriendly;

        /// <summary>
        /// Erstellt und initialisiert eine neue Projectile-Instanz am Caster.
        /// Server-Only — Aufruf nur aus <see cref="SpellExecutor"/>.
        /// </summary>
        public static ServerProjectile Spawn(
            ICombatUnit caster,
            SpellTemplate spell,
            ICombatUnit target,
            CastDestination destination)
        {
            if (caster == null || spell == null || target == null) { return null; }

            Vector3 origin = caster.Position;
            GameObject go = new($"ServerProjectile_{spell.Entry}");
            go.transform.position = origin;

            ServerProjectile p = go.AddComponent<ServerProjectile>();
            p.Initialize(caster, spell, target, destination, origin);
            return p;
        }

        /// <summary>
        /// Erstellt ein gerichtetes Skillshot-Projektil (FLARE-Stil). Fliegt
        /// geradlinig vom Caster in Richtung des Cast-Destinationspunkts
        /// (Cursor) bzw. der Blickrichtung und trifft das erste valide Ziel
        /// auf der Bahn. Server-Only — Aufruf nur aus <see cref="SpellExecutor"/>.
        /// </summary>
        public static ServerProjectile SpawnDirectional(
            ICombatUnit caster,
            SpellTemplate spell,
            CastDestination destination)
        {
            if (caster == null || spell == null) { return null; }

            Vector3 origin = caster.Position;

            // Richtung: bevorzugt zum Cast-Destinationspunkt (Cursor),
            // ansonsten Blickrichtung des Casters. Topdown → Y ignorieren.
            Vector3 dir = destination.HasValue ? destination.Position - origin : caster.Forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) { dir = caster.Forward; dir.y = 0f; }
            if (dir.sqrMagnitude < 0.0001f) { dir = Vector3.forward; }
            dir.Normalize();

            GameObject go = new($"ServerProjectile_{spell.Entry}");
            go.transform.position = origin;

            ServerProjectile p = go.AddComponent<ServerProjectile>();
            p.InitializeDirectional(caster, spell, destination, origin, dir);
            return p;
        }

        void Initialize(
            ICombatUnit caster,
            SpellTemplate spell,
            ICombatUnit target,
            CastDestination destination,
            Vector3 origin)
        {
            m_Caster = caster;
            m_Spell = spell;
            m_Target = target;
            m_Destination = destination;

            m_SpeedMps = SpellUtils.ProjectileSpeedToMps(spell.Speed);

            // Fail-Safe-Flugzeit: maximaler Reichweite-Radius / Speed,
            // plus 50% Toleranz fuer kite-bewegte Ziele. Bei degeneriertem
            // Speed=0 (sollte ShouldUseProjectile bereits filtern) faellt
            // ein konservativer Default greift.
            if (m_SpeedMps > 0f)
            {
                float maxRangeM = SpellUtils.RangeToMeters(spell.Range);
                m_MaxFlightSeconds = maxRangeM > 0f ? (maxRangeM / m_SpeedMps) * 1.5f : k_FallbackFlightSeconds;
            }
            else
            {
                m_MaxFlightSeconds = k_FallbackFlightSeconds;
            }
        }

        /// <summary>
        /// Initialisiert den gerichteten Skillshot-Modus: feste Flugrichtung,
        /// Reichweiten-Limit und Faction-Filter (aus dem primaeren Effekt-Slot).
        /// </summary>
        void InitializeDirectional(
            ICombatUnit caster,
            SpellTemplate spell,
            CastDestination destination,
            Vector3 origin,
            Vector3 direction)
        {
            m_Caster = caster;
            m_Spell = spell;
            m_Target = null;
            m_Destination = destination;
            m_Directional = true;
            m_Origin = origin;
            m_Direction = direction;

            m_SpeedMps = SpellUtils.ProjectileSpeedToMps(spell.Speed);

            float maxRangeM = SpellUtils.RangeToMeters(spell.Range);
            m_MaxRangeMeters = maxRangeM > 0f ? maxRangeM : k_FallbackSkillshotRangeMeters;

            // Faction-Filter aus dem primaeren Effekt-Ziel-Typ ableiten.
            SpellTemplateEffect primary = spell.GetEffect(1);
            SpellTargetType tt = primary.TargetType;
            m_WantHostile = tt == SpellTargetType.UnitHostile
                || tt == SpellTargetType.UnitAny
                || tt == SpellTargetType.UnitAreaSrcHostile
                || tt == SpellTargetType.UnitAreaDstHostile
                || tt == SpellTargetType.UnitAreaDstHostileFromDst;
            m_WantFriendly = tt == SpellTargetType.UnitFriendly
                || tt == SpellTargetType.UnitAny
                || tt == SpellTargetType.UnitAreaSrcFriendly
                || tt == SpellTargetType.UnitAreaDstFriendly
                || tt == SpellTargetType.UnitAreaDstFriendlyFromDst;
            // Default: Hostile, falls der Slot keine eindeutige Faction vorgibt.
            if (!m_WantHostile && !m_WantFriendly) { m_WantHostile = true; }

            // Fail-Safe-Flugzeit aus Reichweite + 50% Toleranz.
            m_MaxFlightSeconds = m_SpeedMps > 0f
                ? (m_MaxRangeMeters / m_SpeedMps) * 1.5f
                : k_FallbackFlightSeconds;
        }

        void Update()
        {
            if (m_Detonated) { return; }

            m_ElapsedSeconds += Time.deltaTime;

            if (m_Directional)
            {
                UpdateDirectional();
                return;
            }

            // Ziel verloren → Projectile verfaellt ohne Hit.
            if (m_Target == null || m_Target.IsDead)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 targetPos = m_Target.Position;
            Vector3 currentPos = transform.position;
            Vector3 toTarget = targetPos - currentPos;
            float distance = toTarget.magnitude;
            float step = m_SpeedMps * Time.deltaTime;

            // Hit-Test: Schritt-Distanz >= Restdistanz, oder bereits in Hit-Radius.
            if (distance <= k_HitRadiusMeters || step >= distance)
            {
                transform.position = targetPos;
                Detonate();
                return;
            }

            // Fail-Safe: Maximale Flugzeit ueberschritten → Hit am aktuellen Ziel.
            // (Konservativ: wer 1.5x Range-Zeit ueberlebt hat, gilt als getroffen,
            //  damit Spells nicht still ins Nichts fliegen.)
            if (m_ElapsedSeconds >= m_MaxFlightSeconds)
            {
                Detonate();
                return;
            }

            transform.position = currentPos + toTarget.normalized * step;
        }

        /// <summary>
        /// Geradlinige Skillshot-Simulation: bewegt das Projektil pro Frame in
        /// <see cref="m_Direction"/>, testet am neuen Punkt auf das erste valide
        /// Ziel und detoniert dort. Verfaellt ohne Hit bei Ueberschreiten der
        /// Reichweite oder Fail-Safe-Flugzeit.
        /// </summary>
        void UpdateDirectional()
        {
            if (m_Caster == null)
            {
                Destroy(gameObject);
                return;
            }

            float step = m_SpeedMps * Time.deltaTime;
            Vector3 nextPos = transform.position + m_Direction * step;
            transform.position = nextPos;

            ICombatUnit hit = FindFirstHit(nextPos);
            if (hit != null)
            {
                DetonateDirectional(hit, nextPos);
                return;
            }

            // Reichweite/Flugzeit ueberschritten → ohne Hit verfallen.
            float traveled = (nextPos - m_Origin).magnitude;
            if (traveled >= m_MaxRangeMeters || m_ElapsedSeconds >= m_MaxFlightSeconds)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Sucht am Punkt <paramref name="pos"/> das naechstgelegene valide Ziel
        /// (Faction-gefiltert, lebendig, nicht der Caster). Allokationsfrei ueber
        /// <see cref="s_SkillshotHitBuffer"/>.
        /// </summary>
        ICombatUnit FindFirstHit(Vector3 pos)
        {
            int hits = Physics.OverlapSphereNonAlloc(
                pos,
                k_SkillshotHitRadiusMeters,
                s_SkillshotHitBuffer,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);

            int casterFaction = m_Caster?.FactionId ?? -1;
            ICombatUnit best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < hits; i++)
            {
                Collider col = s_SkillshotHitBuffer[i];
                s_SkillshotHitBuffer[i] = null;
                if (col == null) { continue; }

                UnitStats stats = col.GetComponentInParent<UnitStats>();
                if (stats == null) { continue; }
                ICombatUnit candidate = stats;
                if (candidate.IsDead) { continue; }
                if (ReferenceEquals(candidate, m_Caster)) { continue; }

                bool sameFaction = candidate.FactionId == casterFaction;
                if (m_WantHostile && !m_WantFriendly && sameFaction) { continue; }
                if (m_WantFriendly && !m_WantHostile && !sameFaction) { continue; }

                float sqr = (candidate.Position - pos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = candidate; }
            }
            return best;
        }

        /// <summary>
        /// Wendet die Spell-Effekte am getroffenen Ziel an; der Impact-Punkt
        /// dient als AoE-Center fuer <c>UnitAreaDst*</c>-Effekte.
        /// </summary>
        void DetonateDirectional(ICombatUnit hit, Vector3 impactPos)
        {
            m_Detonated = true;
            if (m_Caster != null && hit != null && !hit.IsDead)
            {
                SpellExecutor.ApplyAllEffectsAtImpact(m_Caster, m_Spell, hit, CastDestination.At(impactPos));
            }
            Destroy(gameObject);
        }

        void Detonate()
        {
            m_Detonated = true;

            // Nur applyen, wenn Caster noch existiert (z. B. bei
            // Disconnect/Despawn waehrend des Flugs ist das Cast-
            // Kontext-Owner-Objekt evtl. weg).
            if (m_Caster != null && m_Target != null && !m_Target.IsDead)
            {
                SpellExecutor.ApplyAllEffectsAtImpact(m_Caster, m_Spell, m_Target, m_Destination);
            }
            Destroy(gameObject);
        }
    }
}
