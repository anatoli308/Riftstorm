using System;
using Riftstorm.Game.Items;
using Unity.Netcode;

namespace Riftstorm.Game.UI.Console.Commands
{
    /// <summary>
    /// <c>/unequip &lt;slot&gt;</c> — schickt eine ServerRpc an
    /// <see cref="PlayerEquipment"/>, um das Item im angegebenen Equip-Slot
    /// zurueck ins Inventar zu legen. Slot kann als Name (<c>helm</c>,
    /// <c>weapon</c>, <c>shield</c>, ...) oder als Zahl (1..11) uebergeben werden.
    /// </summary>
    public sealed class UnequipCommand : IConsoleCommand
    {
        /// <inheritdoc/>
        public string Name => "unequip";

        /// <inheritdoc/>
        public string Usage => "/unequip <slot>  (e.g. /unequip weapon, /unequip 9)";

        /// <inheritdoc/>
        public void Execute(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                ConsoleLog.Add(Usage, ConsoleChannel.System);
                ConsoleLog.Add("Slots: helm, necklace, chest, belt, legs, feet, hands, ring, weapon, shield, ranged (1..11)", ConsoleChannel.System);
                return;
            }

            if (!TryParseEquipSlot(args[0], out EquipSlot slot))
            {
                ConsoleLog.Add($"/unequip: unknown slot '{args[0]}'.", ConsoleChannel.Error);
                ConsoleLog.Add("Slots: helm, necklace, chest, belt, legs, feet, hands, ring, weapon, shield, ranged (1..11)", ConsoleChannel.System);
                return;
            }

            PlayerEquipment eq = ResolveLocalEquipment();
            if (eq == null)
            {
                ConsoleLog.Add("/unequip: local player not spawned yet (or PlayerEquipment fehlt am Prefab).", ConsoleChannel.Error);
                return;
            }

            eq.RequestUnequipServerRpc(slot);
            ConsoleLog.Add($"Unequip requested: {slot}.", ConsoleChannel.System);
        }

        private static bool TryParseEquipSlot(string raw, out EquipSlot slot)
        {
            slot = EquipSlot.None;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }
            string trimmed = raw.Trim();

            // Numerischer Pfad: 1..11.
            if (int.TryParse(trimmed, out int numeric))
            {
                if (numeric < 1 || numeric > PlayerEquipment.SlotCount)
                {
                    return false;
                }
                slot = (EquipSlot)numeric;
                return true;
            }

            // Namens-Pfad: case-insensitive Enum-Parse, None verworfen.
            if (Enum.TryParse(trimmed, ignoreCase: true, out EquipSlot parsed) && parsed != EquipSlot.None)
            {
                slot = parsed;
                return true;
            }
            return false;
        }

        private static PlayerEquipment ResolveLocalEquipment()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient || nm.LocalClient == null)
            {
                return null;
            }
            NetworkObject po = nm.LocalClient.PlayerObject;
            return po != null ? po.GetComponent<PlayerEquipment>() : null;
        }
    }
}
