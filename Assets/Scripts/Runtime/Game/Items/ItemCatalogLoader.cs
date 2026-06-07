using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Synchroner Lade-Utility fuer Item-Templates aus
    /// <c>StreamingAssets/items/_templates.json</c>. Cached prozessweit
    /// (Lazy-Static-Cache), damit Inventory/Vendor/Loot ohne IO arbeiten.
    /// </summary>
    /// <remarks>
    /// Pattern identisch zu <see cref="Spells.SpellCatalogLoader"/>
    /// und <see cref="Npc.NpcCatalogLoader"/>: static, ohne
    /// ServiceLocator (KISS), Daten unveraenderlich pro Prozess. Bei fehlender
    /// Datei wird ein leeres Dictionary gecached und Lookups liefern <c>null</c>.
    /// </remarks>
    public static class ItemCatalogLoader
    {
        /// <summary>Unterordner unter <c>Application.streamingAssetsPath</c>.</summary>
        public const string SubFolder = "items";

        /// <summary>Dateiname der Template-Tabelle.</summary>
        public const string TemplatesFileName = "_templates.json";

        private static Dictionary<int, ItemTemplate> s_Templates;
        private static Dictionary<string, List<int>> s_ByModel;
        private static bool s_LoadAttempted;

        // ---- Templates --------------------------------------------------

        /// <summary>
        /// Liefert das Item-Template mit <paramref name="entry"/>, oder
        /// <c>null</c>, wenn es nicht existiert.
        /// </summary>
        public static ItemTemplate GetTemplateOrNull(int entry)
        {
            EnsureLoaded();
            return s_Templates != null && s_Templates.TryGetValue(entry, out ItemTemplate t) ? t : null;
        }

        /// <summary>
        /// Versucht das Item mit <paramref name="entry"/> zu laden.
        /// </summary>
        public static bool TryGetTemplate(int entry, out ItemTemplate template)
        {
            EnsureLoaded();
            if (s_Templates != null && s_Templates.TryGetValue(entry, out template))
            {
                return true;
            }
            template = null;
            return false;
        }

        /// <summary>Gesamt-Tabelle (read-only Lookup); nie <c>null</c>.</summary>
        public static IReadOnlyDictionary<int, ItemTemplate> AllTemplates
        {
            get
            {
                EnsureLoaded();
                return s_Templates;
            }
        }

        // ---- Model-Index (fuer /give und Loadout-Resolution) -----------

        /// <summary>
        /// Liefert alle Template-Entries, deren <see cref="ItemTemplate.Model"/>
        /// case-insensitive mit <paramref name="model"/> matcht (z. B. alle
        /// Longbow-Rarities zum Model <c>"longbow"</c>). Leere Liste, wenn
        /// nichts gefunden wurde. Sortierung: <c>required_level asc</c>,
        /// Tie-Break <c>entry asc</c>.
        /// </summary>
        public static IReadOnlyList<int> GetEntriesByModel(string model)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(model) || s_ByModel == null)
            {
                return System.Array.Empty<int>();
            }
            return s_ByModel.TryGetValue(model.ToLowerInvariant(), out List<int> bucket)
                ? bucket
                : (IReadOnlyList<int>)System.Array.Empty<int>();
        }

        /// <summary>
        /// Versucht den ersten (level-niedrigsten) Template-Entry zu finden,
        /// dessen Model-Feld mit <paramref name="model"/> matcht.
        /// </summary>
        public static bool TryGetFirstEntryByModel(string model, out int entry)
        {
            IReadOnlyList<int> entries = GetEntriesByModel(model);
            if (entries.Count > 0)
            {
                entry = entries[0];
                return true;
            }
            entry = 0;
            return false;
        }

        // ---- Cache-Reset (Editor / Tests) ------------------------------

        /// <summary>Setzt den Cache zurueck. Fuer Tests oder Editor-Reload.</summary>
        public static void ResetCacheForTesting()
        {
            s_Templates = null;
            s_ByModel = null;
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
            s_Templates = LoadDictionary(Path.Combine(folder, TemplatesFileName));
            s_ByModel = BuildModelIndex(s_Templates);
        }

        /// <summary>
        /// Baut den lowercase Model-&gt;Entries-Index. Eintraege ohne Model
        /// (Consumables mit <c>"model":"0"</c>) werden ausgelassen, damit
        /// <see cref="GetEntriesByModel"/> nicht versehentlich Traenke
        /// als Equip-Item liefert. Sortierung pro Bucket: <c>required_level asc</c>,
        /// Tie-Break <c>entry asc</c>.
        /// </summary>
        private static Dictionary<string, List<int>> BuildModelIndex(Dictionary<int, ItemTemplate> templates)
        {
            Dictionary<string, List<int>> index = new();
            if (templates == null || templates.Count == 0)
            {
                return index;
            }

            foreach (KeyValuePair<int, ItemTemplate> kv in templates)
            {
                ItemTemplate t = kv.Value;
                if (t == null || string.IsNullOrEmpty(t.Model) || t.Model == "0")
                {
                    continue;
                }
                string key = t.Model.ToLowerInvariant();
                if (!index.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>();
                    index[key] = bucket;
                }
                bucket.Add(kv.Key);
            }

            foreach (List<int> bucket in index.Values)
            {
                bucket.Sort((a, b) =>
                {
                    ItemTemplate ta = templates[a];
                    ItemTemplate tb = templates[b];
                    int cmp = ta.RequiredLevel.CompareTo(tb.RequiredLevel);
                    return cmp != 0 ? cmp : a.CompareTo(b);
                });
            }
            return index;
        }

        /// <summary>
        /// Reader fuer die <c>{ "1": {...}, "2": {...} }</c>-Form des
        /// Migrator-Exports (<c>Tools/Scripts/migrate_game_db.py</c>).
        /// Liefert bei Fehlern ein leeres Dictionary statt <c>null</c>,
        /// damit Konsumenten keinen Null-Guard brauchen.
        /// </summary>
        private static Dictionary<int, ItemTemplate> LoadDictionary(string path)
        {
            const string LogTag = "ItemCatalogLoader";

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[{LogTag}] Datei fehlt: {path}");
                return new Dictionary<int, ItemTemplate>();
            }

            try
            {
                string json = File.ReadAllText(path);
                Dictionary<int, ItemTemplate> result =
                    JsonConvert.DeserializeObject<Dictionary<int, ItemTemplate>>(json)
                    ?? new Dictionary<int, ItemTemplate>();
                Debug.Log($"[{LogTag}] {result.Count} Eintraege geladen aus {path}");
                return result;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[{LogTag}] Fehler beim Laden von {path}: {ex.Message}");
                return new Dictionary<int, ItemTemplate>();
            }
        }
    }
}
