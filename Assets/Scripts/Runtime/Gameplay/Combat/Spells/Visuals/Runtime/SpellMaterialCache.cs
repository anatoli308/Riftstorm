using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Statischer Material-Cache fuer Spell-Sprite-Blend-Modi. Erzeugt pro
    /// <see cref="SpellVisualBlend"/> genau eine Material-Instanz (lazy) und
    /// gibt sie an alle Spawner zurueck. Damit teilen sich tausende
    /// gleichzeitige Spell-Visuals dasselbe Material &#8212; wichtig fuer
    /// SRP-Batching und Draw-Call-Budget bei Horde-Combat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SpellVisualBlend.Default"/> liefert <c>null</c>; der
    /// <see cref="SpriteRenderer"/> faellt dann auf sein Standard-Material
    /// (<c>Sprites/Default</c>, Alpha-Blend) zurueck.
    /// </para>
    /// <para>
    /// <see cref="SpellVisualBlend.Additive"/> nutzt
    /// <c>Riftstorm/SpellSpriteAdditive</c> aus
    /// <c>Assets/Art/Shaders/SpellSpriteAdditive.shader</c>. Fehlt der Shader
    /// (z. B. ausgeschlossen aus dem Build), wird einmalig gewarnt und ein
    /// Fallback-Material auf <c>Sprites/Default</c> verwendet.
    /// </para>
    /// </remarks>
    public static class SpellMaterialCache
    {
        private const string k_AdditiveShader = "Riftstorm/SpellSpriteAdditive";
        private const string k_FallbackShader = "Sprites/Default";

        private static Material s_Additive;
        private static bool s_AdditiveWarned;

        /// <summary>Liefert das Material fuer <paramref name="blend"/>; <c>null</c> bedeutet "SpriteRenderer-Default verwenden".</summary>
        public static Material Get(SpellVisualBlend blend)
        {
            switch (blend)
            {
                case SpellVisualBlend.Additive:
                    return GetAdditive();
                case SpellVisualBlend.Default:
                default:
                    return null;
            }
        }

        private static Material GetAdditive()
        {
            if (s_Additive != null) { return s_Additive; }

            Shader shader = Shader.Find(k_AdditiveShader);
            if (shader == null)
            {
                if (!s_AdditiveWarned)
                {
                    Debug.LogWarning(
                        $"[SpellMaterialCache] Shader '{k_AdditiveShader}' not found. "
                        + "Falling back to default sprite blending. "
                        + "Add the shader to the Always Included Shaders list to ship it.");
                    s_AdditiveWarned = true;
                }
                shader = Shader.Find(k_FallbackShader);
                if (shader == null) { return null; }
            }

            s_Additive = new Material(shader) { name = "SpellSpriteAdditive (cached)" };
            return s_Additive;
        }
    }
}
