using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Synchroner Lade-Utility fuer Spell-Templates aus
    /// <c>StreamingAssets/spells/_templates.json</c>. Cached prozessweit
    /// (Lazy-Static-Cache), damit jeder Caster ohne IO arbeitet.
    /// </summary>
    /// <remarks>
    /// Pattern identisch zu <see cref="Npc.NpcCatalogLoader"/>:
    /// bewusst static und ohne ServiceLocator (KISS), Daten unveraenderlich
    /// pro Prozess. Bei fehlender Datei wird ein leeres Dictionary gecached
    /// und Lookups liefern <c>null</c>.
    /// </remarks>
    public static class SpellCatalogLoader
    {
        /// <summary>Unterordner unter <c>Application.streamingAssetsPath</c>.</summary>
        public const string SubFolder = "spells";

        /// <summary>Dateiname der Template-Tabelle.</summary>
        public const string TemplatesFileName = "_templates.json";

        /// <summary>
        /// Optionale Zusatz-Tabellen, die per <c>icon</c>-Prefix aus dem Haupt-Export
        /// herausgeloest wurden (Items/Scrolls). Werden gemerged in den selben Cache;
        /// Eintraege bleiben aus Runtime-Sicht ganz normale Spell-Templates (in der
        /// Source-DB sind Item-Use-Effekte ebenfalls Spell-Eintraege, nur das Icon
        /// liegt unter <c>Art/item_icons/</c> statt <c>Art/spell_icons/</c>).
        /// </summary>
        public static readonly string[] AdditionalFileNames =
        {
            "_items.json",
            "_scrolls.json",
        };

        private static Dictionary<int, SpellTemplate> s_Templates;
        private static bool s_LoadAttempted;

        // ---- Templates --------------------------------------------------

        /// <summary>
        /// Liefert das Spell-Template mit <paramref name="entry"/>, oder
        /// <c>null</c>, wenn es nicht existiert.
        /// </summary>
        public static SpellTemplate GetTemplateOrNull(int entry)
        {
            EnsureLoaded();
            return s_Templates != null && s_Templates.TryGetValue(entry, out SpellTemplate t) ? t : null;
        }

        /// <summary>
        /// Versucht den Spell mit <paramref name="entry"/> zu laden.
        /// </summary>
        public static bool TryGetTemplate(int entry, out SpellTemplate template)
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
        public static IReadOnlyDictionary<int, SpellTemplate> AllTemplates
        {
            get
            {
                EnsureLoaded();
                return s_Templates;
            }
        }

        // ---- Cache-Reset (Editor / Tests) ------------------------------

        /// <summary>Setzt den Cache zurueck. Fuer Tests oder Editor-Reload.</summary>
        public static void ResetCacheForTesting()
        {
            s_Templates = null;
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

            // Optionale Sub-Tabellen (Items/Scrolls) mergen — Konflikte (gleiche Entry-ID)
            // werden zugunsten der zuerst geladenen Datei verworfen und nur geloggt,
            // damit der Migrator/Splitter Doubletten sichtbar macht.
            for (int i = 0; i < AdditionalFileNames.Length; i++)
            {
                string extraPath = Path.Combine(folder, AdditionalFileNames[i]);
                if (!File.Exists(extraPath))
                {
                    continue;
                }
                Dictionary<int, SpellTemplate> extra = LoadDictionary(extraPath);
                foreach (KeyValuePair<int, SpellTemplate> kvp in extra)
                {
                    if (s_Templates.ContainsKey(kvp.Key))
                    {
                        Debug.LogWarning($"[SpellCatalogLoader] Doppelter Entry {kvp.Key} in {AdditionalFileNames[i]} — ignoriert.");
                        continue;
                    }
                    s_Templates[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Reader fuer die <c>{ "1": {...}, "2": {...} }</c>-Form des
        /// Migrator-Exports (<c>Tools/Scripts/migrate_game_db.py</c>).
        /// Liefert bei Fehlern ein leeres Dictionary statt <c>null</c>,
        /// damit Konsumenten keinen Null-Guard brauchen.
        /// </summary>
        private static Dictionary<int, SpellTemplate> LoadDictionary(string path)
        {
            const string LogTag = "SpellCatalogLoader";

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[{LogTag}] Datei fehlt: {path}");
                return new Dictionary<int, SpellTemplate>();
            }

            try
            {
                string json = File.ReadAllText(path);
                Dictionary<int, SpellTemplate> result =
                    JsonConvert.DeserializeObject<Dictionary<int, SpellTemplate>>(json)
                    ?? new Dictionary<int, SpellTemplate>();
                Debug.Log($"[{LogTag}] {result.Count} Eintraege geladen aus {path}");
                return result;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[{LogTag}] Fehler beim Laden von {path}: {ex.Message}");
                return new Dictionary<int, SpellTemplate>();
            }
        }
    }
}
