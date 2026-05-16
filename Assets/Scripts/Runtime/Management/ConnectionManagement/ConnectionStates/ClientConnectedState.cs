using UnityEngine;

namespace Tolik.Riftstorm.Runtime.ConnectionManagement
{
    /// <summary>
    /// Client is fully connected and approved by the server. Server-driven scene loading will
    /// transition the client into the GameScene automatically through NGO's scene management.
    /// </summary>
    public class ClientConnectedState : ConnectionState
    {
        public override void Enter()
        {
            Debug.Log("[ClientConnectedState] Connected. LocalClientId=" + Manager.NetworkManager.LocalClientId);
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.Success));
        }

        public override void OnClientDisconnect(ulong clientId)
        {
            if (clientId != Manager.NetworkManager.LocalClientId)
            {
                return;
            }

            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.GenericDisconnect));
            Manager.ChangeState(Manager.m_Offline);
        }

        public override void OnUserRequestedShutdown()
        {
            Manager.NetworkManager.Shutdown();
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.UserRequestedDisconnect));
            Manager.ChangeState(Manager.m_Offline);
        }

        public override void OnTransportFailure()
        {
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.GenericDisconnect));
            Manager.ChangeState(Manager.m_Offline);
        }
    }
}
