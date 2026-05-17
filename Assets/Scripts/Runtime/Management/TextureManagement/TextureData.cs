using System;
using UnityEngine;

namespace Riftstorm.Management.TextureManagement
{
    /// <summary>
    /// Eintrag im <see cref="TextureRegistry"/>: identifiziert eine Bilddatei per
    /// Key + absolutem Dateipfad und haelt die geladene <see cref="Texture2D"/>
    /// (kann initial null sein – Lazy-Load via <see cref="LazyTextureLoader"/>).
    /// </summary>
    public sealed class TextureData
    {
        public string Id { get; }
        public string FilePath { get; }
        public Texture2D Texture { get; internal set; }
        public TextureSource Source { get; }
        public DateTime LoadedAt { get; }

        public TextureData(string id, string filePath, Texture2D texture, TextureSource source)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Id cannot be empty", nameof(id));
            }
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("FilePath cannot be empty", nameof(filePath));
            }
            Id = id;
            FilePath = filePath;
            Texture = texture;
            Source = source;
            LoadedAt = DateTime.UtcNow;
        }

        public bool IsValid() => !string.IsNullOrEmpty(Id);

        public bool HasTexture() => Texture != null;

        /// <summary>Zerstoert die hinterlegte Texture2D und nullt das Feld.</summary>
        public void UnloadTexture()
        {
            if (Texture != null)
            {
                UnityEngine.Object.Destroy(Texture);
                Texture = null;
            }
        }

        public override string ToString() => $"TextureData({Id}, {FilePath}, {Source})";
    }

    /// <summary>
    /// Factory fuer <see cref="TextureData"/> mit Validierung. Ohne Material-Feld —
    /// Riftstorm baut keine Materialien aus Skin-Definitionen.
    /// </summary>
    public static class TextureDataFactory
    {
        public static TextureData Create(string id, string filePath, Texture2D texture, TextureSource source)
        {
            return new(id, filePath, texture, source);
        }
    }
}
