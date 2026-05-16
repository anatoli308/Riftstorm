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
