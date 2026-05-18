using System.Collections.Generic;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Runtime-Lookup fuer <see cref="SpellVisualKitDefinition"/> per Kit-ID.
    /// Befuellt vom <see cref="SpellVisualKitDefinitionCatalogLoader"/>;
    /// konsumiert vom <see cref="SpellVisualResolver"/>.
    /// </summary>
    public sealed class SpellVisualKitDefinitionCatalog
    {
        private readonly Dictionary<int, SpellVisualKitDefinition> m_ById;

        /// <summary>Anzahl geladener Kit-Definitionen.</summary>
        public int Count => m_ById.Count;

        /// <summary>Alle Kit-Definitionen (read-only).</summary>
        public IReadOnlyCollection<SpellVisualKitDefinition> All => m_ById.Values;

        /// <summary>
        /// Erzeugt einen Katalog aus dem deserialisierten Wurzel-Dictionary
        /// (<c>"3": {...}</c>-Form). Wenn der innere <c>id</c>-Wert fehlt,
        /// wird er aus dem Key uebernommen.
        /// </summary>
        public SpellVisualKitDefinitionCatalog(IDictionary<string, SpellVisualKitDefinition> raw)
        {
            m_ById = new Dictionary<int, SpellVisualKitDefinition>();
            if (raw == null) { return; }
            foreach (KeyValuePair<string, SpellVisualKitDefinition> kv in raw)
            {
                if (kv.Value == null) { continue; }
                if (!int.TryParse(kv.Key, out int key)) { continue; }
                if (kv.Value.Id == 0) { kv.Value.Id = key; }
                m_ById[key] = kv.Value;
            }
        }

        /// <summary>Sucht eine Kit-Definition per Kit-ID. Liefert <c>false</c>, wenn unbekannt.</summary>
        public bool TryGet(int kitId, out SpellVisualKitDefinition def)
        {
            return m_ById.TryGetValue(kitId, out def);
        }

        /// <summary>Liefert die Kit-Definition oder <c>null</c>, falls die ID unbekannt ist.</summary>
        public SpellVisualKitDefinition Get(int kitId)
        {
            return m_ById.TryGetValue(kitId, out SpellVisualKitDefinition d) ? d : null;
        }
    }
}
