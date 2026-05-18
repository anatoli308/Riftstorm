using System.Collections.Generic;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Runtime-Lookup fuer <see cref="SpellVisualKitMapping"/> per Spell-Entry.
    /// Befuellt vom <see cref="SpellVisualKitMappingCatalogLoader"/>; konsumiert
    /// vom <see cref="SpellVisualResolver"/>.
    /// </summary>
    public sealed class SpellVisualKitMappingCatalog
    {
        private readonly Dictionary<int, SpellVisualKitMapping> m_ByEntry;

        /// <summary>Anzahl Mapping-Eintraege.</summary>
        public int Count => m_ByEntry.Count;

        /// <summary>Alle Eintraege (read-only).</summary>
        public IReadOnlyCollection<SpellVisualKitMapping> All => m_ByEntry.Values;

        /// <summary>
        /// Erzeugt einen Katalog aus dem deserialisierten Wurzel-Dictionary.
        /// Eintraege ohne gueltigen <see cref="SpellVisualKitMapping.Entry"/> werden ueberschrieben
        /// mit dem aus dem Key gelesenen Entry, damit die <c>"1": {...}</c>-Form
        /// auch dann funktioniert, wenn der innere <c>entry</c>-Wert fehlt.
        /// </summary>
        public SpellVisualKitMappingCatalog(IDictionary<string, SpellVisualKitMapping> raw)
        {
            m_ByEntry = new Dictionary<int, SpellVisualKitMapping>();
            if (raw == null) { return; }
            foreach (KeyValuePair<string, SpellVisualKitMapping> kv in raw)
            {
                if (kv.Value == null) { continue; }
                if (!int.TryParse(kv.Key, out int key)) { continue; }
                if (kv.Value.Entry == 0) { kv.Value.Entry = key; }
                m_ByEntry[key] = kv.Value;
            }
        }

        /// <summary>Sucht ein Mapping per Spell-Entry. Liefert <c>false</c>, wenn unbekannt.</summary>
        public bool TryGet(int entry, out SpellVisualKitMapping mapping)
        {
            return m_ByEntry.TryGetValue(entry, out mapping);
        }

        /// <summary>Liefert das Mapping oder <c>null</c>, falls der Entry unbekannt ist.</summary>
        public SpellVisualKitMapping Get(int entry)
        {
            return m_ByEntry.TryGetValue(entry, out SpellVisualKitMapping m) ? m : null;
        }
    }
}
