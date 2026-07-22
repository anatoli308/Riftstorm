using System;
using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Management.FontManagement
{
    /// <summary>
    /// Cache + Loader-Hub fuer <see cref="Font"/>-Assets. Der
    /// <see cref="FontManager"/> fuellt die Registry initial mit Metadaten
    /// (Pfade ohne geladenes Font-Asset), die Loader (Default:
    /// <see cref="LazyFontLoader"/>) holen das Asset erst bei Bedarf nach.
    /// </summary>
    /// <remarks>
    /// Architektur-Spiegel von <c>TextureRegistry</c>/<c>SoundRegistry</c>.
    /// Lookup per Dateiname ohne Extension (z. B. <c>"Friz Quadrata Bold"</c>) —
    /// genau die Werte, die <c>StreamingAssets/interface/ui_fonts.json</c> ueber
    /// <see cref="UIFontConfig"/> liefert.
    /// </remarks>
    public sealed class FontRegistry
    {
        private readonly Dictionary<string, FontData> m_FontCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IFontLoader> m_CustomLoaders = new();

        /// <summary>Read-only Sicht auf den Cache (Lookup per Dateiname ohne Extension).</summary>
        public IReadOnlyDictionary<string, FontData> FontCache => m_FontCache;

        /// <summary>Anzahl registrierter Font-Eintraege. Diagnose-/Logging-Hilfe.</summary>
        public int Count => m_FontCache.Count;

        /// <summary>Konstruktor — registriert den Default-Lazy-Loader.</summary>
        public FontRegistry()
        {
            RegisterLoader("lazy", new LazyFontLoader(this));
        }

        /// <summary>Haengt einen weiteren Loader an (z. B. Addressables-Backend fuer Builds).</summary>
        public void RegisterLoader(string key, IFontLoader loader)
        {
            if (string.IsNullOrEmpty(key) || loader == null)
            {
                Debug.LogWarning("[FontRegistry] Invalid loader registration");
                return;
            }
            m_CustomLoaders[key] = loader;
        }

        /// <summary>Registriert oder ueberschreibt einen Eintrag.</summary>
        public void RegisterFontData(FontData data)
        {
            if (data == null || !data.IsValid())
            {
                Debug.LogWarning("[FontRegistry] Invalid FontData registration");
                return;
            }
            m_FontCache[data.Id] = data;
        }

        internal void UpdateFont(string key, Font font)
        {
            if (m_FontCache.TryGetValue(key, out FontData data))
            {
                data.Font = font;
            }
        }

        /// <summary>
        /// Liefert (falls noetig: laedt) die <see cref="FontData"/> zu einem Key
        /// oder <c>null</c>. Pruefung erfolgt cache-first, dann Loader-Chain.
        /// </summary>
        public FontData GetFontData(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (m_FontCache.TryGetValue(key, out FontData cached) && cached.HasFont())
            {
                return cached;
            }
            return TryLoadFromLoaders(key);
        }

        private FontData TryLoadFromLoaders(string key)
        {
            foreach (IFontLoader loader in m_CustomLoaders.Values)
            {
                if (loader.CanLoad(key))
                {
                    FontData result = loader.Load(key);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Leert den Registry-Index. Font-Assets werden NICHT zerstoert — sie sind
        /// geteilte Projekt-Assets (via <c>AssetDatabase</c> referenziert), kein
        /// zur Laufzeit erzeugter Speicher wie bei Texturen/AudioClips.
        /// </summary>
        public void ClearCache()
        {
            Debug.Log($"[FontRegistry] Cleared {m_FontCache.Count} entries");
            m_FontCache.Clear();
        }
    }
}
