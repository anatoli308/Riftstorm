using System.Collections.Generic;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Runtime-Lookup für Animations-Definitionen (per Name).
    /// Befüllt vom <see cref="SpellAnimationCatalogLoader"/>; konsumiert vom
    /// Visual-Player (z. B. <c>WorldSpellAnimation</c>) und vom
    /// <see cref="SpellVisualCatalog"/>.
    /// </summary>
    public sealed class SpellAnimationCatalog
    {
        private readonly Dictionary<string, SpellAnimationDefinition> m_ByName;

        /// <summary>Anzahl geladener Animationen.</summary>
        public int Count => m_ByName.Count;

        /// <summary>Alle Animationen (read-only).</summary>
        public IReadOnlyCollection<SpellAnimationDefinition> All => m_ByName.Values;

        /// <summary>
        /// Erzeugt einen Katalog aus einer Sammlung geladener Definitionen.
        /// Einträge ohne <see cref="SpellAnimationDefinition.Name"/> werden verworfen.
        /// </summary>
        public SpellAnimationCatalog(IEnumerable<SpellAnimationDefinition> definitions)
        {
            m_ByName = new Dictionary<string, SpellAnimationDefinition>();
            if (definitions == null)
            {
                return;
            }
            foreach (SpellAnimationDefinition def in definitions)
            {
                if (def == null || string.IsNullOrEmpty(def.Name))
                {
                    continue;
                }
                m_ByName[def.Name] = def;
            }
        }

        /// <summary>Sucht eine Animation per Name. Liefert <c>false</c>, wenn unbekannt.</summary>
        public bool TryGet(string name, out SpellAnimationDefinition def)
        {
            if (string.IsNullOrEmpty(name))
            {
                def = null;
                return false;
            }
            return m_ByName.TryGetValue(name, out def);
        }

        /// <summary>Liefert die Animation oder <c>null</c>, falls der Name unbekannt ist.</summary>
        public SpellAnimationDefinition Get(string name)
        {
            return TryGet(name, out SpellAnimationDefinition d) ? d : null;
        }
    }
}
