using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Core.Configuration
{
    /// <summary>
    /// Wiederverwendbarer, prozessweiter Lazy-Cache fuer ein einzelnes
    /// JSON-Config-Asset unter <c>StreamingAssets</c>. Kapselt das ueberall
    /// wiederholte Muster (Lazy-Static-Cache + Default-Fallback + Logging),
    /// sodass konkrete Loader nur noch Metadaten (Tag/Datei/Ordner) liefern und
    /// keine eigene IO-/Fehlerbehandlung mehr duplizieren.
    /// </summary>
    /// <typeparam name="T">
    /// Config-Typ mit parameterlosem Konstruktor. Der Default-Konstruktor liefert
    /// die Fallback-Werte, wenn die Datei fehlt oder das JSON kaputt ist.
    /// </typeparam>
    public sealed class JsonConfigFile<T> where T : new()
    {
        private readonly string m_LogTag;
        private readonly string m_FileName;
        private readonly string m_SubFolder;
        private readonly JsonSerializerSettings m_Settings;

        private T m_Cached;
        private bool m_LoadAttempted;

        /// <summary>Erstellt eine Cache-Instanz fuer genau ein Config-Asset.</summary>
        /// <param name="logTag">Praefix fuer Log-Meldungen (i. d. R. der Loader-Name).</param>
        /// <param name="fileName">Dateiname inkl. Extension (z. B. <c>"hud_config.json"</c>).</param>
        /// <param name="subFolder">Unterordner in <c>StreamingAssets</c> (Default <c>"interface"</c>).</param>
        /// <param name="settings">Optionale Serializer-Settings (z. B. Enum-Converter).</param>
        public JsonConfigFile(string logTag, string fileName, string subFolder = "interface", JsonSerializerSettings settings = null)
        {
            m_LogTag = logTag;
            m_FileName = fileName;
            m_SubFolder = subFolder;
            m_Settings = settings;
        }

        /// <summary>
        /// Liefert den geladenen Config oder einen frischen <typeparamref name="T"/>
        /// mit Default-Werten, falls die Datei fehlt oder das JSON kaputt ist. Der
        /// Rueckgabewert ist nie <c>null</c>. Nur der erste Aufruf macht IO.
        /// </summary>
        public T Load()
        {
            if (m_LoadAttempted)
            {
                return m_Cached;
            }
            m_LoadAttempted = true;

            string path = Path.Combine(Application.streamingAssetsPath, m_SubFolder, m_FileName);
            if (!File.Exists(path))
            {
                Debug.Log($"[{m_LogTag}] Kein Config gefunden ({path}) - Defaults aktiv.");
                m_Cached = new T();
                return m_Cached;
            }

            try
            {
                string json = File.ReadAllText(path);
                m_Cached = (m_Settings == null
                    ? JsonConvert.DeserializeObject<T>(json)
                    : JsonConvert.DeserializeObject<T>(json, m_Settings)) ?? new T();
                Debug.Log($"[{m_LogTag}] Config geladen: {path}");
                return m_Cached;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{m_LogTag}] Fehler beim Laden von {path}: {ex.Message}");
                m_Cached = new T();
                return m_Cached;
            }
        }

        /// <summary>
        /// Setzt Cache + Load-Attempt zurueck. Fuer Tests oder ein
        /// "Reload"-Feature im Editor.
        /// </summary>
        public void ResetCache()
        {
            m_Cached = default;
            m_LoadAttempted = false;
        }
    }
}
