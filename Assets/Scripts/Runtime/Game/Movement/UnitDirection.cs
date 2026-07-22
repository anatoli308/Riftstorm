using Riftstorm.Game.Combat;
using Riftstorm.Game.Input;
using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Movement
{
    /// <summary>
    /// 8-direction system for unit facing/movement
    /// Ordered clockwise for sprite sheet mapping
    /// </summary>
    /// TODO: implementieren und korrekte Richtung Indexe für die 8 Richtungen verwenden
    public enum UnitDirection
    {
        South = 0,
        SouthWest = 1,
        West = 2,
        NorthWest = 3,
        North = 4,
        NorthEast = 5,
        East = 6,
        SouthEast = 7
    };
}