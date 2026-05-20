using System;
using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Statischer Per-Process-Cache, der Frame-<see cref="Sprite"/>s on-demand
    /// aus einer injizierten Texture-Resolver-Funktion aufbaut. Schlüssel ist
    /// <c>"&lt;animName&gt;#&lt;frameIndex&gt;"</c>; die Sprites werden mit
    /// einem pro Frame berechneten Pivot (auf das Canvas-Zentrum der
    /// FLARE-<c>.sa</c>-Quelle ausgerichtet) und PPU aus <c>canvas_size</c>
    /// erzeugt, damit Skalierung über das <c>scale</c>-Feld der
    /// <see cref="SpellAnimationDefinition"/> auf jeder Animation gleich wirkt
    /// und alle Frames um den gleichen Welt-Anker spielen.
    /// </summary>
    /// <remarks>
    /// Der Resolver wird von außen gesetzt (typisch im <c>ApplicationEntryPoint</c>,
    /// der den <c>TextureManager</c> kennt), damit das Gameplay-Assembly nicht
    /// direkt vom Management-/ApplicationLifecycle-Assembly abhängt
    /// (Vermeidung von Asmdef-Zyklen).
    /// </remarks>
    public static class SpellSpriteCache
    {
        private static readonly Dictionary<string, Sprite> s_Sprites = new();

        /// <summary>
        /// Auflöser von Texture-Key (z. B. <c>"spells/fire_001_0"</c>) zu
        /// <see cref="Texture2D"/>. Muss vor dem ersten <see cref="GetSprite"/>-
        /// Aufruf gesetzt sein, sonst liefert der Cache <c>null</c>.
        /// </summary>
        public static Func<string, Texture2D> TextureResolver { get; set; }

        /// <summary>
        /// Liefert (oder baut) den Sprite für <paramref name="frameIndex"/> in
        /// <paramref name="anim"/>. Liefert <c>null</c>, wenn die Textur nicht
        /// aufgelöst werden kann.
        /// </summary>
        public static Sprite GetSprite(SpellAnimationDefinition anim, int frameIndex)
        {
            if (anim == null || string.IsNullOrEmpty(anim.ImagePattern))
            {
                return null;
            }
            if (frameIndex < 0 || frameIndex >= Mathf.Max(anim.FramesCount, 1))
            {
                return null;
            }

            string cacheKey = anim.Name + "#" + frameIndex;
            if (s_Sprites.TryGetValue(cacheKey, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Func<string, Texture2D> resolver = TextureResolver;
            if (resolver == null)
            {
                return null;
            }

            string textureKey = BuildTextureKey(anim.ImagePattern, frameIndex);
            Texture2D tex = resolver(textureKey);
            if (tex == null)
            {
                return null;
            }

            float ppu = anim.CanvasSize > 0 ? anim.CanvasSize : 100f;

            // Pivot pro Frame: PNGs sind tight-cropped, das FLARE-<c>.sa</c>
            // liefert pro Frame nur den oberen-linken Blit-Offset (X, Y) in
            // Source-Pixeln innerhalb einer (canvas_size / scale)-grossen
            // Canvas-Box. Damit alle Frames um den gleichen <em>Welt</em>-
            // Anker (Canvas-Zentrum) rotieren/animieren, wird der Pivot so
            // gewaehlt, dass die Position (anchorX, anchorY) im Source-
            // Koordinatensystem stets auf den gleichen Welt-Punkt faellt.
            // Unity-Sprite-Pivot liegt im Normalbereich (0..1) gemessen
            // vom unteren-linken Texture-Eck; daher die Y-Inversion.
            Vector2 pivot = new(0.5f, 0.5f);
            if (anim.Frames != null && frameIndex >= 0 && frameIndex < anim.Frames.Count
                && anim.Frames[frameIndex] != null
                && tex.width > 0 && tex.height > 0)
            {
                SpellAnimationFrame fr = anim.Frames[frameIndex];
                float sourceSize = anim.Scale > 0f
                    ? anim.CanvasSize / anim.Scale
                    : anim.CanvasSize;
                if (sourceSize > 0f)
                {
                    float anchor = sourceSize * 0.5f;
                    pivot = new Vector2(
                        (anchor - fr.X) / tex.width,
                        1f - (anchor - fr.Y) / tex.height);
                }
            }

            Sprite sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                pivot,
                ppu);
            sprite.name = cacheKey;
            s_Sprites[cacheKey] = sprite;
            return sprite;
        }

        /// <summary>
        /// Verwirft alle gecachten Sprites. Texturen selbst gehören dem
        /// externen Manager und werden nicht zerstört.
        /// </summary>
        public static void ClearCache()
        {
            foreach (Sprite s in s_Sprites.Values)
            {
                if (s != null)
                {
                    UnityEngine.Object.Destroy(s);
                }
            }
            s_Sprites.Clear();
        }

        /// <summary>
        /// Wandelt <c>"spells/fire_001_{frame}.png"</c> in den Texture-Key
        /// <c>"spells/fire_001_0"</c> um (Extension strip + Index-Substitution).
        /// </summary>
        private static string BuildTextureKey(string imagePattern, int frameIndex)
        {
            string path = imagePattern.Replace("{frame}", frameIndex.ToString());
            int dot = path.LastIndexOf('.');
            if (dot > 0)
            {
                path = path.Substring(0, dot);
            }
            return path;
        }
    }
}
