using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Partikel-System-Definition aus <c>StreamingAssets/particles/_particles.json</c>.
    /// 1:1 Mirror der Source-Binärstruktur <c>ParticleSystemInfo</c>
    /// (siehe <c>Tools/Scripts/particle_import/psi_to_json.py</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Maßeinheiten folgen dem Source-Renderer (SFML, 2D):
    /// Geschwindigkeiten/Beschleunigungen in Pixel pro Sekunde, Winkel in
    /// Radianten, Lebensdauern in Sekunden, Farben in <c>[0..1]</c> RGBA.
    /// </para>
    /// <para>
    /// <see cref="Lifetime"/> <c>&lt;= 0</c> bedeutet "läuft, bis der Spawner
    /// das System stoppt" (endlos im Source). Riftstorm caped das im Spawner.
    /// </para>
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ParticleSystemDefinition
    {
        /// <summary>Atlas-Tile-X-Pixel-Offset im <c>particles.png</c> (128×128 Atlas, 4×4 32px-Tiles → Werte ∈ {0,32,64,96}).</summary>
        [JsonProperty("tile_x")] public int TileX { get; set; }

        /// <summary>Atlas-Tile-Y-Pixel-Offset im <c>particles.png</c> (Werte ∈ {0,32,64,96}).</summary>
        [JsonProperty("tile_y")] public int TileY { get; set; }

        /// <summary>Additiver Blend (true) oder Alpha-Blend (false).</summary>
        [JsonProperty("add_blend")] public bool AddBlend { get; set; } = true;

        /// <summary>Partikel pro Sekunde (Emission-Rate).</summary>
        [JsonProperty("emission")] public int Emission { get; set; }

        /// <summary>System-Lebensdauer in Sekunden. <c>&lt;= 0</c> = endlos.</summary>
        [JsonProperty("lifetime")] public float Lifetime { get; set; }

        /// <summary>Untere Schranke der Partikel-Lebensdauer (Sekunden).</summary>
        [JsonProperty("particle_life_min")] public float ParticleLifeMin { get; set; }

        /// <summary>Obere Schranke der Partikel-Lebensdauer (Sekunden).</summary>
        [JsonProperty("particle_life_max")] public float ParticleLifeMax { get; set; }

        /// <summary>Basis-Emissionsrichtung in Radianten (0 = rechts, π/2 = unten in 2D).</summary>
        [JsonProperty("direction")] public float Direction { get; set; }

        /// <summary>Streukegel-Winkel in Radianten (2π = Vollkreis).</summary>
        [JsonProperty("spread")] public float Spread { get; set; }

        /// <summary><c>0/1</c>: ob die Emission relativ zur Bewegung des Casters gedreht wird.</summary>
        [JsonProperty("relative")] public int Relative { get; set; }

        /// <summary>Untere Schranke der Initial-Geschwindigkeit (Pixel/Sek).</summary>
        [JsonProperty("speed_min")] public float SpeedMin { get; set; }

        /// <summary>Obere Schranke der Initial-Geschwindigkeit (Pixel/Sek).</summary>
        [JsonProperty("speed_max")] public float SpeedMax { get; set; }

        /// <summary>Untere Gravitation (Pixel/Sek², Y-positiv = nach unten im Source).</summary>
        [JsonProperty("gravity_min")] public float GravityMin { get; set; }

        /// <summary>Obere Gravitation (Pixel/Sek²).</summary>
        [JsonProperty("gravity_max")] public float GravityMax { get; set; }

        /// <summary>Untere Radial-Beschleunigung (Pixel/Sek², vom Emitter weg).</summary>
        [JsonProperty("radial_accel_min")] public float RadialAccelMin { get; set; }

        /// <summary>Obere Radial-Beschleunigung.</summary>
        [JsonProperty("radial_accel_max")] public float RadialAccelMax { get; set; }

        /// <summary>Untere Tangential-Beschleunigung (Pixel/Sek², senkrecht zur Radialen).</summary>
        [JsonProperty("tangential_accel_min")] public float TangentialAccelMin { get; set; }

        /// <summary>Obere Tangential-Beschleunigung.</summary>
        [JsonProperty("tangential_accel_max")] public float TangentialAccelMax { get; set; }

        /// <summary>Start-Größe (Skalar, 1.0 = nativer 32px-Tile).</summary>
        [JsonProperty("size_start")] public float SizeStart { get; set; }

        /// <summary>End-Größe (Skalar).</summary>
        [JsonProperty("size_end")] public float SizeEnd { get; set; }

        /// <summary>Größen-Varianz (0..1; Source: Mischfaktor zwischen Start und End).</summary>
        [JsonProperty("size_var")] public float SizeVar { get; set; }

        /// <summary>Start-Rotation (Radianten/Sek).</summary>
        [JsonProperty("spin_start")] public float SpinStart { get; set; }

        /// <summary>End-Rotation (Radianten/Sek).</summary>
        [JsonProperty("spin_end")] public float SpinEnd { get; set; }

        /// <summary>Rotations-Varianz.</summary>
        [JsonProperty("spin_var")] public float SpinVar { get; set; }

        /// <summary>Start-Farbe als RGBA-Array (<c>[r, g, b, a]</c>, jeweils 0..1).</summary>
        [JsonProperty("color_start")] public float[] ColorStart { get; set; }

        /// <summary>End-Farbe als RGBA-Array.</summary>
        [JsonProperty("color_end")] public float[] ColorEnd { get; set; }

        /// <summary>Farb-Varianz (Mischfaktor zwischen Start- und End-Farbe).</summary>
        [JsonProperty("color_var")] public float ColorVar { get; set; }

        /// <summary>Alpha-Varianz.</summary>
        [JsonProperty("alpha_var")] public float AlphaVar { get; set; }

        /// <summary>True, wenn die Emission im Caster-Lokal-Koordinatensystem läuft (mitfolgt).</summary>
        public bool IsRelative => Relative != 0;

        /// <summary>True, wenn das System eine endliche Lebensdauer hat.</summary>
        public bool HasFiniteLifetime => Lifetime > 0f;
    }
}
