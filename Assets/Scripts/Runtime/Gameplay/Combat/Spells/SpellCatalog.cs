using System.Collections.Generic;
using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Wurzel-Struktur der <c>spells.json</c>: optionale Schema-Version plus
    /// Liste aller Spells.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpellCatalogDef
    {
        /// <summary>Schema-Version. Aktuell 1.</summary>
        [JsonProperty("version")] public int Version = 1;

        /// <summary>Alle Spell-Einträge. Reihenfolge irrelevant; Lookup via <see cref="SpellDefinition.Id"/>.</summary>
        [JsonProperty("spells")] public List<SpellDefinition> Spells;
    }

    /// <summary>
    /// Runtime-Lookup für Spell-Definitionen. Wird vom <see cref="SpellCatalogLoader"/>
    /// befüllt und über den ServiceLocator bereitgestellt.
    /// </summary>
    public sealed class SpellCatalog
    {
        private readonly Dictionary<string, SpellDefinition> m_ById;

        /// <summary>Anzahl geladener Spell-Definitionen.</summary>
        public int Count => m_ById.Count;

        /// <summary>Alle Spell-Definitionen (read-only).</summary>
        public IReadOnlyCollection<SpellDefinition> All => m_ById.Values;

        /// <summary>
        /// Erzeugt einen Katalog aus einer geladenen Definition. Einträge ohne
        /// gültige <see cref="SpellDefinition.Id"/> werden verworfen.
        /// </summary>
        public SpellCatalog(SpellCatalogDef def)
        {
            m_ById = new Dictionary<string, SpellDefinition>();
            if (def?.Spells == null)
            {
                return;
            }
            foreach (SpellDefinition s in def.Spells)
            {
                if (s == null || string.IsNullOrEmpty(s.Id))
                {
                    continue;
                }
                m_ById[s.Id] = s;
            }
        }

        /// <summary>Sucht einen Spell per Id. Liefert <c>false</c>, wenn nicht vorhanden.</summary>
        public bool TryGet(string id, out SpellDefinition spell)
        {
            if (string.IsNullOrEmpty(id))
            {
                spell = null;
                return false;
            }
            return m_ById.TryGetValue(id, out spell);
        }

        /// <summary>Liefert den Spell oder <c>null</c>, falls die Id unbekannt ist.</summary>
        public SpellDefinition Get(string id)
        {
            return TryGet(id, out SpellDefinition s) ? s : null;
        }
    }
}
