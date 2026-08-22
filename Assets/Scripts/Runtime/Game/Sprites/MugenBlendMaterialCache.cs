using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Erzeugt und teilt Materialien, die MUGENs Transparenzmodi (<c>add</c>,
    /// <c>add1</c>, <c>addalpha</c>, <c>sub</c>, <c>subadd</c>) nachbilden.
    /// Pro Blend-Kombination existiert genau ein Material.
    /// </summary>
    public sealed class MugenBlendMaterialCache
    {
        private const string ShaderName = "Riftstorm/MugenSpriteBlend";
        private static readonly int SrcAlphaId = Shader.PropertyToID("_SrcA");
        private static readonly int DstAlphaId = Shader.PropertyToID("_DstA");
        private static readonly int BlendOpId = Shader.PropertyToID("_AddOp");
        private static readonly int DstFactorId = Shader.PropertyToID("_DstFactor");

        // UnityEngine.Rendering.BlendOp: 0 = Add, 2 = ReverseSubtract.
        private const float BlendOpAdd = 0f;
        private const float BlendOpReverseSubtract = 2f;

        // UnityEngine.Rendering.BlendMode: 1 = One, 10 = OneMinusSrcAlpha.
        private const float BlendFactorOne = 1f;
        private const float BlendFactorOneMinusSrcAlpha = 10f;

        private readonly Dictionary<int, Material> m_Materials = new();

        /// <summary>
        /// Liefert das Material für einen Blend-Zustand. Auch der neutrale Fall
        /// bekommt eines, weil der Shader zusätzlich die PalFX-Farbmathematik
        /// trägt — ohne ihn liessen sich Farbfilter nicht anwenden.
        /// <c>null</c> nur, wenn der Shader fehlt.
        /// </summary>
        public Material Resolve(FlareEffectBlend blend)
        {
            int key = (blend.AlphaSrc << 9) | (blend.AlphaDst << 1) | (blend.IsSubtractive ? 1 : 0);
            if (m_Materials.TryGetValue(key, out Material cached))
            {
                return cached;
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[MugenBlendMaterialCache] Shader '{ShaderName}' fehlt — " +
                    "Effekte werden ohne MUGEN-Blending gezeichnet.");
                m_Materials[key] = null;
                return null;
            }

            Material material = new(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat(SrcAlphaId, blend.AlphaSrc / 255f);
            material.SetFloat(DstAlphaId, blend.AlphaDst / 255f);
            material.SetFloat(BlendOpId, blend.IsSubtractive ? BlendOpReverseSubtract : BlendOpAdd);
            material.SetFloat(
                DstFactorId,
                blend.IsSubtractive ? BlendFactorOne : BlendFactorOneMinusSrcAlpha);
            m_Materials[key] = material;
            return material;
        }

        /// <summary>Zerstört alle erzeugten Materialien.</summary>
        public void Clear()
        {
            foreach (Material material in m_Materials.Values)
            {
                if (material != null)
                {
                    Object.Destroy(material);
                }
            }
            m_Materials.Clear();
        }
    }
}
