using System;
using System.Collections.Generic;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Runtime-Lookup für <see cref="ParticleSystemDefinition"/> per Dateiname
    /// (z. B. <c>"casting_holy"</c>, ohne <c>.psi</c>-Endung).
    /// </summary>
    /// <remarks>
    /// Befüllt vom <see cref="Runtime.ParticleSystemCatalogLoader"/>; konsumiert
    /// vom Caster-Partikel-Spawner. Lookup ist case-insensitive, weil
    /// Source-Datenbanken inkonsistent groß-/kleingeschrieben sind.
    /// </remarks>
    public sealed class ParticleSystemCatalog
    {
        private readonly Dictionary<string, ParticleSystemDefinition> m_ByName;

        /// <summary>Anzahl geladener Partikelsystem-Definitionen.</summary>
        public int Count => m_ByName.Count;

        /// <summary>Alle Definitionen (read-only).</summary>
        public IReadOnlyCollection<ParticleSystemDefinition> All => m_ByName.Values;

        /// <summary>Erzeugt einen Katalog aus dem deserialisierten Wurzel-Dictionary.</summary>
        public ParticleSystemCatalog(IDictionary<string, ParticleSystemDefinition> raw)
        {
            m_ByName = new Dictionary<string, ParticleSystemDefinition>(StringComparer.OrdinalIgnoreCase);
            if (raw == null) { return; }
            foreach (KeyValuePair<string, ParticleSystemDefinition> kv in raw)
            {
                if (kv.Value == null || string.IsNullOrEmpty(kv.Key)) { continue; }
                m_ByName[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// Sucht eine Partikel-Definition. Akzeptiert sowohl <c>"casting_holy"</c>
        /// als auch <c>"casting_holy.psi"</c>; die <c>.psi</c>-Endung wird gestrippt.
        /// </summary>
        public bool TryGet(string nameOrFile, out ParticleSystemDefinition def)
        {
            def = null;
            if (string.IsNullOrEmpty(nameOrFile)) { return false; }
            string key = StripPsi(nameOrFile);
            return m_ByName.TryGetValue(key, out def);
        }

        /// <summary>Liefert die Definition oder <c>null</c>, wenn unbekannt.</summary>
        public ParticleSystemDefinition Get(string nameOrFile)
        {
            return TryGet(nameOrFile, out ParticleSystemDefinition d) ? d : null;
        }

        /// <summary>Strippt eine <c>.psi</c>-Endung (case-insensitive).</summary>
        public static string StripPsi(string name)
        {
            if (string.IsNullOrEmpty(name)) { return string.Empty; }
            if (name.EndsWith(".psi", StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(0, name.Length - 4);
            }
            return name;
        }
    }
}
