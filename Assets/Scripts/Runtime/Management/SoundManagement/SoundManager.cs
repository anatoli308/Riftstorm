using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Riftstorm.Management.SoundManagement
{
    /// <summary>
    /// Pure Service: scannt System-Sounds unter <c>Application.dataPath/Art/sounds</c>
    /// (Editor &amp; Player-Standalone falls die Dateien ins Build-Output kopiert
    /// wurden) und Custom-Sounds unter <c>Application.persistentDataPath/CustomSounds</c>.
    /// Keys sind Dateinamen inklusive Extension (z. B. <c>"e3_stereoskill_spell1.ogg"</c>) —
    /// genau das Format, das <c>spell_visual.sound</c> in <c>_visual_kits.json</c> liefert.
    /// </summary>
    /// <remarks>
    /// Architektur-Spiegel des <see cref="Riftstorm.Management.TextureManagement.TextureManager"/>:
    /// SoundManager + SoundRegistry + LazyAudioLoader, ohne weitere
    /// Audio-Pipeline-Stufen. Lookup per Dateiname (inkl. Extension), weil das
    /// Source-Schema genau so referenziert.
    /// </remarks>
    public sealed class SoundManager
    {
        private const string SystemSubFolder = "Art/sounds";
        private const string CustomDirectoryName = "CustomSounds";
        private static readonly string[] s_SupportedExtensions = { ".ogg", ".wav", ".mp3", ".aif", ".aiff" };

        private readonly SoundRegistry m_Registry = new();

        /// <summary>Direkter Zugriff auf die Registry (fuer Loader-Erweiterungen).</summary>
        public SoundRegistry Registry => m_Registry;

        /// <summary>Konstruktor — scant beide Verzeichnisse und indexiert alle Sound-Dateien.</summary>
        public SoundManager()
        {
            ScanAllSoundDirectories();
            Debug.Log($"[SoundManager] Initialized. Indexed {m_Registry.SoundCache.Count} sounds.");
        }

        /// <summary>Bequemer Accessor: laedt (lazy) und gibt den AudioClip zurueck oder <c>null</c>.</summary>
        public AudioClip GetClip(string fileName)
        {
            SoundData data = m_Registry.GetSoundData(fileName);
            return data != null && data.HasClip() ? data.Clip : null;
        }

        /// <summary>Liefert das volle Daten-Objekt (inkl. Pfad/Source).</summary>
        public SoundData GetSoundData(string fileName) => m_Registry.GetSoundData(fileName);

        /// <summary>Wird vom <c>ServiceLocator.ClearAll()</c> per Reflection aufgerufen.</summary>
        public void ClearCache() => m_Registry.ClearCache();

        private static string GetSystemSoundsFullPath()
            => Path.Combine(Application.dataPath, SystemSubFolder);

        private static string GetCustomSoundsFullPath()
            => Path.Combine(Application.persistentDataPath, CustomDirectoryName);

        private void ScanAllSoundDirectories()
        {
            m_Registry.ClearCache();

            string systemPath = GetSystemSoundsFullPath();
            if (Directory.Exists(systemPath))
            {
                ScanDirectory(systemPath, SoundSource.System);
            }
            else
            {
                Debug.LogWarning($"[SoundManager] System sound directory missing: {systemPath}");
            }

            string customPath = GetCustomSoundsFullPath();
            if (!Directory.Exists(customPath))
            {
                Debug.Log($"[SoundManager] Creating custom dir: {customPath}");
                Directory.CreateDirectory(customPath);
            }
            ScanDirectory(customPath, SoundSource.Custom);
        }

        private void ScanDirectory(string root, SoundSource source)
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
                    Debug.LogError($"[SoundManager] Scan error ({ext}) in {root}: {ex.Message}");
                }
            }

            foreach (string filePath in all)
            {
                // Key = Dateiname INKL. Extension (matched _visual_kits.json -> "sound").
                string key = Path.GetFileName(filePath);
                SoundData data = SoundDataFactory.Create(key, filePath, null, source);
                m_Registry.RegisterSoundData(data);
            }
            Debug.Log($"[SoundManager] Scanned {root}: {all.Count} files ({source})");
        }
    }
}
