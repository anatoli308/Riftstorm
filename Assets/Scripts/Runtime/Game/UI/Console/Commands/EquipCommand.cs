using Riftstorm.Game.Items;
using Unity.Netcode;

namespace Riftstorm.Game.UI.Console.Commands
{
    /// <summary>
    /// <c>/equip &lt;inventorySlotIndex&gt;</c> — schickt eine ServerRpc an
    /// <see cref="PlayerEquipment"/>, um das Item aus dem angegebenen
    /// Inventory-Slot in den passenden Equip-Slot zu legen. Slot-Wahl und
    /// Move-Semantik (Swap, Zweihaender-Offhand-Clear) entscheidet der Server
    /// anhand des Item-Templates.
    /// </summary>
    /// <remarks>
    /// Riftstorm-Dev-Tool. Keine Source-Parity — der Workflow setzt
    /// <c>/inventory</c> zur Slot-Anzeige voraus.
    /// </remarks>
    public sealed class EquipCommand : IConsoleCommand
    {
        /// <inheritdoc/>
        public string Name => "equip";

        /// <inheritdoc/>
        public string Usage => "/equip <inventorySlot>  (e.g. /equip 0)";

        /// <inheritdoc/>
        public void Execute(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                ConsoleLog.Add(Usage, ConsoleChannel.System);
                return;
            }

            if (!int.TryParse(args[0], out int slotIndex) || slotIndex < 0 || slotIndex >= PlayerInventory.Capacity)
            {
                ConsoleLog.Add($"/equip: slot must be an integer in [0,{PlayerInventory.Capacity - 1}].", ConsoleChannel.Error);
                return;
            }

            PlayerEquipment eq = ResolveLocalEquipment();
            if (eq == null)
            {
                ConsoleLog.Add("/equip: local player not spawned yet (or PlayerEquipment fehlt am Prefab).", ConsoleChannel.Error);
                return;
            }

            eq.RequestEquipFromInventoryServerRpc(slotIndex);
            ConsoleLog.Add($"Equip requested from inventory slot {slotIndex}.", ConsoleChannel.System);
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
