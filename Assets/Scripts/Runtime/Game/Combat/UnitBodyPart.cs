// <summary>
//     Riftstorm - Game - Combat - UnitBodyPart.cs
// kommt auf UnitDefines.h: Z. 120–134, damit JSON-Templates ohne Mapping konsumiert werden koennen.
 // TODO: muss noch entsprechend verwendet werden uns halt überall eingesetzt usw wie im original soruce-server.
// </summary>
namespace Riftstorm.Game.Combat
{
    public enum UnitBodyPart
    {
        Feet = 0,
        Legs = 1,
        Torso = 2,
        Hands = 3,
        Head = 4,
        Weapon = 5,
        Offhand = 6,
        WeaponRanged = 7
    };
}