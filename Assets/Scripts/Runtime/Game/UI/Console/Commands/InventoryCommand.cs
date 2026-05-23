using System.Text;
using Riftstorm.Game.Items;
using Unity.Netcode;

namespace Riftstorm.Game.UI.Console.Commands
{
    /// <summary>
    /// <c>/inventory</c> — listet alle nicht-leeren Slots des lokalen
    /// Spieler-Inventars im Console-Backlog auf. Daten werden direkt aus der
    /// replizierten <c>NetworkList</c> auf <see cref="PlayerInventory"/>
    /// gelesen (read-only; keine ServerRpc).
    /// </summary>
    /// <remarks>
    /// Keine Source-Parity: Riftstorm-Dev-Tool. Wird kein Argument geparst.
    /// </remarks>
    public sealed class InventoryCommand : IConsoleCommand
    {
        /// <inheritdoc/>
        public string Name => "inventory";

        /// <inheritdoc/>
        public string Usage => "/inventory  (listet alle belegten Slots)";

        /// <inheritdoc/>
        public void Execute(string[] args)
        {
            PlayerInventory inv = ResolveLocalInventory();
            if (inv == null)
            {
                ConsoleLog.Add("/inventory: local player not spawned yet (or PlayerInventory fehlt am Prefab).", ConsoleChannel.Error);
                return;
            }

            int used = 0;
            StringBuilder sb = new();
            sb.Append("Inventory (").Append(PlayerInventory.Capacity).Append(" slots):");

            for (int i = 0; i < inv.Count; i++)
            {
                InventoryItem slot = inv.GetSlot(i);
                if (slot.IsEmpty)
                {
                    continue;
                }
                used++;
                ItemCatalogLoader.TryGetTemplate(slot.TemplateId, out var t);
                string name = t != null ? t.Name : "<unknown>";
                string model = t != null ? t.Model : "";
                sb.Append("\n  [").Append(i).Append("] #").Append(slot.TemplateId)
                  .Append(" x").Append(slot.Count)
                  .Append(" — ").Append(name);
                if (!string.IsNullOrEmpty(model) && model != "0")
                {
                    sb.Append(" (").Append(model).Append(')');
                }
            }

            if (used == 0)
            {
                sb.Append("\n  <empty>");
            }
            else
            {
                sb.Append("\n  ").Append(used).Append('/').Append(PlayerInventory.Capacity).Append(" slots used.");
            }
            ConsoleLog.Add(sb.ToString(), ConsoleChannel.System);
        }

        private static PlayerInventory ResolveLocalInventory()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient || nm.LocalClient == null)
            {
                return null;
            }
            NetworkObject po = nm.LocalClient.PlayerObject;
            return po != null ? po.GetComponent<PlayerInventory>() : null;
        }
    }
}
