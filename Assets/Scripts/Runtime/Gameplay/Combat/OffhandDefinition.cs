using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Datengetriebene Offhand-Item-Definition. Quelle:
    /// <c>StreamingAssets/combat/offhand_items.json</c>. Modder können neue Einträge
    /// ergänzen, ohne den Code anzufassen — solange ein passender FLARE-Atlas unter
    /// <c>StreamingAssets/player_male</c> / <c>player_female</c> liegt.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class OffhandDefinition
    {
        /// <summary>
        /// Eindeutiger String-Key (z. B. <c>"buckler"</c>, <c>"shield"</c>). Muss
        /// auf den FLARE-Atlas-Namen unter <c>StreamingAssets/player_*/</c> matchen.
        /// </summary>
        [JsonProperty("id")] public string Id;

        /// <summary>Kategorie. Bestimmt Gameplay-Verhalten (Block vs. Cast-Bonus etc.).</summary>
        [JsonProperty("type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public OffhandType Type;

        /// <summary>
        /// Anteil eingehenden Schadens, der bei aktivem Block geschluckt wird (0..1).
        /// 0.3 = 30 % weniger Schaden, 0.6 = 60 % weniger.
        /// </summary>
        [JsonProperty("block_damage_reduction")] public float BlockDamageReduction = 0.3f;

        /// <summary>
        /// Multiplikator auf die Laufgeschwindigkeit, solange geblockt wird (0..1).
        /// 0.5 = halbe Speed im Block, 0.3 = ein Drittel.
        /// </summary>
        [JsonProperty("block_move_speed_multiplier")] public float BlockMoveSpeedMultiplier = 0.5f;

        /// <summary>
        /// Mindest-Zeit zwischen zwei Block-Aktivierungen in Sekunden (Anti-Spam).
        /// </summary>
        [JsonProperty("block_cooldown")] public float BlockCooldown = 0.5f;

        /// <summary>
        /// True, wenn das Item überhaupt blocken kann (Buckler/Shield). Andere
        /// Kategorien (Torch/Quiver/Orb) liefern <c>false</c>.
        /// </summary>
        [JsonIgnore]
        public bool CanBlock => Type is OffhandType.Buckler or OffhandType.Shield;
    }
}
