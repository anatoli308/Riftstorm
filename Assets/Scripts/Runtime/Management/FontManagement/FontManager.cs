using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Riftstorm.Management.FontManagement
{
    /// <summary>
    /// Pure Service, der die UI-Fonts aus <c>Assets/Art/fonts</c> laedt und per
    /// Name nachschlagbar macht. Wird vom <c>ApplicationEntryPoint</c> gebaut,
    /// im <c>ServiceLocator</c> registriert und ueber <see cref="UIFonts"/>
    /// konsumiert.
    /// </summary>
    /// <remarks>
    /// Bewusst schlank gehalten (kein Registry/Loader/Data-Unterbau wie beim
    /// TextureManager): Es gibt nur eine Handvoll Fonts, sie bleiben alle
    /// gleichzeitig im Speicher. Keys sind Dateinamen ohne Extension
    /// (z. B. <c>"Friz Quadrata Bold"</c>) — exakt die Werte aus
    /// <c>StreamingAssets/interface/ui_fonts.json</c> (siehe <see cref="UIFontConfig"/>).
    /// <para>
    /// Unity besitzt keine Runtime-API, um aus rohen <c>.ttf</c>-Bytes ein
    /// <see cref="Font"/> zu bauen, daher laufen die Fonts als Projekt-Assets
    /// und werden im Editor ueber die <c>AssetDatabase</c> geladen (analog zur
    /// dataPath-basierten Natur von <c>TextureManager</c>/<c>SoundManager</c>).
    /// </para>
    /// </remarks>
    public sealed class FontManager
    {
        private const string FontsSubFolder = "Art/fonts";
        private static readonly string[] s_SupportedExtensions = { ".ttf", ".otf" };

        private readonly Dictionary<string, Font> m_FontsByName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Anzahl geladener Fonts. Diagnose-/Logging-Hilfe.</summary>
        public int Count => m_FontsByName.Count;

        /// <summary>Konstruktor — laedt alle Fonts aus <c>Assets/Art/fonts</c>.</summary>
        public FontManager()
        {
            LoadFonts();
            Debug.Log($"[FontManager] Initialized. Loaded {m_FontsByName.Count} fonts.");
        }

        /// <summary>
        /// Liefert das Font-Asset zu <paramref name="fontName"/> oder <c>null</c>,
        /// wenn nicht geladen. Aufrufer (<see cref="UIFonts"/>) faellt dann auf den
        /// Unity-Default-Font zurueck.
        /// </summary>
        public Font GetFont(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
            {
                return null;
            }
            return m_FontsByName.TryGetValue(fontName, out Font font) ? font : null;
        }

        /// <summary>
        /// Leert den Font-Index. Wird vom <c>ServiceLocator.ClearAll()</c> per
        /// Reflection aufgerufen. Die Font-Assets selbst werden NICHT zerstoert —
        /// sie sind geteilte Projekt-Assets.
        /// </summary>
        public void ClearCache() => m_FontsByName.Clear();

        private void LoadFonts()
        {
            string root = Path.Combine(Application.dataPath, FontsSubFolder);
            if (!Directory.Exists(root))
            {
                Debug.LogWarning($"[FontManager] Font directory missing: {root}");
                return;
            }

            foreach (string ext in s_SupportedExtensions)
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(root, "*" + ext, SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FontManager] Scan error ({ext}) in {root}: {ex.Message}");
                    continue;
                }

                foreach (string filePath in files)
                {
                    // Key = Dateiname OHNE Extension (matched ui_fonts.json -> "Friz Quadrata Bold").
                    string key = Path.GetFileNameWithoutExtension(filePath);
                    Font font = LoadFontAsset(filePath);
                    if (font != null)
                    {
                        m_FontsByName[key] = font;
                    }
                }
            }
        }

        private static Font LoadFontAsset(string absolutePath)
        {
#if UNITY_EDITOR
            string assetPath = ToAssetPath(absolutePath);
            Font font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(assetPath);
            if (font == null)
            {
                Debug.LogWarning($"[FontManager] AssetDatabase konnte kein Font-Asset laden: {assetPath}");
            }
            return font;
#else
            // Standalone-Builds: Art/fonts liegt nicht im Build-Output und Unity
            // kann TTF nicht zur Laufzeit als Font laden. UIFonts faellt dann auf
            // den Default-Font zurueck. Fuer Builds ggf. Addressables ergaenzen.
            return null;
#endif
        }

#if UNITY_EDITOR
        private static string ToAssetPath(string absolutePath)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            string full = absolutePath.Replace('\\', '/');
            if (full.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + full.Substring(dataPath.Length);
            }
            return full;
        }
#endif
    }
}