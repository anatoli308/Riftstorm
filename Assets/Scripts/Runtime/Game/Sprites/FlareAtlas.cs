using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Eine zur Laufzeit aufbereitete Animation: <see cref="Sprite"/>s pro Frame und 8 Richtungen.
    /// </summary>
    public sealed class FlareAnimation
    {
        /// <summary>Name der Animation (z. B. "stance", "run").</summary>
        public string Name { get; }
        /// <summary>Wiedergabetyp.</summary>
        public FlareAnimationType Type { get; }
        /// <summary>Gesamtdauer der Animation in Sekunden.</summary>
        public float DurationSeconds { get; }
        /// <summary>Frame-Anzahl der Animation.</summary>
        public int FramesCount { get; }

        /// <summary>
        /// Sprites indiziert über [frame][direction 0..7]. Direction-Reihenfolge nach FLARE-Konvention
        /// (siehe <see cref="FlareDirection.FromVector"/>).
        /// </summary>
        public Sprite[][] Sprites { get; }

        /// <summary>Erzeugt eine bereits aufbereitete Animation.</summary>
        public FlareAnimation(string name, FlareAnimationType type, float durationSeconds, Sprite[][] sprites)
        {
            Name = name;
            Type = type;
            DurationSeconds = durationSeconds;
            FramesCount = sprites?.Length ?? 0;
            Sprites = sprites;
        }
    }

    /// <summary>
    /// Aufbereiteter Atlas einer Sprite-Schicht: Textur + benannte Animationen.
    /// </summary>
    public sealed class FlareAtlas
    {
        /// <summary>Logischer Atlasname (Dateiname ohne Endung).</summary>
        public string Name { get; }
        /// <summary>Zugrundeliegende Atlas-Textur (kann <c>null</c> sein, wenn PNG fehlt).</summary>
        public Texture2D Texture { get; }
        /// <summary>Animationen nach Name.</summary>
        public IReadOnlyDictionary<string, FlareAnimation> Animations { get; }

        /// <summary>Erzeugt einen Atlas-Eintrag.</summary>
        public FlareAtlas(string name, Texture2D texture, IReadOnlyDictionary<string, FlareAnimation> animations)
        {
            Name = name;
            Texture = texture;
            Animations = animations;
        }

        /// <summary>
        /// Versucht, eine Animation per Name aufzulösen.
        /// </summary>
        public bool TryGet(string animationName, out FlareAnimation animation)
        {
            if (animationName != null && Animations != null)
            {
                return Animations.TryGetValue(animationName, out animation);
            }
            animation = null;
            return false;
        }
    }
}
