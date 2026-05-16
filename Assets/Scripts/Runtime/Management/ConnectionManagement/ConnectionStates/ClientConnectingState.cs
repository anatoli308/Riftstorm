using System.Text;
using UnityEngine;

namespace Tolik.Riftstorm.Runtime.ConnectionManagement
{
    /// <summary>
    /// The client has invoked StartClient on the NetworkManager and is waiting for the server's
    /// approval / first connection event.
    /// </summary>
    public class ClientConnectingState : ConnectionState
    {
        public override void Enter()
        {
            // Player-Name als Approval-Payload an den Server &#252;bertragen. NetworkConfig.ConnectionData
            // ist der NGO-Standardkanal f&#252;r solche Pre-Connect-Metadaten und wird in der
            // ApprovalCheck-Callback des Servers als request.Payload geliefert.
            string name = string.IsNullOrWhiteSpace(Manager.PendingPlayerName) ? "Player" : Manager.PendingPlayerName;
            Manager.NetworkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(name);

            bool started = Manager.NetworkManager.StartClient();
            if (!started)
            {
                Debug.LogWarning("[ClientConnectingState] NetworkManager.StartClient() returned false.");
                Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.StartClientFailed));
                Manager.ChangeState(Manager.m_Offline);
                return;
            }

            Debug.Log("[ClientConnectingState] StartClient invoked, waiting for server approval...");
        }

        public override void OnClientConnected(ulong clientId)
        {
            if (clientId != Manager.NetworkManager.LocalClientId)
            {
                return;
            }

            Manager.ChangeState(Manager.m_ClientConnected);
        }

        public override void OnClientDisconnect(ulong clientId)
        {
            if (clientId != Manager.NetworkManager.LocalClientId)
            {
                return;
            }

            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.StartClientFailed));
            Manager.ChangeState(Manager.m_Offline);
        }

        public override void OnTransportFailure()
        {
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.StartClientFailed));
            Manager.ChangeState(Manager.m_Offline);
        }

        public override void OnCancelClientConnectionAttempt()
        {
            Manager.NetworkManager.Shutdown();
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.UserRequestedDisconnect));
            Manager.ChangeState(Manager.m_Offline);
        }
    }
}
