using System;
using System.IO;
using UnityEngine;

namespace Riftstorm.Management.TextureManagement
{
    /// <summary>
    /// Default-Loader: greift auf bereits gescannte Registry-Eintraege zu und
    /// laedt die zugehoerige Bilddatei on demand per
    /// <c>File.ReadAllBytes</c> + <c>Texture2D.LoadImage</c>.
    /// </summary>
    public sealed class LazyTextureLoader : ITextureLoader
    {
        private readonly TextureRegistry m_Registry;

        public LazyTextureLoader(TextureRegistry registry)
        {
            m_Registry = registry;
        }

        public bool CanLoad(string key) => m_Registry.TextureCache.ContainsKey(key);

        public TextureData Load(string key)
        {
            if (!m_Registry.TextureCache.TryGetValue(key, out TextureData data))
            {
                return null;
            }
            if (!File.Exists(data.FilePath))
            {
                Debug.LogWarning($"[LazyTextureLoader] File not found: {data.FilePath}");
                return null;
            }
            if (data.HasTexture())
            {
                return data;
            }

            Texture2D tex = LoadTextureFromFile(data.FilePath);
            if (tex != null)
            {
                tex.name = key;
                m_Registry.UpdateTexture(key, tex);
            }
            return data;
        }

        private Texture2D LoadTextureFromFile(string filePath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                Texture2D tex = new(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                if (tex.LoadImage(bytes))
                {
                    return tex;
                }
                Debug.LogError($"[LazyTextureLoader] LoadImage failed: {filePath}");
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LazyTextureLoader] Error loading {filePath}: {ex.Message}");
                return null;
            }
        }
    }
}
