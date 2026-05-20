namespace Riftstorm.Gameplay.Combat.Spells.Visuals
{
    /// <summary>
    /// Blend-Modus eines Sprite-Layers eines Spell-Visuals. Mirror der
    /// FLARE-Source-Konstanten aus <c>spell_visual.spranim_blend</c> /
    /// <c>spranim2_blend</c>.
    /// </summary>
    /// <remarks>
    /// Annahme der Source-Mapping (Stand: Phase-2-Port):
    /// <list type="bullet">
    ///   <item><c>-1</c>, <c>0</c> &#8594; <see cref="Default"/> (Sprites/Default-Material, Alpha-Blend).</item>
    ///   <item><c>1</c>            &#8594; <see cref="Additive"/> (One/One, ueber <c>Riftstorm/SpellSpriteAdditive</c>-Shader).</item>
    /// </list>
    /// Weitere Modi (Multiply etc.) werden bei Bedarf ergaenzt.
    /// </remarks>
    public enum SpellVisualBlend
    {
        /// <summary>Standard-Alpha-Blending (Default-Sprite-Material).</summary>
        Default = 0,
        /// <summary>Additive Blending (One/One); typisch fuer Glows/Impacts.</summary>
        Additive = 1,
    }
}
