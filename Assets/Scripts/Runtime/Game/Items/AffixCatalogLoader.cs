using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Lazy-static Cache fuer den D2-Affix-Katalog aus
    /// <c>StreamingAssets/items/_affixes.json</c>.
    /// <para>
    /// Die Datei ist ein <c>Dictionary&lt;string entryId, ItemAffix&gt;</c>
    /// (Source-Parity). Beim Laden wird der String-Key in den
    /// <see cref="ItemAffix.Entry"/>-Int promovieret und nach Prefix
    /// (<c>name_single_noun == 0</c>) bzw. Suffix (<c>name_single_noun == 1</c>)
    /// in zwei Buckets aufgeteilt. Damit kann <c>ItemRoller</c> deterministisch
    /// Affixe per Index in den Buckets ziehen.
    /// </para>
    /// <para>
    /// Konsumenten greifen ausschliesslich ueber diesen Loader zu — das
    /// Schema selbst (siehe <see cref="ItemAffix"/>) bleibt pure Daten ohne
    /// Unity-Dependencies, damit es auch dedicated-server-tauglich ist.
    /// </para>
    /// </summary>
    public static class AffixCatalogLoader
    {
        private const string LogTag = "AffixCatalogLoader";
        private const string SubFolder = "items";
        private const string AffixesFileName = "_affixes.json";

        private static Dictionary<int, ItemAffix> s_Affixes;
        private static List<int> s_PrefixIds;
        private static List<int> s_SuffixIds;
        private static bool s_LoadAttempted;

        /// <summary>Alle geladenen Affixe nach Entry-Id (int).</summary>
        public static IReadOnlyDictionary<int, ItemAffix> Affixes
        {
            get
            {
                EnsureLoaded();
                return s_Affixes;
            }
        }

        /// <summary>Pool aller Prefix-Affix-Ids (<c>name_single_noun == 0</c>).</summary>
        public static IReadOnlyList<int> PrefixIds
        {
            get
            {
                EnsureLoaded();
                return s_PrefixIds;
            }
        }

        /// <summary>Pool aller Suffix-Affix-Ids (<c>name_single_noun == 1</c>).</summary>
        public static IReadOnlyList<int> SuffixIds
        {
            get
            {
                EnsureLoaded();
                return s_SuffixIds;
            }
        }

        /// <summary>Versucht einen Affix per Entry-Id zu holen.</summary>
        public static bool TryGetAffix(int entry, out ItemAffix affix)
        {
            EnsureLoaded();
            return s_Affixes.TryGetValue(entry, out affix);
        }

        /// <summary>Setzt den Cache zurueck. Fuer Tests oder Editor-Reload.</summary>
        public static void ResetCacheForTesting()
        {
            s_Affixes = null;
            s_PrefixIds = null;
            s_SuffixIds = null;
            s_LoadAttempted = false;
        }

        // ---- Internals --------------------------------------------------

        private static void EnsureLoaded()
        {
            if (s_LoadAttempted)
            {
                return;
            }
            s_LoadAttempted = true;

            string folder = Path.Combine(Application.streamingAssetsPath, SubFolder);
            string path = Path.Combine(folder, AffixesFileName);
            s_Affixes = LoadDictionary(path);
            BuildBuckets(s_Affixes, out s_PrefixIds, out s_SuffixIds);
        }

        /// <summary>Laedt das Source-Affix-Dictionary (Key=String EntryId).</summary>
        private static Dictionary<int, ItemAffix> LoadDictionary(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[{LogTag}] Datei fehlt: {path}");
                return new Dictionary<int, ItemAffix>();
            }

            try
            {
                string json = File.ReadAllText(path);
                Dictionary<string, ItemAffix> raw =
                    JsonConvert.DeserializeObject<Dictionary<string, ItemAffix>>(json)
                    ?? new Dictionary<string, ItemAffix>();

                Dictionary<int, ItemAffix> result = new(raw.Count);
                foreach (KeyValuePair<string, ItemAffix> kv in raw)
                {
                    ItemAffix affix = kv.Value;
                    if (affix == null)
                    {
                        continue;
                    }
                    // Falls "entry" im Body fehlt, vom Key uebernehmen.
                    if (affix.Entry <= 0 && int.TryParse(kv.Key, out int parsed))
                    {
                        affix.Entry = parsed;
                    }
                    if (affix.Entry > 0)
                    {
                        result[affix.Entry] = affix;
                    }
                }
                Debug.Log($"[{LogTag}] {result.Count} Affixe geladen aus {path}");
                return result;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[{LogTag}] Fehler beim Laden von {path}: {ex.Message}");
                return new Dictionary<int, ItemAffix>();
            }
        }

        /// <summary>Teilt den Katalog in Prefix- und Suffix-Pools auf (sortiert nach Entry).</summary>
        private static void BuildBuckets(
            Dictionary<int, ItemAffix> affixes,
            out List<int> prefixes,
            out List<int> suffixes)
        {
            prefixes = new List<int>();
            suffixes = new List<int>();
            if (affixes == null || affixes.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<int, ItemAffix> kv in affixes)
            {
                ItemAffix a = kv.Value;
                if (a.IsPrefix)
                {
                    prefixes.Add(kv.Key);
                }
                else if (a.IsSuffix)
                {
                    suffixes.Add(kv.Key);
                }
            }
            prefixes.Sort();
            suffixes.Sort();
        }
    }
}
