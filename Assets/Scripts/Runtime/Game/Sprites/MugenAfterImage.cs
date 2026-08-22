using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Nachzieheffekt hinter dem Körper (MUGEN <c>AfterImage</c>): nimmt alle
    /// <c>timegap</c> Ticks den aktuellen Sprite samt Lage auf und zeigt daraus
    /// <c>length</c> Nachbilder im Abstand von <c>framegap</c> Aufnahmen.
    /// </summary>
    /// <remarks>
    /// Farbaddition und -faktor wirken pro Nachbild erneut — das ältere Bild ist
    /// damit stärker eingefärbt als das jüngere, genau wie in Ikemens
    /// <c>AfterImage.recAfterImg</c>.
    /// </remarks>
    public sealed class MugenAfterImage
    {
        /// <summary>Ein aufgenommener Zustand des Körpers.</summary>
        private struct Snapshot
        {
            public Sprite Sprite;
            public Vector3 Position;
            public Vector3 Scale;
            public bool FlipX;
            public bool Valid;
        }

        private readonly Transform m_Parent;
        private readonly SpriteRenderer m_Source;
        private readonly MugenBlendMaterialCache m_Blends;
        private readonly MugenPalFxApplier m_PalFx = new();

        private SpriteRenderer[] m_Ghosts = System.Array.Empty<SpriteRenderer>();
        private Snapshot[] m_History = System.Array.Empty<Snapshot>();
        private int m_Head;
        private int m_TicksUntilCapture;
        private FlareSequenceAfterImage m_Spec;
        private int m_TicksLeft;
        private bool m_Active;

        /// <summary>Erzeugt den Nachzieheffekt über einem Körper-Renderer.</summary>
        public MugenAfterImage(Transform parent, SpriteRenderer source, MugenBlendMaterialCache blends)
        {
            m_Parent = parent;
            m_Source = source;
            m_Blends = blends;
        }

        /// <summary>Startet eine neue Spur mit den Werten des Controllers.</summary>
        public void Begin(FlareSequenceAfterImage spec)
        {
            m_Spec = spec;
            m_TicksLeft = spec.Duration;
            m_Active = spec.Duration != 0;
            m_Head = 0;
            m_TicksUntilCapture = 0;
            EnsureCapacity(spec);
            ClearHistory();
            UpdateGhostAppearance();
        }

        /// <summary>Setzt nur die Restdauer einer laufenden Spur (<c>AfterImageTime</c>).</summary>
        public void Retime(int ticks)
        {
            if (m_Ghosts.Length == 0)
            {
                return;
            }
            m_TicksLeft = ticks;
            m_Active = ticks != 0;
            if (!m_Active)
            {
                HideGhosts();
            }
        }

        /// <summary>Nimmt einen Tick auf und zeichnet die Spur neu.</summary>
        public void Step()
        {
            if (!m_Active)
            {
                return;
            }
            if (m_TicksLeft > 0)
            {
                m_TicksLeft--;
                if (m_TicksLeft == 0)
                {
                    Stop();
                    return;
                }
            }

            if (m_TicksUntilCapture <= 0)
            {
                Capture();
                m_TicksUntilCapture = m_Spec.TimeGap;
            }
            m_TicksUntilCapture--;
            DrawGhosts();
        }

        /// <summary>Beendet die Spur und blendet alle Nachbilder aus.</summary>
        public void Stop()
        {
            m_Active = false;
            HideGhosts();
        }

        /// <summary>Zerstört alle Nachbild-Renderer.</summary>
        public void Destroy()
        {
            for (int i = 0; i < m_Ghosts.Length; i++)
            {
                if (m_Ghosts[i] != null)
                {
                    Object.Destroy(m_Ghosts[i].gameObject);
                }
            }
            m_Ghosts = System.Array.Empty<SpriteRenderer>();
            m_History = System.Array.Empty<Snapshot>();
        }

        /// <summary>Legt Renderer und Ringpuffer für die konfigurierte Spurlänge an.</summary>
        private void EnsureCapacity(FlareSequenceAfterImage spec)
        {
            int historySize = Mathf.Max(1, spec.Length * spec.FrameGap);
            if (m_History.Length < historySize)
            {
                m_History = new Snapshot[historySize];
            }
            if (m_Ghosts.Length >= spec.Length)
            {
                return;
            }
            SpriteRenderer[] grown = new SpriteRenderer[spec.Length];
            System.Array.Copy(m_Ghosts, grown, m_Ghosts.Length);
            for (int i = m_Ghosts.Length; i < grown.Length; i++)
            {
                GameObject go = new($"AfterImage_{i}");
                go.transform.SetParent(m_Parent, worldPositionStays: false);
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingLayerID = m_Source.sortingLayerID;
                // Ikemen setzt Nachbilder zwei Stufen hinter das Original.
                renderer.sortingOrder = m_Source.sortingOrder - 2 - i;
                renderer.enabled = false;
                grown[i] = renderer;
            }
            m_Ghosts = grown;
        }

        /// <summary>Setzt Material und Farbfilter jedes Nachbilds nach seinem Alter.</summary>
        /// <remarks>
        /// Rekurrenz aus Ikemens <c>AfterImage.setup</c>: Nachbild 0 traegt
        /// <c>palbright</c>/<c>palcontrast</c>, jedes weitere addiert <c>paladd</c>
        /// (beim ersten Schritt zusaetzlich <c>palpostbright</c>) und multipliziert
        /// mit <c>palmul</c>. Das ergibt die typische, nach hinten ausbleichende Spur.
        /// </remarks>
        private void UpdateGhostAppearance()
        {
            Material material = m_Blends.Resolve(m_Spec.Blend);
            Vector3 add = m_Spec.PalBright;
            Vector3 mul = m_Spec.PalContrast;
            Vector3 postBright = m_Spec.PalPostBright;
            for (int i = 0; i < m_Ghosts.Length; i++)
            {
                if (material != null)
                {
                    m_Ghosts[i].sharedMaterial = material;
                }
                if (i >= m_Spec.Length)
                {
                    continue;
                }
                if (i > 0)
                {
                    add += m_Spec.PalAdd + postBright;
                    mul = Vector3.Scale(mul, m_Spec.PalMul);
                    postBright = Vector3.zero;
                }
                m_PalFx.Apply(m_Ghosts[i], new FlareSequencePalFx(
                    0, -1, false, add, mul, m_Spec.PalColor, m_Spec.PalInvertAll,
                    Vector3.zero, 0), 0f);
            }
        }

        /// <summary>Schreibt den aktuellen Körperzustand in den Ringpuffer.</summary>
        private void Capture()
        {
            m_History[m_Head] = new Snapshot
            {
                Sprite = m_Source.sprite,
                Position = m_Source.transform.localPosition,
                Scale = m_Source.transform.localScale,
                FlipX = m_Source.flipX,
                Valid = m_Source.sprite != null,
            };
            m_Head = (m_Head + 1) % m_History.Length;
        }

        /// <summary>Legt die Nachbilder auf die passenden Aufnahmen der Vergangenheit.</summary>
        private void DrawGhosts()
        {
            for (int i = 0; i < m_Ghosts.Length && i < m_Spec.Length; i++)
            {
                int back = (i + 1) * m_Spec.FrameGap;
                int index = ((m_Head - back) % m_History.Length + m_History.Length) % m_History.Length;
                Snapshot snapshot = m_History[index];
                SpriteRenderer ghost = m_Ghosts[i];
                if (!snapshot.Valid)
                {
                    ghost.enabled = false;
                    continue;
                }
                ghost.sprite = snapshot.Sprite;
                ghost.flipX = snapshot.FlipX;
                ghost.transform.localPosition = snapshot.Position;
                ghost.transform.localScale = snapshot.Scale;
                ghost.enabled = true;
            }
        }

        private void ClearHistory()
        {
            for (int i = 0; i < m_History.Length; i++)
            {
                m_History[i].Valid = false;
            }
        }

        private void HideGhosts()
        {
            for (int i = 0; i < m_Ghosts.Length; i++)
            {
                if (m_Ghosts[i] != null)
                {
                    m_Ghosts[i].enabled = false;
                }
            }
        }
    }
}
