using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Lädt <c>StreamingAssets/combat/offhand_items.json</c> und liefert einen
    /// <see cref="OffhandCatalog"/>. Reiner Service (kein MonoBehaviour) — wird im
    /// <c>ApplicationEntryPoint</c> per <c>ServiceLocator.Register</c> bereitgestellt
    /// und beim Teardown via Reflection (<c>ClearCache</c>) aufgeräumt.
    /// </summary>
    /// <remarks>
    /// JSON-Format ist offen / modding-freundlich: einfach neue Einträge in die
    /// <c>offhand_items</c>-Liste schreiben. Schema siehe <see cref="OffhandCatalogDef"/>.
    /// </remarks>
    public sealed class OffhandCatalogLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "combat";
        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "offhand_items.json";

        private readonly string m_AbsolutePath;
        private OffhandCatalog m_Cached;

        /// <summary>Vollständiger Pfad zur geladenen JSON-Datei (zum Debuggen / Logging).</summary>
        public string AbsolutePath => m_AbsolutePath;

        /// <summary>True, sobald der Katalog mindestens einmal erfolgreich geladen wurde.</summary>
        public bool IsLoaded => m_Cached != null;

        /// <summary>
        /// Erzeugt einen Loader mit dem Default-Pfad <c>StreamingAssets/combat/offhand_items.json</c>.
        /// </summary>
        public OffhandCatalogLoader()
            : this(Path.Combine(Application.streamingAssetsPath, DefaultSubFolder, DefaultFileName))
        {
        }

        /// <summary>
        /// Erzeugt einen Loader mit explizitem absolutem Pfad. Nützlich für Tests
        /// oder externe Mod-Verzeichnisse.
        /// </summary>
        public OffhandCatalogLoader(string absolutePath)
        {
            m_AbsolutePath = absolutePath;
        }

        /// <summary>
        /// Lädt den Katalog (oder liefert die gecachte Instanz). Liefert <c>null</c>,
        /// wenn die Datei fehlt oder das JSON kaputt ist.
        /// </summary>
        public async Task<OffhandCatalog> LoadAsync()
        {
            if (m_Cached != null)
            {
                return m_Cached;
            }

            string jsonText = await ReadTextAsync(m_AbsolutePath);
            if (jsonText == null)
            {
                Debug.LogWarning($"[OffhandCatalogLoader] offhand_items.json nicht gefunden: {m_AbsolutePath}");
                return null;
            }

            OffhandCatalogDef def;
            try
            {
                def = JsonConvert.DeserializeObject<OffhandCatalogDef>(jsonText);
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[OffhandCatalogLoader] JSON-Parse-Fehler in {m_AbsolutePath}: {ex.Message}");
                return null;
            }
            if (def == null)
            {
                Debug.LogError($"[OffhandCatalogLoader] Leerer Katalog in {m_AbsolutePath}");
                return null;
            }

            m_Cached = new OffhandCatalog(def);
            Debug.Log($"[OffhandCatalogLoader] {m_Cached.Count} Offhand-Items geladen aus {m_AbsolutePath}");
            return m_Cached;
        }

        /// <summary>
        /// Liefert den gecachten Katalog synchron (oder <c>null</c>, falls noch nicht geladen).
        /// </summary>
        public OffhandCatalog GetCached() => m_Cached;

        /// <summary>
        /// Verwirft den Cache. Wird vom ServiceLocator beim Teardown via Reflection aufgerufen.
        /// </summary>
        public void ClearCache()
        {
            m_Cached = null;
        }

        private static async Task<string> ReadTextAsync(string path)
        {
            using UnityWebRequest req = UnityWebRequest.Get(FileUri(path));
            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }
            if (req.result != UnityWebRequest.Result.Success)
            {
                return null;
            }
            return req.downloadHandler.text;
        }

        private static string FileUri(string absolutePath)
        {
            if (absolutePath.Contains("://"))
            {
                return absolutePath;
            }
            return "file:///" + absolutePath.Replace('\\', '/');
        }
    }
}
