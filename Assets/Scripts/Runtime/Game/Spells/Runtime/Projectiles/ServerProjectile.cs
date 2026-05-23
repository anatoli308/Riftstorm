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

        ICombatUnit m_Caster;
        ICombatUnit m_Target;
        SpellTemplate m_Spell;
        CastDestination m_Destination;
        float m_SpeedMps;
        float m_MaxFlightSeconds;
        float m_ElapsedSeconds;
        bool m_Detonated;

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

        void Update()
        {
            if (m_Detonated) { return; }

            m_ElapsedSeconds += Time.deltaTime;

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
