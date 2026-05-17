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
        /// <summary>Gesamtdauer der Animation in Millisekunden.</summary>
        [JsonProperty("duration_ms")] public int DurationMs;
        /// <summary>
        /// Per-Frame-Dauer in Millisekunden (Index = Frame). Wird vom MUGEN-Importer
        /// emittiert, damit MUGEN-typische ungleichmäßige Timings (z. B. Hit-Frame 4 Ticks,
        /// Recovery 12 Ticks) im Animator korrekt abgespielt werden. <c>null</c> bei alten
        /// Atlanten &#8212; in dem Fall verteilt der Animator <see cref="DurationMs"/> gleichmäßig.
        /// </summary>
        [JsonProperty("frame_durations_ms", NullValueHandling = NullValueHandling.Ignore)] public int[] FrameDurationsMs;
        /// <summary>Roher Typ-String aus dem JSON ("looped", "play_once", "back_forth").</summary>
        [JsonProperty("type")] public string TypeRaw;
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
    /// Wurzel-Definition eines FLARE-Sprite-Atlas (eine Schicht: chest, feet, hands, legs ...).
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FlareAtlasDef
    {
        /// <summary>Dateiname der PNG-Atlastextur (liegt neben der JSON in StreamingAssets).</summary>
        [JsonProperty("image")] public string Image;
        /// <summary>Animationen, gemappt auf Namen wie "stance", "run", "swing", "hit", "die".</summary>
        [JsonProperty("animations")] public Dictionary<string, FlareAnimationDef> Animations;
    }
}
