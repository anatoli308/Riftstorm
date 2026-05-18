using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Lädt <c>StreamingAssets/particles/_particles.json</c> und liefert einen
    /// <see cref="ParticleSystemCatalog"/>. Reiner Service (kein MonoBehaviour);
    /// im <c>ApplicationEntryPoint</c> per <c>ServiceLocator.Register</c>
    /// bereitgestellt, beim Teardown via Reflection (<c>ClearCache</c>) aufgeräumt.
    /// </summary>
    /// <remarks>
    /// Das JSON-Format ist ein Wurzel-Objekt mit Partikelsystem-Namen als Keys
    /// (<c>{"casting_holy": {...}, "shoot_fire": {...}}</c>) und mirrort die
    /// Source-<c>.psi</c>-Binärdateien. Erzeugt vom Python-Importer
    /// <c>Tools/Scripts/particle_import/psi_to_json.py</c>.
    /// </remarks>
    public sealed class ParticleSystemCatalogLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "particles";

        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "_particles.json";

        private readonly string m_AbsolutePath;
        private ParticleSystemCatalog m_Cached;

        /// <summary>Vollständiger Pfad zur geladenen JSON-Datei.</summary>
        public string AbsolutePath => m_AbsolutePath;

        /// <summary>True, sobald der Katalog mindestens einmal erfolgreich geladen wurde.</summary>
        public bool IsLoaded => m_Cached != null;

        /// <summary>Erzeugt einen Loader mit dem Default-Pfad.</summary>
        public ParticleSystemCatalogLoader()
            : this(Path.Combine(Application.streamingAssetsPath, DefaultSubFolder, DefaultFileName))
        {
        }

        /// <summary>Erzeugt einen Loader mit explizitem absoluten Pfad (Tests, Mods).</summary>
        public ParticleSystemCatalogLoader(string absolutePath)
        {
            m_AbsolutePath = absolutePath;
        }

        /// <summary>
        /// Lädt den Katalog (oder liefert die gecachte Instanz). Liefert <c>null</c>,
        /// wenn die Datei fehlt oder das JSON kaputt ist.
        /// </summary>
        public async Task<ParticleSystemCatalog> LoadAsync()
        {
            if (m_Cached != null) { return m_Cached; }

            string jsonText = await ReadTextAsync(m_AbsolutePath);
            if (jsonText == null)
            {
                Debug.LogWarning($"[ParticleSystemCatalogLoader] _particles.json nicht gefunden: {m_AbsolutePath}");
                return null;
            }

            Dictionary<string, ParticleSystemDefinition> raw;
            try
            {
                raw = JsonConvert.DeserializeObject<Dictionary<string, ParticleSystemDefinition>>(jsonText);
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[ParticleSystemCatalogLoader] JSON-Parse-Fehler in {m_AbsolutePath}: {ex.Message}");
                return null;
            }
            if (raw == null)
            {
                Debug.LogError($"[ParticleSystemCatalogLoader] Leerer Katalog in {m_AbsolutePath}");
                return null;
            }

            m_Cached = new ParticleSystemCatalog(raw);
            Debug.Log($"[ParticleSystemCatalogLoader] {m_Cached.Count} Partikel-Systeme geladen aus {m_AbsolutePath}");
            return m_Cached;
        }

        /// <summary>Liefert den gecachten Katalog synchron (oder <c>null</c>).</summary>
        public ParticleSystemCatalog GetCached() => m_Cached;

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
