using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Spielt eine einzelne FLARE-Animationsschicht (z. B. chest) auf einem
    /// <see cref="SpriteRenderer"/> ab. Eine Schicht kennt nur den aktuellen
    /// Animationszustand und die Richtung; mehrere Schichten werden vom
    /// <see cref="FlareCharacter"/> synchron gesteuert.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class FlareLayerAnimator : MonoBehaviour
    {
        private SpriteRenderer m_Renderer;
        private FlareAtlas m_Atlas;
        private FlareAnimation m_Current;
        private int m_Direction;
        private float m_Elapsed;
        private bool m_Finished;

        /// <summary>True, sobald eine <see cref="FlareAnimationType.PlayOnce"/>-Animation ihren letzten Frame erreicht hat.</summary>
        public bool IsFinished => m_Finished;

        /// <summary>Aktuell abgespielte Animation (oder <c>null</c>).</summary>
        public FlareAnimation Current => m_Current;

        private void Awake()
        {
            m_Renderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// Setzt den Atlas dieser Schicht. Verwirft die laufende Animation.
        /// </summary>
        public void SetAtlas(FlareAtlas atlas)
        {
            m_Atlas = atlas;
            m_Current = null;
            m_Elapsed = 0f;
            m_Finished = false;
            if (m_Renderer != null)
            {
                m_Renderer.sprite = null;
            }
        }

        /// <summary>
        /// Wechselt zur Animation mit dem angegebenen Namen. Wird die Animation
        /// bereits abgespielt, hat der Aufruf keinen Effekt (außer <paramref name="force"/> ist gesetzt).
        /// </summary>
        public void Play(string animationName, bool force = false)
        {
            if (m_Atlas == null)
            {
                return;
            }
            if (!force && m_Current != null && m_Current.Name == animationName)
            {
                return;
            }
            if (!m_Atlas.TryGet(animationName, out FlareAnimation anim))
            {
                m_Current = null;
                if (m_Renderer != null)
                {
                    m_Renderer.sprite = null;
                }
                return;
            }
            m_Current = anim;
            m_Elapsed = 0f;
            m_Finished = false;
            ApplyFrame();
        }

        /// <summary>
        /// Setzt die aktuelle FLARE-Richtung (0..7). Negative Werte werden ignoriert
        /// und behalten die letzte Richtung bei.
        /// </summary>
        public void SetDirection(int flareDirection)
        {
            if (flareDirection < 0)
            {
                return;
            }
            int clamped = flareDirection & 7;
            if (clamped == m_Direction)
            {
                return;
            }
            m_Direction = clamped;
            ApplyFrame();
        }

        private void Update()
        {
            if (m_Current == null || m_Finished)
            {
                return;
            }
            m_Elapsed += Time.deltaTime;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            if (m_Current == null || m_Current.Sprites == null || m_Current.FramesCount <= 0)
            {
                return;
            }
            int frame = ComputeFrameIndex();
            Sprite[] row = m_Current.Sprites[frame];
            if (row == null || m_Direction >= row.Length)
            {
                return;
            }
            Sprite s = row[m_Direction];
            if (s != null && m_Renderer != null)
            {
                m_Renderer.sprite = s;
            }
        }

        private int ComputeFrameIndex()
        {
            int count = m_Current.FramesCount;
            if (count <= 1 || m_Current.DurationSeconds <= 0f)
            {
                return 0;
            }
            float t = m_Elapsed / m_Current.DurationSeconds;
            switch (m_Current.Type)
            {
                case FlareAnimationType.Looped:
                {
                    float frac = t - Mathf.Floor(t);
                    return Mathf.Clamp(Mathf.FloorToInt(frac * count), 0, count - 1);
                }
                case FlareAnimationType.PlayOnce:
                {
                    if (t >= 1f)
                    {
                        m_Finished = true;
                        return count - 1;
                    }
                    return Mathf.Clamp(Mathf.FloorToInt(t * count), 0, count - 1);
                }
                case FlareAnimationType.BackForth:
                default:
                {
                    // Hin und zurück: 2*(count-1) Schritte pro Zyklus.
                    int span = (count - 1) * 2;
                    if (span <= 0)
                    {
                        return 0;
                    }
                    float cycle = t - Mathf.Floor(t);
                    int idx = Mathf.FloorToInt(cycle * span);
                    return idx < count ? idx : span - idx;
                }
            }
        }
    }
}
