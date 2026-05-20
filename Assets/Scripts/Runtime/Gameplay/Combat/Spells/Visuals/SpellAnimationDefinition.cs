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

        /// <summary>Pro-Frame-Blit-Offsets (X/Y in Source-Pixeln) relativ zur Canvas-Box.</summary>
        [JsonProperty("frames")] public List<SpellAnimationFrame> Frames;

        /// <summary>True, wenn ein nicht-trivialer Loop-Bereich definiert ist.</summary>
        public bool HasLoop => LoopEnd > LoopStart;
    }

    /// <summary>
    /// Eintrag fuer einen einzelnen Sprite-Frame. Enthaelt die Top-Left-Blit-
    /// Position innerhalb der Source-Canvas-Box, so wie sie das FLARE-
    /// Original-<c>.sa</c>-Format pro Zeile <c>index,x,y</c> liefert. Die
    /// tatsaechliche Frame-Groesse wird zur Laufzeit aus dem PNG (Texture
    /// width/height) gezogen &#8212; <c>.sa</c>/JSON haelt nur den Offset.
    /// </summary>
    /// <remarks>
    /// Koordinaten sind in <em>Source-Pixeln</em> (FLARE-Down, Origin oben-
    /// links) relativ zur Canvas-Box <c>canvas_size / scale</c>. Werden vom
    /// <c>SpellSpriteCache</c> in den Unity-Sprite-Pivot umgerechnet, damit
    /// der Anker stets auf dem Canvas-Zentrum sitzt (Spell-Zentrum bleibt
    /// ueber alle Frames der Sequenz konsistent).
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellAnimationFrame
    {
        /// <summary>
        /// X-Offset (in Source-Pixeln) des oberen-linken PNG-Ecks innerhalb
        /// der quadratischen Canvas-Box.
        /// </summary>
        [JsonProperty("x")] public int X;

        /// <summary>
        /// Y-Offset (in Source-Pixeln, FLARE-down: 0 = oben) des oberen-linken
        /// PNG-Ecks innerhalb der quadratischen Canvas-Box.
        /// </summary>
        [JsonProperty("y")] public int Y;
    }
}
