using Unity.Entities;
using Unity.NetCode;

namespace Riftstorm.Networking
{
    /// <summary>
    /// Bootstrap für Netcode for Entities Worlds.
    /// Erstellt je nach RequestedPlayType (Editor/MPPM/CLI) Client-, Server-, oder ClientAndServer-Worlds.
    /// </summary>
    /// <remarks>
    /// Lifecycle:
    /// <list type="bullet">
    ///   <item>Editor ohne MPPM: ClientAndServer (Host)</item>
    ///   <item>Editor mit MPPM: pro Virtual Player gemäß Role-Dropdown</item>
    ///   <item>Server-Build (CLI <c>-server</c>): nur ServerWorld</item>
    ///   <item>Client-Build (CLI <c>-client</c>): nur ClientWorld</item>
    /// </list>
    /// AutoConnectPort = 0 → kein Auto-Connect (manueller Connect via UI/Bridge).
    /// </remarks>
    public sealed class RiftstormBootstrap : ClientServerBootstrap
    {
        /// <summary>
        /// Initialisiert die NfE-Worlds beim Editor-/Build-Start.
        /// </summary>
        /// <param name="defaultWorldName">Von Unity vorgegebener Default-World-Name.</param>
        /// <returns>true wenn Worlds erfolgreich erstellt.</returns>
        public override bool Initialize(string defaultWorldName)
        {
            // Phase 1: Noch keine NfE-Worlds erstellen → vermeidet Server-Tick-Spam ohne Connection.
            // In Phase 2 wird hier CreateDefaultClientServerWorlds() aufgerufen + AutoConnectPort gesetzt.
            CreateLocalWorld(defaultWorldName);
            return true;
        }
    }
}
