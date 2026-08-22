using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Treffer-Teil der Move-Bühne: löst zum <c>HitDef</c>-Tick den Trefferfunken
    /// an der Vorderkante der Trainingspuppe aus, spielt den Trefferton und friert
    /// die Szene für <c>pausetime</c> ein.
    /// </summary>
    public sealed partial class MugenMoveStage
    {
        /// <summary>Sichtdauer eines Trefferfunkens, falls seine Animation endlos läuft.</summary>
        private const float SparkFallbackSeconds = 0.3f;

        private GameObject m_SparkGo;
        private FlareLayerAnimator m_SparkAnim;
        private SpriteRenderer m_SparkRenderer;
        private float m_SparkEndsAt;
        private MugenAfterImage m_AfterImage;
        private int m_NextAfterImageIndex;
        private int m_NextModifyIndex;
        private int m_NextBodyTransIndex;

        /// <summary>Löst fällige Treffer aus und blendet einen abgelaufenen Funken aus.</summary>
        private void AdvanceHits()
        {
            if (m_SparkRenderer != null && m_SparkRenderer.enabled && m_Elapsed >= m_SparkEndsAt)
            {
                m_SparkRenderer.enabled = false;
            }

            int tick = Mathf.FloorToInt(m_Elapsed * TicksPerSecond);
            while (m_NextHitIndex < m_Sequence.Hits.Length
                   && m_Sequence.Hits[m_NextHitIndex].Time <= tick)
            {
                TriggerHit(m_Sequence.Hits[m_NextHitIndex]);
                m_NextHitIndex++;
            }
        }

        /// <summary>
        /// Legt der Puppe fällige <c>ChangeAnim2</c>-Animationen auf. Damit zeigt
        /// die Showcase Würfe und Griffe so, wie MUGEN sie darstellt: das Opfer
        /// wird mit den Sprites des Angreifers gezeichnet.
        /// </summary>
        private void AdvanceVictim()
        {
            if (m_Dummy == null || m_Sequence.Victim.Length == 0)
            {
                return;
            }
            int tick = Mathf.FloorToInt(m_Elapsed * TicksPerSecond);
            while (m_NextVictimIndex < m_Sequence.Victim.Length
                   && m_Sequence.Victim[m_NextVictimIndex].Time <= tick)
            {
                m_Dummy.PlayVictim(m_Sequence.Victim[m_NextVictimIndex].Anim);
                m_NextVictimIndex++;
            }
        }

        /// <summary>Funke, Ton, Reaktion der Puppe und Trefferpause eines <c>HitDef</c>.</summary>
        private void TriggerHit(FlareSequenceHit hit)
        {
            if (hit.HasSound && m_SoundBank != null && m_AudioSource != null
                && m_SoundBank.TryGet(hit.SoundGroup, hit.SoundIndex, out AudioClip clip))
            {
                m_AudioSource.PlayOneShot(clip);
            }
            m_Dummy?.React(
                hit.Victim, FacingSign(),
                Mathf.Max(SparkFallbackSeconds, hit.PauseTime / TicksPerSecond));
            ShowSpark(hit);
            // `pausetime` friert Angreifer und Getroffenen gleichermassen ein.
            m_PauseTicksLeft = Mathf.Max(m_PauseTicksLeft, hit.PauseTime);
        }

        /// <summary>
        /// Setzt den Funken an die Vorderkante der Puppe. MUGENs <c>sparkxy</c>
        /// zählt von dort aus zurück Richtung Angreifer, y vom Boden nach oben.
        /// </summary>
        private void ShowSpark(FlareSequenceHit hit)
        {
            if (string.IsNullOrEmpty(hit.Spark) || m_Dummy == null)
            {
                return;
            }
            FlareAtlas atlas = m_BodyLayer.Atlas;
            if (atlas == null || !atlas.TryGet(hit.Spark, out FlareAnimation anim) || anim == null)
            {
                return;
            }

            EnsureSparkLayer(atlas);
            int facingSign = FacingSign();
            m_SparkGo.transform.localPosition = m_Dummy.LocalPosition + new Vector3(
                facingSign * hit.SparkX / PixelsPerUnit,
                -hit.SparkY / PixelsPerUnit,
                0f);
            m_SparkRenderer.enabled = true;
            m_SparkAnim.Play(hit.Spark, force: true);
            m_SparkEndsAt = m_Elapsed + Mathf.Max(SparkFallbackSeconds, anim.DurationSeconds);
        }

        /// <summary>Legt den wiederverwendeten Funken-Renderer an.</summary>
        private void EnsureSparkLayer(FlareAtlas atlas)
        {
            if (m_SparkGo != null)
            {
                return;
            }
            m_SparkGo = new GameObject("HitSpark");
            m_SparkGo.transform.SetParent(m_MugenSpace, worldPositionStays: false);

            m_SparkRenderer = m_SparkGo.AddComponent<SpriteRenderer>();
            m_SparkRenderer.sortingLayerID = m_BodyRenderer != null ? m_BodyRenderer.sortingLayerID : 0;
            // Der Funke liegt in MUGEN immer vor beiden Kämpfern.
            m_SparkRenderer.sortingOrder = short.MaxValue - 1;
            m_SparkRenderer.enabled = false;
            Material additive = m_Blends.Resolve(new FlareEffectBlend("add", 255, 255));
            if (additive != null)
            {
                m_SparkRenderer.sharedMaterial = additive;
            }

            m_SparkAnim = m_SparkGo.AddComponent<FlareLayerAnimator>();
            m_SparkAnim.SetBlendMaterials(m_Blends);
            m_SparkAnim.SetAtlas(atlas);
            m_SparkAnim.SetDirection(m_Direction);
        }

        /// <summary>Blendet den Funken aus und setzt die Treffer-Spur zurück.</summary>
        private void ResetHits()
        {
            m_NextHitIndex = 0;
            m_SparkEndsAt = 0f;
            if (m_SparkRenderer != null)
            {
                m_SparkRenderer.enabled = false;
            }
        }

        /// <summary>Startet fällige Nachzieh-Spuren und treibt die laufende weiter.</summary>
        private void AdvanceAfterImage(int tick)
        {
            while (m_NextAfterImageIndex < m_Sequence.AfterImage.Length
                   && m_Sequence.AfterImage[m_NextAfterImageIndex].Time <= tick)
            {
                FlareSequenceAfterImage spec = m_Sequence.AfterImage[m_NextAfterImageIndex];
                m_AfterImage ??= new MugenAfterImage(m_VisualRoot, m_BodyRenderer, m_Blends);
                if (spec.IsRetime)
                {
                    m_AfterImage.Retime(spec.Duration);
                }
                else
                {
                    m_AfterImage.Begin(spec);
                }
                m_NextAfterImageIndex++;
            }
            m_AfterImage?.Step();
        }

        /// <summary>Wendet fällige <c>ModifyExplod</c>-Änderungen auf ihre Effekte an.</summary>
        private void AdvanceModifies(int tick)
        {
            while (m_NextModifyIndex < m_Sequence.Modifies.Length
                   && m_Sequence.Modifies[m_NextModifyIndex].Time <= tick)
            {
                ApplyModify(m_Sequence.Modifies[m_NextModifyIndex]);
                m_NextModifyIndex++;
            }
        }

        /// <summary>Ändert alle Effekt-Layer mit der passenden MUGEN-ID.</summary>
        private void ApplyModify(FlareSequenceModify modify)
        {
            for (int i = 0; i < m_Layers.Count; i++)
            {
                EffectLayer layer = m_Layers[i];
                if (layer.Spec.Id != modify.Id || layer.Go == null)
                {
                    continue;
                }
                if (modify.Scale.HasValue)
                {
                    Vector2 scale = modify.Scale.Value;
                    layer.Go.transform.localScale = new Vector3(
                        layer.Spec.Facing * Mathf.Max(0.01f, scale.x),
                        layer.Spec.VFacing * Mathf.Max(0.01f, scale.y),
                        1f);
                }
                if (modify.Angle.HasValue)
                {
                    layer.Go.transform.localRotation = Quaternion.Euler(0f, 0f, modify.Angle.Value);
                }
                if (modify.SprPriority.HasValue && layer.Spec.Layer == 0)
                {
                    layer.Renderer.sortingOrder = SortingOrderFor(modify.SprPriority.Value, 1);
                }
                if (modify.Velocity.HasValue)
                {
                    layer.Velocity = modify.Velocity.Value;
                    layer.BindTicksLeft = 0;
                }
                if (modify.RemoveTime.HasValue && modify.RemoveTime.Value >= 0)
                {
                    layer.EndAt = m_Elapsed + modify.RemoveTime.Value / TicksPerSecond;
                }
            }
        }

        /// <summary>Setzt Nachzieh-Spur und Effekt-Änderungen zurück.</summary>
        private void ResetTrails()
        {
            m_NextAfterImageIndex = 0;
            m_NextModifyIndex = 0;
            m_NextBodyTransIndex = 0;
            m_AfterImage?.Stop();
            ApplyBodyBlend(s_DefaultBodyBlend);
        }

        /// <summary>
        /// Schaltet die Transparenz des Charakters auf den fälligen <c>Trans</c>-Wert.
        /// MUGEN setzt sie pro Tick neu; statisch gilt sie ab ihrem Tick weiter.
        /// </summary>
        private void AdvanceBodyTrans(int tick)
        {
            while (m_NextBodyTransIndex < m_Sequence.BodyTrans.Length
                   && m_Sequence.BodyTrans[m_NextBodyTransIndex].Time <= tick)
            {
                ApplyBodyBlend(m_Sequence.BodyTrans[m_NextBodyTransIndex].Blend);
                m_NextBodyTransIndex++;
            }
        }

        /// <summary>Setzt das Blend-Material des Körpers.</summary>
        private void ApplyBodyBlend(FlareEffectBlend blend)
        {
            Material material = m_Blends.Resolve(blend);
            if (material != null && m_BodyRenderer != null)
            {
                m_BodyRenderer.sharedMaterial = material;
            }
        }

        /// <summary>Zerstört den Funken-Renderer.</summary>
        private void DestroySpark()
        {
            if (m_SparkGo != null)
            {
                Object.Destroy(m_SparkGo);
                m_SparkGo = null;
                m_SparkAnim = null;
                m_SparkRenderer = null;
            }
            m_AfterImage?.Destroy();
            m_AfterImage = null;
        }
    }
}
