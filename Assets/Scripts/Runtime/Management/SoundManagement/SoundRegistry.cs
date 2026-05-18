using System;
using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Management.SoundManagement
{
    /// <summary>
    /// Cache + Loader-Hub fuer AudioClips. Der <see cref="SoundManager"/> fuellt
    /// die Registry initial mit Metadaten (Pfade ohne geladenen AudioClip), die
    /// Loader (Default: <see cref="LazyAudioLoader"/>) holen die Audio-Daten erst
    /// bei Bedarf nach.
    /// </summary>
    public sealed class SoundRegistry
    {
        private readonly Dictionary<string, SoundData> m_SoundCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ISoundLoader> m_CustomLoaders = new();

        /// <summary>Read-only Sicht auf den Cache (Lookup per Dateiname inkl. Extension).</summary>
        public IReadOnlyDictionary<string, SoundData> SoundCache => m_SoundCache;

        /// <summary>Konstruktor — registriert den Default-Lazy-Loader.</summary>
        public SoundRegistry()
        {
            RegisterLoader("lazy", new LazyAudioLoader(this));
        }

        /// <summary>Haengt einen weiteren Loader an (z. B. Streaming-Backend).</summary>
        public void RegisterLoader(string key, ISoundLoader loader)
        {
            if (string.IsNullOrEmpty(key) || loader == null)
            {
                Debug.LogWarning("[SoundRegistry] Invalid loader registration");
                return;
            }
            m_CustomLoaders[key] = loader;
        }

        /// <summary>Registriert oder ueberschreibt einen Eintrag (Custom &gt; System).</summary>
        public void RegisterSoundData(SoundData data)
        {
            if (data == null || !data.IsValid())
            {
                Debug.LogWarning("[SoundRegistry] Invalid SoundData registration");
                return;
            }
            if (m_SoundCache.ContainsKey(data.Id) && data.Source == SoundSource.Custom)
            {
                Debug.Log($"[SoundRegistry] Custom override for key: {data.Id}");
            }
            m_SoundCache[data.Id] = data;
        }

        internal void UpdateClip(string key, AudioClip clip)
        {
            if (m_SoundCache.TryGetValue(key, out SoundData data))
            {
                data.Clip = clip;
            }
        }

        /// <summary>
        /// Liefert (falls noetig: laedt) die <see cref="SoundData"/> zu einem Key
        /// oder <c>null</c>. Pruefung erfolgt cache-first, dann Loader-Chain.
        /// </summary>
        public SoundData GetSoundData(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (m_SoundCache.TryGetValue(key, out SoundData cached) && cached.HasClip())
            {
                return cached;
            }
            return TryLoadFromLoaders(key);
        }

        private SoundData TryLoadFromLoaders(string key)
        {
            foreach (ISoundLoader loader in m_CustomLoaders.Values)
            {
                if (loader.CanLoad(key))
                {
                    SoundData result = loader.Load(key);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }

        /// <summary>Zerstoert alle gecachten AudioClips und leert den Registry-Index.</summary>
        public void ClearCache()
        {
            int destroyed = 0;
            foreach (SoundData data in m_SoundCache.Values)
            {
                if (data?.Clip != null)
                {
                    UnityEngine.Object.Destroy(data.Clip);
                    destroyed++;
                }
            }
            Debug.Log($"[SoundRegistry] Cleared {m_SoundCache.Count} entries, destroyed {destroyed} clips");
            m_SoundCache.Clear();
        }
    }
}
