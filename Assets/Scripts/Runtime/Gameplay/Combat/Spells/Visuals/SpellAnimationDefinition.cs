using System.Collections.Generic;
using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// 1:1-Abbild eines Animations-JSON unter
    /// <c>StreamingAssets/spells/animations/&lt;name&gt;.json</c>. Beschreibt eine
    /// Sprite-Sheet-Animation: Bildquelle (<see cref="ImagePattern"/>), Anzahl
    /// Frames, Frame-Größen, Loop-Bereich und Wiedergabe-Tempo.
    /// </summary>
    /// <remarks>
    /// Mirror der C++-Original-Struktur. Die Sprite-Frame-PNGs liegen unter
    /// <c>Assets/Art/&lt;image_pattern&gt;</c>; <c>{frame}</c> wird zur Laufzeit
    /// durch den 1-basierten Frame-Index ersetzt.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellAnimationDefinition
    {
        /// <summary>Eindeutiger Animations-Name (= Dateiname ohne Endung).</summary>
        [JsonProperty("name")] public string Name = string.Empty;

        /// <summary>
        /// Pfad-Template für Sprite-Frames relativ zu <c>Assets/Art/</c>. Enthält
        /// den Platzhalter <c>{frame}</c>, der durch den 1-basierten Frame-Index
        /// ersetzt wird (z. B. <c>spells/fire_001_{frame}.png</c>).
        /// </summary>
        [JsonProperty("image_pattern")] public string ImagePattern = string.Empty;

        /// <summary>
        /// Kantenlänge der quadratischen Canvas-Box in Pixeln, in die alle Frames
        /// einer Animation gezeichnet werden. Aus dem C++-Original übernommen.
        /// </summary>
        [JsonProperty("canvas_size")] public int CanvasSize;

        /// <summary>Globale Skalierung der Frames (1.0 = nativ).</summary>
        [JsonProperty("scale")] public float Scale = 1f;

        /// <summary>Anzahl Frames in der Sequenz.</summary>
        [JsonProperty("frames_count")] public int FramesCount;

        /// <summary>Frame-Delay in Millisekunden (gleich für alle Frames).</summary>
        [JsonProperty("delay_ms")] public int DelayMs;

        /// <summary>
        /// 0-basierter Loop-Start-Index. Wenn <c>loop_end &gt; loop_start</c>, wird
        /// dieser Teilbereich endlos wiederholt (für Aura-Loops). Sonst One-Shot.
        /// </summary>
        [JsonProperty("loop_start")] public int LoopStart;

        /// <summary>0-basierter Loop-End-Index (inklusiv). 0 = kein Loop.</summary>
        [JsonProperty("loop_end")] public int LoopEnd;

        /// <summary>Pro-Frame-Größen (Breite/Höhe in Pixeln) für korrektes Cropping.</summary>
        [JsonProperty("frames")] public List<SpellAnimationFrame> Frames;

        /// <summary>True, wenn ein nicht-trivialer Loop-Bereich definiert ist.</summary>
        public bool HasLoop => LoopEnd > LoopStart;
    }

    /// <summary>
    /// Größenangabe eines einzelnen Sprite-Frames in Pixeln.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellAnimationFrame
    {
        /// <summary>Frame-Breite in Pixeln.</summary>
        [JsonProperty("w")] public int Width;

        /// <summary>Frame-Höhe in Pixeln.</summary>
        [JsonProperty("h")] public int Height;
    }
}
