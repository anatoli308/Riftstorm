using System.Text;
using Riftstorm.Game.Combat;
using Riftstorm.Gameplay.Combat;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.UI.Console.Commands
{
    /// <summary>
    /// <c>/weapon &lt;id&gt;</c> — wechselt die Waffe des lokalen Spielers.
    /// Die Id muss einem Eintrag aus <c>StreamingAssets/combat/weapons.json</c>
    /// entsprechen (z.B. <c>shortsword</c>, <c>dagger</c>, <c>staff_grey</c>).
    /// Die <c>weapons.json</c>-Ids matchen 1:1 das <c>model</c>-Feld in
    /// <c>StreamingAssets/items/_templates.json</c> (Source-Itemtabelle).
    /// </summary>
    /// <remarks>
    /// Ohne Argument listet der Command die ersten verfuegbaren Ids aus dem
    /// Katalog. Die eigentliche Validierung + das Schreiben des
    /// <c>m_CurrentWeaponId</c>-NetworkVariable passiert autoritativ in
    /// <see cref="PlayerCombat.TryRequestEquipWeapon"/> ->
    /// <c>RequestEquipWeaponServerRpc</c>.
    /// </remarks>
    public sealed class WeaponCommand : IConsoleCommand
    {
        private const int k_PreviewIdCount = 12;

        /// <inheritdoc/>
        public string Name => "weapon";

        /// <inheritdoc/>
        public string Usage => "/weapon <id>  (e.g. /weapon shortsword)";

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
            if (string.IsNullOrEmpty(requestedId))
            {
                ConsoleLog.Add(Usage, ConsoleChannel.Error);
                return;
            }

            // Frueh-Validate auf dem Owner-Client: schlechte Eingaben muessen nicht
            // den ServerRpc bemuehen — sofortiges Feedback im Backlog.
            WeaponCatalogLoader loader = ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = loader?.GetCached();
            if (catalog != null && !catalog.TryGet(requestedId, out _))
            {
                ConsoleLog.Add($"Unknown weapon id '{requestedId}'.", ConsoleChannel.Error);
                ConsoleLog.Add(BuildCatalogPreview(), ConsoleChannel.System);
                return;
            }

            PlayerCombat combat = ResolveLocalPlayerCombat();
            if (combat == null)
            {
                ConsoleLog.Add("/weapon: local player not spawned yet.", ConsoleChannel.Error);
                return;
            }

            combat.TryRequestEquipWeapon(requestedId);
            ConsoleLog.Add($"Equip requested: {requestedId}", ConsoleChannel.System);
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
            WeaponCatalogLoader loader = ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = loader?.GetCached();
            if (catalog == null || catalog.All == null || catalog.All.Count == 0)
            {
                return "Weapon catalog not loaded.";
            }

            StringBuilder sb = new();
            sb.Append("Available: ");
            int i = 0;
            foreach (WeaponDefinition w in catalog.All)
            {
                if (i >= k_PreviewIdCount)
                {
                    sb.Append(", ... (").Append(catalog.All.Count - k_PreviewIdCount).Append(" more)");
                    break;
                }
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(w.Id);
                i++;
            }
            return sb.ToString();
        }
    }
}
