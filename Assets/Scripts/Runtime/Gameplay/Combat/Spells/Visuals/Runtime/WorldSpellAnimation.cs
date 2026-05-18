using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Client-lokales Visual eines einzelnen Cast-Events. Hält einen
    /// <see cref="SpellAnimationPlayer"/> als Child und durchläuft drei
    /// Phasen (Casting → Travel → Impact). Jede Phase ist optional; fehlt
    /// die zugehörige Animation, wird die Phase übersprungen.
    /// </summary>
    /// <remarks>
    /// Bewusst keine eigene <c>StateMachine&lt;,&gt;</c> — die Sequenz ist
    /// linear und kurzlebig. Billboarding läuft in <c>LateUpdate</c> gegen
    /// die <see cref="Camera.main"/>. Zerstört sich selbst, sobald die
    /// letzte aktive Phase fertig ist.
    /// </remarks>
    public sealed class WorldSpellAnimation : MonoBehaviour
    {
        /// <summary>Lineare Phasen-Reihenfolge.</summary>
        public enum Phase
        {
            /// <summary>Noch nichts gespielt.</summary>
            None = 0,
            /// <summary>Animation am Caster (Channel/Wind-up).</summary>
            Casting = 1,
            /// <summary>Projektil bewegt sich vom Caster zum Ziel.</summary>
            Travel = 2,
            /// <summary>Treffer-Animation am Ziel.</summary>
            Impact = 3,
            /// <summary>Sequenz fertig; GameObject wird zerstört.</summary>
            Done = 4,
        }

        private const float k_TravelArrivalEpsilon = 0.05f;

        private SpellAnimationPlayer m_Player;
        private Camera m_Camera;

        private SpellVisualDefinition m_Kit;
        private SpellAnimationCatalog m_Anims;
        private Transform m_Source;
        private Transform m_Target;

        private Phase m_Phase = Phase.None;
        private Vector3 m_TravelFrom;
        private Vector3 m_TravelTo;
        private float m_TravelSpeed;

        /// <summary>Aktuelle Phase (für Debug/Tests).</summary>
        public Phase CurrentPhase => m_Phase;

        /// <summary>
        /// Startet die Visual-Sequenz. Muss direkt nach dem
        /// <see cref="Object.Instantiate(Object)"/> aufgerufen werden.
        /// </summary>
        /// <param name="kit">Per-Spell-Visual-Kit (Phasen-Namen + Travel-Speed).</param>
        /// <param name="anims">Animations-Katalog zur Auflösung der Namen.</param>
        /// <param name="source">Caster-Transform (Anker für Casting/Travel-Start).</param>
        /// <param name="target">Ziel-Transform (Anker für Travel-Ende/Impact). Bei <c>null</c>
        ///   wird das Visual am Caster gespielt (Self-Target).</param>
        public void Play(
            SpellVisualDefinition kit,
            SpellAnimationCatalog anims,
            Transform source,
            Transform target)
        {
            m_Kit = kit;
            m_Anims = anims;
            m_Source = source;
            m_Target = target != null ? target : source;
            m_TravelSpeed = kit != null ? kit.TravelSpeed : 0f;

            EnsureRefs();
            AnchorToSource();
            StartNextPhase(Phase.Casting);
        }

        void Awake()
        {
            EnsureRefs();
        }

        void Update()
        {
            if (m_Phase == Phase.Travel)
            {
                TickTravel();
            }
            else if (m_Phase == Phase.Casting || m_Phase == Phase.Impact)
            {
                // Casting bleibt am Caster, Impact am Ziel.
                AnchorTo(m_Phase == Phase.Casting ? m_Source : m_Target);
            }
        }

        void LateUpdate()
        {
            if (m_Camera == null)
            {
                return;
            }
            // Topdown-Billboard: zur Kamera ausrichten.
            Vector3 fwd = transform.position - m_Camera.transform.position;
            if (fwd.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(fwd, m_Camera.transform.up);
            }
        }

        private void EnsureRefs()
        {
            if (m_Player == null)
            {
                m_Player = GetComponentInChildren<SpellAnimationPlayer>();
            }
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }
        }

        private void StartNextPhase(Phase phase)
        {
            switch (phase)
            {
                case Phase.Casting:
                    if (TryResolve(m_Kit?.CastingAnim, out SpellAnimationDefinition castAnim))
                    {
                        m_Phase = Phase.Casting;
                        AnchorTo(m_Source);
                        m_Player.OnFinished = OnCastingFinished;
                        m_Player.Play(castAnim, loop: false);
                        return;
                    }
                    goto case Phase.Travel;

                case Phase.Travel:
                    if (m_Source != null && m_Target != null && m_Source != m_Target
                        && TryResolve(m_Kit?.TravelAnim, out SpellAnimationDefinition travelAnim))
                    {
                        m_Phase = Phase.Travel;
                        m_TravelFrom = m_Source.position;
                        m_TravelTo = m_Target.position;
                        transform.position = m_TravelFrom;
                        m_Player.OnFinished = null;
                        m_Player.Play(travelAnim, loop: travelAnim.HasLoop);
                        return;
                    }
                    goto case Phase.Impact;

                case Phase.Impact:
                    if (TryResolve(m_Kit?.ImpactAnim, out SpellAnimationDefinition impactAnim))
                    {
                        m_Phase = Phase.Impact;
                        AnchorTo(m_Target);
                        m_Player.OnFinished = OnImpactFinished;
                        m_Player.Play(impactAnim, loop: false);
                        return;
                    }
                    goto case Phase.Done;

                case Phase.Done:
                    m_Phase = Phase.Done;
                    Destroy(gameObject);
                    return;
            }
        }

        private void OnCastingFinished()
        {
            StartNextPhase(Phase.Travel);
        }

        private void OnImpactFinished()
        {
            StartNextPhase(Phase.Done);
        }

        private void TickTravel()
        {
            if (m_Target != null)
            {
                m_TravelTo = m_Target.position;
            }
            Vector3 current = transform.position;
            Vector3 toTarget = m_TravelTo - current;
            float dist = toTarget.magnitude;

            float speed = m_TravelSpeed > 0f ? m_TravelSpeed : 20f;
            float step = speed * Time.deltaTime;

            if (dist <= step + k_TravelArrivalEpsilon)
            {
                transform.position = m_TravelTo;
                StartNextPhase(Phase.Impact);
                return;
            }
            transform.position = current + toTarget / dist * step;
        }

        private void AnchorToSource() => AnchorTo(m_Source);

        private void AnchorTo(Transform anchor)
        {
            if (anchor != null)
            {
                transform.position = anchor.position;
            }
        }

        private bool TryResolve(string animName, out SpellAnimationDefinition def)
        {
            def = null;
            if (string.IsNullOrEmpty(animName) || m_Anims == null)
            {
                return false;
            }
            return m_Anims.TryGet(animName, out def);
        }
    }
}
