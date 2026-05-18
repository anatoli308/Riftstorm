using System.Collections.Generic;
using Newtonsoft.Json;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Wurzel-Struktur der <c>auras.json</c>: optionale Schema-Version plus
    /// Liste aller Auren.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AuraCatalogDef
    {
        /// <summary>Schema-Version. Aktuell 1.</summary>
        [JsonProperty("version")] public int Version = 1;

        /// <summary>Alle Aura-Einträge. Reihenfolge irrelevant; Lookup via <see cref="AuraDefinition.Id"/>.</summary>
        [JsonProperty("auras")] public List<AuraDefinition> Auras;
    }

    /// <summary>
    /// Runtime-Lookup für Aura-Definitionen. Wird vom <see cref="AuraCatalogLoader"/>
    /// befüllt und über den ServiceLocator bereitgestellt.
    /// </summary>
    public sealed class AuraCatalog
    {
        private readonly Dictionary<string, AuraDefinition> m_ById;

        /// <summary>Anzahl geladener Aura-Definitionen.</summary>
        public int Count => m_ById.Count;

        /// <summary>Alle Aura-Definitionen (read-only).</summary>
        public IReadOnlyCollection<AuraDefinition> All => m_ById.Values;

        /// <summary>
        /// Erzeugt einen Katalog aus einer geladenen Definition. Einträge ohne
        /// gültige <see cref="AuraDefinition.Id"/> werden verworfen.
        /// </summary>
        public AuraCatalog(AuraCatalogDef def)
        {
            m_ById = new Dictionary<string, AuraDefinition>();
            if (def?.Auras == null)
            {
                return;
            }
            foreach (AuraDefinition a in def.Auras)
            {
                if (a == null || string.IsNullOrEmpty(a.Id))
                {
                    continue;
                }
                m_ById[a.Id] = a;
            }
        }

        /// <summary>Sucht eine Aura per Id. Liefert <c>false</c>, wenn nicht vorhanden.</summary>
        public bool TryGet(string id, out AuraDefinition aura)
        {
            if (string.IsNullOrEmpty(id))
            {
                aura = null;
                return false;
            }
            return m_ById.TryGetValue(id, out aura);
        }

        /// <summary>Liefert die Aura oder <c>null</c>, falls die Id unbekannt ist.</summary>
        public AuraDefinition Get(string id)
        {
            return TryGet(id, out AuraDefinition a) ? a : null;
        }
    }
}
