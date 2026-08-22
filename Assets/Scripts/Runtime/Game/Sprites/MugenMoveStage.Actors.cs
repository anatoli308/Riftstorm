using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Akteur-Teil der Move-Bühne: die vom Move gespawnten Helfer. In MUGEN ist
    /// ein Helper ein eigenständiger Charakter — er hat eine eigene Animationskette,
    /// eine eigene Position und trägt bei den meisten Angriffen die Treffer.
    /// </summary>
    public sealed partial class MugenMoveStage
    {
        /// <summary>Ein laufender Helfer mit eigenem Renderer und eigener Kette.</summary>
        private sealed class HelperActor
        {
            public FlareSequenceHelper Spec;
            public GameObject Go;
            public FlareLayerAnimator Anim;
            public SpriteRenderer Renderer;
            public float SpawnAt;
            public int Step;
            public int NextSoundIndex;
            public int NextHitIndex;
            public bool Visible;
            public Vector2 Velocity;
            public Vector2 Offset;
            public int NextBindIndex;
            public int BindTicksLeft;
            public Vector2 BindOffset;
            public bool Bound;
        }

        private readonly List<HelperActor> m_Helpers = new();

        /// <summary>
        /// Baut je Helfer einen Akteur samt seiner Effekte und liefert die Dauer,
        /// die der längste davon dem Zyklus hinzufügt.
        /// </summary>
        private float BuildHelpers(FlareSequence sequence, FlareAtlas atlas)
        {
            if (atlas == null)
            {
                return 0f;
            }
            float cycle = 0f;
            int facingSign = FacingSign();

            foreach (FlareSequenceHelper spec in sequence.Helpers)
            {
                Vector2 offset = new(spec.PosX, spec.PosY);
                foreach (FlareSequenceEffect fx in spec.Effects)
                {
                    cycle = Mathf.Max(cycle, CreateEffectLayer(fx, atlas, offset));
                }
                if (spec.Anims.Length == 0)
                {
                    continue;
                }

                GameObject go = new($"Helper_{spec.Anims[0].Anim}");
                go.transform.SetParent(m_MugenSpace, worldPositionStays: false);
                go.transform.localPosition = HelperPosition(spec, facingSign);
                // Ein Helfer ist ein Charakter und erbt dessen size (Ikemen: h.size = c.size).
                go.transform.localScale = m_VisualRoot.localScale;

                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingLayerID = m_BodyRenderer != null ? m_BodyRenderer.sortingLayerID : 0;
                // Ein Helfer ist ein Charakter, kein Effekt — aber MUGEN sortiert ihn
                // mit seiner eigenen sprpriority in dieselbe Liste wie alles andere.
                renderer.sortingOrder = SortingOrderFor(spec.SprPriority, 0);
                renderer.enabled = false;

                FlareLayerAnimator animator = go.AddComponent<FlareLayerAnimator>();
                animator.SetBlendMaterials(m_Blends);
                animator.SetAtlas(atlas);
                animator.SetDirection(m_Direction);

                m_Helpers.Add(new HelperActor
                {
                    Spec = spec,
                    Go = go,
                    Anim = animator,
                    Renderer = renderer,
                    SpawnAt = spec.Time / TicksPerSecond,
                });

                FlareSequenceAnim last = spec.Anims[spec.Anims.Length - 1];
                float tail = atlas.TryGet(last.Anim, out FlareAnimation anim) && anim != null
                    ? anim.DurationSeconds
                    : MinCycleSeconds;
                cycle = Mathf.Max(cycle, last.Time / TicksPerSecond + tail);
            }
            return cycle;
        }

        /// <summary>Lädt die Töne aller Helfer vor, damit sie zum Tick sofort spielen.</summary>
        private async Task PreloadHelperSoundsAsync(FlareSequence sequence)
        {
            if (m_SoundBank == null)
            {
                return;
            }
            foreach (FlareSequenceHelper helper in sequence.Helpers)
            {
                foreach (FlareSequenceSound sound in helper.Sounds)
                {
                    await m_SoundBank.LoadAsync(sound.Group, sound.Index);
                }
                foreach (FlareSequenceHit hit in helper.Hits)
                {
                    if (hit.HasSound)
                    {
                        await m_SoundBank.LoadAsync(hit.SoundGroup, hit.SoundIndex);
                    }
                }
            }
        }

        /// <summary>
        /// Blendet fällige Helfer ein und schaltet ihre Animationskette weiter.
        /// Ein späteres Kettenglied <b>ersetzt</b> das vorherige — genau wie MUGENs
        /// <c>ChangeState</c> die Animation des Helfers austauscht.
        /// </summary>
        private void AdvanceHelpers(int tick)
        {
            for (int i = 0; i < m_Helpers.Count; i++)
            {
                HelperActor actor = m_Helpers[i];
                if (!actor.Visible)
                {
                    if (m_Elapsed < actor.SpawnAt)
                    {
                        continue;
                    }
                    actor.Visible = true;
                    actor.Renderer.enabled = true;
                    actor.Step = 0;
                    actor.Anim.Play(actor.Spec.Anims[0].Anim, force: true);
                }

                FlareSequenceAnim[] chain = actor.Spec.Anims;
                while (actor.Step + 1 < chain.Length
                       && m_Elapsed >= chain[actor.Step + 1].Time / TicksPerSecond)
                {
                    actor.Step++;
                    actor.Anim.Play(chain[actor.Step].Anim, force: true);
                }

                StepHelperMotion(actor);
                AdvanceHelperBinds(actor, tick);
                AdvanceHelperSounds(actor);
                AdvanceHelperHits(actor, tick);
            }
        }

        /// <summary>
        /// Setzt <c>BindToParent</c>/<c>BindToRoot</c> um: solange die Bindung läuft,
        /// klebt der Helfer am Körper des Charakters statt seiner eigenen Flugbahn
        /// zu folgen. <c>time = -1</c> bindet dauerhaft.
        /// </summary>
        private void AdvanceHelperBinds(HelperActor actor, int tick)
        {
            FlareSequenceBind[] binds = actor.Spec.Binds;
            while (actor.NextBindIndex < binds.Length && binds[actor.NextBindIndex].Time <= tick)
            {
                FlareSequenceBind bind = binds[actor.NextBindIndex];
                actor.NextBindIndex++;
                actor.Bound = true;
                actor.BindOffset = bind.Offset;
                actor.BindTicksLeft = bind.Duration;
            }
            if (!actor.Bound)
            {
                return;
            }
            if (actor.BindTicksLeft == 0)
            {
                actor.Bound = false;
                return;
            }
            if (actor.BindTicksLeft > 0)
            {
                actor.BindTicksLeft--;
            }
            int facingSign = FacingSign();
            Vector2 anchor = m_BodyOffset + actor.BindOffset;
            actor.Go.transform.localPosition = new Vector3(
                facingSign * anchor.x / PixelsPerUnit, -anchor.y / PixelsPerUnit, 0f);
        }

        /// <summary>
        /// Bewegt einen Helfer wie MUGEN einen Charakter: erst die Position um die
        /// Geschwindigkeit versetzen, dann die Beschleunigung aufaddieren. Damit
        /// beschreibt ein geworfenes Objekt seine Wurfparabel statt still zu stehen.
        /// </summary>
        private void StepHelperMotion(HelperActor actor)
        {
            if (actor.Bound || !actor.Spec.Motion.IsMoving)
            {
                return;
            }
            actor.Offset += new Vector2(actor.Velocity.x * FacingSign(), actor.Velocity.y);
            actor.Velocity += actor.Spec.Motion.Acceleration;
            actor.Go.transform.localPosition = HelperPosition(actor.Spec, FacingSign())
                + new Vector3(actor.Offset.x / PixelsPerUnit, -actor.Offset.y / PixelsPerUnit, 0f);
        }

        /// <summary>Löst die fälligen <c>PlaySnd</c>-Aufrufe eines Helfers aus.</summary>
        private void AdvanceHelperSounds(HelperActor actor)
        {
            if (m_SoundBank == null || m_AudioSource == null)
            {
                return;
            }
            FlareSequenceSound[] sounds = actor.Spec.Sounds;
            while (actor.NextSoundIndex < sounds.Length
                   && m_Elapsed >= sounds[actor.NextSoundIndex].Time / TicksPerSecond)
            {
                FlareSequenceSound sound = sounds[actor.NextSoundIndex];
                if (m_SoundBank.TryGet(sound.Group, sound.Index, out AudioClip clip))
                {
                    m_AudioSource.PlayOneShot(clip, Mathf.Clamp01(sound.Volume));
                }
                actor.NextSoundIndex++;
            }
        }

        /// <summary>Löst die Treffer eines Helfers aus — dort sitzt meist die HitDef.</summary>
        private void AdvanceHelperHits(HelperActor actor, int tick)
        {
            FlareSequenceHit[] hits = actor.Spec.Hits;
            while (actor.NextHitIndex < hits.Length && hits[actor.NextHitIndex].Time <= tick)
            {
                TriggerHit(hits[actor.NextHitIndex]);
                actor.NextHitIndex++;
            }
        }

        /// <summary>Setzt alle Helfer auf ihren Ausgangszustand zurück.</summary>
        private void ResetHelpers()
        {
            int facingSign = FacingSign();
            for (int i = 0; i < m_Helpers.Count; i++)
            {
                HelperActor actor = m_Helpers[i];
                actor.Visible = false;
                actor.Step = 0;
                actor.NextSoundIndex = 0;
                actor.NextHitIndex = 0;
                actor.Velocity = actor.Spec.Motion.Velocity;
                actor.Offset = Vector2.zero;
                actor.NextBindIndex = 0;
                actor.BindTicksLeft = 0;
                actor.BindOffset = Vector2.zero;
                actor.Bound = false;
                if (actor.Renderer != null)
                {
                    actor.Renderer.enabled = false;
                }
                if (actor.Go != null)
                {
                    actor.Go.transform.localPosition = HelperPosition(actor.Spec, facingSign);
                }
            }
        }

        /// <summary>Dreht die Helfer auf eine neue Blickrichtung.</summary>
        private void SetHelperDirection(int direction)
        {
            int facingSign = FacingSign();
            for (int i = 0; i < m_Helpers.Count; i++)
            {
                HelperActor actor = m_Helpers[i];
                actor.Anim?.SetDirection(direction);
                if (actor.Go != null)
                {
                    actor.Go.transform.localPosition = HelperPosition(actor.Spec, facingSign);
                }
            }
        }

        /// <summary>Zerstört alle Helfer-Akteure.</summary>
        private void DestroyHelpers()
        {
            for (int i = 0; i < m_Helpers.Count; i++)
            {
                if (m_Helpers[i].Go != null)
                {
                    Object.Destroy(m_Helpers[i].Go);
                }
            }
            m_Helpers.Clear();
        }

        /// <summary>Lokale Position eines Helfers; x ist facing-relativ, y zählt nach unten.</summary>
        private static Vector3 HelperPosition(FlareSequenceHelper spec, int facingSign)
        {
            return new Vector3(
                facingSign * spec.PosX / PixelsPerUnit,
                -spec.PosY / PixelsPerUnit,
                0f);
        }
    }
}
