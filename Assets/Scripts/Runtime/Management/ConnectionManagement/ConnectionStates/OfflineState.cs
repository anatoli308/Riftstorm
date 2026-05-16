using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Tolik.Riftstorm.Runtime.ConnectionManagement
{
    /// <summary>
    /// Idle state. Accepts requests to start either a client or a server.
    /// </summary>
    public class OfflineState : ConnectionState
    {
        public override void StartClient(string ipAddress, ushort port, string playerName)
        {
            UnityTransport transport = Manager.NetworkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[OfflineState] UnityTransport component is missing on the NetworkManager GameObject.");
                Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.StartClientFailed));
                return;
            }

            transport.SetConnectionData(ipAddress, port);
            Manager.PendingPlayerName = playerName;
            Manager.ChangeState(Manager.m_ClientConnecting);
        }

        public override void StartServer(string listenAddress, ushort port)
        {
            UnityTransport transport = Manager.NetworkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[OfflineState] UnityTransport component is missing on the NetworkManager GameObject.");
                Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.StartServerFailed));
                return;
            }

            transport.SetConnectionData(listenAddress, port, listenAddress);
            Manager.ChangeState(Manager.m_StartingServer);
        }
    }
}
