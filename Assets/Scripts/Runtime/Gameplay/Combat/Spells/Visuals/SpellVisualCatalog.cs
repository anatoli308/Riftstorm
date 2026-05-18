using System.Collections.Generic;
using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Wurzel-DTO der <c>StreamingAssets/spells/spell_visuals.json</c>.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellVisualCatalogDef
    {
        /// <summary>Schema-Version (für zukünftige Migrationen).</summary>
        [JsonProperty("version")] public int Version = 1;

        /// <summary>Liste aller per-Spell-Visual-Kits.</summary>
        [JsonProperty("visuals")] public List<SpellVisualDefinition> Visuals;
    }

    /// <summary>
    /// Runtime-Lookup für <see cref="SpellVisualDefinition"/> per <c>spell_id</c>.
    /// Befüllt vom <see cref="SpellVisualCatalogLoader"/>; konsumiert auf
    /// Client-Seite vom <c>PlaySpellCastClientRpc</c>-Handler in
    /// <c>PlayerCombat</c>.
    /// </summary>
    public sealed class SpellVisualCatalog
    {
        private readonly Dictionary<string, SpellVisualDefinition> m_BySpellId;

        /// <summary>Anzahl Visual-Kits.</summary>
        public int Count => m_BySpellId.Count;

        /// <summary>Alle Visual-Kits (read-only).</summary>
        public IReadOnlyCollection<SpellVisualDefinition> All => m_BySpellId.Values;

        /// <summary>
        /// Erzeugt einen Katalog aus dem deserialisierten Wurzel-DTO.
        /// Einträge ohne <see cref="SpellVisualDefinition.SpellId"/> werden verworfen.
        /// </summary>
        public SpellVisualCatalog(SpellVisualCatalogDef def)
        {
            m_BySpellId = new Dictionary<string, SpellVisualDefinition>();
            if (def?.Visuals == null)
            {
                return;
            }
            foreach (SpellVisualDefinition v in def.Visuals)
            {
                if (v == null || string.IsNullOrEmpty(v.SpellId))
                {
                    continue;
                }
                m_BySpellId[v.SpellId] = v;
            }
        }

        /// <summary>Sucht ein Visual-Kit per <c>spell_id</c>.</summary>
        public bool TryGet(string spellId, out SpellVisualDefinition def)
        {
            if (string.IsNullOrEmpty(spellId))
            {
                def = null;
                return false;
            }
            return m_BySpellId.TryGetValue(spellId, out def);
        }

        /// <summary>Liefert das Visual-Kit oder <c>null</c>, falls die ID unbekannt ist.</summary>
        public SpellVisualDefinition Get(string spellId)
        {
            return TryGet(spellId, out SpellVisualDefinition d) ? d : null;
        }
    }
}
