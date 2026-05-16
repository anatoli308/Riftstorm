using UnityEngine;

namespace Tolik.Riftstorm.Runtime.ConnectionManagement
{
    /// <summary>
    /// The server has invoked StartServer on the NetworkManager and is waiting for the
    /// OnServerStarted callback before transitioning into ServerListeningState.
    /// </summary>
    public class StartingServerState : ConnectionState
    {
        public override void Enter()
        {
            bool started = Manager.NetworkManager.StartServer();
            if (!started)
            {
                Debug.LogError("[StartingServerState] NetworkManager.StartServer() returned false.");
                Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.StartServerFailed));
                Manager.ChangeState(Manager.m_Offline);
                return;
            }

            Debug.Log("[StartingServerState] StartServer invoked, waiting for OnServerStarted...");
        }

        public override void OnServerStarted()
        {
            Manager.ChangeState(Manager.m_ServerListening);
        }

        public override void OnTransportFailure()
        {
            Manager.EventManager.Broadcast(new ConnectionEvent(ConnectStatus.StartServerFailed));
            Manager.ChangeState(Manager.m_Offline);
        }
    }
}
