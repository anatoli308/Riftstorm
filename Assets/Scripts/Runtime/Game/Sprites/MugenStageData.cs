using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Rohform einer aus MUGEN konvertierten Stage
    /// (<c>StreamingAssets/Custom_Stages/&lt;Name&gt;/&lt;Name&gt;.json</c>).
    /// </summary>
    /// <remarks>
    /// Die Stage liefert die Bezugswerte, die in den Charakterdateien fehlen:
    /// Standposition beider Kämpfer, Bodenhöhe (<c>zoffset</c>) und Breite des
    /// Kameraausschnitts (<c>localcoord</c>). Ohne sie sind bildschirmverankerte
    /// Effekte und Projektilweiten geraten.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class MugenStageDef
    {
        /// <summary>Anzeigename aus <c>[Info] name</c>.</summary>
        [JsonProperty("name")] public string Name;

        /// <summary>Kameraausschnitt in MUGEN-Pixeln (Breite, Höhe).</summary>
        [JsonProperty("localcoord")] public float[] LocalCoord;

        /// <summary>Bildschirm-Y des Bodens; y zählt von der Bildoberkante nach unten.</summary>
        [JsonProperty("zoffset")] public float ZOffset;

        /// <summary>Startposition von Spieler 1 im Kameraraum.</summary>
        [JsonProperty("p1start")] public float[] P1Start;

        /// <summary>Startposition von Spieler 2 im Kameraraum.</summary>
        [JsonProperty("p2start")] public float[] P2Start;

        /// <summary>Waagerechter Abstand beider Startpositionen in MUGEN-Pixeln.</summary>
        [JsonProperty("player_distance")] public float PlayerDistance;

        /// <summary>Erste Atlas-Seite; Rückfall, wenn <see cref="Images"/> leer ist.</summary>
        [JsonProperty("image")] public string Image;

        /// <summary>Alle Atlas-Seiten in Seitenreihenfolge.</summary>
        [JsonProperty("images")] public string[] Images;

        /// <summary>Sprite-Rechtecke im Atlas.</summary>
        [JsonProperty("sprites")] public MugenStageSpriteDef[] Sprites;

        /// <summary>Hintergrundebenen in Zeichenreihenfolge.</summary>
        [JsonProperty("layers")] public MugenStageLayerDef[] Layers;
    }

    /// <summary>Ein gepacktes Stage-Sprite mit MUGEN-Achse als Pivot.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class MugenStageSpriteDef
    {
        /// <summary>Schlüssel <c>"group_image"</c>, wie ihn die Ebenen referenzieren.</summary>
        [JsonProperty("key")] public string Key;

        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
        [JsonProperty("w")] public int W;
        [JsonProperty("h")] public int H;

        /// <summary>MUGEN-Achse relativ zur linken oberen Sprite-Ecke.</summary>
        [JsonProperty("pivotX")] public int PivotX;

        /// <summary>MUGEN-Achse relativ zur linken oberen Sprite-Ecke.</summary>
        [JsonProperty("pivotY")] public int PivotY;

        /// <summary>Index der Atlas-Seite.</summary>
        [JsonProperty("page")] public int Page;
    }

    /// <summary>Eine <c>[BG ...]</c>-Ebene der Stage.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class MugenStageLayerDef
    {
        [JsonProperty("name")] public string Name;

        /// <summary><c>layerno</c>: 1 zeichnet vor den Kämpfern, sonst dahinter.</summary>
        [JsonProperty("layer")] public int Layer;

        /// <summary>Position im Kameraraum (x ab Bildmitte, y ab Bildoberkante).</summary>
        [JsonProperty("start")] public float[] Start;

        /// <summary>Parallaxfaktor; bei ruhender Kamera ohne Wirkung.</summary>
        [JsonProperty("delta")] public float[] Delta;

        [JsonProperty("scale")] public float[] Scale;

        /// <summary>Wiederholung: 0 = keine, 1 = füllt die Kamerabreite, n = n Kopien.</summary>
        [JsonProperty("tile")] public int[] Tile;

        /// <summary>Zeitachse der Ebene; ein Eintrag bei <c>type = normal</c>.</summary>
        [JsonProperty("frames")] public MugenStageFrameDef[] Frames;
    }

    /// <summary>Ein Frame einer animierten Hintergrundebene.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class MugenStageFrameDef
    {
        [JsonProperty("sprite")] public string Sprite;

        /// <summary>Anzeigedauer; 0 heißt "bleibt stehen".</summary>
        [JsonProperty("ms")] public long Ms;

        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
    }

    /// <summary>Laufzeitform einer Stage: fertige Sprites und Maße in Unity-Einheiten.</summary>
    public sealed class MugenStage
    {
        /// <summary>Anzeigename der Stage.</summary>
        public string Name { get; set; }

        /// <summary>Breite des Kameraausschnitts in MUGEN-Pixeln.</summary>
        public float ScreenWidth { get; set; }

        /// <summary>Höhe des Kameraausschnitts in MUGEN-Pixeln.</summary>
        public float ScreenHeight { get; set; }

        /// <summary>Bildschirm-Y des Bodens in MUGEN-Pixeln.</summary>
        public float GroundLevel { get; set; }

        /// <summary>Standposition von Spieler 1 im Kameraraum.</summary>
        public Vector2 P1Start { get; set; }

        /// <summary>Waagerechter Abstand beider Kämpfer in MUGEN-Pixeln.</summary>
        public float PlayerDistance { get; set; }

        /// <summary>Hintergrundebenen in Zeichenreihenfolge.</summary>
        public MugenStageLayer[] Layers { get; set; }

        /// <summary>Atlas-Seiten; werden beim Entladen zerstört.</summary>
        public Texture2D[] Textures { get; set; }
    }

    /// <summary>Laufzeitform einer Hintergrundebene.</summary>
    public sealed class MugenStageLayer
    {
        /// <summary>Name aus der <c>.def</c>, nur zur Diagnose.</summary>
        public string Name { get; set; }

        /// <summary>1 = vor den Kämpfern, sonst dahinter.</summary>
        public int Layer { get; set; }

        /// <summary>Position im Kameraraum in MUGEN-Pixeln.</summary>
        public Vector2 Start { get; set; }

        /// <summary>Skalierung aus <c>scalestart</c>.</summary>
        public Vector2 Scale { get; set; }

        /// <summary>Wiederholung entlang x/y.</summary>
        public Vector2Int Tile { get; set; }

        /// <summary>Zeitachse der Ebene.</summary>
        public MugenStageFrame[] Frames { get; set; }
    }

    /// <summary>Ein Frame einer Hintergrundebene mit fertigem Sprite.</summary>
    public sealed class MugenStageFrame
    {
        /// <summary>Darzustellendes Sprite; kann <c>null</c> sein, wenn es im Atlas fehlt.</summary>
        public Sprite Sprite { get; set; }

        /// <summary>Anzeigedauer in Sekunden; 0 heißt "bleibt stehen".</summary>
        public float Seconds { get; set; }

        /// <summary>Zusätzlicher Versatz aus der AIR-Zeile in MUGEN-Pixeln.</summary>
        public Vector2 Offset { get; set; }
    }
}
