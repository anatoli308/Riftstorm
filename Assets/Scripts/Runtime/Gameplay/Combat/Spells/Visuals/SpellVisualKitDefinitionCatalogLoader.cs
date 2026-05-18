using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Laedt <c>StreamingAssets/spells/_visual_kits.json</c> und liefert einen
    /// <see cref="SpellVisualKitDefinitionCatalog"/>. Reiner Service (kein
    /// MonoBehaviour); im <c>ApplicationEntryPoint</c> per
    /// <c>ServiceLocator.Register</c> bereitgestellt, beim Teardown via
    /// Reflection (<c>ClearCache</c>) aufgeraeumt.
    /// </summary>
    /// <remarks>
    /// Das JSON-Format ist ein Wurzel-Objekt mit Integer-String-Keys
    /// (<c>{"3": {...}, "4": {...}}</c>) und mirrort die Source-Tabelle
    /// <c>spell_visual</c>.
    /// </remarks>
    public sealed class SpellVisualKitDefinitionCatalogLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "spells";

        /// <summary>Default-Dateiname (Source-Tabelle <c>spell_visual</c>).</summary>
        public const string DefaultFileName = "_visual_kits.json";

        private readonly string m_AbsolutePath;
        private SpellVisualKitDefinitionCatalog m_Cached;

        /// <summary>Vollstaendiger Pfad zur geladenen JSON-Datei.</summary>
        public string AbsolutePath => m_AbsolutePath;

        /// <summary>True, sobald der Katalog mindestens einmal erfolgreich geladen wurde.</summary>
        public bool IsLoaded => m_Cached != null;

        /// <summary>Erzeugt einen Loader mit dem Default-Pfad.</summary>
        public SpellVisualKitDefinitionCatalogLoader()
            : this(Path.Combine(Application.streamingAssetsPath, DefaultSubFolder, DefaultFileName))
        {
        }

        /// <summary>Erzeugt einen Loader mit explizitem absoluten Pfad (Tests, Mods).</summary>
        public SpellVisualKitDefinitionCatalogLoader(string absolutePath)
        {
            m_AbsolutePath = absolutePath;
        }

        /// <summary>
        /// Laedt den Katalog (oder liefert die gecachte Instanz). Liefert <c>null</c>,
        /// wenn die Datei fehlt oder das JSON kaputt ist.
        /// </summary>
        public async Task<SpellVisualKitDefinitionCatalog> LoadAsync()
        {
            if (m_Cached != null) { return m_Cached; }

            string jsonText = await ReadTextAsync(m_AbsolutePath);
            if (jsonText == null)
            {
                Debug.LogWarning($"[SpellVisualKitDefinitionCatalogLoader] _visual_kits.json nicht gefunden: {m_AbsolutePath}");
                return null;
            }

            Dictionary<string, SpellVisualKitDefinition> raw;
            try
            {
                raw = JsonConvert.DeserializeObject<Dictionary<string, SpellVisualKitDefinition>>(jsonText);
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[SpellVisualKitDefinitionCatalogLoader] JSON-Parse-Fehler in {m_AbsolutePath}: {ex.Message}");
                return null;
            }
            if (raw == null)
            {
                Debug.LogError($"[SpellVisualKitDefinitionCatalogLoader] Leerer Katalog in {m_AbsolutePath}");
                return null;
            }

            m_Cached = new SpellVisualKitDefinitionCatalog(raw);
            Debug.Log($"[SpellVisualKitDefinitionCatalogLoader] {m_Cached.Count} Spell-Visual-Kits geladen aus {m_AbsolutePath}");
            return m_Cached;
        }

        /// <summary>Liefert den gecachten Katalog synchron (oder <c>null</c>).</summary>
        public SpellVisualKitDefinitionCatalog GetCached() => m_Cached;

        /// <summary>Verwirft den Cache. Vom ServiceLocator beim Teardown via Reflection aufgerufen.</summary>
        public void ClearCache()
        {
            m_Cached = null;
        }

        private static async Task<string> ReadTextAsync(string path)
        {
            using UnityWebRequest req = UnityWebRequest.Get(FileUri(path));
            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            while (!op.isDone) { await Task.Yield(); }
            if (req.result != UnityWebRequest.Result.Success) { return null; }
            return req.downloadHandler.text;
        }

        private static string FileUri(string absolutePath)
        {
            if (absolutePath.Contains("://")) { return absolutePath; }
            return "file:///" + absolutePath.Replace('\\', '/');
        }
    }
}
