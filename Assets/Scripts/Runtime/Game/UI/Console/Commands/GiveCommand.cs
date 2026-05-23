using System.Globalization;
using Riftstorm.Game.Items;
using Unity.Netcode;

namespace Riftstorm.Game.UI.Console.Commands
{
    /// <summary>
    /// <c>/give &lt;entry|model&gt; [count]</c> — legt das angeforderte Item
    /// server-autoritativ in das eigene Inventar des aufrufenden Spielers.
    /// <paramref name="entry"/> kann entweder eine Template-Id aus
    /// <c>StreamingAssets/items/_templates.json</c> sein (z. B. <c>6701</c>)
    /// oder ein Model-Name (z. B. <c>longbow</c>); im zweiten Fall waehlt
    /// <see cref="ItemCatalogLoader.TryGetFirstEntryByModel"/> den
    /// level-niedrigsten Treffer.
    /// </summary>
    /// <remarks>
    /// Keine Source-Parity: Riftstorm-Dev-Tool. Owner-Client liest Args, der
    /// Server validiert das Template und schreibt die <c>NetworkList</c> in
    /// <see cref="PlayerInventory"/>.
    /// </remarks>
    public sealed class GiveCommand : IConsoleCommand
    {
        /// <inheritdoc/>
        public string Name => "give";

        /// <inheritdoc/>
        public string Usage => "/give <entry|model> [count]  (e.g. /give longbow 1 or /give 6701)";

        /// <inheritdoc/>
        public void Execute(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                ConsoleLog.Add(Usage, ConsoleChannel.System);
                return;
            }

            string token = args[0].Trim();
            int count = 1;
            if (args.Length >= 2 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount) && parsedCount > 0)
            {
                count = parsedCount;
            }

            int entry;
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedEntry) && parsedEntry > 0)
            {
                entry = parsedEntry;
            }
            else if (!ItemCatalogLoader.TryGetFirstEntryByModel(token, out entry))
            {
                ConsoleLog.Add($"/give: kein Item mit Entry/Model '{token}'.", ConsoleChannel.Error);
                return;
            }

            if (!ItemCatalogLoader.TryGetTemplate(entry, out var template) || template == null)
            {
                ConsoleLog.Add($"/give: Template {entry} fehlt im Katalog.", ConsoleChannel.Error);
                return;
            }

            PlayerInventory inv = ResolveLocalInventory();
            if (inv == null)
            {
                ConsoleLog.Add("/give: local player not spawned yet (or PlayerInventory fehlt am Prefab).", ConsoleChannel.Error);
                return;
            }

            inv.RequestGiveServerRpc(entry, count);
            ConsoleLog.Add($"/give: requested {count}x #{entry} \"{template.Name}\" ({template.Model}).", ConsoleChannel.System);
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
