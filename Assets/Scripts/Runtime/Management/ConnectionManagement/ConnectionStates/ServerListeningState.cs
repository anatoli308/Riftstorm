using System.Text;
using UnityEngine;

namespace Tolik.Riftstorm.Runtime.ConnectionManagement
{
    /// <summary>
    /// The server is up and accepting clients. Clients connecting / disconnecting are tracked
    /// here so higher-level systems can react via the connection event stream.
    /// </summary>
    public class ServerListeningState : ConnectionState
    {
        public override void Enter()
        {
            Debug.Log("[ServerListeningState] Server is listening.");
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.Success));
        }

        public override void ApprovalCheck(Unity.Netcode.NetworkManager.ConnectionApprovalRequest request, Unity.Netcode.NetworkManager.ConnectionApprovalResponse response)
        {
            int currentPlayerCount = Manager.NetworkManager.ConnectedClientsIds.Count;
            if (currentPlayerCount >= Manager.MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "ServerFull";
                return;
            }

            // Vom Client &#252;bertragenen Anzeigenamen aus dem Approval-Payload lesen.
            string playerName = "Player";
            if (request.Payload != null && request.Payload.Length > 0)
            {
                try
                {
                    playerName = Encoding.UTF8.GetString(request.Payload);
                }
                catch
                {
                    playerName = "Player";
                }
            }
            Manager.SetApprovedName(request.ClientNetworkId, playerName);

            response.Approved = true;
            response.CreatePlayerObject = true;
        }

        public override void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[ServerListeningState] Client {clientId} connected. Total={Manager.NetworkManager.ConnectedClientsIds.Count}");
        }

        public override void OnClientDisconnect(ulong clientId)
        {
            Manager.RemoveApprovedName(clientId);
            Debug.Log($"[ServerListeningState] Client {clientId} disconnected. Total={Manager.NetworkManager.ConnectedClientsIds.Count}");
        }

        public override void OnServerStopped()
        {
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.ServerEndedSession));
            Manager.ChangeState(Manager.m_Offline);
        }

        public override void OnTransportFailure()
        {
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.GenericDisconnect));
            Manager.ChangeState(Manager.m_Offline);
        }

        public override void OnUserRequestedShutdown()
        {
            Manager.NetworkManager.Shutdown();
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.UserRequestedDisconnect));
            Manager.ChangeState(Manager.m_Offline);
        }
    }
}
