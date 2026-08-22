using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Spielt einen MUGEN-Move so ab, wie die Engine ihn darstellt: die Body-Kette
    /// wechselt per <c>ChangeAnim</c> über die Zeit, jeder <c>Explod</c>/
    /// <c>Projectile</c>/<c>Helper</c> erscheint zu seinem Tick an seiner Position,
    /// und jeder <c>PlaySnd</c> wird zum selben Tick ausgelöst.
    /// </summary>
    /// <remarks>
    /// Reine Darstellungs-Klasse ohne MonoBehaviour: der Owner treibt sie über
    /// <see cref="Tick"/> und räumt sie über <see cref="Dispose"/> ab. Die Zeitachse
    /// ist selbst geführt (nicht <c>Time.time</c>), damit Pausieren und
    /// Zeitlupe ohne Sonderfälle funktionieren.
    /// </remarks>
    public sealed partial class MugenMoveStage : System.IDisposable
    {
        /// <summary>MUGEN simuliert mit fester Tickrate; alle <c>time</c>-Werte sind Ticks.</summary>
        public const float TicksPerSecond = 60f;

        /// <summary>Umrechnung MUGEN-Pixel → Unity-Einheiten (muss zum Atlas passen).</summary>
        private const float PixelsPerUnit = 64f;

        /// <summary>Abstand zweier MUGEN-Prioritätsstufen in Unity-Sortier-Ordern.</summary>
        private const int SortingSpread = 2;

        /// <summary>Kürzester sinnvoller Zyklus, damit ein leerer Move nicht durchdreht.</summary>
        private const float MinCycleSeconds = 0.25f;

        /// <summary>Normales Alpha-Blending — der Ausgangszustand des Körpers.</summary>
        private static readonly FlareEffectBlend s_DefaultBodyBlend = new("none", 255, 0);

        /// <summary>Darstellungs-Parameter der Bühne.</summary>
        public struct Settings
        {
            /// <summary>
            /// Mindest-Sichtdauer eines Effekts. MUGEN-Glows mit <c>removetime = 1</c>
            /// leben ~17 ms und wären sonst unsichtbar. 0 = exakte MUGEN-Zeiten.
            /// </summary>
            public float MinEffectVisibleSeconds;
            /// <summary>MUGEN-Bildschirmbreite (<c>localcoord</c>) für Screen-Postypes.</summary>
            public float ScreenWidth;
            /// <summary>Bildschirm-Y des Bodens in MUGEN-Pixeln (y zählt von oben).</summary>
            public float GroundLevel;
            /// <summary>
            /// Standposition des Charakters im Kameraraum (<c>p1startx</c> der Stage).
            /// Der Kämpfer steht nicht in der Bildmitte, deshalb muss dieser Versatz
            /// aus jeder bildschirmverankerten Position herausgerechnet werden.
            /// </summary>
            public float CameraOffsetX;
            /// <summary>
            /// Wenn false, bleiben bildschirmverankerte Effekte (<c>left</c>/<c>right</c>/
            /// <c>front</c>/<c>back</c>) aus — das sind Vollbild-Flashes und Balken,
            /// die den Charakter im Showcase sonst zudecken.
            /// </summary>
            public bool ShowScreenEffects;
            /// <summary>Abstand der Trainingspuppe in MUGEN-Pixeln; 0 = keine Puppe.</summary>
            public float DummyDistance;
        }

        /// <summary>Ein gespawnter Effekt mit eigenem Renderer und eigener Zeitspanne.</summary>
        public sealed class EffectLayer
        {
            /// <summary>Die MUGEN-Spezifikation, aus der dieser Layer gebaut wurde.</summary>
            public FlareSequenceEffect Spec;
            /// <summary>Das erzeugte GameObject (Kind der Visuals-Wurzel).</summary>
            public GameObject Go;
            /// <summary>Animator dieses Layers.</summary>
            public FlareLayerAnimator Anim;
            /// <summary>Renderer dieses Layers.</summary>
            public SpriteRenderer Renderer;
            /// <summary>Nächster Einblendzeitpunkt in Sekunden ab Zyklusstart.</summary>
            public float StartAt;
            /// <summary>Nächster Ausblendzeitpunkt in Sekunden ab Zyklusstart.</summary>
            public float EndAt;
            /// <summary>Wiederholungsabstand in Sekunden (0 = einmalig).</summary>
            public float PeriodSeconds;
            /// <summary>True, solange der Layer sichtbar ist.</summary>
            public bool Visible;
            /// <summary>True, wenn der Layer in diesem Zyklus nicht mehr erscheint.</summary>
            public bool Done;

            // Ausgangswerte, weil StartAt/EndAt bei periodischen Spawnern weiterwandern.
            internal float FirstStartAt;
            internal float FirstEndAt;
            // Eigenbewegung in MUGEN-Pixeln, integriert pro Tick ab Spawn.
            internal Vector3 BasePosition;
            internal Vector2 Offset;
            internal Vector2 Velocity;
            internal int BindTicksLeft;
            internal int FacingSign;
            // True, sobald der HitDef eines Projektils in diesem Flug ausgelöst hat.
            internal bool HasHit;
            // Verschiebung durch den Helfer, der diesen Effekt gespawnt hat.
            internal Vector2 ExtraOffset;
        }

        /// <summary>Ein <c>PlaySnd</c>-Aufruf mit vorgeladenem Clip.</summary>
        private sealed class SoundCue
        {
            public FlareSequenceSound Spec;
            public AudioClip Clip;
            public float NextAt;
            public bool Done;
        }

        private readonly FlareCharacter m_Character;
        private readonly FlareLayerAnimator m_BodyLayer;
        private readonly SpriteRenderer m_BodyRenderer;
        private readonly Transform m_VisualRoot;
        private readonly MugenSoundBank m_SoundBank;
        private readonly AudioSource m_AudioSource;
        private readonly MugenBlendMaterialCache m_Blends = new();
        private readonly MugenPalFxApplier m_PalFx = new();
        private readonly List<EffectLayer> m_Layers = new();
        private readonly List<SoundCue> m_Cues = new();

        private readonly int m_BodySortingOrder;
        private readonly Transform m_MugenSpace;
        private readonly Quaternion m_BodyBaseRotation;
        private readonly Vector3 m_BodyBaseScale;
        private Settings m_Settings;
        private FlareSequence m_Sequence;
        private int m_Direction;
        private float m_Elapsed;
        private float m_TickCarry;
        private int m_BodyStep;
        private bool m_PalFxActive;
        // Charakter-Eigenbewegung in MUGEN-Pixeln, y nach unten.
        private Vector3 m_BodyOrigin;
        private Vector2 m_BodyOffset;
        private Vector2 m_BodyVelocity;
        private int m_NextMoveIndex;
        private int m_NextPauseIndex;
        private int m_NextHitIndex;
        private int m_NextVictimIndex;
        private int m_NextPriorityIndex;
        private int m_NextPhysicsIndex;
        private FlarePhysics m_Physics = FlarePhysics.None;
        private int m_NextAngleIndex;
        private int m_PauseTicksLeft;
        private float m_Gravity = 0.5f;
        private float m_StandFriction = 0.85f;
        private float m_CrouchFriction = 0.82f;
        private MugenHitDummy m_Dummy;

        /// <summary>X-Versatz des Gegners in MUGEN-Pixeln; 0, wenn keine Puppe steht.</summary>
        private float m_OpponentOffsetX;

        /// <summary>Der aktuell aufgebaute Move (<c>null</c>, solange nichts gebaut ist).</summary>
        public FlareSequence Sequence => m_Sequence;

        /// <summary>Sekunden seit Zyklusstart.</summary>
        public float Elapsed => m_Elapsed;

        /// <summary>Gesamtdauer eines Durchlaufs in Sekunden.</summary>
        public float CycleSeconds { get; private set; }

        /// <summary>Index des aktuell dargestellten Body-Ketten-Glieds.</summary>
        public int BodyStep => m_BodyStep;

        /// <summary>Alle gespawnten Effekt-Layer des aktuellen Moves.</summary>
        public IReadOnlyList<EffectLayer> Layers => m_Layers;

        /// <summary>
        /// Ungeskalierter MUGEN-Pixelraum über dem Bodenpunkt des Charakters. Der
        /// Charakter selbst wird mit <c>size.xscale</c> gezeichnet, alles andere —
        /// Explods, Projektile, Funken, die Gegenseite und die Stage — nicht.
        /// </summary>
        public Transform MugenSpace => m_MugenSpace;

        /// <summary>
        /// Erzeugt eine Bühne über einem bereits aufgebauten Charakter.
        /// </summary>
        /// <param name="character">Steuert die Body-Animation.</param>
        /// <param name="bodyLayer">Die Sprite-Schicht des Körpers; ihr Transform
        /// definiert den Raum (Skalierung/Rotation), in dem Effekte platziert werden.</param>
        /// <param name="soundBank">Sample-Quelle des Charakters, darf <c>null</c> sein.</param>
        /// <param name="audioSource">Ausgabe für <c>PlaySnd</c>, darf <c>null</c> sein.</param>
        public MugenMoveStage(
            FlareCharacter character,
            FlareLayerAnimator bodyLayer,
            MugenSoundBank soundBank,
            AudioSource audioSource)
        {
            m_Character = character;
            m_BodyLayer = bodyLayer;
            m_VisualRoot = bodyLayer.transform;
            m_BodyRenderer = bodyLayer.GetComponent<SpriteRenderer>();
            m_SoundBank = soundBank;
            m_AudioSource = audioSource;
            m_BodyOrigin = m_VisualRoot.localPosition;
            m_BodyBaseRotation = m_VisualRoot.localRotation;
            m_BodyBaseScale = m_VisualRoot.localScale;

            // Ikemen zieht size.xscale nur über das Charakter-Sprite (char.go: drawscale =
            // basescale * c.size.xscale). Explods bekommen ihre Größe aus dem eigenen
            // scale-Parameter. Dieser Knoten hebt die Charakter-Skalierung wieder auf,
            // damit darunter echte MUGEN-Pixel gelten.
            GameObject space = new("MugenSpace");
            m_MugenSpace = space.transform;
            m_MugenSpace.SetParent(m_VisualRoot, worldPositionStays: false);
            m_MugenSpace.localScale = InverseScale(m_VisualRoot.localScale);

            // Der Körper bekommt Luft nach oben, damit Effekte gleicher MUGEN-Priorität
            // davor einsortiert werden können, statt mit ihm zu kollidieren.
            m_BodySortingOrder = m_BodyRenderer != null ? m_BodyRenderer.sortingOrder : 0;
            m_BodyLayer.SetBlendMaterials(m_Blends);
            if (m_BodyRenderer != null)
            {
                // Der Body braucht denselben Shader wie die Effekte, sonst greift PalFX nicht.
                Material bodyMaterial = m_Blends.Resolve(s_DefaultBodyBlend);
                if (bodyMaterial != null)
                {
                    m_BodyRenderer.sharedMaterial = bodyMaterial;
                }
            }
        }

        /// <summary>
        /// Baut den Move auf: Effekt-Layer erzeugen, Sounds vorladen, Zyklus starten.
        /// Ein vorher gebauter Move wird vollständig abgeräumt.
        /// </summary>
        public async Task BuildAsync(FlareSequence sequence, int direction, Settings settings)
        {
            Clear();
            if (sequence == null)
            {
                return;
            }

            m_Sequence = sequence;
            m_Direction = direction;
            m_Settings = settings;

            FlareAtlas atlas = m_BodyLayer.Atlas;
            m_Gravity = atlas != null ? atlas.Gravity : 0.5f;
            m_StandFriction = atlas != null ? atlas.StandFriction : 0.85f;
            m_CrouchFriction = atlas != null ? atlas.CrouchFriction : 0.82f;
            BuildPriorityRanks(sequence);
            EnsureDummy(atlas);
            float cycle = BuildBody(sequence, atlas);
            cycle = Mathf.Max(cycle, BuildEffects(sequence, atlas));
            cycle = Mathf.Max(cycle, BuildHelpers(sequence, atlas));
            cycle = Mathf.Max(cycle, await BuildSoundsAsync(sequence));
            await PreloadHitSoundsAsync(sequence);
            await PreloadHelperSoundsAsync(sequence);

            CycleSeconds = Mathf.Max(MinCycleSeconds, cycle);
            Restart();
        }

        /// <summary>
        /// Legt die Trainingspuppe an, sobald ein Abstand konfiguriert ist. Sie
        /// erscheint nur bei Moves, die den Gegner treffen oder ihm eine Animation
        /// aufzwingen — sonst stünde sie sinnlos im Bild und verdeckte den Charakter.
        /// </summary>
        private void EnsureDummy(FlareAtlas atlas)
        {
            bool wanted = atlas != null && m_Settings.DummyDistance > 0f && SequenceNeedsOpponent();
            m_OpponentOffsetX = wanted ? FacingSign() * m_Settings.DummyDistance : 0f;
            if (!wanted)
            {
                m_Dummy?.SetVisible(false);
                return;
            }
            m_Dummy ??= new MugenHitDummy(
                m_MugenSpace, atlas, m_Settings.DummyDistance,
                m_BodySortingOrder, m_Blends, m_VisualRoot.localScale);
            m_Dummy.SetVisible(true);
            m_Dummy.SetAttackerFacing(m_Direction, m_Settings.DummyDistance);
        }

        /// <summary>
        /// True, wenn der Move eine Gegenseite braucht: er trifft irgendwo oder er
        /// zwingt dem Gegner eine Animation auf. Bei den meisten Angriffen hängt die
        /// <c>HitDef</c> nicht am Charakter, sondern an einem seiner Helfer.
        /// </summary>
        private bool SequenceNeedsOpponent()
        {
            if (m_Sequence.Hits.Length > 0 || m_Sequence.Victim.Length > 0)
            {
                return true;
            }
            if (HasProjectileHit(m_Sequence.Effects))
            {
                return true;
            }
            foreach (FlareSequenceHelper helper in m_Sequence.Helpers)
            {
                if (helper.Hits.Length > 0 || HasProjectileHit(helper.Effects))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn eines der Geschosse beim Einschlag einen Treffer auslöst.</summary>
        private static bool HasProjectileHit(FlareSequenceEffect[] effects)
        {
            foreach (FlareSequenceEffect fx in effects)
            {
                if (fx.Hit != null || fx.HitAnim != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Lädt die Treffergeräusche vor, damit sie zum Tick sofort spielen.</summary>
        private async Task PreloadHitSoundsAsync(FlareSequence sequence)
        {
            if (m_SoundBank == null)
            {
                return;
            }
            foreach (FlareSequenceHit hit in sequence.Hits)
            {
                if (hit.HasSound)
                {
                    await m_SoundBank.LoadAsync(hit.SoundGroup, hit.SoundIndex);
                }
            }
            foreach (FlareSequenceEffect fx in sequence.Effects)
            {
                if (fx.Hit != null && fx.Hit.Value.HasSound)
                {
                    await m_SoundBank.LoadAsync(fx.Hit.Value.SoundGroup, fx.Hit.Value.SoundIndex);
                }
            }
        }

        /// <summary>Treibt die Zeitachse um <paramref name="deltaTime"/> Sekunden weiter.</summary>
        public void Tick(float deltaTime)
        {
            if (m_Sequence == null)
            {
                return;
            }
            // SuperPause friert die ganze Szene ein; nur die Pause selbst läuft weiter.
            if (m_PauseTicksLeft > 0)
            {
                m_TickCarry += deltaTime * TicksPerSecond;
                while (m_TickCarry >= 1f && m_PauseTicksLeft > 0)
                {
                    m_TickCarry -= 1f;
                    m_PauseTicksLeft--;
                }
                return;
            }

            m_Elapsed += deltaTime;
            if (m_Elapsed >= CycleSeconds)
            {
                Restart();
                return;
            }
            AdvanceBodyChain();
            AdvanceEffects();
            AdvanceSounds();
            AdvancePalFx();
            AdvanceHits();
            AdvanceVictim();
            m_Dummy?.Tick(deltaTime);

            // Eigenbewegung integriert MUGEN in ganzen Ticks, nicht in Frame-Zeit.
            m_TickCarry += deltaTime * TicksPerSecond;
            while (m_TickCarry >= 1f)
            {
                m_TickCarry -= 1f;
                int tick = Mathf.FloorToInt(m_Elapsed * TicksPerSecond);
                StepEffectMotion();
                StepBodyMotion();
                m_Dummy?.StepMotion();
                AdvanceSprPriority(tick);
                AdvanceBodyAngle(tick);
                AdvanceModifies(tick);
                AdvanceAfterImage(tick);
                AdvanceBodyTrans(tick);
                AdvanceTurns(tick);
                AdvanceInvisible(tick);
                AdvanceHelpers(tick);
            }
            EnterPauseIfDue();
        }

        /// <summary>Startet Body, Effekte und Sounds synchron neu.</summary>
        public void Restart()
        {
            m_Elapsed = 0f;
            m_TickCarry = 0f;
            m_BodyStep = 0;
            m_BodyOffset = Vector2.zero;
            m_BodyVelocity = Vector2.zero;
            m_NextMoveIndex = 0;
            m_NextPauseIndex = 0;
            m_PauseTicksLeft = 0;
            m_NextVictimIndex = 0;
            m_NextPriorityIndex = 0;
            m_NextPhysicsIndex = 0;
            m_Physics = m_Sequence.Physics;
            m_NextAngleIndex = 0;
            // Ohne `sprpriority` im Statedef-Kopf startet MUGEN bei 0.
            ApplyBodyPriority(0);
            AdvanceSprPriority(0);
            ApplyBodyAngle(0f, Vector2.one);
            AdvanceBodyAngle(0);
            m_Dummy?.ResetToIdle();
            ResetBodyState();
            ResetHits();
            ResetTrails();
            ResetHelpers();
            m_VisualRoot.localPosition = m_BodyOrigin;

            if (m_Sequence != null && m_Sequence.Anims.Length > 0)
            {
                m_Character.SetDirection(m_Direction);
                m_Character.Play(m_Sequence.Anims[0].Anim, force: true);
            }

            for (int i = 0; i < m_Layers.Count; i++)
            {
                EffectLayer layer = m_Layers[i];
                layer.StartAt = layer.FirstStartAt;
                layer.EndAt = layer.FirstEndAt;
                layer.Visible = false;
                layer.Done = false;
                ResetMotion(layer);
                if (layer.Renderer != null)
                {
                    layer.Renderer.enabled = false;
                }
            }
            for (int i = 0; i < m_Cues.Count; i++)
            {
                m_Cues[i].NextAt = m_Cues[i].Spec.Time / TicksPerSecond;
                m_Cues[i].Done = false;
            }
            m_PalFxActive = false;
            if (m_Sequence.PalFx.Length > 0)
            {
                ClearPalFx();
            }
            if (m_AudioSource != null)
            {
                m_AudioSource.Stop();
            }
        }

        /// <summary>Dreht Charakter und Effekte auf eine neue FLARE-Richtung.</summary>
        public void SetDirection(int direction)
        {
            m_BaseDirection = direction;
            ApplyDirection(direction);
        }

        /// <summary>
        /// Wendet eine Blickrichtung an, ohne die Ausgangsrichtung des Moves zu
        /// vergessen — <c>Turn</c> nutzt denselben Weg wie die Bedienung.
        /// </summary>
        private void ApplyDirection(int direction)
        {
            m_Direction = direction;
            m_Character.SetDirection(direction);
            m_SparkAnim?.SetDirection(direction);
            SetHelperDirection(direction);
            m_Dummy?.SetAttackerFacing(direction, m_Settings.DummyDistance);

            int facingSign = FacingSign();
            for (int i = 0; i < m_Layers.Count; i++)
            {
                EffectLayer layer = m_Layers[i];
                if (layer.Anim != null)
                {
                    layer.Anim.SetDirection(direction);
                }
                if (layer.Go != null)
                {
                    layer.FacingSign = facingSign;
                    layer.BasePosition = ResolvePosition(layer.Spec, facingSign) + new Vector3(
                        facingSign * layer.ExtraOffset.x / PixelsPerUnit,
                        -layer.ExtraOffset.y / PixelsPerUnit,
                        0f);
                    layer.Go.transform.localPosition = layer.BasePosition;
                }
            }
        }

        /// <summary>Zerstört alle Effekt-Layer und verwirft den aufgebauten Move.</summary>
        public void Clear()
        {
            if (m_PalFxActive)
            {
                m_PalFx.Clear(m_BodyRenderer);
            }
            m_PalFxActive = false;
            m_VisualRoot.localPosition = m_BodyOrigin;
            for (int i = 0; i < m_Layers.Count; i++)
            {
                if (m_Layers[i].Go != null)
                {
                    Object.Destroy(m_Layers[i].Go);
                }
            }
            m_Layers.Clear();
            DestroyHelpers();
            m_Cues.Clear();
            m_Sequence = null;
            m_Elapsed = 0f;
            m_BodyStep = 0;
            CycleSeconds = MinCycleSeconds;
            if (m_AudioSource != null)
            {
                m_AudioSource.Stop();
            }
        }

        /// <summary>Räumt Layer und geteilte Materialien ab.</summary>
        public void Dispose()
        {
            Clear();
            DestroySpark();
            m_Dummy?.Destroy();
            m_Dummy = null;
            m_Blends.Clear();
            if (m_MugenSpace != null)
            {
                Object.Destroy(m_MugenSpace.gameObject);
            }
        }

        /// <summary>Kehrwert je Achse; 0 bleibt 0, damit kein Unendlich entsteht.</summary>
        private static Vector3 InverseScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Approximately(scale.x, 0f) ? 1f : 1f / scale.x,
                Mathf.Approximately(scale.y, 0f) ? 1f : 1f / scale.y,
                Mathf.Approximately(scale.z, 0f) ? 1f : 1f / scale.z);
        }

        /// <summary>
        /// Übernimmt jeden bis <paramref name="tick"/> fälligen Winkelwechsel des
        /// Charakters (<c>AngleSet</c>/<c>AngleDraw</c>).
        /// </summary>
        private void AdvanceBodyAngle(int tick)
        {
            while (m_NextAngleIndex < m_Sequence.BodyAngle.Length
                   && m_Sequence.BodyAngle[m_NextAngleIndex].Time <= tick)
            {
                FlareSequenceBodyAngle entry = m_Sequence.BodyAngle[m_NextAngleIndex];
                ApplyBodyAngle(entry.Angle, entry.Scale);
                m_NextAngleIndex++;
            }
        }

        /// <summary>
        /// Dreht und skaliert das Charakter-Sprite. Ikemen zieht <c>angledraw</c>
        /// nur über den Charakter, nicht über seine Explods — deshalb hält der
        /// MUGEN-Raum darunter die Drehung und die Skalierung wieder heraus.
        /// </summary>
        private void ApplyBodyAngle(float angle, Vector2 scale)
        {
            m_VisualRoot.localRotation = m_BodyBaseRotation * Quaternion.Euler(0f, 0f, angle);
            m_VisualRoot.localScale = new Vector3(
                m_BodyBaseScale.x * scale.x,
                m_BodyBaseScale.y * scale.y,
                m_BodyBaseScale.z);
            m_MugenSpace.localRotation = Quaternion.Euler(0f, 0f, -angle);
            m_MugenSpace.localScale = InverseScale(m_VisualRoot.localScale);
        }

        // -------------------------------------------------------------------------
        // Aufbau
        // -------------------------------------------------------------------------

        /// <summary>Startet das erste Body-Glied und liefert die Dauer der Kette.</summary>
        private float BuildBody(FlareSequence sequence, FlareAtlas atlas)
        {
            if (sequence.Anims.Length == 0)
            {
                return 0f;
            }
            FlareSequenceAnim last = sequence.Anims[sequence.Anims.Length - 1];
            float tail = atlas != null && atlas.TryGet(last.Anim, out FlareAnimation anim) && anim != null
                ? anim.DurationSeconds
                : 0f;
            return last.Time / TicksPerSecond + tail;
        }

        /// <summary>Lädt die Samples des Moves vor und liefert das späteste Cue als Zyklusdauer.</summary>
        private async Task<float> BuildSoundsAsync(FlareSequence sequence)
        {
            if (m_SoundBank == null || m_AudioSource == null || sequence.Sounds.Length == 0)
            {
                return 0f;
            }
            float cycle = 0f;
            foreach (FlareSequenceSound sound in sequence.Sounds)
            {
                AudioClip clip = await m_SoundBank.LoadAsync(sound.Group, sound.Index);
                if (clip == null)
                {
                    continue;
                }
                float startAt = sound.Time / TicksPerSecond;
                m_Cues.Add(new SoundCue { Spec = sound, Clip = clip, NextAt = startAt });
                cycle = Mathf.Max(cycle, startAt + clip.length);
            }
            return cycle;
        }

        // -------------------------------------------------------------------------
        // Zeitachse
        // -------------------------------------------------------------------------

        /// <summary>Schaltet den Körper auf das nächste ChangeAnim-Glied seiner Kette.</summary>
        private void AdvanceBodyChain()
        {
            FlareSequenceAnim[] chain = m_Sequence.Anims;
            while (m_BodyStep + 1 < chain.Length
                   && m_Elapsed >= chain[m_BodyStep + 1].Time / TicksPerSecond)
            {
                m_BodyStep++;
                m_Character.Play(chain[m_BodyStep].Anim, force: true);
            }
        }


        /// <summary>
        /// Färbt Körper und — bei <c>AllPalFX</c> — auch die Effekte mit dem gerade
        /// wirkenden Filter. Überlappen mehrere, gewinnt der zuletzt gestartete,
        /// wie in MUGENs Controller-Reihenfolge.
        /// </summary>
        private void AdvancePalFx()
        {
            if (m_Sequence.PalFx.Length == 0)
            {
                return;
            }
            int tick = Mathf.FloorToInt(m_Elapsed * TicksPerSecond);
            int activeIndex = -1;
            for (int i = 0; i < m_Sequence.PalFx.Length; i++)
            {
                FlareSequencePalFx candidate = m_Sequence.PalFx[i];
                bool started = tick >= candidate.Time;
                bool expired = candidate.Duration >= 0 && tick >= candidate.Time + candidate.Duration;
                if (started && !expired)
                {
                    activeIndex = i;
                }
            }

            if (activeIndex < 0)
            {
                if (m_PalFxActive)
                {
                    m_PalFxActive = false;
                    ClearPalFx();
                }
                return;
            }

            FlareSequencePalFx palFx = m_Sequence.PalFx[activeIndex];
            m_PalFxActive = true;
            float elapsedTicks = tick - palFx.Time;
            m_PalFx.Apply(m_BodyRenderer, palFx, elapsedTicks);
            for (int i = 0; i < m_Layers.Count; i++)
            {
                if (palFx.AffectsAll)
                {
                    m_PalFx.Apply(m_Layers[i].Renderer, palFx, elapsedTicks);
                }
                else
                {
                    m_PalFx.Clear(m_Layers[i].Renderer);
                }
            }
        }

        /// <summary>Nimmt den Farbfilter von Körper und allen Effekten zurück.</summary>
        private void ClearPalFx()
        {
            m_PalFx.Clear(m_BodyRenderer);
            for (int i = 0; i < m_Layers.Count; i++)
            {
                m_PalFx.Clear(m_Layers[i].Renderer);
            }
        }

        /// <summary>
        /// Wendet die fälligen Bewegungs-Controller an und integriert einen Tick
        /// Charakterbewegung. MUGEN rechnet in Pixeln pro Tick: erst Position,
        /// dann Schwerkraft auf die vertikale Geschwindigkeit. Am Boden stoppt der Fall.
        /// </summary>
        private void StepBodyMotion()
        {
            int tick = Mathf.FloorToInt(m_Elapsed * TicksPerSecond);
            while (m_NextPhysicsIndex < m_Sequence.PhysicsChanges.Length
                   && m_Sequence.PhysicsChanges[m_NextPhysicsIndex].Time <= tick)
            {
                m_Physics = m_Sequence.PhysicsChanges[m_NextPhysicsIndex].Value;
                m_NextPhysicsIndex++;
            }
            while (m_NextMoveIndex < m_Sequence.Moves.Length
                   && m_Sequence.Moves[m_NextMoveIndex].Time <= tick)
            {
                ApplyMove(m_Sequence.Moves[m_NextMoveIndex]);
                m_NextMoveIndex++;
            }
            if (m_Sequence.Moves.Length == 0 && !m_Sequence.Airborne)
            {
                return;
            }

            m_BodyOffset += new Vector2(m_BodyVelocity.x * FacingSign(), m_BodyVelocity.y);
            ApplyPhysics();
            if (m_BodyOffset.y > 0f)
            {
                // y zählt nach unten; positiv hiesse unter dem Boden.
                m_BodyOffset.y = 0f;
                m_BodyVelocity = Vector2.zero;
            }
            ApplyBodyOffset();
        }

        /// <summary>
        /// Führt den Körper nach Ikemens <c>posUpdate</c> nach: in der Luft zieht die
        /// Schwerkraft, am Boden bremst die Reibung. Unterhalb eines MUGEN-Pixels pro
        /// Tick setzt die Engine <c>vel.x</c> hart auf 0, sonst kröche der Charakter ewig.
        /// </summary>
        private void ApplyPhysics()
        {
            switch (m_Physics)
            {
                case FlarePhysics.Air:
                    m_BodyVelocity.y += m_Gravity;
                    break;
                case FlarePhysics.Stand:
                    m_BodyVelocity.x *= m_StandFriction;
                    if (Mathf.Abs(m_BodyVelocity.x) < 1f)
                    {
                        m_BodyVelocity.x = 0f;
                    }
                    break;
                case FlarePhysics.Crouch:
                    m_BodyVelocity.x *= m_CrouchFriction;
                    break;
            }
        }

        /// <summary>Wendet einen einzelnen Bewegungs-Controller an.</summary>
        private void ApplyMove(FlareSequenceMove move)
        {
            switch (move.Kind)
            {
                case FlareMoveKind.VelSet:
                    m_BodyVelocity = new Vector2(move.X, move.Y);
                    break;
                case FlareMoveKind.VelAdd:
                    m_BodyVelocity += new Vector2(move.X, move.Y);
                    break;
                case FlareMoveKind.VelMul:
                    m_BodyVelocity = new Vector2(m_BodyVelocity.x * move.X, m_BodyVelocity.y * move.Y);
                    break;
                case FlareMoveKind.PosSet:
                    m_BodyOffset = new Vector2(move.X, move.Y);
                    break;
                case FlareMoveKind.PosAdd:
                    m_BodyOffset += new Vector2(move.X, move.Y);
                    break;
            }
        }

        /// <summary>
        /// Schiebt die Visuals-Wurzel um die Charakterbewegung und hält die bereits
        /// gelösten Effekte dagegen fest. MUGEN bindet einen Explod nur, solange
        /// seine <c>bindtime</c> läuft; danach steht er im Raum.
        /// </summary>
        private void ApplyBodyOffset()
        {
            Vector3 world = new(
                m_BodyOffset.x / PixelsPerUnit,
                -m_BodyOffset.y / PixelsPerUnit,
                0f);
            m_VisualRoot.localPosition = m_BodyOrigin + world;

            for (int i = 0; i < m_Layers.Count; i++)
            {
                EffectLayer layer = m_Layers[i];
                if (layer.Go == null || layer.BindTicksLeft != 0)
                {
                    continue;
                }
                layer.Go.transform.localPosition = layer.BasePosition - world + new Vector3(
                    layer.Offset.x / PixelsPerUnit,
                    -layer.Offset.y / PixelsPerUnit,
                    0f);
            }
        }

        /// <summary>Startet eine fällige <c>SuperPause</c>, sobald ihr Tick erreicht ist.</summary>
        private void EnterPauseIfDue()
        {
            int tick = Mathf.FloorToInt(m_Elapsed * TicksPerSecond);
            while (m_NextPauseIndex < m_Sequence.Pauses.Length
                   && m_Sequence.Pauses[m_NextPauseIndex].Time <= tick)
            {
                m_PauseTicksLeft = Mathf.Max(m_PauseTicksLeft, m_Sequence.Pauses[m_NextPauseIndex].Duration);
                m_NextPauseIndex++;
            }
        }

        /// <summary>Löst fällige <c>PlaySnd</c>-Cues aus.</summary>
        private void AdvanceSounds()
        {
            for (int i = 0; i < m_Cues.Count; i++)
            {
                SoundCue cue = m_Cues[i];
                if (cue.Done || m_Elapsed < cue.NextAt)
                {
                    continue;
                }
                m_AudioSource.PlayOneShot(cue.Clip, Mathf.Clamp01(cue.Spec.Volume));
                if (cue.Spec.Period > 0)
                {
                    cue.NextAt += cue.Spec.Period / TicksPerSecond;
                }
                else
                {
                    cue.Done = true;
                }
            }
        }
    }
}

