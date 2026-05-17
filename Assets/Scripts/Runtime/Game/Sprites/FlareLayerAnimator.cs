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

        /// <summary>
        /// Der per <see cref="SetAtlas"/> zugewiesene Atlas. Read-only zugaenglich
        /// fuer Tools/Test-Komponenten (z. B. <c>MugenAnimationShowcase</c>), die
        /// alle verfuegbaren Animationsnamen enumerieren wollen, ohne die
        /// MUGEN-Stats-JSON erneut zu parsen. Liefert <c>null</c>, bis
        /// <see cref="SetAtlas"/> aufgerufen wurde.
        /// </summary>
        public FlareAtlas Atlas => m_Atlas;

        /// <summary>
        /// Aktuell sichtbarer Frame-Index der laufenden Animation (0..FramesCount-1).
        /// Liefert <c>0</c>, wenn keine Animation läuft. Wird vom Combat-System
        /// (<c>MugenHitboxRuntime</c>) gelesen, um die richtige Frame-Zelle für
        /// Attack-/Hurt-Boxen zu finden.
        /// </summary>
        public int CurrentFrameIndex => m_Current != null ? ComputeFrameIndex() : 0;

        /// <summary>
        /// Aktuelle FLARE-Richtung (0..7), die zuletzt mit <see cref="SetDirection"/> gesetzt wurde.
        /// </summary>
        public int CurrentDirection => m_Direction;

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
                // Per-Zelle Spiegelung: der Importer markiert W/NW/SW als flipH=true,
                // damit der seitenansichts-MUGEN-Charakter nach links blickt.
                bool flipX = false;
                bool flipY = false;
                bool[][] fh = m_Current.FlipH;
                if (fh != null)
                {
                    bool[] fhRow = fh[frame];
                    if (fhRow != null && m_Direction < fhRow.Length)
                    {
                        flipX = fhRow[m_Direction];
                    }
                }
                bool[][] fv = m_Current.FlipV;
                if (fv != null)
                {
                    bool[] fvRow = fv[frame];
                    if (fvRow != null && m_Direction < fvRow.Length)
                    {
                        flipY = fvRow[m_Direction];
                    }
                }
                m_Renderer.flipX = flipX;
                m_Renderer.flipY = flipY;
            }
        }

        private int ComputeFrameIndex()
        {
            int count = m_Current.FramesCount;
            if (count <= 1 || m_Current.DurationSeconds <= 0f)
            {
                return 0;
            }
            float[] perFrame = m_Current.FrameDurations;
            float total = m_Current.DurationSeconds;
            switch (m_Current.Type)
            {
                case FlareAnimationType.Looped:
                {
                    float local = m_Elapsed - Mathf.Floor(m_Elapsed / total) * total;
                    return ResolveFrameByElapsed(local, perFrame, count, total);
                }
                case FlareAnimationType.PlayOnce:
                {
                    if (m_Elapsed >= total)
                    {
                        m_Finished = true;
                        return count - 1;
                    }
                    return ResolveFrameByElapsed(m_Elapsed, perFrame, count, total);
                }
                case FlareAnimationType.BackForth:
                default:
                {
                    // Hin und zurück: forward-Phase (total) + reverse-Phase (total) = 2*total.
                    float cycle = 2f * total;
                    float local = m_Elapsed - Mathf.Floor(m_Elapsed / cycle) * cycle;
                    if (local <= total)
                    {
                        return ResolveFrameByElapsed(local, perFrame, count, total);
                    }
                    int mirrored = ResolveFrameByElapsed(local - total, perFrame, count, total);
                    return Mathf.Clamp(count - 1 - mirrored, 0, count - 1);
                }
            }
        }

        /// <summary>
        /// Findet den aktuellen Frame entweder per Per-Frame-Dauer (Prefix-Sum)
        /// oder &#8212; falls keine Per-Frame-Daten vorliegen &#8212; per
        /// gleichmäßiger Verteilung über die Gesamtdauer.
        /// </summary>
        private static int ResolveFrameByElapsed(float elapsed, float[] perFrame, int count, float total)
        {
            if (perFrame != null && perFrame.Length == count)
            {
                float acc = 0f;
                for (int i = 0; i < count; i++)
                {
                    acc += perFrame[i];
                    if (elapsed < acc)
                    {
                        return i;
                    }
                }
                return count - 1;
            }
            float t = elapsed / total;
            return Mathf.Clamp(Mathf.FloorToInt(t * count), 0, count - 1);
        }
    }
}
