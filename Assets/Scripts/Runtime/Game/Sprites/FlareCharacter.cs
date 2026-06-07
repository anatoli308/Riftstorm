using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Verbindet mehrere <see cref="FlareLayerAnimator"/>-Schichten (chest, feet, hands, legs ...)
    /// zu einem Charakter. Alle Schichten teilen Animationsname und Richtung.
    /// </summary>
    public sealed class FlareCharacter : MonoBehaviour
    {
        [SerializeField] private List<FlareLayerAnimator> m_Layers = new();

        private string m_CurrentAnimation;
        private int m_CurrentDirection;

        /// <summary>Aktuell gespielter Animationsname (oder <c>null</c>).</summary>
        public string CurrentAnimation => m_CurrentAnimation;

        /// <summary>Aktuelle FLARE-Richtung (0..7).</summary>
        public int CurrentDirection => m_CurrentDirection;

        /// <summary>
        /// Nominale Spieldauer der aktuell laufenden Animation in Sekunden (Maximum
        /// über alle Schichten). Liefert <c>0</c>, wenn keine Schicht eine
        /// passende Animation hält — z. B. weil der Atlas die Animation nicht
        /// kennt. Wird vom <see cref="Combat.UnitCombatVisuals"/>
        /// als Safety-Net-Deadline genutzt, falls die PlayOnce-Erkennung fehl-
        /// schlägt (MUGEN-Konverter ohne <c>duration=-1</c> am letzten Frame
        /// erzeugt Looped-Atttacken, bei denen <see cref="IsPlayOnceFinished"/>
        /// niemals <c>true</c> wird).
        /// </summary>
        public float CurrentDurationSeconds
        {
            get
            {
                float max = 0f;
                for (int i = 0; i < m_Layers.Count; i++)
                {
                    FlareLayerAnimator layer = m_Layers[i];
                    if (layer == null || layer.Current == null)
                    {
                        continue;
                    }
                    float d = layer.Current.DurationSeconds;
                    if (d > max)
                    {
                        max = d;
                    }
                }
                return max;
            }
        }

        /// <summary>
        /// <c>true</c>, wenn aktuell eine <see cref="FlareAnimationType.PlayOnce"/>-Animation
        /// läuft und alle Schichten mit dieser Animation ihren letzten Frame erreicht haben.
        /// Für gelooopte Animationen (Stance/Run/Block) immer <c>false</c>.
        /// </summary>
        public bool IsPlayOnceFinished
        {
            get
            {
                bool sawPlayOnce = false;
                for (int i = 0; i < m_Layers.Count; i++)
                {
                    FlareLayerAnimator layer = m_Layers[i];
                    if (layer == null || layer.Current == null)
                    {
                        continue;
                    }
                    if (layer.Current.Type != FlareAnimationType.PlayOnce)
                    {
                        continue;
                    }
                    sawPlayOnce = true;
                    if (!layer.IsFinished)
                    {
                        return false;
                    }
                }
                return sawPlayOnce;
            }
        }

        /// <summary>Fügt eine Schicht zur Komposition hinzu.</summary>
        public void RegisterLayer(FlareLayerAnimator layer)
        {
            if (layer == null || m_Layers.Contains(layer))
            {
                return;
            }
            m_Layers.Add(layer);
        }

        /// <summary>
        /// Tauscht den Atlas einer per <c>GameObject.name</c> identifizierten Schicht
        /// (z. B. <c>"MainHand"</c> / <c>"OffHand"</c>) zur Laufzeit. Nach dem Swap
        /// wird die aktuell laufende Animation in der richtigen Blickrichtung
        /// sofort neu gestartet, damit der frisch geladene Atlas direkt sichtbar
        /// ist. <paramref name="atlas"/> = <c>null</c> macht die Schicht unsichtbar
        /// (z. B. <c>/offhand none</c>). Liefert <c>false</c>, wenn keine Schicht
        /// mit diesem Namen registriert ist.
        /// </summary>
        public bool SetLayerAtlas(string layerName, FlareAtlas atlas)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return false;
            }
            for (int i = 0; i < m_Layers.Count; i++)
            {
                FlareLayerAnimator layer = m_Layers[i];
                if (layer == null || layer.gameObject.name != layerName)
                {
                    continue;
                }
                layer.SwapAtlas(atlas, m_CurrentAnimation, m_CurrentDirection);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Spielt auf allen Schichten dieselbe Animation ab. Schichten, die diese
        /// Animation nicht kennen, werden vom <see cref="FlareLayerAnimator"/> still geleert.
        /// </summary>
        public void Play(string animationName, bool force = false)
        {
            if (!force && m_CurrentAnimation == animationName)
            {
                return;
            }
            m_CurrentAnimation = animationName;
            for (int i = 0; i < m_Layers.Count; i++)
            {
                FlareLayerAnimator layer = m_Layers[i];
                if (layer != null)
                {
                    layer.Play(animationName, force);
                }
            }
        }

        /// <summary>
        /// Setzt die Richtung aller Schichten synchron.
        /// </summary>
        public void SetDirection(int flareDirection)
        {
            if (flareDirection < 0 || flareDirection == m_CurrentDirection)
            {
                return;
            }
            m_CurrentDirection = flareDirection & 7;
            for (int i = 0; i < m_Layers.Count; i++)
            {
                FlareLayerAnimator layer = m_Layers[i];
                if (layer != null)
                {
                    layer.SetDirection(m_CurrentDirection);
                }
            }
        }

        /// <summary>
        /// Liefert den Frame-Index der aktuell laufenden Animation. Da alle Schichten
        /// synchron mit demselben Namen und derselben Dauer abgespielt werden, reicht
        /// der erste Layer mit einer aktiven Animation als Wahrheitsquelle.
        /// Liefert <c>0</c>, wenn keine Schicht spielt.
        /// </summary>
        public int CurrentFrameIndex
        {
            get
            {
                for (int i = 0; i < m_Layers.Count; i++)
                {
                    FlareLayerAnimator layer = m_Layers[i];
                    if (layer != null && layer.Current != null)
                    {
                        return layer.CurrentFrameIndex;
                    }
                }
                return 0;
            }
        }

        /// <summary>
        /// Versucht, die aktuell sichtbare <see cref="FlareCell"/> für die aktive
        /// Animation, den aktuellen Frame und die aktuelle Richtung aufzulösen.
        /// Liefert die erste Zelle über alle Schichten, deren <c>Cells</c>-Matrix
        /// einen passenden Eintrag enthält. Wird vom Combat-System für
        /// Attack-/Hurt-Box-Lookups verwendet.
        /// </summary>
        /// <param name="cell">Die aufgelöste Zelle (kann <c>null</c> sein, wenn keine Schicht eine Zelle für diesen Slot hat).</param>
        /// <returns><c>true</c>, wenn eine Zelle gefunden wurde.</returns>
        public bool TryGetCurrentCell(out FlareCell cell)
        {
            int dir = m_CurrentDirection & 7;
            for (int i = 0; i < m_Layers.Count; i++)
            {
                FlareLayerAnimator layer = m_Layers[i];
                if (layer == null)
                {
                    continue;
                }
                FlareAnimation anim = layer.Current;
                if (anim == null || anim.Cells == null)
                {
                    continue;
                }
                int frame = layer.CurrentFrameIndex;
                if (frame < 0 || frame >= anim.Cells.Length)
                {
                    continue;
                }
                FlareCell[] row = anim.Cells[frame];
                if (row == null || dir >= row.Length)
                {
                    continue;
                }
                FlareCell c = row[dir];
                if (c != null)
                {
                    cell = c;
                    return true;
                }
            }
            cell = null;
            return false;
        }
    }
}
