using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Datengetriebene Waffen-Definition. Quelle: <c>StreamingAssets/combat/weapons.json</c>.
    /// Modder können neue Einträge ergänzen, ohne den Code anzufassen.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class WeaponDefinition
    {
        /// <summary>
        /// Eindeutiger String-Key (z. B. <c>"sword"</c>, <c>"long_bow"</c>). Muss
        /// auf den FLARE-Atlas-Namen unter <c>StreamingAssets/player_*/</c> matchen,
        /// damit das Visual-System dieselbe Datei laden kann.
        /// </summary>
        [JsonProperty("id")] public string Id;

        /// <summary>Kategorie. Bestimmt Attack-Animation (Swing vs. Shoot).</summary>
        [JsonProperty("type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public WeaponType Type;

        /// <summary>Cooldown zwischen zwei Attacken in Sekunden.</summary>
        [JsonProperty("attack_cooldown")] public float AttackCooldown = 1f;

        /// <summary>Reichweite in Unity-Weltunits (Melee ≈ 1.5, Ranged 10+).</summary>
        [JsonProperty("range")] public float Range = 1.5f;

        /// <summary>Basis-Schaden pro Treffer (server-seitig).</summary>
        [JsonProperty("base_damage")] public int BaseDamage = 10;

        /// <summary>
        /// Relativer Frame-Anteil (0..1), bei dem der Server den Treffer auflöst
        /// (Hitscan / Overlap-Check). Standard 0.5 = Mitte der Anim.
        /// </summary>
        [JsonProperty("hit_resolve_progress")] public float HitResolveProgress = 0.5f;

        /// <summary>True, wenn die Attacke ein Fernkampfschuss ist (Bow / Crossbow / Gun).</summary>
        [JsonIgnore]
        public bool IsRanged => Type is WeaponType.Bow or WeaponType.Crossbow or WeaponType.Gun;

        /// <summary>
        /// Wählt die passende Attack-Animation für eine Waffenkategorie.
        /// Regel: Bow/Crossbow/Gun → <see cref="CombatAnim.Shoot"/>, sonst <see cref="CombatAnim.Swing"/>.
        /// </summary>
        public static CombatAnim PickAttackAnim(WeaponType type)
        {
            return type switch
            {
                WeaponType.Bow or WeaponType.Crossbow or WeaponType.Gun => CombatAnim.Shoot,
                _ => CombatAnim.Swing,
            };
        }

        /// <summary>Convenience: liefert die Attack-Animation dieser Definition.</summary>
        public CombatAnim AttackAnim => PickAttackAnim(Type);
    }
}
