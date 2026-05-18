using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Scant <c>StreamingAssets/spells/animations/</c> rekursiv und lädt jedes
    /// <c>*.json</c> als <see cref="SpellAnimationDefinition"/> in einen
    /// <see cref="SpellAnimationCatalog"/>. Reiner Service (kein MonoBehaviour);
    /// wird im <c>ApplicationEntryPoint</c> per <c>ServiceLocator.Register</c>
    /// bereitgestellt.
    /// </summary>
    /// <remarks>
    /// Auf Desktop-Plattformen wird via <see cref="Directory"/> gelistet;
    /// die Inhalte werden über <see cref="UnityWebRequest"/> gelesen, damit der
    /// Pfad auch auf Plattformen mit JAR/APK-Zugriff funktioniert.
    /// </remarks>
    public sealed class SpellAnimationCatalogLoader
    {
        /// <summary>Default-Unterordner in <c>StreamingAssets</c>.</summary>
        public const string DefaultSubFolder = "spells/animations";

        private readonly string m_AbsoluteFolder;
        private SpellAnimationCatalog m_Cached;

        /// <summary>Geladener Ordner (zum Debuggen / Logging).</summary>
        public string AbsoluteFolder => m_AbsoluteFolder;

        /// <summary>True, sobald der Katalog mindestens einmal erfolgreich geladen wurde.</summary>
        public bool IsLoaded => m_Cached != null;

        /// <summary>
        /// Erzeugt einen Loader mit dem Default-Pfad
        /// <c>StreamingAssets/spells/animations/</c>.
        /// </summary>
        public SpellAnimationCatalogLoader()
            : this(Path.Combine(Application.streamingAssetsPath, DefaultSubFolder))
        {
        }

        /// <summary>
        /// Erzeugt einen Loader mit explizitem absolutem Ordner-Pfad.
        /// </summary>
        public SpellAnimationCatalogLoader(string absoluteFolder)
        {
            m_AbsoluteFolder = absoluteFolder;
        }

        /// <summary>
        /// Lädt alle Animations-JSON-Dateien (oder liefert die gecachte Instanz).
        /// Liefert <c>null</c>, wenn der Ordner fehlt.
        /// </summary>
        public async Task<SpellAnimationCatalog> LoadAsync()
        {
            if (m_Cached != null)
            {
                return m_Cached;
            }

            if (!Directory.Exists(m_AbsoluteFolder))
            {
                Debug.LogWarning($"[SpellAnimationCatalogLoader] Ordner fehlt: {m_AbsoluteFolder}");
                return null;
            }

            string[] files = Directory.GetFiles(m_AbsoluteFolder, "*.json", SearchOption.TopDirectoryOnly);
            List<SpellAnimationDefinition> defs = new(files.Length);
            int errors = 0;

            foreach (string file in files)
            {
                string jsonText = await ReadTextAsync(file);
                if (jsonText == null)
                {
                    errors++;
                    continue;
                }

                SpellAnimationDefinition def;
                try
                {
                    def = JsonConvert.DeserializeObject<SpellAnimationDefinition>(jsonText);
                }
                catch (JsonException ex)
                {
                    Debug.LogError($"[SpellAnimationCatalogLoader] JSON-Parse-Fehler in {file}: {ex.Message}");
                    errors++;
                    continue;
                }
                if (def == null)
                {
                    errors++;
                    continue;
                }
                // Falls "name" im JSON fehlt, vom Dateinamen ableiten.
                if (string.IsNullOrEmpty(def.Name))
                {
                    def.Name = Path.GetFileNameWithoutExtension(file);
                }
                defs.Add(def);
            }

            m_Cached = new SpellAnimationCatalog(defs);
            Debug.Log($"[SpellAnimationCatalogLoader] {m_Cached.Count} Animationen geladen aus {m_AbsoluteFolder} (errors: {errors})");
            return m_Cached;
        }

        /// <summary>
        /// Liefert den gecachten Katalog synchron (oder <c>null</c>, falls noch nicht geladen).
        /// </summary>
        public SpellAnimationCatalog GetCached() => m_Cached;

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
