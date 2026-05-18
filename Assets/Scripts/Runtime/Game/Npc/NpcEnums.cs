using System;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Kanonischer Riftstorm-NPC-Typ. 1:1 aus
    /// <c>source_server/Shared/NpcDefines.h</c> (enum <c>NpcType</c>).
    /// Wird per <see cref="NpcTemplate.Type"/> direkt aus
    /// <c>StreamingAssets/npc/_templates.json</c> deserialisiert
    /// (Newtonsoft mappt int -&gt; enum-Wert).
    /// </summary>
    public enum RiftstormNpcType
    {
        /// <summary>Kein Typ / nicht klassifiziert (DB-Sentinel 0).</summary>
        None = 0,
        Beast = 1,
        Humanoid = 2,
        Undead = 3,
        Demon = 4,
        Elemental = 5,
        Dragon = 6,
        Giant = 7,
        Mechanical = 8,
        Aberration = 9,
    }

    /// <summary>
    /// Bewegungs-Default eines NPC-Spawn-Eintrags
    /// (<c>npc.movement_type</c>). 1:1 aus
    /// <c>source_server/Shared/NpcDefines.h</c> (enum <c>NpcDefaultMovement</c>).
    /// </summary>
    public enum NpcDefaultMovement
    {
        /// <summary>Steht still (idle). Talker/Quest-Giver/Vendor.</summary>
        None = 0,
        /// <summary>Wandert innerhalb <c>wander_distance</c> um den Spawn-Punkt.</summary>
        Random = 1,
        /// <summary>Folgt einer Waypoint-Route (<c>path_id</c>).</summary>
        Patrol = 2,
    }

    /// <summary>
    /// Bitmaske der NPC-Flags aus <c>npc_template.npc_flags</c>. 1:1 aus
    /// <c>source_server/Shared/NpcDefines.h</c> (enum <c>NpcFlags</c>).
    /// Bestimmt, welche Interaktionen ein NPC anbietet (Gossip, Vendor, ...).
    /// </summary>
    [Flags]
    public enum NpcFlagsMask
    {
        /// <summary>Keine Interaktion (reiner Mob).</summary>
        None = 0,
        Gossip = 0x0001,
        QuestGiver = 0x0002,
        Vendor = 0x0004,
        Trainer = 0x0008,
        Innkeeper = 0x0010,
        FlightMaster = 0x0020,
        Banker = 0x0040,
        Repair = 0x0080,
        Mailbox = 0x0100,
        SpendExpCredit = 0x0200,
        LevelToCredit = 0x0400,
        TalkCredit = 0x0800,
    }
}
