using System;
using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Statischer Per-Process-Cache, der Frame-<see cref="Sprite"/>s on-demand
    /// aus einer injizierten Texture-Resolver-Funktion aufbaut. Schlüssel ist
    /// <c>"&lt;animName&gt;#&lt;frameIndex&gt;"</c>; die Sprites werden mit Pivot
    /// (0.5, 0.5) und PPU aus <c>canvas_size</c> erzeugt, damit Skalierung über
    /// das <c>scale</c>-Feld der <see cref="SpellAnimationDefinition"/> auf
    /// jeder Animation gleich wirkt.
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
            Sprite sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
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
