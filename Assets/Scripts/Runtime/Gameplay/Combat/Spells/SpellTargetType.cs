namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Zielwahl-Schema eines einzelnen Spell-Effects. Der Spell-Caster
    /// löst hieraus die Liste der tatsächlich getroffenen Einheiten.
    /// </summary>
    /// <remarks>
    /// Vereinfacht aus <c>SpellDefines.h::TargetType</c>. <c>AreaSrc</c> = um den
    /// Caster, <c>AreaDst</c> = um den gewählten Zielpunkt / das gewählte Ziel.
    /// </remarks>
    public enum SpellTargetType
    {
        /// <summary>Kein Ziel (z. B. passive Effekte).</summary>
        None = 0,

        /// <summary>Caster selbst.</summary>
        SelfCaster = 1,

        /// <summary>Ein einzelnes verbündetes Unit.</summary>
        FriendlyUnit = 10,
        /// <summary>Ein einzelnes feindliches Unit.</summary>
        HostileUnit = 11,
        /// <summary>Beliebiges Unit (egal ob friendly/hostile).</summary>
        AnyUnit = 12,

        /// <summary>Alle Verbündeten in Radius um den Caster.</summary>
        AreaSrcFriendly = 20,
        /// <summary>Alle Verbündeten in Radius um das Ziel / den Zielpunkt.</summary>
        AreaDstFriendly = 21,
        /// <summary>Alle Feinde in Radius um den Caster.</summary>
        AreaSrcHostile = 22,
        /// <summary>Alle Feinde in Radius um das Ziel / den Zielpunkt.</summary>
        AreaDstHostile = 23,

        /// <summary>Bodenpunkt (Spieler klickt eine Stelle).</summary>
        GroundPoint = 30,
        /// <summary>Game-Object (Truhe, Tür, Banner).</summary>
        GameObject = 31,
    }
}
