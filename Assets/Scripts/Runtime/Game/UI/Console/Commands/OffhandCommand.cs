using System.Text;
using Riftstorm.Game.Combat;
using Riftstorm.Gameplay.Combat;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Unity.Netcode;

namespace Riftstorm.Game.UI.Console.Commands
{
    /// <summary>
    /// <c>/offhand &lt;id|none&gt;</c> — wechselt die Offhand des lokalen Spielers.
    /// Die Id muss einem Eintrag aus <c>StreamingAssets/combat/offhand_items.json</c>
    /// entsprechen (z. B. <c>buckler</c>, <c>iron_buckler</c>, <c>shield</c>).
    /// Mit <c>none</c>, <c>clear</c> oder leerem Argument wird die Offhand
    /// abgelegt (Slot wird auf Server-Seite geleert, OffHand-FLARE-Layer auf den
    /// Clients via NetVar-OnValueChanged automatisch ausgeblendet).
    /// </summary>
    /// <remarks>
    /// Server lehnt das Equip ab, falls aktuell eine TwoHanded-Waffe gefuehrt
    /// wird. Validierung + NetVar-Write passieren autoritativ in
    /// <see cref="PlayerCombat.TryRequestEquipOffhand"/> -&gt;
    /// <c>RequestEquipOffhandServerRpc</c>.
    /// </remarks>
    public sealed class OffhandCommand : IConsoleCommand
    {
        private const int k_PreviewIdCount = 12;

        /// <inheritdoc/>
        public string Name => "offhand";

        /// <inheritdoc/>
        public string Usage => "/offhand <id|none>  (e.g. /offhand buckler)";

        /// <inheritdoc/>
        public void Execute(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                ConsoleLog.Add(Usage, ConsoleChannel.System);
                ConsoleLog.Add(BuildCatalogPreview(), ConsoleChannel.System);
                return;
            }

            string requestedId = args[0].Trim().ToLowerInvariant();
            bool isClear = string.IsNullOrEmpty(requestedId)
                           || requestedId == "none"
                           || requestedId == "clear";

            // Frueh-Validate auf dem Owner-Client (nur fuer echte Equip-Anfragen).
            if (!isClear)
            {
                OffhandCatalogLoader loader = ServiceLocator.Get<OffhandCatalogLoader>();
                OffhandCatalog catalog = loader?.GetCached();
                if (catalog != null && !catalog.TryGet(requestedId, out _))
                {
                    ConsoleLog.Add($"Unknown offhand id '{requestedId}'.", ConsoleChannel.Error);
                    ConsoleLog.Add(BuildCatalogPreview(), ConsoleChannel.System);
                    return;
                }
            }

            PlayerCombat combat = ResolveLocalPlayerCombat();
            if (combat == null)
            {
                ConsoleLog.Add("/offhand: local player not spawned yet.", ConsoleChannel.Error);
                return;
            }

            combat.TryRequestEquipOffhand(isClear ? string.Empty : requestedId);
            ConsoleLog.Add(isClear ? "Offhand cleared." : $"Offhand requested: {requestedId}", ConsoleChannel.System);
        }

        private static PlayerCombat ResolveLocalPlayerCombat()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient || nm.LocalClient == null)
            {
                return null;
            }
            NetworkObject po = nm.LocalClient.PlayerObject;
            return po != null ? po.GetComponent<PlayerCombat>() : null;
        }

        private static string BuildCatalogPreview()
        {
            OffhandCatalogLoader loader = ServiceLocator.Get<OffhandCatalogLoader>();
            OffhandCatalog catalog = loader?.GetCached();
            if (catalog == null || catalog.All == null || catalog.All.Count == 0)
            {
                return "Offhand catalog not loaded.";
            }

            StringBuilder sb = new();
            sb.Append("Available: none");
            int i = 0;
            foreach (OffhandDefinition o in catalog.All)
            {
                if (i >= k_PreviewIdCount)
                {
                    sb.Append(", ... (").Append(catalog.All.Count - k_PreviewIdCount).Append(" more)");
                    break;
                }
                sb.Append(", ").Append(o.Id);
                i++;
            }
            return sb.ToString();
        }
    }
}
