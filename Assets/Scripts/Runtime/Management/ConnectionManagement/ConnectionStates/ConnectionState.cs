using Tolik.Riftstorm.Runtime.Core;
using Unity.Netcode;

namespace Tolik.Riftstorm.Runtime.ConnectionManagement
{
    /// <summary>
    /// Abstract base for all connection states. Every callback has a no-op default so concrete
    /// states only need to override what they actually care about.
    /// </summary>
    public abstract class ConnectionState : State<ConnectionManager>
    {
        public override void Enter() { }
        public override void Exit() { }

        public virtual void OnClientConnected(ulong clientId) { }
        public virtual void OnClientDisconnect(ulong clientId) { }
        public virtual void OnServerStarted() { }
        public virtual void OnServerStopped() { }
        public virtual void OnTransportFailure() { }
        public virtual void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response) { }

        public virtual void StartClient(string ipAddress, ushort port, string playerName) { }
        public virtual void StartServer(string listenAddress, ushort port) { }
        public virtual void OnUserRequestedShutdown() { }
        public virtual void OnCancelClientConnectionAttempt() { }
    }
}
