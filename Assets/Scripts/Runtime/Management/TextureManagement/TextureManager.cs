using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Riftstorm.Management.TextureManagement
{
    /// <summary>
    /// Pure Service: scannt System-Texturen unter <c>Application.dataPath/Art</c>
    /// (Editor &amp; Player-Standalone falls die Dateien ins Build-Output kopiert
    /// wurden) und Custom-Texturen unter <c>Application.persistentDataPath/CustomTextures</c>.
    /// Keys sind relative Pfade ohne Extension (z. B. <c>"interface/unit_frame"</c>).
    /// </summary>
    /// <remarks>
    /// Aufbau: TextureManager + TextureRegistry + LazyTextureLoader, aber ohne
    /// Material-Pipeline — Riftstorm-Code baut noch keine Materialien aus
    /// Skin-Definitionen.
    /// </remarks>
    public sealed class TextureManager
    {
        private const string SystemSubFolder = "Art";
        private const string CustomDirectoryName = "CustomTextures";
        private static readonly string[] s_SupportedExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff" };

        private readonly TextureRegistry m_Registry = new();

        public TextureRegistry Registry => m_Registry;

        public TextureManager()
        {
            ScanAllTextureDirectories();
            Debug.Log($"[TextureManager] Initialized. Indexed {m_Registry.TextureCache.Count} textures.");
        }

        /// <summary>Bequemer Accessor: laedt (lazy) und gibt die Texture2D zurueck oder null.</summary>
        public Texture2D GetTexture(string key)
        {
            TextureData data = m_Registry.GetTextureData(key);
            return data != null && data.HasTexture() ? data.Texture : null;
        }

        public TextureData GetTextureData(string key) => m_Registry.GetTextureData(key);

        /// <summary>Wird vom <c>ServiceLocator.ClearAll()</c> per Reflection aufgerufen.</summary>
        public void ClearCache() => m_Registry.ClearCache();

        private static string GetSystemTexturesFullPath()
            => Path.Combine(Application.dataPath, SystemSubFolder);

        private static string GetCustomTexturesFullPath()
            => Path.Combine(Application.persistentDataPath, CustomDirectoryName);

        private void ScanAllTextureDirectories()
        {
            m_Registry.ClearCache();

            string systemPath = GetSystemTexturesFullPath();
            if (Directory.Exists(systemPath))
            {
                ScanDirectory(systemPath, TextureSource.System);
            }
            else
            {
                Debug.LogWarning($"[TextureManager] System texture directory missing: {systemPath}");
            }

            string customPath = GetCustomTexturesFullPath();
            if (!Directory.Exists(customPath))
            {
                Debug.Log($"[TextureManager] Creating custom dir: {customPath}");
                Directory.CreateDirectory(customPath);
            }
            ScanDirectory(customPath, TextureSource.Custom);
        }

        private void ScanDirectory(string root, TextureSource source)
        {
            List<string> all = new();
            foreach (string ext in s_SupportedExtensions)
            {
                try
                {
                    all.AddRange(Directory.GetFiles(root, "*" + ext, SearchOption.AllDirectories));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TextureManager] Scan error ({ext}) in {root}: {ex.Message}");
                }
            }

            foreach (string filePath in all)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string relative = GetRelativePath(root, filePath);
                string key = Path
                    .Combine(Path.GetDirectoryName(relative) ?? string.Empty, fileName)
                    .Replace('\\', '/');

                TextureData data = TextureDataFactory.Create(key, filePath, null, source);
                m_Registry.RegisterTextureData(data);
            }
            Debug.Log($"[TextureManager] Scanned {root}: {all.Count} files ({source})");
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            Uri baseUri = new(basePath + Path.DirectorySeparatorChar);
            Uri fullUri = new(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString());
        }
    }
}
