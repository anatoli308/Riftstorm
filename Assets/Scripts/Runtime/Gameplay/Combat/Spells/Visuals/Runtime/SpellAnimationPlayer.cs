using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Spielt eine <see cref="SpellAnimationDefinition"/> auf einem
    /// <see cref="SpriteRenderer"/> ab. Tickt frame-genau im <c>Update</c>
    /// per akkumuliertem Zeitdelta (kein Polling, keine Coroutines).
    /// Bietet One-Shot- und Loop-Wiedergabe (<c>loop_start..loop_end</c>).
    /// Beendet sich bei One-Shot mit <see cref="OnFinished"/>.
    /// </summary>
    /// <remarks>
    /// Kennt selbst keinerlei Spell-Logik — wird vom <see cref="WorldSpellAnimation"/>
    /// instanziiert und gesteuert. So bleibt der Player wiederverwendbar für
    /// jede Phase (Casting/Travel/Impact).
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpellAnimationPlayer : MonoBehaviour
    {
        private SpriteRenderer m_Renderer;
        private SpellAnimationDefinition m_Anim;
        private bool m_Loop;
        private float m_FrameDuration;
        private float m_Accumulator;
        private int m_FrameIndex;
        private bool m_Finished;

        /// <summary>True, wenn die letzte One-Shot-Wiedergabe komplett ist.</summary>
        public bool IsFinished => m_Finished;

        /// <summary>Aktuell laufende Animationsdefinition (oder <c>null</c>).</summary>
        public SpellAnimationDefinition CurrentAnim => m_Anim;

        /// <summary>Wird einmalig nach dem letzten Frame einer One-Shot-Wiedergabe gefeuert.</summary>
        public System.Action OnFinished;

        /// <summary>
        /// Startet die Wiedergabe von <paramref name="anim"/>. Setzt Frame 0 sofort.
        /// </summary>
        /// <param name="anim">Animationsdefinition; bei <c>null</c> beendet sich der Player sofort.</param>
        /// <param name="loop">Wenn <c>true</c> und <c>anim.HasLoop</c>, wird nach Erreichen von
        ///   <c>loop_end</c> zurück auf <c>loop_start</c> gesprungen. Sonst One-Shot.</param>
        public void Play(SpellAnimationDefinition anim, bool loop)
        {
            EnsureRenderer();
            m_Anim = anim;
            m_Loop = loop && anim != null && anim.HasLoop;
            m_FrameIndex = 0;
            m_Accumulator = 0f;
            m_Finished = false;

            if (anim == null || anim.FramesCount <= 0)
            {
                m_Finished = true;
                m_Renderer.sprite = null;
                OnFinished?.Invoke();
                return;
            }

            m_FrameDuration = Mathf.Max(anim.DelayMs, 1) / 1000f;
            transform.localScale = Vector3.one * Mathf.Max(anim.Scale, 0.0001f);
            ApplyFrame(0);
        }

        /// <summary>Stoppt die Wiedergabe und macht den Renderer leer.</summary>
        public void Stop()
        {
            m_Anim = null;
            m_Finished = true;
            if (m_Renderer != null)
            {
                m_Renderer.sprite = null;
            }
        }

        void Awake()
        {
            EnsureRenderer();
        }

        void Update()
        {
            if (m_Finished || m_Anim == null)
            {
                return;
            }

            m_Accumulator += Time.deltaTime;
            while (m_Accumulator >= m_FrameDuration)
            {
                m_Accumulator -= m_FrameDuration;
                AdvanceFrame();
                if (m_Finished)
                {
                    return;
                }
            }
        }

        private void AdvanceFrame()
        {
            int next = m_FrameIndex + 1;

            if (m_Loop && next > m_Anim.LoopEnd)
            {
                next = Mathf.Max(0, m_Anim.LoopStart);
            }
            else if (next >= m_Anim.FramesCount)
            {
                // Letzten Frame stehen lassen, fertig melden.
                m_Finished = true;
                OnFinished?.Invoke();
                return;
            }

            m_FrameIndex = next;
            ApplyFrame(next);
        }

        private void ApplyFrame(int index)
        {
            Sprite s = SpellSpriteCache.GetSprite(m_Anim, index);
            m_Renderer.sprite = s;
        }

        private void EnsureRenderer()
        {
            if (m_Renderer == null)
            {
                m_Renderer = GetComponent<SpriteRenderer>();
            }
        }
    }
}
