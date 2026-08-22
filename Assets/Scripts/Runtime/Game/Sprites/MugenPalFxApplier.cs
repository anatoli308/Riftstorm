using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Schreibt einen MUGEN-Farbfilter über einen <see cref="MaterialPropertyBlock"/>
    /// auf Sprite-Renderer. Das geteilte Blend-Material bleibt dabei unangetastet,
    /// sodass jeder Renderer seinen eigenen Filter tragen kann.
    /// </summary>
    /// <remarks>
    /// Rechnet die MUGEN-Skalen in die Shader-Skalen um, wie Ikemens
    /// <c>PalFX.getFinalPalFx</c>: <c>add / 255</c>, <c>mul / 256</c>,
    /// <c>gray = 1 - color</c>.
    /// </remarks>
    public sealed class MugenPalFxApplier
    {
        private const float AddScale = 255f;
        private const float MulScale = 256f;

        private static readonly int AddId = Shader.PropertyToID("_PalAdd");
        private static readonly int MulId = Shader.PropertyToID("_PalMul");
        private static readonly int GrayId = Shader.PropertyToID("_PalGray");
        private static readonly int InvertId = Shader.PropertyToID("_PalInvert");

        private readonly MaterialPropertyBlock m_Block = new();

        /// <summary>Setzt den Filter auf einen Renderer.</summary>
        /// <param name="renderer">Ziel-Renderer; <c>null</c> wird ignoriert.</param>
        /// <param name="palFx">Der wirkende Filter.</param>
        /// <param name="elapsedTicks">Ticks seit Filterstart, für die Sinus-Phase.</param>
        public void Apply(SpriteRenderer renderer, FlareSequencePalFx palFx, float elapsedTicks)
        {
            if (renderer == null)
            {
                return;
            }
            Vector3 add = palFx.Add + ResolveSinAdd(palFx, elapsedTicks);
            renderer.GetPropertyBlock(m_Block);
            m_Block.SetVector(AddId, new Vector4(add.x / AddScale, add.y / AddScale, add.z / AddScale, 0f));
            m_Block.SetVector(MulId, new Vector4(
                palFx.Mul.x / MulScale, palFx.Mul.y / MulScale, palFx.Mul.z / MulScale, 0f));
            m_Block.SetFloat(GrayId, Mathf.Clamp01(1f - palFx.Color / MulScale));
            m_Block.SetFloat(InvertId, palFx.InvertAll ? 1f : 0f);
            renderer.SetPropertyBlock(m_Block);
        }

        /// <summary>Stellt den neutralen Zustand wieder her.</summary>
        public void Clear(SpriteRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }
            renderer.GetPropertyBlock(m_Block);
            m_Block.SetVector(AddId, Vector4.zero);
            m_Block.SetVector(MulId, new Vector4(1f, 1f, 1f, 0f));
            m_Block.SetFloat(GrayId, 0f);
            m_Block.SetFloat(InvertId, 0f);
            renderer.SetPropertyBlock(m_Block);
        }

        /// <summary>
        /// Sinusförmiger Anteil der Farbaddition. MUGEN startet bei Periode 2 eine
        /// Viertelphase später, damit der Effekt sofort sichtbar ist.
        /// </summary>
        private static Vector3 ResolveSinAdd(FlareSequencePalFx palFx, float elapsedTicks)
        {
            if (palFx.SinPeriod <= 1)
            {
                return Vector3.zero;
            }
            float phase = 2f * Mathf.PI * (elapsedTicks % palFx.SinPeriod) / palFx.SinPeriod;
            if (palFx.SinPeriod == 2)
            {
                phase += Mathf.PI * 0.5f;
            }
            return palFx.SinAdd * Mathf.Sin(phase);
        }
    }
}
