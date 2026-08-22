using System.Collections.Generic;
using Newtonsoft.Json;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// FLARE-Animationstyp. Bestimmt, wie die Frame-Sequenz abgespielt wird.
    /// </summary>
    public enum FlareAnimationType
    {
        /// <summary>Einmal vorwärts, dann zurück (Idle/Stance-Schwingen).</summary>
        BackForth,
        /// <summary>Endlosschleife (Lauf-Animation).</summary>
        Looped,
        /// <summary>Einmal abspielen und auf letztem Frame stehen bleiben (Angriff/Tod).</summary>
        PlayOnce,
    }

    /// <summary>
    /// Eine einzelne FLARE-Zelle: Quellrechteck im PNG-Atlas plus Anker-Offset
    /// (ox, oy) vom Top-Left zum Charakter-Fußpunkt.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareCell
    {
        /// <summary>Linke X-Koordinate im Atlas (Top-Left-Origin, Pixel).</summary>
        [JsonProperty("x")] public int X;
        /// <summary>Obere Y-Koordinate im Atlas (Top-Left-Origin, Pixel).</summary>
        [JsonProperty("y")] public int Y;
        /// <summary>Breite des Quellrechtecks in Pixel.</summary>
        [JsonProperty("w")] public int W;
        /// <summary>Höhe des Quellrechtecks in Pixel.</summary>
        [JsonProperty("h")] public int H;
        /// <summary>Anker-X-Offset relativ zur Top-Left-Ecke (Pixel).</summary>
        [JsonProperty("ox")] public int Ox;
        /// <summary>Anker-Y-Offset relativ zur Top-Left-Ecke (Pixel).</summary>
        [JsonProperty("oy")] public int Oy;
        /// <summary>
        /// Index der Atlas-Seite, auf der dieses Quellrechteck liegt (0 = erste PNG).
        /// Wird vom Importer nur emittiert, wenn ein Charakter auf mehrere Seiten
        /// verteilt werden musste (Textur-Höhenlimit 16384 px). Fehlt der Wert,
        /// gilt Seite 0 &#8212; identisch zu einseitigen Alt-Atlanten.
        /// </summary>
        [JsonProperty("atlasPage", NullValueHandling = NullValueHandling.Ignore)] public int AtlasPage;
        /// <summary>
        /// Optionale Angriffs-Hitboxen (MUGEN Clsn1) in Pixel-Koordinaten relativ zum
        /// Charakter-Anker (X = rechts, Y = nach unten). Jede Box ist [x1, y1, x2, y2]
        /// und bereits richtungs-gespiegelt vom Importer. <c>null</c> wenn die Zelle keine
        /// Attack-Boxen definiert &#8212; dann fällt das Combat-System auf den
        /// skalaren HitRadius zurück.
        /// </summary>
        [JsonProperty("attackBoxes", NullValueHandling = NullValueHandling.Ignore)] public int[][] AttackBoxes;
        /// <summary>
        /// Optionale Hurt-Hitboxen (MUGEN Clsn2) in Pixel-Koordinaten relativ zum
        /// Charakter-Anker. Format wie <see cref="AttackBoxes"/>. <c>null</c> wenn die
        /// Zelle keine Hurt-Boxen definiert.
        /// </summary>
        [JsonProperty("hurtBoxes", NullValueHandling = NullValueHandling.Ignore)] public int[][] HurtBoxes;
        /// <summary>
        /// Horizontale Spiegelung dieser Zelle. Wird vom Importer für W/NW/SW gesetzt,
        /// damit der seitenansichts-MUGEN-Charakter korrekt nach links blickt.
        /// </summary>
        [JsonProperty("flipH", NullValueHandling = NullValueHandling.Ignore)] public bool FlipH;
        /// <summary>
        /// Vertikale Spiegelung dieser Zelle. Aktuell unbenutzt, aber vom Importer
        /// schon befüllbar (defensive).
        /// </summary>
        [JsonProperty("flipV", NullValueHandling = NullValueHandling.Ignore)] public bool FlipV;
    }

    /// <summary>
    /// Eine FLARE-Animation: Frame-Anzahl, Gesamtdauer und 2D-Matrix
    /// [frame][direction] der einzelnen Zellen.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareAnimationDef
    {
        /// <summary>Anzahl Frames in der Animation.</summary>
        [JsonProperty("frames_count")] public int FramesCount;
        /// <summary>
        /// Gesamtdauer der Animation in Millisekunden. <c>long</c>, damit eine defekte
        /// AIR-Zeile (absurder Tick-Wert) beim Deserialisieren keinen Int32-Overflow
        /// wirft; der Loader klammert den Wert anschlie&#223;end auf einen spielbaren Bereich.
        /// </summary>
        [JsonProperty("duration_ms")] public long DurationMs;
        /// <summary>
        /// Per-Frame-Dauer in Millisekunden (Index = Frame). Wird vom MUGEN-Importer
        /// emittiert, damit MUGEN-typische ungleichm&#228;&#223;ige Timings (z. B. Hit-Frame 4 Ticks,
        /// Recovery 12 Ticks) im Animator korrekt abgespielt werden. <c>null</c> bei alten
        /// Atlanten &#8212; in dem Fall verteilt der Animator <see cref="DurationMs"/> gleichm&#228;&#223;ig.
        /// <c>long</c> aus demselben Overflow-Schutz-Grund wie <see cref="DurationMs"/>.
        /// </summary>
        [JsonProperty("frame_durations_ms", NullValueHandling = NullValueHandling.Ignore)] public long[] FrameDurationsMs;
        /// <summary>
        /// Blend-Modus je Frame als <c>[op, src, dst]</c>: <c>op</c> 0 = normal,
        /// 1 = additiv, 2 = subtraktiv; <c>src</c>/<c>dst</c> sind MUGEN-Alphas 0..255.
        /// Stammt aus den AIR-Flags (<c>A</c>, <c>A1</c>, <c>S</c>, <c>AS&lt;s&gt;D&lt;d&gt;</c>).
        /// <c>null</c> bei Atlanten ohne Blend-Daten.
        /// </summary>
        [JsonProperty("frame_blends", NullValueHandling = NullValueHandling.Ignore)] public int[][] FrameBlends;
        /// <summary>
        /// Frame, auf den eine geloopte Animation zurückspringt (MUGEN <c>LoopStart</c>).
        /// 0 = von vorn. Ohne diesen Wert ruckelt jede Animation mit Einleitung.
        /// </summary>
        [JsonProperty("loop_start")] public int LoopStart;
        /// <summary>Roher Typ-String aus dem JSON ("looped", "play_once", "back_forth").</summary>
        [JsonProperty("type")] public string TypeRaw;
        /// <summary>
        /// Lesbarer Move-Name aus der MUGEN-Reserved-Action-Konvention (z. B. "Standing",
        /// "Run forward", "Air attack"). Leer/<c>null</c> bei Custom-Nummern ohne bekannte
        /// Bedeutung &#8212; MUGEN-Actions tragen selbst keine Namen, nur Nummern.
        /// </summary>
        [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)] public string Label;
        /// <summary>Frames als verschachtelte Liste: [frame_index][direction_index 0..7].</summary>
        [JsonProperty("frames")] public List<List<FlareCell>> Frames;

        /// <summary>
        /// Liefert den geparsten Animationstyp; unbekannte Werte werden zu <see cref="FlareAnimationType.Looped"/>.
        /// </summary>
        public FlareAnimationType Type
        {
            get
            {
                return TypeRaw switch
                {
                    "back_forth" => FlareAnimationType.BackForth,
                    "play_once" => FlareAnimationType.PlayOnce,
                    "looped" => FlareAnimationType.Looped,
                    _ => FlareAnimationType.Looped,
                };
            }
        }
    }

    /// <summary>
    /// Ein vom Move gespawnter Effekt (Explod / Projectile / Helper): welche
    /// Animation, wann (Spawn-Tick), wo (Offset + <c>postype</c>) und in welcher
    /// Zeichen-Reihenfolge. Erlaubt originalgetreues Compositing (gleichzeitig
    /// über der Body-Animation), statt die Clips nur sequentiell aufzulisten.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareEffectDef
    {
        /// <summary>Atlas-Key der Effekt-Animation (z. B. "action_7000").</summary>
        [JsonProperty("anim")] public string Anim;
        /// <summary>Spawn-Zeitpunkt in MUGEN-Ticks (60/s) relativ zum Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Wiederholungsperiode in Ticks aus <c>Time % N = K</c>; 0 = einmalig.</summary>
        [JsonProperty("period", NullValueHandling = NullValueHandling.Ignore)] public int Period;
        /// <summary>Positions-Offset [x, y] in MUGEN-Pixeln (y nach unten positiv).</summary>
        [JsonProperty("pos")] public int[] Pos;
        /// <summary>MUGEN-Positionstyp (p1/p2/front/back/left/right/none).</summary>
        [JsonProperty("postype", NullValueHandling = NullValueHandling.Ignore)] public string PosType;
        /// <summary>Sprite-Priorität (Zeichen-Reihenfolge; negativ = hinter dem Körper).</summary>
        [JsonProperty("sprpriority")] public int SprPriority;

        /// <summary>MUGEN <c>ontop</c>/<c>under</c>: 1 = über allem, -1 = unter allem, 0 = normal.</summary>
        [JsonProperty("layer")] public int Layer;

        /// <summary>MUGEN-Explod-ID (-1 = keine). Ziel von <c>RemoveExplod</c>.</summary>
        [JsonProperty("id")] public int Id = -1;

        /// <summary>Drehung in Grad gegen den Uhrzeigersinn.</summary>
        [JsonProperty("angle")] public float Angle;

        /// <summary>Startgeschwindigkeit [vx, vy] in MUGEN-Pixeln pro Tick.</summary>
        [JsonProperty("vel", NullValueHandling = NullValueHandling.Ignore)] public float[] Vel;

        /// <summary>Beschleunigung [ax, ay] in MUGEN-Pixeln pro Tick².</summary>
        [JsonProperty("accel", NullValueHandling = NullValueHandling.Ignore)] public float[] Accel;

        /// <summary>Reibungsfaktor [fx, fy] pro Tick (1 = keine Reibung).</summary>
        [JsonProperty("friction", NullValueHandling = NullValueHandling.Ignore)] public float[] Friction;

        /// <summary>Ticks, die der Effekt am Charakter klebt, bevor er sich frei bewegt.</summary>
        [JsonProperty("bindtime")] public int BindTime = 1;

        /// <summary>Tick eines <c>RemoveExplod</c> auf diesen Effekt; <c>null</c> = keiner.</summary>
        [JsonProperty("removeat", NullValueHandling = NullValueHandling.Ignore)] public int? RemoveAt;
        /// <summary>Skalierung [sx, sy] (Standard [1, 1]).</summary>
        [JsonProperty("scale", NullValueHandling = NullValueHandling.Ignore)] public float[] Scale;
        /// <summary>Horizontale Blickrichtung: 1 normal, -1 gespiegelt.</summary>
        [JsonProperty("facing")] public int Facing = 1;
        /// <summary>Vertikale Spiegelung: 1 normal, -1 gespiegelt.</summary>
        [JsonProperty("vfacing")] public int VFacing = 1;
        /// <summary>Lebensdauer in Ticks: &gt;=0 = Ticks, -1 = dauerhaft, &lt;=-2 = bis Anim-Ende.</summary>
        [JsonProperty("removetime")] public int RemoveTime = -2;
        /// <summary>Blend-Modus (none/add/add1/addalpha/sub/subadd).</summary>
        [JsonProperty("trans", NullValueHandling = NullValueHandling.Ignore)] public string Trans;
        /// <summary>Blend-Alpha [source, destination] 0..255; destination steuert die Additivität.</summary>
        [JsonProperty("alpha", NullValueHandling = NullValueHandling.Ignore)] public int[] Alpha;

        /// <summary>
        /// Der <c>HitDef</c> eines Projektils. In MUGEN ist ein <c>Projectile</c>-
        /// Controller zugleich ein HitDef; Funke und Ton lösen aber erst beim
        /// Einschlag aus, nicht beim Abschuss. <c>null</c> = kein Treffer.
        /// </summary>
        [JsonProperty("hit", NullValueHandling = NullValueHandling.Ignore)] public FlareHitDef Hit;

        /// <summary>
        /// Atlas-Key von <c>projhitanim</c>: die Animation, auf die ein Projektil
        /// beim Einschlag umschaltet. <c>null</c> = es fliegt unverändert weiter.
        /// </summary>
        [JsonProperty("hitanim", NullValueHandling = NullValueHandling.Ignore)] public string HitAnim;
    }

    /// <summary>
    /// Ein Glied der sichtbaren Body-Kette: Animation plus der MUGEN-Tick, ab dem
    /// sie den Körper darstellt (aus dem <c>time = N</c>-Trigger des ChangeAnim).
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareSequenceAnimDef
    {
        /// <summary>Atlas-Key der Body-Animation.</summary>
        [JsonProperty("anim")] public string Anim;
        /// <summary>Umschaltzeitpunkt in MUGEN-Ticks (60/s) relativ zum Move-Start.</summary>
        [JsonProperty("time")] public int Time;
    }

    /// <summary>
    /// Ein <c>PlaySnd</c>-Aufruf eines Moves: Sample-Adresse im <c>.snd</c>-Bank
    /// des Charakters plus der MUGEN-Tick, zu dem er ausgelöst wird.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareSoundDef
    {
        /// <summary>Sample-Gruppe der MUGEN-Sound-Bank.</summary>
        [JsonProperty("group")] public int Group;
        /// <summary>Sample-Index innerhalb der Gruppe.</summary>
        [JsonProperty("index")] public int Index;
        /// <summary>Auslösezeitpunkt in MUGEN-Ticks (60/s) ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Wiederholungsperiode in Ticks (0 = einmalig).</summary>
        [JsonProperty("period")] public int Period;
        /// <summary>Linearer Lautstärkefaktor 0..1.</summary>
        [JsonProperty("volume")] public float Volume = 1f;
        /// <summary>1, wenn das Sample in Schleife läuft.</summary>
        [JsonProperty("loop")] public int Loop;
    }

    /// <summary>
    /// Ein <c>PalFX</c>/<c>AllPalFX</c>-Aufruf eines Moves. Skala wie in MUGEN:
    /// <c>add</c> in 0..255, <c>mul</c> mit 256 = neutral, <c>color</c> mit
    /// 256 = volle Sättigung.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlarePalFxDef
    {
        /// <summary>Startzeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Wirkdauer in Ticks; -1 = dauerhaft.</summary>
        [JsonProperty("duration")] public int Duration = 1;
        /// <summary><c>self</c> = nur der Charakter, <c>all</c> = auch alle Effekte.</summary>
        [JsonProperty("scope", NullValueHandling = NullValueHandling.Ignore)] public string Scope;
        /// <summary>Farbaddition [r, g, b] in 0..255 (negativ erlaubt).</summary>
        [JsonProperty("add", NullValueHandling = NullValueHandling.Ignore)] public int[] Add;
        /// <summary>Farbmultiplikator [r, g, b], 256 = neutral.</summary>
        [JsonProperty("mul", NullValueHandling = NullValueHandling.Ignore)] public int[] Mul;
        /// <summary>Sättigung, 256 = unverändert, 0 = Graustufen.</summary>
        [JsonProperty("color")] public int Color = 256;
        /// <summary>1 = Farben invertieren.</summary>
        [JsonProperty("invertall")] public int InvertAll;
        /// <summary>Amplitude der sinusförmigen Addition [r, g, b].</summary>
        [JsonProperty("sinadd", NullValueHandling = NullValueHandling.Ignore)] public int[] SinAdd;
        /// <summary>Periode der sinusförmigen Addition in Ticks (0 = keine).</summary>
        [JsonProperty("sinperiod")] public int SinPeriod;
    }

    /// <summary>
    /// Ein <c>HitDef</c>, reduziert auf das Sichtbare: Trefferfunke, dessen Lage
    /// und der Trefferton. <c>sparkxy</c> zählt von der Vorderkante des Getroffenen,
    /// y vom Boden des Angreifers nach oben (in MUGEN also negativ).
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareHitDef
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Atlas-Key des Funkens; <c>null</c> = kein Funke.</summary>
        [JsonProperty("spark", NullValueHandling = NullValueHandling.Ignore)] public string Spark;
        /// <summary>Funken-Offset [x, y] in MUGEN-Pixeln.</summary>
        [JsonProperty("sparkpos", NullValueHandling = NullValueHandling.Ignore)] public int[] SparkPos;
        /// <summary>Trefferton; <c>null</c> = keiner.</summary>
        [JsonProperty("sound", NullValueHandling = NullValueHandling.Ignore)] public FlareSoundDef Sound;
        /// <summary>Trefferpause in Ticks, die beide Seiten einfriert.</summary>
        [JsonProperty("pausetime")] public int PauseTime;
        /// <summary>
        /// <c>animtype</c>: 0=light, 1=medium, 2=hard, 3=back, 4=up, 5=diagup.
        /// Wählt zusammen mit <see cref="GroundType"/> die Trefferanimation des Opfers.
        /// </summary>
        [JsonProperty("animtype")] public int AnimType;
        /// <summary><c>ground.type</c>: 1=high, 2=low, 3=trip.</summary>
        [JsonProperty("groundtype")] public int GroundType = 1;
        /// <summary><c>ground.velocity</c> [x, y] in MUGEN-Pixeln pro Tick.</summary>
        [JsonProperty("groundvel", NullValueHandling = NullValueHandling.Ignore)] public float[] GroundVel;
        /// <summary><c>air.velocity</c> [x, y] in MUGEN-Pixeln pro Tick.</summary>
        [JsonProperty("airvel", NullValueHandling = NullValueHandling.Ignore)] public float[] AirVel;
        /// <summary>1, wenn der Treffer das Opfer zu Fall bringt.</summary>
        [JsonProperty("fall")] public int Fall;
        /// <summary><c>ground.hittime</c>: Ticks, die das Opfer in der Reaktion bleibt.</summary>
        [JsonProperty("hittime")] public int HitTime;
    }

    /// <summary>
    /// Ein <c>AfterImage</c>/<c>AfterImageTime</c>: der Nachzieheffekt hinter dem
    /// Körper. <c>paladd</c> ist 0..255, <c>palmul</c> ein echter Faktor (1 = neutral);
    /// beide wirken pro Nachbild erneut, dadurch verblasst die Spur.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareAfterImageDef
    {
        /// <summary>Startzeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Wirkdauer in Ticks; -1 = dauerhaft.</summary>
        [JsonProperty("duration")] public int Duration = 1;
        /// <summary>1, wenn nur die Restdauer einer laufenden Spur gesetzt wird.</summary>
        [JsonProperty("retime")] public int Retime;
        /// <summary>Anzahl der Nachbilder.</summary>
        [JsonProperty("length")] public int Length = 20;
        /// <summary>Ticks zwischen zwei Aufnahmen.</summary>
        [JsonProperty("timegap")] public int TimeGap = 1;
        /// <summary>Aufnahmen Abstand zwischen zwei sichtbaren Nachbildern.</summary>
        [JsonProperty("framegap")] public int FrameGap = 4;
        /// <summary>Farbaddition des ersten Nachbilds [r, g, b] in 0..255.</summary>
        [JsonProperty("palbright", NullValueHandling = NullValueHandling.Ignore)] public int[] PalBright;
        /// <summary>Farbfaktor des ersten Nachbilds [r, g, b], 256 = neutral.</summary>
        [JsonProperty("palcontrast", NullValueHandling = NullValueHandling.Ignore)] public int[] PalContrast;
        /// <summary>Einmalige Zusatzaddition ab dem zweiten Nachbild [r, g, b].</summary>
        [JsonProperty("palpostbright", NullValueHandling = NullValueHandling.Ignore)] public int[] PalPostBright;
        /// <summary>Farbsättigung der Spur, 256 = voll, 0 = Graustufen.</summary>
        [JsonProperty("palcolor")] public int PalColor = 256;
        /// <summary>1, wenn die Spur farblich invertiert wird.</summary>
        [JsonProperty("palinvertall")] public int PalInvertAll;
        /// <summary>Farbaddition pro weiterem Nachbild [r, g, b] in 0..255.</summary>
        [JsonProperty("paladd", NullValueHandling = NullValueHandling.Ignore)] public int[] PalAdd;
        /// <summary>Farbfaktor pro weiterem Nachbild [r, g, b], 1 = neutral.</summary>
        [JsonProperty("palmul", NullValueHandling = NullValueHandling.Ignore)] public float[] PalMul;
        /// <summary>Blend-Modus der Nachbilder.</summary>
        [JsonProperty("trans", NullValueHandling = NullValueHandling.Ignore)] public string Trans;
        /// <summary>Blend-Alpha [source, destination].</summary>
        [JsonProperty("alpha", NullValueHandling = NullValueHandling.Ignore)] public int[] Alpha;
    }

    /// <summary>
    /// Ein <c>ModifyExplod</c>: ändert einen laufenden Effekt anhand seiner ID.
    /// Nicht gesetzte Felder bleiben <c>null</c> und lassen den Effekt unverändert.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareModifyDef
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>ID des betroffenen Effekts.</summary>
        [JsonProperty("id")] public int Id;
        /// <summary>Neue Skalierung [sx, sy].</summary>
        [JsonProperty("scale", NullValueHandling = NullValueHandling.Ignore)] public float[] Scale;
        /// <summary>Neue Sprite-Priorität.</summary>
        [JsonProperty("sprpriority", NullValueHandling = NullValueHandling.Ignore)] public int? SprPriority;
        /// <summary>Neue Lebensdauer in Ticks.</summary>
        [JsonProperty("removetime", NullValueHandling = NullValueHandling.Ignore)] public int? RemoveTime;
        /// <summary>Neue Drehung in Grad.</summary>
        [JsonProperty("angle", NullValueHandling = NullValueHandling.Ignore)] public float? Angle;
        /// <summary>Neue Geschwindigkeit [vx, vy].</summary>
        [JsonProperty("vel", NullValueHandling = NullValueHandling.Ignore)] public float[] Vel;
        /// <summary>Neue Beschleunigung [ax, ay].</summary>
        [JsonProperty("accel", NullValueHandling = NullValueHandling.Ignore)] public float[] Accel;
    }

    /// <summary>
    /// Ein <c>Trans</c>-Controller: setzt die Transparenz des Charakters selbst
    /// (z. B. <c>AddAlpha</c> beim Ein-/Ausblenden).
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareBodyTransDef
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Blend-Modus (none/add/add1/addalpha/sub/subadd).</summary>
        [JsonProperty("trans", NullValueHandling = NullValueHandling.Ignore)] public string Trans;
        /// <summary>Blend-Alpha [source, destination] 0..255.</summary>
        [JsonProperty("alpha", NullValueHandling = NullValueHandling.Ignore)] public int[] Alpha;
    }

    /// <summary>
    /// /// Eine Änderung der Zeichenpriorität des Charakters: der
    /// <c>sprpriority</c>-Kopfeintrag des Statedefs (Tick 0) und jeder
    /// <c>SprPriority</c>-Controller danach.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareSprPriorityDef
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Neue Zeichenpriorität; höher zeichnet weiter vorne.</summary>
        [JsonProperty("value")] public int Value;
    }

    /// <summary>
    /// Der Zeichenwinkel des Charakters ab diesem Tick (<c>AngleSet</c> /
    /// <c>AngleDraw</c> / <c>AngleAdd</c>).
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareBodyAngleDef
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Drehung in Grad gegen den Uhrzeigersinn.</summary>
        [JsonProperty("angle")] public float Angle;
        /// <summary>Zusatzskalierung [sx, sy] aus <c>AngleDraw</c>.</summary>
        [JsonProperty("scale", NullValueHandling = NullValueHandling.Ignore)] public float[] Scale;
    }

    /// <summary>
    /// Ein vom Move gespawnter Helfer. In MUGEN ist das ein eigenständiger
    /// Charakter mit eigener Animationskette, Position und Trefferprüfung — die
    /// Treffer- und Endanimation eines Angriffs liegt praktisch immer hier.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareHelperDef
    {
        /// <summary>Spawn-Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Position [x, y] in MUGEN-Pixeln relativ zum Charakter.</summary>
        [JsonProperty("pos", NullValueHandling = NullValueHandling.Ignore)] public int[] Pos;
        /// <summary>Eigene Animationskette; spätere Glieder ersetzen frühere.</summary>
        [JsonProperty("anims", NullValueHandling = NullValueHandling.Ignore)] public FlareSequenceAnimDef[] Anims;
        /// <summary>Vom Helfer gespawnte Effekte, Position relativ zu ihm.</summary>
        [JsonProperty("effects", NullValueHandling = NullValueHandling.Ignore)] public FlareEffectDef[] Effects;
        /// <summary>Vom Helfer ausgelöste Töne.</summary>
        [JsonProperty("sounds", NullValueHandling = NullValueHandling.Ignore)] public FlareSoundDef[] Sounds;
        /// <summary>Treffer des Helfers — hier hängt bei den meisten Moves die HitDef.</summary>
        [JsonProperty("hits", NullValueHandling = NullValueHandling.Ignore)] public FlareHitDef[] Hits;
        /// <summary>
        /// <c>BindToParent</c>/<c>BindToRoot</c>: ab diesen Ticks folgt der Helfer
        /// dem Charakter statt frei zu fliegen. Kann leer sein.
        /// </summary>
        [JsonProperty("binds", NullValueHandling = NullValueHandling.Ignore)] public FlareBindDef[] Binds;
        /// <summary>Zeichenpriorität aus dem Statedef-Header des Helfers.</summary>
        [JsonProperty("sprpriority")] public int SprPriority;
        /// <summary>Startgeschwindigkeit [x, y] in MUGEN-Pixeln pro Tick.</summary>
        [JsonProperty("vel", NullValueHandling = NullValueHandling.Ignore)] public float[] Velocity;
        /// <summary>Beschleunigung [x, y] in MUGEN-Pixeln pro Tick².</summary>
        [JsonProperty("accel", NullValueHandling = NullValueHandling.Ignore)] public float[] Acceleration;
    }

    /// <summary>Ein <c>SuperPause</c>/<c>Pause</c>: friert die Szene für N Ticks ein.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlarePauseDef
    {
        /// <summary>Startzeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Dauer in Ticks.</summary>
        [JsonProperty("duration")] public int Duration;
    }

    /// <summary>
    /// Ein Bewegungs-Controller des Charakters (<c>VelSet</c>, <c>VelAdd</c>,
    /// <c>VelMul</c>, <c>PosSet</c>, <c>PosAdd</c>) in MUGEN-Pixeln pro Tick.
    /// <c>x</c> ist facing-relativ, <c>y</c> zählt nach unten (0 = Boden).
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareMoveDef
    {
        /// <summary>Zeitpunkt in MUGEN-Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Controller-Name in Kleinschreibung.</summary>
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)] public string Type;
        /// <summary>Horizontaler Wert.</summary>
        [JsonProperty("x")] public float X;
        /// <summary>Vertikaler Wert (nach unten positiv).</summary>
        [JsonProperty("y")] public float Y;
    }

    /// <summary>
    /// Eine „gehört-zusammen“-Gruppe aus der MUGEN-State-Machine: die sichtbare
    /// Body-Kette eines Moves plus die davon gespawnten Effekt-/Waffen-Animationen.
    /// Wird vom Importer aus <c>states.json</c> abgeleitet und in die Atlas-JSON gebacken.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareSequenceDef
    {
        /// <summary>MUGEN-Statedef-Nummer, aus der die Gruppe stammt.</summary>
        [JsonProperty("state")] public int State;
        /// <summary>Lesbarer Move-Name (aus der Reserved-Action-Konvention) oder leer.</summary>
        [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)] public string Label;
        /// <summary>
        /// Freitext-Überschrift des CNS-Blocks (z. B. „New victim anim states“).
        /// Charaktere mit eigenen Move-Sätzen benennen ihre Gruppen nur so.
        /// </summary>
        [JsonProperty("group", NullValueHandling = NullValueHandling.Ignore)] public string Group;
        /// <summary>
        /// Einordnung nach MUGEN-Statenummernband: System/Normal/Command/Special/
        /// Hyper/Hit, <c>Custom</c> für charaktereigene Moves, <c>Effect</c> für
        /// Statedefs ohne eigene Body-Animation.
        /// </summary>
        [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)] public string Category;
        /// <summary>Sichtbare Body-Kette, nach Umschaltzeitpunkt sortiert.</summary>
        [JsonProperty("anims")] public FlareSequenceAnimDef[] Anims;
        /// <summary>
        /// Vom Move gespawnte Effekt-/Waffen-Animationen aus <c>Explod</c> /
        /// <c>Projectile</c> / <c>Helper</c>-Controllern (mit Timing/Position). Kann leer sein.
        /// </summary>
        [JsonProperty("effects", NullValueHandling = NullValueHandling.Ignore)] public FlareEffectDef[] Effects;
        /// <summary>Zeitlich platzierte <c>PlaySnd</c>-Aufrufe des Moves. Kann leer sein.</summary>
        [JsonProperty("sounds", NullValueHandling = NullValueHandling.Ignore)] public FlareSoundDef[] Sounds;
        /// <summary>Zeitlich platzierte Farbfilter des Moves. Kann leer sein.</summary>
        [JsonProperty("palfx", NullValueHandling = NullValueHandling.Ignore)] public FlarePalFxDef[] PalFx;
        /// <summary>Zeitlich platzierte Pausen des Moves. Kann leer sein.</summary>
        [JsonProperty("pauses", NullValueHandling = NullValueHandling.Ignore)] public FlarePauseDef[] Pauses;
        /// <summary>Bewegungs-Controller des Charakters. Kann leer sein.</summary>
        [JsonProperty("moves", NullValueHandling = NullValueHandling.Ignore)] public FlareMoveDef[] Moves;
        /// <summary>Treffer-Definitionen des Moves. Kann leer sein.</summary>
        [JsonProperty("hits", NullValueHandling = NullValueHandling.Ignore)] public FlareHitDef[] Hits;
        /// <summary>Nachzieh-Effekte des Moves. Kann leer sein.</summary>
        [JsonProperty("afterimage", NullValueHandling = NullValueHandling.Ignore)] public FlareAfterImageDef[] AfterImage;
        /// <summary>Änderungen an laufenden Effekten. Kann leer sein.</summary>
        [JsonProperty("modifies", NullValueHandling = NullValueHandling.Ignore)] public FlareModifyDef[] Modifies;
        /// <summary>Transparenzwechsel des Charakters selbst. Kann leer sein.</summary>
        [JsonProperty("bodytrans", NullValueHandling = NullValueHandling.Ignore)] public FlareBodyTransDef[] BodyTrans;
        /// <summary>
        /// <c>ChangeAnim2</c>: Animationen, die der Move dem Gegner aufzwingt und
        /// dabei aus den Sprites <i>dieses</i> Charakters zeichnet — Würfe, Griffe
        /// und Opferdarstellungen. Kann leer sein.
        /// </summary>
        [JsonProperty("victim", NullValueHandling = NullValueHandling.Ignore)] public FlareSequenceAnimDef[] Victim;
        /// <summary>
        /// Zeichenpriorität des Charakters über die Zeit (<c>sprpriority</c> im
        /// Statedef-Kopf plus jeder <c>SprPriority</c>-Controller). Kann leer sein.
        /// </summary>
        [JsonProperty("sprpriority", NullValueHandling = NullValueHandling.Ignore)] public FlareSprPriorityDef[] SprPriority;
        /// <summary>Zeichenwinkel des Charakters über die Zeit. Kann leer sein.</summary>
        [JsonProperty("bodyangle", NullValueHandling = NullValueHandling.Ignore)] public FlareBodyAngleDef[] BodyAngle;
        /// <summary>Vom Move gespawnte Helfer mit eigener Animationskette. Kann leer sein.</summary>
        [JsonProperty("helpers", NullValueHandling = NullValueHandling.Ignore)] public FlareHelperDef[] Helpers;
        /// <summary>1, wenn der Statedef <c>physics = A</c> nutzt und Schwerkraft wirkt.</summary>
        [JsonProperty("airborne")] public int Airborne;
        /// <summary>
        /// <c>physics</c> aus dem Statedef-Kopf: "A" (Schwerkraft), "S" (Stand-Reibung),
        /// "C" (Hock-Reibung), "N" (keine Physik). Leer, wenn der Statedef nichts angibt.
        /// </summary>
        [JsonProperty("physics", NullValueHandling = NullValueHandling.Ignore)] public string Physics;
        /// <summary>
        /// <c>StateTypeSet</c>-Umschaltungen der Körperphysik über die Zeit. Ein Sprung
        /// beginnt am Boden und wird erst beim Absprung luftgetragen. Kann leer sein.
        /// </summary>
        [JsonProperty("physicschanges", NullValueHandling = NullValueHandling.Ignore)] public FlarePhysicsChangeDef[] PhysicsChanges;
        /// <summary><c>Turn</c>: Ticks, an denen die Blickrichtung kippt. Kann leer sein.</summary>
        [JsonProperty("turns", NullValueHandling = NullValueHandling.Ignore)] public FlareTimeMarkerDef[] Turns;
        /// <summary>
        /// <c>AssertSpecial invisible</c>: ab diesen Ticks ist der Körper unsichtbar
        /// (Teleport-, Verschwinde- und Besitz-Moves). Kann leer sein.
        /// </summary>
        [JsonProperty("invisible", NullValueHandling = NullValueHandling.Ignore)] public FlareTimeMarkerDef[] Invisible;
    }

    /// <summary>Ein <c>StateTypeSet physics = ...</c> zu einem Zeitpunkt.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlarePhysicsChangeDef
    {
        /// <summary>Zeitpunkt in Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Neuer Physik-Typ: "S", "C", "A" oder "N".</summary>
        [JsonProperty("value")] public string Value;
    }

    /// <summary>Ein Zeitpunkt ohne weitere Werte (<c>Turn</c>, <c>AssertSpecial</c>).</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareTimeMarkerDef
    {
        /// <summary>Zeitpunkt in Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
    }

    /// <summary>Ein <c>BindToParent</c>/<c>BindToRoot</c> eines Helfers.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareBindDef
    {
        /// <summary>Zeitpunkt in Ticks ab Move-Start.</summary>
        [JsonProperty("time")] public int Time;
        /// <summary>Versatz [x, y] zur Achse des Charakters in MUGEN-Pixeln.</summary>
        [JsonProperty("pos", NullValueHandling = NullValueHandling.Ignore)] public int[] Pos;
        /// <summary>Bindungsdauer in Ticks; -1 = dauerhaft.</summary>
        [JsonProperty("duration")] public int Duration = 1;
    }

    /// <summary>
    /// Wurzel-Definition eines FLARE-Sprite-Atlas (eine Schicht: chest, feet, hands, legs ...).
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareAtlasDef
    {
        /// <summary>Dateiname der PNG-Atlastextur (liegt neben der JSON in StreamingAssets).</summary>
        [JsonProperty("image")] public string Image;
        /// <summary>
        /// Alle Atlas-Seiten in Reihenfolge (Index == <see cref="FlareCell.AtlasPage"/>).
        /// Wird vom Importer für mehrseitige Charaktere emittiert. <c>null</c> bei
        /// einseitigen Alt-Atlanten &#8212; dann greift der Loader auf <see cref="Image"/> zurück.
        /// </summary>
        [JsonProperty("images", NullValueHandling = NullValueHandling.Ignore)] public string[] Images;
        /// <summary>MUGEN <c>movement.yaccel</c>: Pixel pro Tick², die einen Char in der Luft ziehen.</summary>
        [JsonProperty("gravity")] public float Gravity = 0.5f;
        /// <summary>MUGEN <c>movement.stand.friction</c>: Faktor auf <c>vel.x</c> pro Tick am Boden.</summary>
        [JsonProperty("standFriction")] public float StandFriction = 0.85f;
        /// <summary>MUGEN <c>movement.crouch.friction</c>: Faktor auf <c>vel.x</c> pro Tick in der Hocke.</summary>
        [JsonProperty("crouchFriction")] public float CrouchFriction = 0.82f;
        /// <summary>Animationen, gemappt auf Namen wie "stance", "run", "swing", "hit", "die".</summary>
        [JsonProperty("animations")] public Dictionary<string, FlareAnimationDef> Animations;
        /// <summary>
        /// „Gehört-zusammen“-Gruppen aus der MUGEN-State-Machine (Body-Kette + Effekte).
        /// <c>null</c> bei Atlanten ohne <c>states.json</c>-Ableitung.
        /// </summary>
        [JsonProperty("sequences", NullValueHandling = NullValueHandling.Ignore)] public FlareSequenceDef[] Sequences;
    }
}
