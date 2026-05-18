using System;
using UnityEngine;

namespace Riftstorm.Management.SoundManagement
{
    /// <summary>
    /// Eintrag im <see cref="SoundRegistry"/>: identifiziert eine Audio-Datei per
    /// Key + absolutem Dateipfad und haelt den geladenen <see cref="AudioClip"/>
    /// (kann initial null sein – Lazy-Load via <see cref="LazyAudioLoader"/>).
    /// </summary>
    public sealed class SoundData
    {
        /// <summary>Lookup-Schluessel (Dateiname inkl. Extension, z. B. <c>"skill_heal.wav"</c>).</summary>
        public string Id { get; }

        /// <summary>Absoluter Pfad zur Audio-Datei auf der Platte.</summary>
        public string FilePath { get; }

        /// <summary>Geladener AudioClip oder <c>null</c> wenn noch nicht angefragt.</summary>
        public AudioClip Clip { get; internal set; }

        /// <summary>Quelle (System/Custom).</summary>
        public SoundSource Source { get; }

        /// <summary>Zeitpunkt der Indexierung (UTC).</summary>
        public DateTime LoadedAt { get; }

        /// <summary>Konstruktor mit Validierung.</summary>
        public SoundData(string id, string filePath, AudioClip clip, SoundSource source)
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
            Clip = clip;
            Source = source;
            LoadedAt = DateTime.UtcNow;
        }

        /// <summary>True wenn die Id nicht leer ist.</summary>
        public bool IsValid() => !string.IsNullOrEmpty(Id);

        /// <summary>True wenn der AudioClip bereits geladen ist.</summary>
        public bool HasClip() => Clip != null;

        /// <summary>Zerstoert den hinterlegten AudioClip und nullt das Feld.</summary>
        public void UnloadClip()
        {
            if (Clip != null)
            {
                UnityEngine.Object.Destroy(Clip);
                Clip = null;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"SoundData({Id}, {FilePath}, {Source})";
    }

    /// <summary>Factory fuer <see cref="SoundData"/> mit Validierung.</summary>
    public static class SoundDataFactory
    {
        /// <summary>Erzeugt einen neuen <see cref="SoundData"/>-Eintrag.</summary>
        public static SoundData Create(string id, string filePath, AudioClip clip, SoundSource source)
        {
            return new(id, filePath, clip, source);
        }
    }
}
