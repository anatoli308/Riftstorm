using System.Collections.Generic;
using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Wurzel-Struktur der <c>weapons.json</c>: optionale Schema-Version plus Liste der Waffen.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class WeaponCatalogDef
    {
        /// <summary>Schema-Version. Aktuell 1.</summary>
        [JsonProperty("version")] public int Version = 1;

        /// <summary>Alle Waffen-Einträge. Reihenfolge irrelevant; Lookup via <see cref="WeaponDefinition.Id"/>.</summary>
        [JsonProperty("weapons")] public List<WeaponDefinition> Weapons;
    }

    /// <summary>
    /// Runtime-Lookup für Waffen-Definitionen. Wird vom <c>WeaponCatalogLoader</c> befüllt
    /// und über den ServiceLocator bereitgestellt.
    /// </summary>
    public sealed class WeaponCatalog
    {
        private readonly Dictionary<string, WeaponDefinition> m_ById;

        /// <summary>Anzahl geladener Waffen-Definitionen.</summary>
        public int Count => m_ById.Count;

        /// <summary>Alle Waffen-Definitionen (read-only).</summary>
        public IReadOnlyCollection<WeaponDefinition> All => m_ById.Values;

        /// <summary>
        /// Erzeugt einen Katalog aus einer geladenen Definition. Einträge ohne
        /// gültige <see cref="WeaponDefinition.Id"/> werden verworfen.
        /// </summary>
        public WeaponCatalog(WeaponCatalogDef def)
        {
            m_ById = new Dictionary<string, WeaponDefinition>();
            if (def?.Weapons == null)
            {
                return;
            }
            foreach (WeaponDefinition w in def.Weapons)
            {
                if (w == null || string.IsNullOrEmpty(w.Id))
                {
                    continue;
                }
                m_ById[w.Id] = w;
            }
        }

        /// <summary>
        /// Sucht eine Waffe per Id. Liefert <c>false</c>, wenn nicht vorhanden.
        /// </summary>
        public bool TryGet(string id, out WeaponDefinition weapon)
        {
            if (string.IsNullOrEmpty(id))
            {
                weapon = null;
                return false;
            }
            return m_ById.TryGetValue(id, out weapon);
        }

        /// <summary>
        /// Liefert die Waffe oder <c>null</c>, falls die Id unbekannt ist.
        /// </summary>
        public WeaponDefinition Get(string id)
        {
            return TryGet(id, out WeaponDefinition w) ? w : null;
        }
    }
}
