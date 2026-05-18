using System;
using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Kit-Definition aus <c>StreamingAssets/spells/_visual_kits.json</c>.
    /// 1:1 Mirror der Source-Tabelle <c>spell_visual</c>: bis zu zwei
    /// Sprite-Layer (primaer + sekundaer) mit eigenen Offsets, Faerbungen
    /// und Blend-Modi, optional ein Partikel-System, ein Sound sowie
    /// Boden-/Unit-Glow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Spranim</b>: Dateiname inklusive <c>.sa</c>-Endung
    /// (z. B. <c>"magic_008d.sa"</c>). Riftstorms
    /// <see cref="SpellAnimationCatalog"/> indexiert die portierten Animationen
    /// ohne Endung; deshalb in den Resolver-Helpern <see cref="PrimaryAnimationName"/>
    /// bzw. <see cref="SecondaryAnimationName"/> verwenden.
    /// </para>
    /// <para>
    /// <b>Y-Offsets</b>: <c>spranim_y</c> / <c>spranim_y_2</c> koennen sowohl
    /// Integer-Pixel als auch nicht-numerische Tags wie <c>"-heightp"</c> (=
    /// negative Sprite-Hoehe) enthalten. Daher als String gehalten — Konsumenten
    /// werten sie ueber <see cref="TryGetPrimaryY"/> / <see cref="TryGetSecondaryY"/>
    /// aus und behandeln den Fallback selbst.
    /// </para>
    /// <para>
    /// <b>Glow-Farben</b>: 32-Bit ARGB im DB-Original; hier <see cref="long"/>,
    /// weil der DB-Export unsigned ist und sonst im int-Bereich ueberlaeuft.
    /// </para>
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellVisualKitDefinition
    {
        /// <summary>Primaerschluessel (= Kit-ID; auf diesen verweisen die <see cref="SpellVisualKitMapping"/>-Felder).</summary>
        [JsonProperty("id")] public int Id { get; set; }

        // ---- Primaerer Sprite-Layer ------------------------------------

        /// <summary>Sprite-Animation des primaeren Layers (Dateiname mit <c>.sa</c>).</summary>
        [JsonProperty("spranim")] public string Spranim { get; set; }

        /// <summary>X-Offset des primaeren Layers in Pixeln (Source-Koordinaten).</summary>
        [JsonProperty("spranim_x")] public int SpranimX { get; set; }

        /// <summary>Y-Offset des primaeren Layers; String, weil Tags wie <c>"-heightp"</c> erlaubt sind.</summary>
        [JsonProperty("spranim_y")] public string SpranimY { get; set; }

        /// <summary>Tint-Color (Source-Index) des primaeren Layers. <c>-1</c> = unveraendert.</summary>
        [JsonProperty("sprcolor")] public long Sprcolor { get; set; } = -1;

        /// <summary>Blend-Mode des primaeren Layers (Source-Index). <c>-1</c> = Default/Alpha.</summary>
        [JsonProperty("spranim_blend")] public int SpranimBlend { get; set; } = -1;

        // ---- Sekundaerer Sprite-Layer (optional) -----------------------

        /// <summary>Sprite-Animation des sekundaeren Layers (Aura-Glow, Cast-Glyph, ...).</summary>
        [JsonProperty("spranim_2")] public string Spranim2 { get; set; }

        /// <summary>X-Offset des sekundaeren Layers in Pixeln.</summary>
        [JsonProperty("spranim_x_2")] public int Spranim2X { get; set; }

        /// <summary>Y-Offset des sekundaeren Layers (String, vgl. <see cref="SpranimY"/>).</summary>
        [JsonProperty("spranim_y_2")] public string Spranim2Y { get; set; }

        /// <summary>Tint-Color des sekundaeren Layers. <c>-1</c> = unveraendert.</summary>
        [JsonProperty("sprcolor_2")] public long Sprcolor2 { get; set; } = -1;

        /// <summary>Blend-Mode des sekundaeren Layers. <c>-1</c> = Default.</summary>
        [JsonProperty("spranim2_blend")] public int Spranim2Blend { get; set; } = -1;

        /// <summary>0/1: ob der sekundaere Layer ueber dem primaeren liegt.</summary>
        [JsonProperty("spranim2_topmost")] public int Spranim2TopmostInt { get; set; }

        // ---- Partikel / Sound / Glow ----------------------------------

        /// <summary>Optionaler Partikel-System-Bezeichner (Source-Format). Aktuell ungenutzt im Riftstorm-Renderer.</summary>
        [JsonProperty("psystem")] public string Psystem { get; set; }

        /// <summary>Sound-Dateiname (relativ zum Sound-Verzeichnis), der beim Trigger des Kits gespielt wird.</summary>
        [JsonProperty("sound")] public string Sound { get; set; }

        /// <summary>Boden-Glow-Farbe (ARGB als unsigned). <c>0</c> = kein Glow.</summary>
        [JsonProperty("ground_glow_color")] public long GroundGlowColor { get; set; }

        /// <summary>Unit-Glow-Farbe (ARGB als unsigned). <c>0</c> = kein Glow.</summary>
        [JsonProperty("unit_glow_color")] public long UnitGlowColor { get; set; }

        // ---- Helper ----------------------------------------------------

        /// <summary>True, wenn der sekundaere Layer ueber dem primaeren liegt.</summary>
        public bool Spranim2Topmost => Spranim2TopmostInt != 0;

        /// <summary>True, wenn der primaere Layer gesetzt ist.</summary>
        public bool HasPrimary => !string.IsNullOrEmpty(Spranim);

        /// <summary>True, wenn der sekundaere Layer gesetzt ist.</summary>
        public bool HasSecondary => !string.IsNullOrEmpty(Spranim2);

        /// <summary>Primaerer Animations-Name ohne <c>.sa</c>-Endung (zum Lookup im <see cref="SpellAnimationCatalog"/>).</summary>
        public string PrimaryAnimationName => StripSa(Spranim);

        /// <summary>Sekundaerer Animations-Name ohne <c>.sa</c>-Endung.</summary>
        public string SecondaryAnimationName => StripSa(Spranim2);

        /// <summary>Versucht, den primaeren Y-Offset als Pixel-Integer zu interpretieren. <c>false</c> bei Tags wie <c>"-heightp"</c>.</summary>
        public bool TryGetPrimaryY(out int pixels) => TryParsePixel(SpranimY, out pixels);

        /// <summary>Versucht, den sekundaeren Y-Offset als Pixel-Integer zu interpretieren.</summary>
        public bool TryGetSecondaryY(out int pixels) => TryParsePixel(Spranim2Y, out pixels);

        /// <summary>Strippt eine <c>.sa</c>-Endung (case-insensitive). Liefert Eingabe unveraendert ohne Endung zurueck.</summary>
        public static string StripSa(string spranim)
        {
            if (string.IsNullOrEmpty(spranim)) { return string.Empty; }
            if (spranim.EndsWith(".sa", StringComparison.OrdinalIgnoreCase))
            {
                return spranim.Substring(0, spranim.Length - 3);
            }
            return spranim;
        }

        private static bool TryParsePixel(string raw, out int pixels)
        {
            pixels = 0;
            if (string.IsNullOrEmpty(raw)) { return false; }
            return int.TryParse(raw, out pixels);
        }
    }
}
