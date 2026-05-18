using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Lädt <c>StreamingAssets/spells/spell_visuals.json</c> und liefert einen
    /// <see cref="SpellVisualCatalog"/>. Reiner Service (kein MonoBehaviour);
    /// im <c>ApplicationEntryPoint</c> per <c>ServiceLocator.Register</c>
    /// bereitgestellt, beim Teardown via Reflection (<c>ClearCache</c>) aufgeräumt.
    /// </summary>
    /// <remarks>
    /// Die JSON-Datei wird typischerweise vom Python-Generator
    /// <c>Tools/GenerateSpellVisuals.py</c> erzeugt und kann von Designern
    /// händisch übersteuert werden.
    /// </remarks>
    public sealed class SpellVisualCatalogLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "spells";
        /// <summary>Default-Dateiname.</summary>
        public const string DefaultFileName = "spell_visuals.json";

        private readonly string m_AbsolutePath;
        private SpellVisualCatalog m_Cached;

        /// <summary>Vollständiger Pfad zur geladenen JSON-Datei.</summary>
        public string AbsolutePath => m_AbsolutePath;

        /// <summary>True, sobald der Katalog mindestens einmal erfolgreich geladen wurde.</summary>
        public bool IsLoaded => m_Cached != null;

        /// <summary>
        /// Erzeugt einen Loader mit dem Default-Pfad
        /// <c>StreamingAssets/spells/spell_visuals.json</c>.
        /// </summary>
        public SpellVisualCatalogLoader()
            : this(Path.Combine(Application.streamingAssetsPath, DefaultSubFolder, DefaultFileName))
        {
        }

        /// <summary>
        /// Erzeugt einen Loader mit explizitem absolutem Pfad. Nützlich für Tests
        /// oder externe Mod-Verzeichnisse.
        /// </summary>
        public SpellVisualCatalogLoader(string absolutePath)
        {
            m_AbsolutePath = absolutePath;
        }

        /// <summary>
        /// Lädt den Katalog (oder liefert die gecachte Instanz). Liefert <c>null</c>,
        /// wenn die Datei fehlt oder das JSON kaputt ist.
        /// </summary>
        public async Task<SpellVisualCatalog> LoadAsync()
        {
            if (m_Cached != null)
            {
                return m_Cached;
            }

            string jsonText = await ReadTextAsync(m_AbsolutePath);
            if (jsonText == null)
            {
                Debug.LogWarning($"[SpellVisualCatalogLoader] spell_visuals.json nicht gefunden: {m_AbsolutePath}");
                return null;
            }

            SpellVisualCatalogDef def;
            try
            {
                def = JsonConvert.DeserializeObject<SpellVisualCatalogDef>(jsonText);
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[SpellVisualCatalogLoader] JSON-Parse-Fehler in {m_AbsolutePath}: {ex.Message}");
                return null;
            }
            if (def == null)
            {
                Debug.LogError($"[SpellVisualCatalogLoader] Leerer Katalog in {m_AbsolutePath}");
                return null;
            }

            m_Cached = new SpellVisualCatalog(def);
            Debug.Log($"[SpellVisualCatalogLoader] {m_Cached.Count} Spell-Visuals geladen aus {m_AbsolutePath}");
            return m_Cached;
        }

        /// <summary>
        /// Liefert den gecachten Katalog synchron (oder <c>null</c>, falls noch nicht geladen).
        /// </summary>
        public SpellVisualCatalog GetCached() => m_Cached;

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
