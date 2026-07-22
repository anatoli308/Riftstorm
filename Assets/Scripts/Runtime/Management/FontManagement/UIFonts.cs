using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Management.FontManagement
{
    /// <summary>
    /// Statischer Accessor fuer die UI-Fonts. Kombiniert die per JSON
    /// (<see cref="UIFontConfigLoader"/>) konfigurierte Rolle-zu-Name-Zuordnung
    /// mit dem ueber <see cref="FontResolver"/> injizierten <see cref="FontManager"/>.
    /// </summary>
    /// <remarks>
    /// Alle Getter sind null-safe: fehlt der Resolver oder das Font-Asset,
    /// wird <c>null</c> zurueckgegeben und <see cref="Apply"/> macht einen
    /// No-Op — UI Toolkit faellt dann auf den Default-Font zurueck.
    /// <para>
    /// <see cref="FontResolver"/> wird vom <c>ApplicationEntryPoint</c> gesetzt
    /// (<c>FontResolver = fontManager.GetFont</c>). Diese Injection vermeidet
    /// einen Asmdef-Zyklus zwischen <c>Riftstorm.Management</c> und
    /// <c>Riftstorm.ApplicationLifecycle</c> (analog <c>SpellSpriteCache.TextureResolver</c>).
    /// </para>
    /// </remarks>
    public static class UIFonts
    {
        /// <summary>
        /// Auflöser von Font-Name (z. B. <c>"Friz Quadrata Bold"</c>) zu
        /// <see cref="Font"/>. Muss vor dem ersten Getter-Zugriff gesetzt sein,
        /// sonst liefern alle Getter <c>null</c>.
        /// </summary>
        public static Func<string, Font> FontResolver { get; set; }

        /// <summary>Display-/Titel-Font (z. B. Login-Screen Headline).</summary>
        public static Font Title => Resolve(UIFontConfigLoader.Load().title);

        /// <summary>Heading-Font (Spieler-/Target-Namen, Section-Header).</summary>
        public static Font Heading => Resolve(UIFontConfigLoader.Load().heading);

        /// <summary>Body-Font (Eingabefelder, Fliesstext).</summary>
        public static Font Body => Resolve(UIFontConfigLoader.Load().body);

        /// <summary>Small-Font (Statuszeilen, Tooltips, Untertitel).</summary>
        public static Font Small => Resolve(UIFontConfigLoader.Load().small);

        /// <summary>Keybind-Font (Tastenkuerzel auf Action-Slots).</summary>
        public static Font Keybind => Resolve(UIFontConfigLoader.Load().keybind);

        /// <summary>Numeric-Font (HP/Mana/XP-Werte auf den Bars).</summary>
        public static Font Numeric => Resolve(UIFontConfigLoader.Load().numeric);

        /// <summary>Dialog-Font (Confirm-Boxen, Story-Texte).</summary>
        public static Font Dialog => Resolve(UIFontConfigLoader.Load().dialog);

        /// <summary>
        /// Setzt <see cref="IStyle.unityFontDefinition"/> auf <paramref name="font"/>.
        /// No-Op bei <c>null</c>-Element oder <c>null</c>-Font.
        /// </summary>
        public static void Apply(VisualElement element, Font font)
        {
            if (element == null || font == null)
            {
                return;
            }
            element.style.unityFontDefinition = new StyleFontDefinition(font);
        }

        static Font Resolve(string fontName)
        {
            Func<string, Font> resolver = FontResolver;
            return resolver?.Invoke(fontName);
        }
    }
}
