using System;
using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Management.TextureManagement
{
    /// <summary>
    /// Cache + Loader-Hub fuer Texturen. Der <see cref="TextureManager"/>
    /// fuellt die Registry initial mit Metadaten (Pfade ohne geladene
    /// Texture2D), die Loader (Default: <see cref="LazyTextureLoader"/>) holen
    /// die Pixel-Daten erst bei Bedarf nach.
    /// </summary>
    public sealed class TextureRegistry
    {
        private readonly Dictionary<string, TextureData> m_TextureCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ITextureLoader> m_CustomLoaders = new();

        public IReadOnlyDictionary<string, TextureData> TextureCache => m_TextureCache;

        public TextureRegistry()
        {
            RegisterLoader("lazy", new LazyTextureLoader(this));
        }

        /// <summary>Haengt einen weiteren Loader an (z. B. Addressables-Backend).</summary>
        public void RegisterLoader(string key, ITextureLoader loader)
        {
            if (string.IsNullOrEmpty(key) || loader == null)
            {
                Debug.LogWarning("[TextureRegistry] Invalid loader registration");
                return;
            }
            m_CustomLoaders[key] = loader;
        }

        /// <summary>Registriert oder ueberschreibt einen Eintrag (Custom &gt; System).</summary>
        public void RegisterTextureData(TextureData data)
        {
            if (data == null || !data.IsValid())
            {
                Debug.LogWarning("[TextureRegistry] Invalid TextureData registration");
                return;
            }
            if (m_TextureCache.ContainsKey(data.Id) && data.Source == TextureSource.Custom)
            {
                Debug.Log($"[TextureRegistry] Custom override for key: {data.Id}");
            }
            m_TextureCache[data.Id] = data;
        }

        internal void UpdateTexture(string key, Texture2D texture)
        {
            if (m_TextureCache.TryGetValue(key, out TextureData data))
            {
                data.Texture = texture;
            }
        }

        /// <summary>
        /// Liefert (falls noetig: laedt) die <see cref="TextureData"/> zu einem Key
        /// oder null. Pruefung erfolgt cache-first, dann Loader-Chain.
        /// </summary>
        public TextureData GetTextureData(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (m_TextureCache.TryGetValue(key, out TextureData cached) && cached.HasTexture())
            {
                return cached;
            }
            return TryLoadFromLoaders(key);
        }

        private TextureData TryLoadFromLoaders(string key)
        {
            foreach (ITextureLoader loader in m_CustomLoaders.Values)
            {
                if (loader.CanLoad(key))
                {
                    TextureData result = loader.Load(key);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }

        /// <summary>Zerstoert alle gecachten Texturen und leert den Registry-Index.</summary>
        public void ClearCache()
        {
            int destroyed = 0;
            foreach (TextureData data in m_TextureCache.Values)
            {
                if (data?.Texture != null)
                {
                    UnityEngine.Object.Destroy(data.Texture);
                    destroyed++;
                }
            }
            Debug.Log($"[TextureRegistry] Cleared {m_TextureCache.Count} entries, destroyed {destroyed} textures");
            m_TextureCache.Clear();
        }
    }
}
