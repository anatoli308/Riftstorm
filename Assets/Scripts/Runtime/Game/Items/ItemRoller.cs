using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Server-seitige, deterministische Item-Roll-Engine. Pure Static — keine
    /// Unity-Dependencies in der Hot-Path-Logik (nur Logging).
    /// <para>
    /// Eingaben: Template-Id, Ziel-Rarity, Seed. Ausgabe: <see cref="ItemInstance"/>
    /// mit aufgeloesten Affix-Ids und Quality-Scores. Der Seed wird in
    /// <see cref="System.Random"/> gefuettert, damit dasselbe Tripel
    /// reproduzierbar dasselbe Item liefert (wichtig fuer Loot-Recovery
    /// / Replays / Tests).
    /// </para>
    /// <para>
    /// Slot-Konvention: <c>Affix1Id</c> = Prefix, <c>Affix2Id</c> = Suffix
    /// (analog Source D2 — Adjektiv vorne, Nomen hinten). Bei Rarity = Magic
    /// (1 Affix) wird zufaellig zwischen Prefix/Suffix entschieden; der
    /// unbenutzte Slot bleibt 0. Gems bleiben in Phase 18 ungeritzt — die
    /// Felder existieren in <see cref="ItemInstance"/>, aber das Rollen kommt
    /// erst mit Phase 19 dazu, sobald die <c>_gems.json</c> in ein
    /// einheitliches Schema gehoben ist.
    /// </para>
    /// <para>
    /// Affix-Pool wird per <see cref="ItemTemplate.ItemLevel"/> gegen
    /// <see cref="ItemAffix.MinLevel"/>..<see cref="ItemAffix.MaxLevel"/>
    /// gefiltert. Wenn dadurch der Pool leer wird (z. B. Item-Level
    /// ausserhalb der Source-Range), faellt der Roller auf den
    /// ungefilterten Pool zurueck — Designentscheidung: lieber ein Affix
    /// "off-level" als ein leerer Slot.
    /// </para>
    /// </summary>
    public static class ItemRoller
    {
        private const string LogTag = "ItemRoller";
        private const int FallbackItemLevel = 1;

        /// <summary>
        /// Rollt eine konkrete Item-Instanz fuer das gegebene Template.
        /// Bei unbekanntem Template wird eine Instanz mit nur dem Template-Feld
        /// zurueckgegeben (kein Affix-Pool, keine Stats); der Aufrufer kann
        /// trotzdem damit weiterarbeiten.
        /// </summary>
        public static ItemInstance Roll(int templateId, ItemRarity rarity, ulong seed)
        {
            if (templateId <= 0)
            {
                return ItemInstance.Empty;
            }

            ItemInstance instance = ItemInstance.FromTemplate(templateId);
            instance.Rarity = rarity;

            int affixCount = RarityRules.AffixCount(rarity);
            if (affixCount <= 0)
            {
                return instance;
            }

            int itemLevel = ResolveItemLevel(templateId);
            // Seed wird auf int beschnitten — Determinismus pro (player, template, slot)
            // ist Aufrufer-Sache; hier reicht der Mix aus seed-bits.
            System.Random rng = new(unchecked((int)(seed ^ (seed >> 32))));

            ushort prefixId = 0;
            ushort suffixId = 0;
            byte prefixScore = 0;
            byte suffixScore = 0;

            if (affixCount == 1)
            {
                bool pickPrefix = rng.Next(0, 2) == 0;
                if (pickPrefix)
                {
                    prefixId = PickAffix(AffixCatalogLoader.PrefixIds, itemLevel, rng);
                    if (prefixId == 0)
                    {
                        // Fallback: Suffix probieren, wenn Prefix-Pool leer war.
                        suffixId = PickAffix(AffixCatalogLoader.SuffixIds, itemLevel, rng);
                        if (suffixId != 0)
                        {
                            suffixScore = RollScore(rng);
                        }
                    }
                    else
                    {
                        prefixScore = RollScore(rng);
                    }
                }
                else
                {
                    suffixId = PickAffix(AffixCatalogLoader.SuffixIds, itemLevel, rng);
                    if (suffixId == 0)
                    {
                        prefixId = PickAffix(AffixCatalogLoader.PrefixIds, itemLevel, rng);
                        if (prefixId != 0)
                        {
                            prefixScore = RollScore(rng);
                        }
                    }
                    else
                    {
                        suffixScore = RollScore(rng);
                    }
                }
            }
            else
            {
                prefixId = PickAffix(AffixCatalogLoader.PrefixIds, itemLevel, rng);
                suffixId = PickAffix(AffixCatalogLoader.SuffixIds, itemLevel, rng);
                if (prefixId != 0)
                {
                    prefixScore = RollScore(rng);
                }
                if (suffixId != 0)
                {
                    suffixScore = RollScore(rng);
                }
            }

            instance.Affix1Id = prefixId;
            instance.Affix1Score = prefixScore;
            instance.Affix2Id = suffixId;
            instance.Affix2Score = suffixScore;
            return instance;
        }

        /// <summary>
        /// Hilfsfunktion zum Mischen von drei Werten (z. B. PlayerId, TemplateId,
        /// Slot) in einen 64-Bit-Seed. SplitMix64-Variante — schnell und gut
        /// streuend, ohne Crypto-Anspruch.
        /// </summary>
        public static ulong MakeSeed(ulong a, ulong b, ulong c)
        {
            ulong x = a;
            x = unchecked((x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL);
            x = unchecked((x ^ (x >> 27)) * 0x94D049BB133111EBUL);
            x ^= x >> 31;
            x ^= b + 0x9E3779B97F4A7C15UL + (x << 6) + (x >> 2);
            x ^= c + 0x9E3779B97F4A7C15UL + (x << 6) + (x >> 2);
            return x;
        }

        // ---- Internals --------------------------------------------------

        private static int ResolveItemLevel(int templateId)
        {
            if (ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) && template != null)
            {
                return template.ItemLevel > 0 ? template.ItemLevel : FallbackItemLevel;
            }
            return FallbackItemLevel;
        }

        private static ushort PickAffix(IReadOnlyList<int> pool, int itemLevel, System.Random rng)
        {
            if (pool == null || pool.Count == 0)
            {
                return 0;
            }

            // Erst level-gefiltert versuchen.
            List<int> filtered = new();
            for (int i = 0; i < pool.Count; i++)
            {
                int id = pool[i];
                if (!AffixCatalogLoader.TryGetAffix(id, out ItemAffix affix) || affix == null)
                {
                    continue;
                }
                if (itemLevel >= affix.MinLevel && itemLevel <= affix.MaxLevel)
                {
                    filtered.Add(id);
                }
            }

            int pick;
            if (filtered.Count > 0)
            {
                pick = filtered[rng.Next(0, filtered.Count)];
            }
            else
            {
                // Kein Affix in der Range — Fallback: irgendeinen aus dem Pool.
                pick = pool[rng.Next(0, pool.Count)];
                Debug.LogWarning($"[{LogTag}] Pool fuer ItemLevel {itemLevel} leer, falle auf vollen Pool ({pool.Count}) zurueck.");
            }

            return pick > ushort.MaxValue ? (ushort)0 : (ushort)pick;
        }

        private static byte RollScore(System.Random rng)
        {
            // 0..100 inklusive.
            return (byte)rng.Next(0, 101);
        }
    }
}
