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
    }
}
