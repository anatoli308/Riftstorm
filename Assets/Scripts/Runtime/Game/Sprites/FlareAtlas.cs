using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Eine zur Laufzeit aufbereitete Animation: <see cref="Sprite"/>s pro Frame und 8 Richtungen.
    /// </summary>
    public sealed class FlareAnimation
    {
        /// <summary>Name der Animation (z. B. "stance", "run").</summary>
        public string Name { get; }
        /// <summary>
        /// Lesbarer Move-Name aus der MUGEN-Reserved-Action-Konvention (z. B. "Standing",
        /// "Air attack"). Leer, wenn die Action-Nummer keiner bekannten Konvention entspricht.
        /// </summary>
        public string Label { get; }
        /// <summary>Wiedergabetyp.</summary>
        public FlareAnimationType Type { get; }
        /// <summary>Gesamtdauer der Animation in Sekunden.</summary>
        public float DurationSeconds { get; }
        /// <summary>Frame-Anzahl der Animation.</summary>
        public int FramesCount { get; }

        /// <summary>
        /// Per-Frame-Dauer in Sekunden (Index = Frame). <c>null</c>, wenn die
        /// Atlas-JSON keinen <c>frame_durations_ms</c>-Block enthält &#8212; der
        /// Animator verteilt dann <see cref="DurationSeconds"/> gleichmäßig über alle Frames.
        /// Wenn vorhanden, ist die Summe identisch zu <see cref="DurationSeconds"/>.
        /// </summary>
        public float[] FrameDurations { get; }

        /// <summary>
        /// Sprites indiziert über [frame][direction 0..7]. Direction-Reihenfolge nach FLARE-Konvention
        /// (siehe <see cref="FlareDirection.FromVector"/>).
        /// </summary>
        public Sprite[][] Sprites { get; }

        /// <summary>
        /// Rohzellen pro [frame][direction] aus dem JSON-Atlas, inklusive optionaler
        /// <see cref="FlareCell.AttackBoxes"/> / <see cref="FlareCell.HurtBoxes"/> in
        /// MUGEN-Pixelkoordinaten relativ zum Charakter-Anker. Wird vom Combat-System
        /// (<c>MugenHitboxRuntime</c>) gelesen, um pro Frame echte Treffer-Volumen zu bauen.
        /// Sprites werden aus diesen Zellen abgeleitet — die Zellen bleiben dauerhaft
        /// erhalten und sind dieselben Referenzen wie im deserialisierten <see cref="FlareAtlasDef"/>.
        /// </summary>
        public FlareCell[][] Cells { get; }

        /// <summary>
        /// Horizontale Spiegelung pro [frame][direction]. Wird vom Importer für W/NW/SW gesetzt,
        /// damit der seitenansichts-MUGEN-Charakter nach links blickt. <c>null</c>, wenn keine Zelle
        /// dieser Animation eine Spiegelung benötigt (häufiger Fall).
        /// </summary>
        public bool[][] FlipH { get; }

        /// <summary>
        /// Vertikale Spiegelung pro [frame][direction]. Aktuell unbenutzt vom Importer.
        /// <c>null</c>, wenn keine Zelle gespiegelt ist.
        /// </summary>
        public bool[][] FlipV { get; }

        /// <summary>
        /// Blend-Modus je Frame aus den AIR-Flags. <c>null</c>, wenn die Animation
        /// durchgehend normal gezeichnet wird.
        /// </summary>
        public FlareEffectBlend[] FrameBlends { get; }

        /// <summary>
        /// Frame, auf den eine geloopte Animation zurückspringt (MUGEN <c>LoopStart</c>).
        /// 0 = von vorn.
        /// </summary>
        public int LoopStart { get; }

        /// <summary>
        /// Zeit vom Animationsstart bis <see cref="LoopStart"/> in Sekunden — alles
        /// davor ist Einleitung und wird nur beim ersten Durchlauf gezeigt.
        /// </summary>
        public float LoopStartSeconds { get; }

        /// <summary>Erzeugt eine bereits aufbereitete Animation.</summary>
        public FlareAnimation(string name, string label, FlareAnimationType type, float durationSeconds, Sprite[][] sprites, FlareCell[][] cells, bool[][] flipH, bool[][] flipV, float[] frameDurations, FlareEffectBlend[] frameBlends = null, int loopStart = 0)
        {
            Name = name;
            Label = label;
            Type = type;
            DurationSeconds = durationSeconds;
            FramesCount = sprites?.Length ?? 0;
            Sprites = sprites;
            Cells = cells;
            FlipH = flipH;
            FlipV = flipV;
            FrameDurations = frameDurations;
            FrameBlends = frameBlends;
            LoopStart = Mathf.Clamp(loopStart, 0, Mathf.Max(0, FramesCount - 1));
            LoopStartSeconds = ResolveLoopStartSeconds(frameDurations, LoopStart);
        }

        private static float ResolveLoopStartSeconds(float[] frameDurations, int loopStart)
        {
            if (frameDurations == null || loopStart <= 0)
            {
                return 0f;
            }
            float sum = 0f;
            for (int i = 0; i < loopStart && i < frameDurations.Length; i++)
            {
                sum += frameDurations[i];
            }
            return sum;
        }
    }

    /// <summary>
    /// MUGEN-Transparenzmodus eines Effekts samt Quell-/Ziel-Alpha (0..255).
    /// Das Ziel-Alpha entscheidet, wie additiv gezeichnet wird: 0 = normales
    /// Alpha-Blending, 255 = voll additiv, dazwischen = Zwei-Pass-Mischung.
    /// </summary>
    public readonly struct FlareEffectBlend
    {
        /// <summary>Modus-Name (none/add/add1/addalpha/sub/subadd).</summary>
        public string Trans { get; }
        /// <summary>Quell-Alpha 0..255.</summary>
        public int AlphaSrc { get; }
        /// <summary>Ziel-Alpha 0..255.</summary>
        public int AlphaDst { get; }

        /// <summary>Erzeugt einen Blend-Zustand.</summary>
        public FlareEffectBlend(string trans, int alphaSrc, int alphaDst)
        {
            Trans = string.IsNullOrEmpty(trans) ? "none" : trans;
            AlphaSrc = Mathf.Clamp(alphaSrc, 0, 255);
            AlphaDst = Mathf.Clamp(alphaDst, 0, 255);
        }

        /// <summary>True, wenn subtraktiv gezeichnet wird.</summary>
        public bool IsSubtractive => Trans == "sub" || Trans == "subadd";

        /// <summary>True, wenn das Ziel-Alpha überhaupt eine Beimischung erzeugt.</summary>
        public bool IsBlended => AlphaDst > 0 || IsSubtractive;
    }

    /// <summary>
    /// Eigenbewegung eines Effekts, wie Ikemens <c>Explod.update</c> sie pro Tick
    /// integriert: <c>pos += vel</c>, danach <c>vel = vel * friction + accel</c>.
    /// Solange <see cref="BindTicks"/> läuft, klebt der Effekt am Charakter und
    /// die Bewegung wird jeden Tick wieder überschrieben.
    /// </summary>
    public readonly struct FlareEffectMotion
    {
        /// <summary>Startgeschwindigkeit in MUGEN-Pixeln pro Tick (y zählt nach unten).</summary>
        public Vector2 Velocity { get; }
        /// <summary>Beschleunigung in MUGEN-Pixeln pro Tick².</summary>
        public Vector2 Acceleration { get; }
        /// <summary>Multiplikator auf die Geschwindigkeit pro Tick (1 = keine Reibung).</summary>
        public Vector2 Friction { get; }
        /// <summary>Ticks, die der Effekt am Charakter gebunden bleibt.</summary>
        public int BindTicks { get; }
        /// <summary>Drehung in Grad gegen den Uhrzeigersinn.</summary>
        public float Angle { get; }

        /// <summary>True, wenn sich am Effekt überhaupt etwas bewegt.</summary>
        public bool IsMoving => Velocity != Vector2.zero || Acceleration != Vector2.zero;

        /// <summary>Erzeugt einen Bewegungszustand.</summary>
        public FlareEffectMotion(Vector2 velocity, Vector2 acceleration, Vector2 friction, int bindTicks, float angle)
        {
            Velocity = velocity;
            Acceleration = acceleration;
            Friction = friction;
            BindTicks = bindTicks;
            Angle = angle;
        }
    }

    /// <summary>
    /// Laufzeit-Sicht eines vom Move gespawnten Effekts: Animation, Spawn-Tick,
    /// Positions-Offset (MUGEN-Pixel), Positionstyp und Sprite-Priorität. Erlaubt
    /// das Compositing der Effekte gleichzeitig über der Body-Animation.
    /// </summary>
    public sealed class FlareSequenceEffect
    {
        /// <summary>Atlas-Key der Effekt-Animation.</summary>
        public string Anim { get; }
        /// <summary>MUGEN-Explod-ID (-1 = keine).</summary>
        public int Id { get; }
        /// <summary>Spawn-Zeitpunkt in MUGEN-Ticks (60/s) ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>
        /// Wiederholungsperiode in Ticks (0 = einmalig). MUGEN baut Dauerstrahlen
        /// und Partikelstroeme, indem derselbe Effekt alle N Ticks neu spawnt.
        /// </summary>
        public int Period { get; }
        /// <summary>Positions-Offset X in MUGEN-Pixeln (rechts positiv).</summary>
        public int PosX { get; }
        /// <summary>Positions-Offset Y in MUGEN-Pixeln (unten positiv).</summary>
        public int PosY { get; }
        /// <summary>MUGEN-Positionstyp (p1/p2/front/back/left/right/none).</summary>
        public string PosType { get; }
        /// <summary>Sprite-Priorität (negativ = hinter dem Körper).</summary>
        public int SprPriority { get; }
        /// <summary>
        /// MUGEN-Layer-Hinweis: 1 = <c>ontop</c> (über allem), -1 = <c>under</c>
        /// (unter allem), 0 = normal nach <see cref="SprPriority"/> sortiert.
        /// </summary>
        public int Layer { get; }
        /// <summary>Skalierung X (Standard 1).</summary>
        public float ScaleX { get; }
        /// <summary>Skalierung Y (Standard 1).</summary>
        public float ScaleY { get; }
        /// <summary>Horizontale Blickrichtung (1 oder -1).</summary>
        public int Facing { get; }
        /// <summary>Vertikale Spiegelung (1 oder -1).</summary>
        public int VFacing { get; }
        /// <summary>Lebensdauer in Ticks: &gt;=0 Ticks, -1 dauerhaft, &lt;=-2 bis Anim-Ende.</summary>
        public int RemoveTime { get; }
        /// <summary>Tick eines <c>RemoveExplod</c> auf diesen Effekt; -1 = keiner.</summary>
        public int RemoveAt { get; }
        /// <summary>Eigenbewegung und Drehung.</summary>
        public FlareEffectMotion Motion { get; }
        /// <summary>Transparenz-/Blend-Zustand.</summary>
        public FlareEffectBlend Blend { get; }
        /// <summary>
        /// Der <c>HitDef</c> eines Projektils; <c>null</c> bei allen anderen Effekten.
        /// Er löst aus, sobald das Geschoss den Gegner erreicht.
        /// </summary>
        public FlareSequenceHit? Hit { get; }
        /// <summary>Animation beim Einschlag (<c>projhitanim</c>); <c>null</c> = keine.</summary>
        public string HitAnim { get; }

        /// <summary>Erzeugt einen Effekt-Eintrag.</summary>
        public FlareSequenceEffect(
            string anim, int id, int time, int period, int posX, int posY, string posType,
            int sprPriority, int layer, float scaleX, float scaleY, int facing, int vFacing,
            int removeTime, int removeAt, FlareEffectMotion motion, FlareEffectBlend blend,
            FlareSequenceHit? hit = null, string hitAnim = null)
        {
            Anim = anim;
            Id = id;
            Time = time;
            Period = period;
            PosX = posX;
            PosY = posY;
            PosType = posType;
            SprPriority = sprPriority;
            Layer = layer;
            ScaleX = scaleX;
            ScaleY = scaleY;
            Facing = facing;
            VFacing = vFacing;
            RemoveTime = removeTime;
            RemoveAt = removeAt;
            Motion = motion;
            Blend = blend;
            Hit = hit;
            HitAnim = hitAnim;
        }
    }

    /// <summary>
    /// Ein Glied der sichtbaren Body-Kette eines Moves: die Animation und der
    /// MUGEN-Tick (60/s), ab dem sie den Körper darstellt. MUGEN wechselt den
    /// Body per <c>ChangeAnim</c> mitten im Move, statt Clips aneinanderzureihen.
    /// </summary>
    public readonly struct FlareSequenceAnim
    {
        /// <summary>Atlas-Key der Body-Animation.</summary>
        public string Anim { get; }
        /// <summary>Umschaltzeitpunkt in MUGEN-Ticks relativ zum Move-Start.</summary>
        public int Time { get; }

        /// <summary>Erzeugt ein Body-Ketten-Glied.</summary>
        public FlareSequenceAnim(string anim, int time)
        {
            Anim = anim;
            Time = time;
        }
    }

    /// <summary>
    /// Ein zeitlich platzierter <c>PlaySnd</c>-Aufruf eines Moves.
    /// </summary>
    public readonly struct FlareSequenceSound
    {
        /// <summary>Sample-Gruppe der MUGEN-Sound-Bank.</summary>
        public int Group { get; }
        /// <summary>Sample-Index innerhalb der Gruppe.</summary>
        public int Index { get; }
        /// <summary>Auslösezeitpunkt in MUGEN-Ticks (60/s) ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Wiederholungsperiode in Ticks (0 = einmalig).</summary>
        public int Period { get; }
        /// <summary>Linearer Lautstärkefaktor 0..1.</summary>
        public float Volume { get; }
        /// <summary>True, wenn das Sample in Schleife läuft.</summary>
        public bool Loop { get; }

        /// <summary>Erzeugt einen Sound-Eintrag.</summary>
        public FlareSequenceSound(int group, int index, int time, int period, float volume, bool loop)
        {
            Group = group;
            Index = index;
            Time = time;
            Period = period;
            Volume = volume;
            Loop = loop;
        }
    }

    /// <summary>
    /// Ein zeitlich platzierter MUGEN-Farbfilter. Werte in MUGEN-Skala:
    /// <see cref="Add"/> in 0..255, <see cref="Mul"/> mit 256 = neutral,
    /// <see cref="Color"/> mit 256 = volle Sättigung.
    /// </summary>
    public readonly struct FlareSequencePalFx
    {
        /// <summary>Startzeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Wirkdauer in Ticks; -1 = dauerhaft.</summary>
        public int Duration { get; }
        /// <summary>True, wenn der Filter auch die gespawnten Effekte färbt (<c>AllPalFX</c>).</summary>
        public bool AffectsAll { get; }
        /// <summary>Farbaddition in 0..255 (negativ erlaubt).</summary>
        public Vector3 Add { get; }
        /// <summary>Farbmultiplikator, 256 = neutral.</summary>
        public Vector3 Mul { get; }
        /// <summary>Sättigung, 256 = unverändert, 0 = Graustufen.</summary>
        public int Color { get; }
        /// <summary>True, wenn die Farben invertiert werden.</summary>
        public bool InvertAll { get; }
        /// <summary>Amplitude der sinusförmigen Addition.</summary>
        public Vector3 SinAdd { get; }
        /// <summary>Periode der sinusförmigen Addition in Ticks (0 = keine).</summary>
        public int SinPeriod { get; }

        /// <summary>Erzeugt einen Farbfilter-Eintrag.</summary>
        public FlareSequencePalFx(
            int time, int duration, bool affectsAll, Vector3 add, Vector3 mul,
            int color, bool invertAll, Vector3 sinAdd, int sinPeriod)
        {
            Time = time;
            Duration = duration;
            AffectsAll = affectsAll;
            Add = add;
            Mul = mul;
            Color = color;
            InvertAll = invertAll;
            SinAdd = sinAdd;
            SinPeriod = sinPeriod;
        }
    }

    /// <summary>
    /// Ein <c>Trans</c>-Controller: die Transparenz des Charakters selbst wechselt
    /// ab diesem Tick (z. B. <c>AddAlpha</c> beim Ein-/Ausblenden).
    /// </summary>
    public readonly struct FlareSequenceBodyTrans
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Ab diesem Tick geltender Blend-Zustand.</summary>
        public FlareEffectBlend Blend { get; }

        /// <summary>Erzeugt einen Transparenzwechsel.</summary>
        public FlareSequenceBodyTrans(int time, FlareEffectBlend blend)
        {
            Time = time;
            Blend = blend;
        }
    }

    /// <summary>
    /// Eine Änderung der Zeichenpriorität des Charakters (<c>sprpriority</c> im
    /// Statedef-Kopf oder ein <c>SprPriority</c>-Controller). MUGEN sortiert
    /// Charakter und Effekte in einer gemeinsamen Liste aufsteigend nach diesem
    /// Wert — höher heißt weiter vorne.
    /// </summary>
    public readonly struct FlareSequenceSprPriority
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Ab diesem Tick geltende Zeichenpriorität.</summary>
        public int Value { get; }

        /// <summary>Erzeugt einen Prioritätswechsel.</summary>
        public FlareSequenceSprPriority(int time, int value)
        {
            Time = time;
            Value = value;
        }
    }

    /// <summary>
    /// Der Zeichenwinkel des Charakters ab einem Tick. MUGEN dreht das
    /// Charakter-Sprite um seinen Achsenpunkt; <c>AngleDraw</c> bringt dazu eine
    /// eigene Zusatzskalierung mit.
    /// </summary>
    public readonly struct FlareSequenceBodyAngle
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Drehung in Grad gegen den Uhrzeigersinn.</summary>
        public float Angle { get; }
        /// <summary>Zusatzskalierung des Charakter-Sprites.</summary>
        public Vector2 Scale { get; }

        /// <summary>Erzeugt einen Winkelwechsel.</summary>
        public FlareSequenceBodyAngle(int time, float angle, Vector2 scale)
        {
            Time = time;
            Angle = angle;
            Scale = scale;
        }
    }

    /// <summary>
    /// Ein vom Move gespawnter Helfer: eigener Akteur mit eigener Animationskette,
    /// eigener Position und eigener Trefferprüfung.
    /// </summary>
    public sealed class FlareSequenceHelper
    {
        /// <summary>Spawn-Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        public int Time { get; set; }
        /// <summary>Position X in MUGEN-Pixeln relativ zum Charakter.</summary>
        public int PosX { get; set; }
        /// <summary>Position Y in MUGEN-Pixeln (nach unten positiv).</summary>
        public int PosY { get; set; }
        /// <summary>Animationskette; ein späteres Glied ersetzt das vorherige.</summary>
        public FlareSequenceAnim[] Anims { get; set; } = System.Array.Empty<FlareSequenceAnim>();
        /// <summary>Vom Helfer gespawnte Effekte.</summary>
        public FlareSequenceEffect[] Effects { get; set; } = System.Array.Empty<FlareSequenceEffect>();
        /// <summary>Vom Helfer ausgelöste Töne.</summary>
        public FlareSequenceSound[] Sounds { get; set; } = System.Array.Empty<FlareSequenceSound>();
        /// <summary>Treffer des Helfers.</summary>
        public FlareSequenceHit[] Hits { get; set; } = System.Array.Empty<FlareSequenceHit>();
        /// <summary>Zeichenpriorität des Helfers aus seinem Statedef-Header.</summary>
        public int SprPriority { get; set; }
        /// <summary>Eigenbewegung des Helfers; trägt die Flugbahn geworfener Objekte.</summary>
        public FlareEffectMotion Motion { get; set; }
        /// <summary>Bindungen an den Charakter, nach Tick sortiert.</summary>
        public FlareSequenceBind[] Binds { get; set; } = System.Array.Empty<FlareSequenceBind>();
    }

    /// <summary>Ein <c>BindToParent</c>/<c>BindToRoot</c>: der Helfer folgt dem Charakter.</summary>
    public readonly struct FlareSequenceBind
    {
        /// <summary>Zeitpunkt in Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Versatz zur Achse des Charakters in MUGEN-Pixeln (y nach unten).</summary>
        public Vector2 Offset { get; }
        /// <summary>Bindungsdauer in Ticks; -1 = dauerhaft.</summary>
        public int Duration { get; }

        /// <summary>Erzeugt eine Bindung.</summary>
        public FlareSequenceBind(int time, Vector2 offset, int duration)
        {
            Time = time;
            Offset = offset;
            Duration = duration;
        }
    }

    /// <summary>Eine <c>SuperPause</c>/<c>Pause</c>-Spanne in MUGEN-Ticks.</summary>
    public readonly struct FlareSequencePause
    {
        /// <summary>Startzeitpunkt in Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Dauer in Ticks.</summary>
        public int Duration { get; }

        /// <summary>Erzeugt eine Pausenspanne.</summary>
        public FlareSequencePause(int time, int duration)
        {
            Time = time;
            Duration = duration;
        }
    }

    /// <summary>
    /// <c>physics</c> aus dem Statedef-Kopf. Bestimmt, wie die Engine den Körper
    /// zwischen zwei Ticks nachführt.
    /// </summary>
    public enum FlarePhysics
    {
        /// <summary><c>physics = N</c> oder nicht angegeben — die Engine greift nicht ein.</summary>
        None,
        /// <summary><c>physics = S</c> — <c>vel.x</c> wird pro Tick mit der Stand-Reibung multipliziert.</summary>
        Stand,
        /// <summary><c>physics = C</c> — <c>vel.x</c> wird pro Tick mit der Hock-Reibung multipliziert.</summary>
        Crouch,
        /// <summary><c>physics = A</c> — <c>vel.y</c> wächst pro Tick um die Schwerkraft.</summary>
        Air,
    }

    /// <summary>Art eines MUGEN-Bewegungs-Controllers.</summary>
    public enum FlareMoveKind
    {
        /// <summary><c>VelSet</c> — Geschwindigkeit ersetzen.</summary>
        VelSet,
        /// <summary><c>VelAdd</c> — Geschwindigkeit erhöhen.</summary>
        VelAdd,
        /// <summary><c>VelMul</c> — Geschwindigkeit skalieren.</summary>
        VelMul,
        /// <summary><c>PosSet</c> — Position setzen.</summary>
        PosSet,
        /// <summary><c>PosAdd</c> — Position verschieben.</summary>
        PosAdd,
    }

    /// <summary>
    /// Ein Bewegungs-Controller des Charakters in MUGEN-Pixeln pro Tick.
    /// <see cref="X"/> ist facing-relativ, <see cref="Y"/> zählt nach unten.
    /// </summary>
    public readonly struct FlareSequenceMove
    {
        /// <summary>Zeitpunkt in Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Art des Controllers.</summary>
        public FlareMoveKind Kind { get; }
        /// <summary>Horizontaler Wert.</summary>
        public float X { get; }
        /// <summary>Vertikaler Wert (nach unten positiv).</summary>
        public float Y { get; }

        /// <summary>Erzeugt einen Bewegungs-Eintrag.</summary>
        public FlareSequenceMove(int time, FlareMoveKind kind, float x, float y)
        {
            Time = time;
            Kind = kind;
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Was ein Treffer dem Opfer antut. Ikemen wählt dessen Zustand nach seinem
    /// StateType (stehend &#8594; 5000), <c>common1.cns</c> darin die Animation aus
    /// <see cref="AnimType"/> und <see cref="GroundType"/>.
    /// </summary>
    public readonly struct FlareVictimReaction
    {
        /// <summary>0=light, 1=medium, 2=hard, 3=back, 4=up, 5=diagup.</summary>
        public int AnimType { get; }
        /// <summary>1=high, 2=low, 3=trip.</summary>
        public int GroundType { get; }
        /// <summary>Rückstoß am Boden in MUGEN-Pixeln pro Tick (y nach unten).</summary>
        public Vector2 GroundVelocity { get; }
        /// <summary>Rückstoß in der Luft in MUGEN-Pixeln pro Tick.</summary>
        public Vector2 AirVelocity { get; }
        /// <summary>True, wenn der Treffer das Opfer zu Fall bringt.</summary>
        public bool Fall { get; }
        /// <summary>Ticks, die das Opfer in der Trefferreaktion bleibt.</summary>
        public int HitTime { get; }

        /// <summary>True, wenn das Opfer abhebt statt nur zu zucken.</summary>
        public bool LiftsOff => Fall || GroundVelocity.y != 0f;

        /// <summary>Erzeugt eine Trefferreaktion.</summary>
        public FlareVictimReaction(
            int animType, int groundType, Vector2 groundVelocity,
            Vector2 airVelocity, bool fall, int hitTime)
        {
            AnimType = animType;
            GroundType = groundType;
            GroundVelocity = groundVelocity;
            AirVelocity = airVelocity;
            Fall = fall;
            HitTime = hitTime;
        }

        /// <summary>
        /// Atlas-Key der Trefferanimation nach der Regel aus <c>common1.cns</c>
        /// Statedef 5000: Rückwärtstreffer nutzen 5030, Hoch- und Schrägtreffer
        /// 5051/5052 sofern vorhanden, alles andere 5000/5010 plus <c>animtype</c>.
        /// </summary>
        public string ResolveAnimation(FlareAtlas atlas)
        {
            if (AnimType >= 4 && atlas != null)
            {
                string lifted = "action_" + (5047 + AnimType);
                if (atlas.Animations.ContainsKey(lifted))
                {
                    return lifted;
                }
            }
            if (AnimType >= 3)
            {
                return "action_5030";
            }
            return "action_" + ((GroundType == 1 ? 5000 : 5010) + AnimType);
        }
    }

    /// <summary>
    /// Das Sichtbare eines <c>HitDef</c>: Trefferfunke, dessen Lage relativ zur
    /// Vorderkante des Getroffenen, und der Trefferton.
    /// </summary>
    public readonly struct FlareSequenceHit
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Atlas-Key des Funkens; leer = kein Funke.</summary>
        public string Spark { get; }
        /// <summary>Funken-Offset X in MUGEN-Pixeln.</summary>
        public int SparkX { get; }
        /// <summary>Funken-Offset Y in MUGEN-Pixeln (nach unten positiv).</summary>
        public int SparkY { get; }
        /// <summary>Sample-Gruppe des Treffertons (-1 = keiner).</summary>
        public int SoundGroup { get; }
        /// <summary>Sample-Index des Treffertons.</summary>
        public int SoundIndex { get; }
        /// <summary>Trefferpause in Ticks.</summary>
        public int PauseTime { get; }
        /// <summary>Was der Treffer beim Opfer auslöst.</summary>
        public FlareVictimReaction Victim { get; }

        /// <summary>True, wenn ein Trefferton hinterlegt ist.</summary>
        public bool HasSound => SoundGroup >= 0;

        /// <summary>Erzeugt einen Treffer-Eintrag.</summary>
        public FlareSequenceHit(
            int time, string spark, int sparkX, int sparkY,
            int soundGroup, int soundIndex, int pauseTime,
            FlareVictimReaction victim = default)
        {
            Time = time;
            Spark = spark;
            SparkX = sparkX;
            SparkY = sparkY;
            SoundGroup = soundGroup;
            SoundIndex = soundIndex;
            PauseTime = pauseTime;
            Victim = victim;
        }
    }

    /// <summary>
    /// Eine Spur von Nachbildern hinter dem Körper (<c>AfterImage</c>). Das erste
    /// Nachbild startet auf <see cref="PalBright"/>/<see cref="PalContrast"/>,
    /// jedes weitere addiert <see cref="PalAdd"/> und multipliziert mit
    /// <see cref="PalMul"/> — dadurch verblasst die Spur (Ikemen <c>AfterImage.setup</c>).
    /// </summary>
    public readonly struct FlareSequenceAfterImage
    {
        /// <summary>Startzeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Wirkdauer in Ticks; -1 = dauerhaft.</summary>
        public int Duration { get; }
        /// <summary>True, wenn nur die Restdauer einer laufenden Spur gesetzt wird.</summary>
        public bool IsRetime { get; }
        /// <summary>Anzahl der Nachbilder.</summary>
        public int Length { get; }
        /// <summary>Ticks zwischen zwei Aufnahmen.</summary>
        public int TimeGap { get; }
        /// <summary>Aufnahmen-Abstand zwischen zwei sichtbaren Nachbildern.</summary>
        public int FrameGap { get; }
        /// <summary>Farbaddition des ersten Nachbilds in 0..255.</summary>
        public Vector3 PalBright { get; }
        /// <summary>Farbfaktor des ersten Nachbilds, 256 = neutral.</summary>
        public Vector3 PalContrast { get; }
        /// <summary>Einmalige Zusatzaddition ab dem zweiten Nachbild.</summary>
        public Vector3 PalPostBright { get; }
        /// <summary>Farbsättigung der Spur, 256 = voll, 0 = Graustufen.</summary>
        public int PalColor { get; }
        /// <summary>True, wenn die Spur farblich invertiert wird.</summary>
        public bool PalInvertAll { get; }
        /// <summary>Farbaddition pro weiterem Nachbild in 0..255.</summary>
        public Vector3 PalAdd { get; }
        /// <summary>Farbfaktor pro weiterem Nachbild, 1 = neutral.</summary>
        public Vector3 PalMul { get; }
        /// <summary>Blend-Zustand der Nachbilder.</summary>
        public FlareEffectBlend Blend { get; }

        /// <summary>Erzeugt einen Nachzieh-Eintrag.</summary>
        public FlareSequenceAfterImage(
            int time, int duration, bool isRetime, int length, int timeGap, int frameGap,
            Vector3 palBright, Vector3 palContrast, Vector3 palPostBright, int palColor,
            bool palInvertAll, Vector3 palAdd, Vector3 palMul, FlareEffectBlend blend)
        {
            Time = time;
            Duration = duration;
            IsRetime = isRetime;
            Length = length;
            TimeGap = timeGap;
            FrameGap = frameGap;
            PalBright = palBright;
            PalContrast = palContrast;
            PalPostBright = palPostBright;
            PalColor = palColor;
            PalInvertAll = palInvertAll;
            PalAdd = palAdd;
            PalMul = palMul;
            Blend = blend;
        }
    }

    /// <summary>
    /// Eine Änderung an einem laufenden Effekt (<c>ModifyExplod</c>), adressiert
    /// über dessen ID. Nicht gesetzte Felder lassen den Effekt unverändert.
    /// </summary>
    public readonly struct FlareSequenceModify
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>ID des betroffenen Effekts.</summary>
        public int Id { get; }
        /// <summary>Neue Skalierung, oder <c>null</c>.</summary>
        public Vector2? Scale { get; }
        /// <summary>Neue Sprite-Priorität, oder <c>null</c>.</summary>
        public int? SprPriority { get; }
        /// <summary>Neue Lebensdauer in Ticks, oder <c>null</c>.</summary>
        public int? RemoveTime { get; }
        /// <summary>Neue Drehung in Grad, oder <c>null</c>.</summary>
        public float? Angle { get; }
        /// <summary>Neue Geschwindigkeit, oder <c>null</c>.</summary>
        public Vector2? Velocity { get; }
        /// <summary>Neue Beschleunigung, oder <c>null</c>.</summary>
        public Vector2? Acceleration { get; }

        /// <summary>Erzeugt eine Effekt-Änderung.</summary>
        public FlareSequenceModify(
            int time, int id, Vector2? scale, int? sprPriority, int? removeTime,
            float? angle, Vector2? velocity, Vector2? acceleration)
        {
            Time = time;
            Id = id;
            Scale = scale;
            SprPriority = sprPriority;
            RemoveTime = removeTime;
            Angle = angle;
            Velocity = velocity;
            Acceleration = acceleration;
        }
    }

    /// <summary>
    /// Eine „gehört-zusammen“-Gruppe aus der MUGEN-State-Machine: sichtbare
    /// Body-Animationen eines Moves plus alle Spuren, die der Statedef auslöst.
    /// Alle Animationsnamen existieren im selben <see cref="FlareAtlas"/>.
    /// </summary>
    public sealed class FlareSequence
    {
        /// <summary>MUGEN-Statedef-Nummer, aus der die Gruppe stammt.</summary>
        public int State { get; set; }
        /// <summary>Lesbarer Move-Name oder leer.</summary>
        public string Label { get; set; }
        /// <summary>Freitext-Überschrift des CNS-Blocks, unter der der Autor den Move gruppiert.</summary>
        public string Group { get; set; }
        /// <summary>
        /// Einordnung nach MUGEN-Statenummernband (System/Normal/Command/Special/
        /// Hyper/Hit), <c>Custom</c> für charaktereigene Moves, <c>Effect</c> für
        /// Statedefs ohne eigene Body-Animation.
        /// </summary>
        public string Category { get; set; } = "Custom";
        /// <summary>Sichtbare Body-Kette, nach Umschaltzeitpunkt sortiert.</summary>
        public FlareSequenceAnim[] Anims { get; set; } = System.Array.Empty<FlareSequenceAnim>();
        /// <summary>Gespawnte Effekt-/Waffen-Animationen mit Timing/Position.</summary>
        public FlareSequenceEffect[] Effects { get; set; } = System.Array.Empty<FlareSequenceEffect>();
        /// <summary>Zeitlich platzierte <c>PlaySnd</c>-Aufrufe.</summary>
        public FlareSequenceSound[] Sounds { get; set; } = System.Array.Empty<FlareSequenceSound>();
        /// <summary>Zeitlich platzierte Farbfilter.</summary>
        public FlareSequencePalFx[] PalFx { get; set; } = System.Array.Empty<FlareSequencePalFx>();
        /// <summary>Zeitlich platzierte Pausen.</summary>
        public FlareSequencePause[] Pauses { get; set; } = System.Array.Empty<FlareSequencePause>();
        /// <summary>Bewegungs-Controller des Charakters.</summary>
        public FlareSequenceMove[] Moves { get; set; } = System.Array.Empty<FlareSequenceMove>();
        /// <summary>Treffer-Definitionen.</summary>
        public FlareSequenceHit[] Hits { get; set; } = System.Array.Empty<FlareSequenceHit>();
        /// <summary>Nachzieh-Effekte.</summary>
        public FlareSequenceAfterImage[] AfterImage { get; set; } = System.Array.Empty<FlareSequenceAfterImage>();
        /// <summary>Änderungen an laufenden Effekten.</summary>
        public FlareSequenceModify[] Modifies { get; set; } = System.Array.Empty<FlareSequenceModify>();
        /// <summary>Transparenzwechsel des Charakters selbst, nach Tick sortiert.</summary>
        public FlareSequenceBodyTrans[] BodyTrans { get; set; } = System.Array.Empty<FlareSequenceBodyTrans>();
        /// <summary>
        /// Animationen, die der Move dem Gegner aufzwingt (<c>ChangeAnim2</c>) —
        /// gezeichnet aus den Sprites dieses Charakters. Nach Tick sortiert.
        /// </summary>
        public FlareSequenceAnim[] Victim { get; set; } = System.Array.Empty<FlareSequenceAnim>();
        /// <summary>
        /// Zeichenpriorität des Charakters über die Zeit, nach Tick sortiert.
        /// Leer heißt: es gilt durchgehend die MUGEN-Vorgabe 0.
        /// </summary>
        public FlareSequenceSprPriority[] SprPriority { get; set; } = System.Array.Empty<FlareSequenceSprPriority>();
        /// <summary>Zeichenwinkel des Charakters über die Zeit, nach Tick sortiert.</summary>
        public FlareSequenceBodyAngle[] BodyAngle { get; set; } = System.Array.Empty<FlareSequenceBodyAngle>();
        /// <summary>Vom Move gespawnte Helfer, nach Spawn-Tick sortiert.</summary>
        public FlareSequenceHelper[] Helpers { get; set; } = System.Array.Empty<FlareSequenceHelper>();
        /// <summary>True, wenn der Statedef <c>physics = A</c> nutzt und Schwerkraft wirkt.</summary>
        public bool Airborne { get; set; }
        /// <summary>
        /// Physik des Statedefs. Bestimmt, wie MUGEN den Körper zwischen zwei Ticks
        /// nachführt: Schwerkraft in der Luft, Reibung am Boden, sonst nichts.
        /// </summary>
        public FlarePhysics Physics { get; set; } = FlarePhysics.None;
        /// <summary>Physik-Umschaltungen über die Zeit, nach Tick sortiert.</summary>
        public FlareSequencePhysics[] PhysicsChanges { get; set; } = System.Array.Empty<FlareSequencePhysics>();
        /// <summary>Ticks, an denen <c>Turn</c> die Blickrichtung kippt.</summary>
        public int[] Turns { get; set; } = System.Array.Empty<int>();
        /// <summary>Ticks, ab denen <c>AssertSpecial invisible</c> den Körper ausblendet.</summary>
        public int[] Invisible { get; set; } = System.Array.Empty<int>();
    }

    /// <summary>Ein <c>StateTypeSet physics = ...</c> zu einem Zeitpunkt.</summary>
    public readonly struct FlareSequencePhysics
    {
        /// <summary>Zeitpunkt in Ticks ab Move-Start.</summary>
        public int Time { get; }
        /// <summary>Ab diesem Tick geltende Körperphysik.</summary>
        public FlarePhysics Value { get; }

        /// <summary>Erzeugt einen Physik-Wechsel.</summary>
        public FlareSequencePhysics(int time, FlarePhysics value)
        {
            Time = time;
            Value = value;
        }
    }

    /// <summary>
    /// Aufbereiteter Atlas einer Sprite-Schicht: Textur(en) + benannte Animationen.
    /// </summary>
    public sealed class FlareAtlas
    {
        /// <summary>Logischer Atlasname (Dateiname ohne Endung).</summary>
        public string Name { get; }
        /// <summary>
        /// Alle Atlas-Seiten-Texturen (Index == <see cref="FlareCell.AtlasPage"/>).
        /// Einzelne Einträge können <c>null</c> sein, wenn ein PNG fehlt. Bei
        /// einseitigen Charakteren enthält das Array genau eine Textur.
        /// </summary>
        public Texture2D[] Textures { get; }
        /// <summary>
        /// Bequemer Zugriff auf die erste Atlas-Seite (kann <c>null</c> sein, wenn
        /// keine Textur geladen wurde). Bestehender Code, der nur eine Seite kennt,
        /// bleibt damit gültig.
        /// </summary>
        public Texture2D Texture => Textures != null && Textures.Length > 0 ? Textures[0] : null;
        /// <summary>Animationen nach Name.</summary>
        public IReadOnlyDictionary<string, FlareAnimation> Animations { get; }
        /// <summary>MUGEN <c>movement.yaccel</c> in Pixeln pro Tick².</summary>
        public float Gravity { get; set; } = 0.5f;
        /// <summary>MUGEN <c>movement.stand.friction</c>: Faktor auf <c>vel.x</c> pro Tick am Boden.</summary>
        public float StandFriction { get; set; } = 0.85f;
        /// <summary>MUGEN <c>movement.crouch.friction</c>: Faktor auf <c>vel.x</c> pro Tick in der Hocke.</summary>
        public float CrouchFriction { get; set; } = 0.82f;
        /// <summary>
        /// „Gehört-zusammen“-Gruppen aus der MUGEN-State-Machine. Nie <c>null</c>
        /// (leer, wenn der Atlas keine Sequenz-Daten trägt).
        /// </summary>
        public IReadOnlyList<FlareSequence> Sequences { get; }

        /// <summary>Erzeugt einen Atlas-Eintrag.</summary>
        public FlareAtlas(string name, Texture2D[] textures, IReadOnlyDictionary<string, FlareAnimation> animations, IReadOnlyList<FlareSequence> sequences = null)
        {
            Name = name;
            Textures = textures ?? System.Array.Empty<Texture2D>();
            Animations = animations;
            Sequences = sequences ?? System.Array.Empty<FlareSequence>();
        }

        /// <summary>
        /// Versucht, eine Animation per Name aufzulösen.
        /// </summary>
        public bool TryGet(string animationName, out FlareAnimation animation)
        {
            if (animationName != null && Animations != null)
            {
                return Animations.TryGetValue(animationName, out animation);
            }
            animation = null;
            return false;
        }
    }
}
