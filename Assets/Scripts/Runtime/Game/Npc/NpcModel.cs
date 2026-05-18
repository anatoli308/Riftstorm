using Newtonsoft.Json;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// DTO fuer einen Eintrag aus <c>StreamingAssets/npc/_models.json</c>
    /// (migriert 1:1 aus <c>game.db.npc_models</c>). Verknuepft eine
    /// abstrakte Model-ID mit dem konkreten FLARE-Atlas-Dateinamen.
    /// </summary>
    /// <remarks>
    /// <b>Wichtig:</b> <see cref="Name"/> IST der Atlas-Dateiname (ohne
    /// <c>.json</c>/<c>.png</c>-Endung), z. B. <c>"antlion_small"</c>. Der
    /// <see cref="FlareNpcSpawner"/> uebergibt ihn direkt an
    /// <see cref="Riftstorm.Game.Sprites.FlareAtlasLoader.LoadAsync"/>.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class NpcModel
    {
        /// <summary>Primaerschluessel (Model-ID). Referenziert von <see cref="NpcTemplate.ModelId"/>.</summary>
        [JsonProperty("id")] public int Id { get; set; }

        /// <summary>FLARE-Atlas-Dateiname OHNE Endung.</summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>
        /// Hoehe des NPC-Modells in FLARE-Einheiten. Wird vom Spawner an
        /// <see cref="Riftstorm.Game.Combat.UnitStats.ApplyBaseStats"/>
        /// (<c>hitRadius</c>/<c>selectionRadius</c>) weitergegeben.
        /// </summary>
        [JsonProperty("height")] public float Height { get; set; }
    }
}
