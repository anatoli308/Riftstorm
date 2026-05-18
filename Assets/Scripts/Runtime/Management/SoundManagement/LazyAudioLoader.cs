using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Riftstorm.Management.SoundManagement
{
    /// <summary>
    /// Default-Loader: greift auf bereits gescannte Registry-Eintraege zu und
    /// laedt die zugehoerige Audio-Datei on demand per
    /// <c>UnityWebRequestMultimedia.GetAudioClip</c>. Synchron blockierend (das
    /// Pattern spiegelt den <see cref="LazyTextureLoader"/>) — fuer kurze
    /// Spell-Cast-Sounds (~50 KB .ogg) akzeptabel; ein Async-Pfad kann spaeter
    /// nachgeruestet werden, sobald Streaming-Sounds dazukommen.
    /// </summary>
    public sealed class LazyAudioLoader : ISoundLoader
    {
        private readonly SoundRegistry m_Registry;

        /// <summary>Konstruktor.</summary>
        public LazyAudioLoader(SoundRegistry registry)
        {
            m_Registry = registry;
        }

        /// <inheritdoc/>
        public bool CanLoad(string key) => m_Registry.SoundCache.ContainsKey(key);

        /// <inheritdoc/>
        public SoundData Load(string key)
        {
            if (!m_Registry.SoundCache.TryGetValue(key, out SoundData data))
            {
                return null;
            }
            if (!File.Exists(data.FilePath))
            {
                Debug.LogWarning($"[LazyAudioLoader] File not found: {data.FilePath}");
                return null;
            }
            if (data.HasClip())
            {
                return data;
            }

            AudioClip clip = LoadClipFromFile(data.FilePath);
            if (clip != null)
            {
                clip.name = key;
                m_Registry.UpdateClip(key, clip);
            }
            return data;
        }

        private static AudioClip LoadClipFromFile(string filePath)
        {
            try
            {
                AudioType type = ResolveAudioType(filePath);
                string uri = new Uri(filePath).AbsoluteUri;
                using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, type);
                UnityWebRequestAsyncOperation op = req.SendWebRequest();
                while (!op.isDone) { /* synchroner Spin — Datei liegt lokal, Latenz < 1 ms. */ }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[LazyAudioLoader] GetAudioClip failed ({req.result}): {filePath} — {req.error}");
                    return null;
                }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null)
                {
                    Debug.LogError($"[LazyAudioLoader] DownloadHandlerAudioClip returned null: {filePath}");
                }
                return clip;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LazyAudioLoader] Error loading {filePath}: {ex.Message}");
                return null;
            }
        }

        private static AudioType ResolveAudioType(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) { return AudioType.UNKNOWN; }
            ext = ext.ToLowerInvariant();
            return ext switch
            {
                ".ogg" => AudioType.OGGVORBIS,
                ".wav" => AudioType.WAV,
                ".mp3" => AudioType.MPEG,
                ".aif" or ".aiff" => AudioType.AIFF,
                _ => AudioType.UNKNOWN,
            };
        }
    }
}
