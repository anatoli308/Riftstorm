using System.Collections.Generic;
using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Wurzel-Struktur der <c>offhand_items.json</c>: optionale Schema-Version
    /// plus Liste der Offhand-Items.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class OffhandCatalogDef
    {
        /// <summary>Schema-Version. Aktuell 1.</summary>
        [JsonProperty("version")] public int Version = 1;

        /// <summary>Alle Offhand-Einträge. Reihenfolge irrelevant; Lookup via <see cref="OffhandDefinition.Id"/>.</summary>
        [JsonProperty("offhand_items")] public List<OffhandDefinition> Items;
    }

    /// <summary>
    /// Runtime-Lookup für Offhand-Definitionen. Wird vom <c>OffhandCatalogLoader</c>
    /// befüllt und über den ServiceLocator bereitgestellt.
    /// </summary>
    public sealed class OffhandCatalog
    {
        private readonly Dictionary<string, OffhandDefinition> m_ById;

        /// <summary>Anzahl geladener Offhand-Definitionen.</summary>
        public int Count => m_ById.Count;

        /// <summary>Alle Offhand-Definitionen (read-only).</summary>
        public IReadOnlyCollection<OffhandDefinition> All => m_ById.Values;

        /// <summary>
        /// Erzeugt einen Katalog aus einer geladenen Definition. Einträge ohne
        /// gültige <see cref="OffhandDefinition.Id"/> werden verworfen.
        /// </summary>
        public OffhandCatalog(OffhandCatalogDef def)
        {
            m_ById = new Dictionary<string, OffhandDefinition>();
            if (def?.Items == null)
            {
                return;
            }
            foreach (OffhandDefinition o in def.Items)
            {
                if (o == null || string.IsNullOrEmpty(o.Id))
                {
                    continue;
                }
                m_ById[o.Id] = o;
            }
        }

        /// <summary>
        /// Sucht ein Offhand-Item per Id. Liefert <c>false</c>, wenn nicht vorhanden.
        /// </summary>
        public bool TryGet(string id, out OffhandDefinition item)
        {
            if (string.IsNullOrEmpty(id))
            {
                item = null;
                return false;
            }
            return m_ById.TryGetValue(id, out item);
        }

        /// <summary>
        /// Liefert das Offhand-Item oder <c>null</c>, falls die Id unbekannt ist.
        /// </summary>
        public OffhandDefinition Get(string id)
        {
            return TryGet(id, out OffhandDefinition o) ? o : null;
        }
    }
}
