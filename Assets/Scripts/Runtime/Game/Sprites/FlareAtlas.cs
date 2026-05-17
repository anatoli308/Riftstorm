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
        /// Per-Frame-Dauer in Sekunden (Index = Frame). <c>null</c>, wenn die
        /// Atlas-JSON keinen <c>frame_durations_ms</c>-Block enthält &#8212; der
        /// Animator verteilt dann <see cref="DurationSeconds"/> gleichmäßig über alle Frames.
        /// Wenn vorhanden, ist die Summe identisch zu <see cref="DurationSeconds"/>.
        /// </summary>
        public float[] FrameDurations { get; }

        /// <summary>
        /// Sprites indiziert über [frame][direction 0..7]. Direction-Reihenfolge nach FLARE-Konvention
        /// (siehe <see cref="FlareDirection.FromVector"/>).
        /// </summary>
        public Sprite[][] Sprites { get; }

        /// <summary>
        /// Rohzellen pro [frame][direction] aus dem JSON-Atlas, inklusive optionaler
        /// <see cref="FlareCell.AttackBoxes"/> / <see cref="FlareCell.HurtBoxes"/> in
        /// MUGEN-Pixelkoordinaten relativ zum Charakter-Anker. Wird vom Combat-System
        /// (<c>MugenHitboxRuntime</c>) gelesen, um pro Frame echte Treffer-Volumen zu bauen.
        /// Sprites werden aus diesen Zellen abgeleitet — die Zellen bleiben dauerhaft
        /// erhalten und sind dieselben Referenzen wie im deserialisierten <see cref="FlareAtlasDef"/>.
        /// </summary>
        public FlareCell[][] Cells { get; }

        /// <summary>
        /// Horizontale Spiegelung pro [frame][direction]. Wird vom Importer für W/NW/SW gesetzt,
        /// damit der seitenansichts-MUGEN-Charakter nach links blickt. <c>null</c>, wenn keine Zelle
        /// dieser Animation eine Spiegelung benötigt (häufiger Fall).
        /// </summary>
        public bool[][] FlipH { get; }

        /// <summary>
        /// Vertikale Spiegelung pro [frame][direction]. Aktuell unbenutzt vom Importer.
        /// <c>null</c>, wenn keine Zelle gespiegelt ist.
        /// </summary>
        public bool[][] FlipV { get; }

        /// <summary>Erzeugt eine bereits aufbereitete Animation.</summary>
        public FlareAnimation(string name, FlareAnimationType type, float durationSeconds, Sprite[][] sprites, FlareCell[][] cells, bool[][] flipH, bool[][] flipV, float[] frameDurations)
        {
            Name = name;
            Type = type;
            DurationSeconds = durationSeconds;
            FramesCount = sprites?.Length ?? 0;
            Sprites = sprites;
            Cells = cells;
            FlipH = flipH;
            FlipV = flipV;
            FrameDurations = frameDurations;
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
