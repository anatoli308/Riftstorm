namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Defines the different animation states a unit can be in during combat.
    /// TODO: implementieren und in der Animation-Controller-Logik verwenden.
    /// </summary>
    public enum UnitAnimation
    {
        Stance = 0,
        Run = 1,
        Attack = 2,
        Cast = 3,
        Hit = 4,
        Die = 5,
        CastAlt = 6,    // Alternative cast animation
        Spawn = 7,      // Spawn/appear animation
        CritDie = 8,    // Critical death animation
        Swing = 9,      // Melee swing animation
        Block = 10,     // Block animation
        Shoot = 11,     // Ranged attack animation
    };
}