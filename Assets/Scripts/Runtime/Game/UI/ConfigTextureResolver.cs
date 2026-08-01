using System.IO;
using Riftstorm.Management.TextureManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using UnityEngine;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Gemeinsamer Textur-Resolver fuer die JSON-Config-Loader (HUD, Character,
    /// Inventory, NameTag, ...). Kapselt den wiederholten
    /// <see cref="TextureManager"/>-Lookup inkl. Guard- und Warn-Logging, sodass
    /// jeder Loader nur seinen Log-Tag durchreicht statt den Code zu duplizieren.
    /// </summary>
    public static class ConfigTextureResolver
    {
        /// <summary>
        /// Resolved einen Textur-Key via <see cref="TextureManager"/>. Liefert
        /// <c>null</c>, wenn der Key leer ist, der TextureManager nicht
        /// registriert ist oder die Datei fehlt.
        /// </summary>
        /// <param name="logTag">Praefix fuer Warn-Meldungen (i. d. R. der Loader-Name).</param>
        /// <param name="textureKey">Relativer Pfad unter <c>Art</c> ohne Extension.</param>
        public static Texture2D LoadTextureOrNull(string logTag, string textureKey)
        {
            if (string.IsNullOrWhiteSpace(textureKey))
            {
                return null;
            }

            TextureManager manager = ServiceLocator.Get<TextureManager>();
            if (manager == null)
            {
                Debug.LogWarning($"[{logTag}] TextureManager nicht im ServiceLocator (Key '{textureKey}').");
                return null;
            }

            Texture2D tex = manager.GetTexture(textureKey);
            if (tex == null)
            {
                Debug.LogWarning($"[{logTag}] Textur nicht gefunden: '{textureKey}'");
            }
            return tex;
        }

        /// <summary>
        /// Loest den Icon-Key fuer ein Item-Template auf (Dateiname ohne
        /// Extension, optional mit Praefix) und laedt die Textur. Liefert
        /// <c>null</c> bei leerem Icon-Feld.
        /// </summary>
        public static Texture2D LoadItemIconOrNull(string logTag, string iconFileName, string keyPrefix)
        {
            if (string.IsNullOrWhiteSpace(iconFileName))
            {
                return null;
            }
            string baseName = Path.GetFileNameWithoutExtension(iconFileName);
            string key = string.IsNullOrEmpty(keyPrefix) ? baseName : keyPrefix + baseName;
            return LoadTextureOrNull(logTag, key);
        }
    }
}
