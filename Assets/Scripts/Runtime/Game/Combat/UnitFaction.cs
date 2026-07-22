using Riftstorm.Game.Combat;
using Riftstorm.Game.Input;
using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    public enum UnitFaction
    {
        PlayerDefault = 0,  // Default player faction (friendly to other players)
        Friendly = 1,       // Friendly NPCs (quest givers, vendors)
        Neutral = 2,        // Neutral creatures (attackable but don't aggro)
        Hostile = 3,        // Hostile mobs (will attack on sight)
        PvP = 4
    };
}