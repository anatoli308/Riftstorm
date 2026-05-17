using System;
using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.ApplicationLifecycle.UI
{
    /// <summary>
    /// Pure Service, der die im Projekt vorhandenen <see cref="Font"/>-Assets
    /// per Name nachschlagbar macht. Wird vom <c>ApplicationEntryPoint</c>
    /// mit den Inspector-zugewiesenen Fonts gebaut und im
    /// <c>ServiceLocator</c> registriert.
    /// </summary>
    /// <remarks>
    /// Unity kann <c>.ttf</c>-Dateien zur Laufzeit nicht direkt aus
    /// <c>StreamingAssets</c> als <see cref="Font"/> laden — deshalb bleiben
    /// die Font-Assets normale Projekt-Assets (z. B. unter <c>Assets/Fonts/</c>),
    /// werden aber NICHT ueber <c>Resources</c> geladen. Die Zuordnung
    /// "Rolle → Font-Name" kommt aus <c>StreamingAssets/interface/ui_fonts.json</c>
    /// (siehe <see cref="UIFontConfigLoader"/>).
    /// </remarks>
    public sealed class FontRegistry
    {
        readonly Dictionary<string, Font> m_FontsByName;

        /// <summary>
        /// Baut die Registry aus einer Liste Inspector-zugewiesener Fonts.
        /// <c>null</c>-Eintraege und Eintraege ohne <see cref="UnityEngine.Object.name"/>
        /// werden ignoriert. Case-insensitive Lookup.
        /// </summary>
        public FontRegistry(IReadOnlyList<Font> fonts)
        {
            m_FontsByName = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
            if (fonts == null)
            {
                return;
            }
            for (int i = 0; i < fonts.Count; i++)
            {
                Font font = fonts[i];
                if (font == null || string.IsNullOrEmpty(font.name))
                {
                    continue;
                }
                m_FontsByName[font.name] = font;
            }
        }

        /// <summary>
        /// Liefert das Font-Asset zu <paramref name="fontName"/> oder
        /// <c>null</c>, wenn nicht registriert. Aufrufer faellt typischerweise
        /// auf Unity-Default zurueck (UI Toolkit zeigt dann LegacyRuntime).
        /// </summary>
        public Font Get(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
            {
                return null;
            }
            return m_FontsByName.TryGetValue(fontName, out Font font) ? font : null;
        }

        /// <summary>Anzahl registrierter Fonts. Diagnose-/Logging-Hilfe.</summary>
        public int Count => m_FontsByName.Count;
    }
}
