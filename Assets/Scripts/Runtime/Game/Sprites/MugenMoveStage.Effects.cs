using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Effekt-Teil der Move-Bühne: Aufbau der Explod-Layer, ihre Sichtbarkeit über
    /// die Zeit, ihre Eigenbewegung und die MUGEN-Regeln für Position, Schichtung
    /// und Lebensdauer.
    /// </summary>
    public sealed partial class MugenMoveStage
    {
        /// <summary>Erzeugt für jeden Effekt einen eigenen Renderer und liefert die Zyklusdauer.</summary>
        private float BuildEffects(FlareSequence sequence, FlareAtlas atlas)
        {
            if (atlas == null)
            {
                return 0f;
            }
            float cycle = 0f;
            foreach (FlareSequenceEffect fx in sequence.Effects)
            {
                cycle = Mathf.Max(cycle, CreateEffectLayer(fx, atlas, Vector2.zero));
            }
            return cycle;
        }

        /// <summary>
        /// Baut einen Effekt-Layer und liefert, bis wann er den Zyklus verlängert.
        /// <paramref name="extraOffset"/> verschiebt ihn in MUGEN-Pixeln — Effekte
        /// eines Helfers liegen relativ zu dessen Position, nicht zum Charakter.
        /// </summary>
        private float CreateEffectLayer(FlareSequenceEffect fx, FlareAtlas atlas, Vector2 extraOffset)
        {
            if (!m_Settings.ShowScreenEffects && IsScreenAnchored(fx.PosType))
            {
                return 0f;
            }
            if (!atlas.TryGet(fx.Anim, out FlareAnimation fxAnim) || fxAnim == null)
            {
                return 0f;
            }
            int facingSign = FacingSign();

            GameObject go = new($"Fx_{fx.Anim}");
            go.transform.SetParent(m_MugenSpace, worldPositionStays: false);
            Vector3 basePosition = ResolvePosition(fx, facingSign) + new Vector3(
                facingSign * extraOffset.x / PixelsPerUnit,
                -extraOffset.y / PixelsPerUnit,
                0f);
            go.transform.localPosition = basePosition;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, fx.Motion.Angle);
            // facing/vfacing spiegeln über das Vorzeichen der Skalierung — das
            // komponiert sauber mit der per-Zelle-Spiegelung des Animators.
            go.transform.localScale = new Vector3(
                fx.Facing * Mathf.Max(0.01f, fx.ScaleX),
                fx.VFacing * Mathf.Max(0.01f, fx.ScaleY),
                1f);

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerID = m_BodyRenderer != null ? m_BodyRenderer.sortingLayerID : 0;
            renderer.sortingOrder = ResolveSortingOrder(fx);
            renderer.enabled = false;
            Material blend = m_Blends.Resolve(fx.Blend);
            if (blend != null)
            {
                renderer.sharedMaterial = blend;
            }

            FlareLayerAnimator animator = go.AddComponent<FlareLayerAnimator>();
            // Ein explizites `trans` am Explod schlaegt den AIR-Blend der Animation.
            if (!fx.Blend.IsBlended)
            {
                animator.SetBlendMaterials(m_Blends);
            }
            animator.SetAtlas(atlas);
            animator.SetDirection(m_Direction);

            float startAt = fx.Time / TicksPerSecond;
            float period = fx.Period > 0 ? fx.Period / TicksPerSecond : 0f;
            float endAt = ResolveEndAt(fx, startAt, fxAnim.DurationSeconds);
            if (period > 0f)
            {
                // Ein periodischer Spawn darf seinen Nachfolger nicht überleben.
                endAt = Mathf.Min(endAt, startAt + period);
            }
            if (fx.RemoveAt >= 0)
            {
                endAt = Mathf.Min(endAt, fx.RemoveAt / TicksPerSecond);
            }

            EffectLayer layer = new()
            {
                Spec = fx,
                Go = go,
                Anim = animator,
                Renderer = renderer,
                StartAt = startAt,
                EndAt = endAt,
                PeriodSeconds = period,
                FirstStartAt = startAt,
                FirstEndAt = endAt,
                BasePosition = basePosition,
                FacingSign = facingSign,
                ExtraOffset = extraOffset,
            };
            ResetMotion(layer);
            m_Layers.Add(layer);

            return startAt + Mathf.Max(MinCycleSeconds, fxAnim.DurationSeconds);
        }

        /// <summary>Blendet Effekte zu ihrem Tick ein und nach <c>removetime</c> wieder aus.</summary>
        private void AdvanceEffects()
        {
            for (int i = 0; i < m_Layers.Count; i++)
            {
                EffectLayer layer = m_Layers[i];
                if (layer.Done)
                {
                    continue;
                }
                if (layer.Visible && m_Elapsed >= layer.EndAt)
                {
                    layer.Visible = false;
                    layer.Renderer.enabled = false;
                    if (layer.PeriodSeconds > 0f)
                    {
                        // MUGEN baut Dauerstrahlen, indem derselbe Explod alle N Ticks neu spawnt.
                        layer.StartAt += layer.PeriodSeconds;
                        layer.EndAt += layer.PeriodSeconds;
                    }
                    else
                    {
                        layer.Done = true;
                    }
                    continue;
                }
                if (!layer.Visible && m_Elapsed >= layer.StartAt && m_Elapsed < layer.EndAt)
                {
                    layer.Visible = true;
                    layer.Renderer.enabled = true;
                    layer.Anim.Play(layer.Spec.Anim, force: true);
                    ResetMotion(layer);
                }
            }
        }

        /// <summary>Setzt Geschwindigkeit, Offset und Bindung eines Layers auf den Spawn-Zustand.</summary>
        private static void ResetMotion(EffectLayer layer)
        {
            layer.Velocity = layer.Spec.Motion.Velocity;
            layer.Offset = Vector2.zero;
            layer.BindTicksLeft = layer.Spec.Motion.BindTicks;
            layer.HasHit = false;
            if (layer.Go != null)
            {
                layer.Go.transform.localPosition = layer.BasePosition;
            }
        }

        /// <summary>
        /// Integriert die Eigenbewegung sichtbarer Effekte um einen MUGEN-Tick,
        /// wie Ikemens <c>Explod.update</c>: erst Position, dann
        /// <c>vel = vel * friction + accel</c>. Solange die Bindung an den Charakter
        /// läuft, überschreibt MUGEN die Position jeden Tick — der Effekt bleibt
        /// also stehen und die Bindung zählt nur herunter.
        /// </summary>
        private void StepEffectMotion()
        {
            for (int i = 0; i < m_Layers.Count; i++)
            {
                EffectLayer layer = m_Layers[i];
                if (!layer.Visible || !layer.Spec.Motion.IsMoving)
                {
                    continue;
                }
                if (layer.BindTicksLeft != 0)
                {
                    layer.BindTicksLeft--;
                    continue;
                }

                layer.Offset += new Vector2(layer.Velocity.x * layer.FacingSign, layer.Velocity.y);
                Vector2 friction = layer.Spec.Motion.Friction;
                layer.Velocity = new Vector2(
                    layer.Velocity.x * friction.x + layer.Spec.Motion.Acceleration.x,
                    layer.Velocity.y * friction.y + layer.Spec.Motion.Acceleration.y);
                StopBelowFrictionThreshold(layer, friction);

                layer.Go.transform.localPosition = layer.BasePosition + new Vector3(
                    layer.Offset.x / PixelsPerUnit,
                    -layer.Offset.y / PixelsPerUnit,
                    0f);
                CheckProjectileImpact(layer);
            }
        }

        /// <summary>
        /// Löst den <c>HitDef</c> eines Projektils aus, sobald es den Gegner
        /// erreicht. MUGEN kennt dafür echte Kollisionsboxen; für die Showcase
        /// genügt der Moment, in dem das Geschoss die Standlinie der Puppe
        /// überquert — dort zeichnet MUGEN den Funken ohnehin hin.
        /// </summary>
        private void CheckProjectileImpact(EffectLayer layer)
        {
            if (layer.HasHit || m_OpponentOffsetX == 0f)
            {
                return;
            }
            if (layer.Spec.Hit == null && layer.Spec.HitAnim == null)
            {
                return;
            }
            float travelled = layer.Offset.x * layer.FacingSign;
            float distance = Mathf.Abs(m_OpponentOffsetX) - layer.FacingSign * layer.Spec.PosX;
            if (travelled < distance)
            {
                return;
            }
            layer.HasHit = true;
            if (layer.Spec.Hit != null)
            {
                TriggerHit(layer.Spec.Hit.Value);
            }
            PlayImpactAnimation(layer);
        }

        /// <summary>
        /// Schaltet ein eingeschlagenes Geschoss auf <c>projhitanim</c> um und hält
        /// es an, wie Ikemen im Status <c>ProjHit</c>. Ohne eigene Einschlag-Animation
        /// fliegt es unverändert weiter — auch das ist MUGENs Verhalten.
        /// </summary>
        private void PlayImpactAnimation(EffectLayer layer)
        {
            FlareAtlas atlas = m_BodyLayer.Atlas;
            if (layer.Spec.HitAnim == null || atlas == null
                || !atlas.TryGet(layer.Spec.HitAnim, out FlareAnimation impact) || impact == null)
            {
                return;
            }
            layer.Velocity = Vector2.zero;
            layer.Anim.Play(layer.Spec.HitAnim, force: true);
            layer.EndAt = m_Elapsed + Mathf.Max(MinCycleSeconds, impact.DurationSeconds);
        }

        /// <summary>
        /// MUGEN nullt eine abgebremste Achse, sobald sie unter 0.1 px/Tick fällt —
        /// sonst kröchen Effekte mit Reibung endlos weiter.
        /// </summary>
        private static void StopBelowFrictionThreshold(EffectLayer layer, Vector2 friction)
        {
            const float MinSpeed = 0.1f;
            float x = Mathf.Abs(friction.x) < 1f && Mathf.Abs(layer.Velocity.x) < MinSpeed ? 0f : layer.Velocity.x;
            float y = Mathf.Abs(friction.y) < 1f && Mathf.Abs(layer.Velocity.y) < MinSpeed ? 0f : layer.Velocity.y;
            layer.Velocity = new Vector2(x, y);
        }

        /// <summary>
        /// True für Postypes, die Ikemen als <c>Space_screen</c> behandelt: sie hängen
        /// am Bildschirmrand statt am Charakter (Vollbild-Flashes, Balken, Panels).
        /// </summary>
        private static bool IsScreenAnchored(string posType)
        {
            return posType is "left" or "right" or "front" or "back";
        }

        /// <summary>
        /// Facing-Vorzeichen aus der FLARE-Richtung: West-Richtungen (NW/W/SW)
        /// spiegeln die Effekt-X-Offsets, weil MUGENs <c>pos.x</c> facing-relativ ist.
        /// </summary>
        private int FacingSign()
        {
            return m_Direction == 0 || m_Direction == 1 || m_Direction == 7 ? -1 : 1;
        }

        /// <summary>
        /// Lokale Position eines Effekts nach seinem <c>postype</c>, entsprechend
        /// Ikemens <c>Explod.setPos</c> und <c>Char.helperPos</c>: <c>p1</c> hängt am
        /// Charakter, <c>p2</c> am Gegner, <c>left</c>/<c>right</c>/<c>front</c>/
        /// <c>back</c> sind Bildschirm-Anker und <c>none</c> ist eine absolute
        /// Stage-Koordinate ohne jeden Charakterbezug.
        /// </summary>
        private Vector3 ResolvePosition(FlareSequenceEffect fx, int facingSign)
        {
            float half = m_Settings.ScreenWidth * 0.5f;
            switch (fx.PosType)
            {
                case "left":
                    return ScreenPosition(-half + fx.PosX, fx.PosY);
                case "right":
                    return ScreenPosition(half + fx.PosX, fx.PosY);
                case "front":
                    return ScreenPosition((facingSign > 0 ? half : -half) + fx.PosX, fx.PosY);
                case "back":
                    return facingSign > 0
                        ? ScreenPosition(half - fx.PosX, fx.PosY)
                        : ScreenPosition(-half + fx.PosX, fx.PosY);
                case "p2":
                    // Der Gegner schaut zurück, also spiegelt sein Facing das x-Offset.
                    return new Vector3(
                        (m_OpponentOffsetX - facingSign * fx.PosX) / PixelsPerUnit,
                        -fx.PosY / PixelsPerUnit,
                        0f);
                case "none":
                    // Absolute Stage-Koordinate: keine Facing-Spiegelung.
                    return new Vector3(
                        (fx.PosX - m_Settings.CameraOffsetX) / PixelsPerUnit,
                        -fx.PosY / PixelsPerUnit,
                        0f);
                default:
                    // MUGEN-Charakterraum: x facing-relativ, y zählt nach unten.
                    return new Vector3(
                        facingSign * fx.PosX / PixelsPerUnit,
                        -fx.PosY / PixelsPerUnit,
                        0f);
            }
        }

        /// <summary>
        /// MUGEN-Bildschirmkoordinate (x ab Bildmitte, y ab Bildoberkante nach unten)
        /// als lokale Position relativ zum Bodenpunkt des Charakters. Der Charakter
        /// steht auf <c>p1startx</c> und nicht in der Bildmitte — ohne diesen Abzug
        /// wandern Hyper-Panels und Vollbild-Balken um genau diesen Betrag zur Seite.
        /// </summary>
        private Vector3 ScreenPosition(float pixelX, float pixelY)
        {
            return new Vector3(
                (pixelX - m_Settings.CameraOffsetX) / PixelsPerUnit,
                (m_Settings.GroundLevel - pixelY) / PixelsPerUnit,
                0f);
        }

        /// <summary>Über allem — auch über den Vordergrundebenen der Stage.</summary>
        private const int OnTopOrder = 31000;

        /// <summary>Unter dem Charakter, aber noch vor dem Stage-Hintergrund.</summary>
        private const int UnderOrder = -20000;

        /// <summary>
        /// Alle im Move vorkommenden <c>sprpriority</c>-Werte, aufsteigend und
        /// doppelfrei. Der Index in dieser Liste ist die Sortierstufe.
        /// </summary>
        private readonly List<int> m_PriorityRanks = new();

        /// <summary>
        /// Sammelt die Prioritätsstufen des Moves. Ikemen sortiert Charakter und
        /// Effekte in EINER Liste aufsteigend nach <c>sprpriority</c>
        /// (<c>anim.go: DrawList.draw</c>), die Absolutwerte sind bedeutungslos.
        /// MUGEN-Autoren schreiben deshalb Werte wie 99999999 — als Unity-
        /// <c>sortingOrder</c> würde das überlaufen. Darum werden die Werte auf
        /// dichte Stufen verdichtet.
        /// </summary>
        private void BuildPriorityRanks(FlareSequence sequence)
        {
            m_PriorityRanks.Clear();
            // 0 ist MUGENs Vorgabe für einen Charakter ohne `sprpriority`.
            AddPriorityRank(0);
            foreach (FlareSequenceSprPriority prio in sequence.SprPriority)
            {
                AddPriorityRank(prio.Value);
            }
            foreach (FlareSequenceEffect fx in sequence.Effects)
            {
                AddPriorityRank(fx.Layer == 0 ? fx.SprPriority : 0);
            }
            foreach (FlareSequenceModify modify in sequence.Modifies)
            {
                if (modify.SprPriority.HasValue)
                {
                    AddPriorityRank(modify.SprPriority.Value);
                }
            }
            foreach (FlareSequenceHelper helper in sequence.Helpers)
            {
                AddPriorityRank(helper.SprPriority);
                foreach (FlareSequenceEffect fx in helper.Effects)
                {
                    AddPriorityRank(fx.Layer == 0 ? fx.SprPriority : 0);
                }
            }
            m_PriorityRanks.Sort();
        }

        /// <summary>Nimmt einen Prioritätswert auf, wenn er noch nicht bekannt ist.</summary>
        private void AddPriorityRank(int value)
        {
            if (!m_PriorityRanks.Contains(value))
            {
                m_PriorityRanks.Add(value);
            }
        }

        /// <summary>Sortierstufe eines Prioritätswerts (0 = hinterste Stufe).</summary>
        private int PriorityRank(int value)
        {
            int index = m_PriorityRanks.BinarySearch(value);
            return index >= 0 ? index : Mathf.Clamp(~index, 0, m_PriorityRanks.Count - 1);
        }

        /// <summary>
        /// Sortier-Order einer Prioritätsstufe. <paramref name="bias"/> löst
        /// Gleichstand auf: MUGEN zeichnet einen Effekt gleicher
        /// <c>sprpriority</c> vor dem Körper, weil er später in der Zeichenliste
        /// entsteht.
        /// </summary>
        private int SortingOrderFor(int sprPriority, int bias)
        {
            return m_BodySortingOrder + (PriorityRank(sprPriority) * SortingSpread) + bias;
        }

        /// <summary>
        /// Sortier-Order eines Effekts. <c>ontop</c>/<c>under</c> übersteuern die
        /// Priorität komplett — Ikemen zieht diese Layer in eigene Durchgänge vor
        /// bzw. hinter den gesamten Hauptdurchgang (<c>system.go: drawTop</c>).
        /// </summary>
        private int ResolveSortingOrder(FlareSequenceEffect fx)
        {
            if (fx.Layer > 0)
            {
                return OnTopOrder;
            }
            if (fx.Layer < 0)
            {
                return UnderOrder;
            }
            return SortingOrderFor(fx.SprPriority, 1);
        }

        /// <summary>
        /// Setzt die Zeichenpriorität des Körpers auf den zum Tick geltenden Wert.
        /// Ohne das läge der Charakter bei Hypern hinter seinem eigenen
        /// Vollbild-Hintergrundpanel.
        /// </summary>
        private void AdvanceSprPriority(int tick)
        {
            while (m_NextPriorityIndex < m_Sequence.SprPriority.Length
                   && m_Sequence.SprPriority[m_NextPriorityIndex].Time <= tick)
            {
                ApplyBodyPriority(m_Sequence.SprPriority[m_NextPriorityIndex].Value);
                m_NextPriorityIndex++;
            }
        }

        /// <summary>Schreibt eine Prioritätsstufe auf Körper und Trainingspuppe.</summary>
        private void ApplyBodyPriority(int sprPriority)
        {
            int order = SortingOrderFor(sprPriority, 0);
            if (m_BodyRenderer != null)
            {
                m_BodyRenderer.sortingOrder = order;
            }
            m_Dummy?.SetSortingOrder(order);
        }

        /// <summary>
        /// Zeitpunkt, zu dem der Effekt verschwindet. Ikemen <c>Explod.update</c>:
        /// <c>removetime</c> &gt;= 0 nach so vielen Ticks, -1 nie, &lt;= -2 sobald
        /// die Animation durchgelaufen ist.
        /// </summary>
        private float ResolveEndAt(FlareSequenceEffect fx, float startAt, float animDuration)
        {
            if (fx.RemoveTime >= 0)
            {
                float ticks = fx.RemoveTime / TicksPerSecond;
                return startAt + Mathf.Max(ticks, m_Settings.MinEffectVisibleSeconds);
            }
            if (fx.RemoveTime <= -2)
            {
                return startAt + Mathf.Max(m_Settings.MinEffectVisibleSeconds, animDuration);
            }
            return float.MaxValue;
        }
    }
}
