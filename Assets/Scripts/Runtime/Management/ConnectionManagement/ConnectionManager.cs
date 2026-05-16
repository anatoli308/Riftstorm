using System;
using System.Collections.Generic;
using Tolik.Riftstorm.Runtime.Core;
using Unity.Netcode;
using UnityEngine;

namespace Tolik.Riftstorm.Runtime.ConnectionManagement
{
    /// <summary>
    /// State machine wrapper around <see cref="Unity.Netcode.NetworkManager"/> mirroring the
    /// RemakeSoF ConnectionManager pattern. Forwards NetworkManager callbacks to the current
    /// ConnectionState and exposes intent-style entry points (StartClient / StartServer).
    /// </summary>
    public class ConnectionManager : StateMachine<ConnectionState, ConnectionManager>
    {
        [SerializeField]
        NetworkManager m_NetworkManager;
        public NetworkManager NetworkManager => m_NetworkManager;

        [SerializeField, Tooltip("Max number of players the server will accept.")]
        int m_MaxPlayers = 15;
        public int MaxPlayers => m_MaxPlayers;

        /// <summary>
        /// Player name that the client passed into StartClient. Used by higher level systems
        /// (player spawn, UI) once the client is approved.
        /// </summary>
        public string PendingPlayerName { get; internal set; } = "Player";

        internal readonly OfflineState m_Offline = new();
        internal readonly ClientConnectingState m_ClientConnecting = new();
        internal readonly ClientConnectedState m_ClientConnected = new();
        internal readonly StartingServerState m_StartingServer = new();
        internal readonly ServerListeningState m_ServerListening = new();

        void Awake()
        {
            DontDestroyOnLoad(gameObject);

            if (m_NetworkManager == null)
            {
                m_NetworkManager = NetworkManager.Singleton;
            }

            if (m_NetworkManager == null)
            {
                Debug.LogError("[ConnectionManager] No NetworkManager assigned and none in the scene.");
                return;
            }

            List<ConnectionState> states = new()
            {
                m_Offline, m_ClientConnecting, m_ClientConnected, m_StartingServer, m_ServerListening
            };
            InitializeStates(states, m_Offline);

            m_NetworkManager.OnConnectionEvent += OnConnectionEvent;
            m_NetworkManager.OnServerStarted += OnServerStarted;
            m_NetworkManager.ConnectionApprovalCallback += ApprovalCheck;
            m_NetworkManager.OnTransportFailure += OnTransportFailure;
            m_NetworkManager.OnServerStopped += OnServerStopped;
        }

        void OnDestroy()
        {
            if (m_NetworkManager == null)
            {
                return;
            }

            m_NetworkManager.OnConnectionEvent -= OnConnectionEvent;
            m_NetworkManager.OnServerStarted -= OnServerStarted;
            m_NetworkManager.ConnectionApprovalCallback -= ApprovalCheck;
            m_NetworkManager.OnTransportFailure -= OnTransportFailure;
            m_NetworkManager.OnServerStopped -= OnServerStopped;
        }

        void OnConnectionEvent(NetworkManager nm, ConnectionEventData data)
        {
            switch (data.EventType)
            {
                case Unity.Netcode.ConnectionEvent.ClientConnected:
                    m_CurrentState.OnClientConnected(data.ClientId);
                    break;
                case Unity.Netcode.ConnectionEvent.ClientDisconnected:
                    m_CurrentState.OnClientDisconnect(data.ClientId);
                    break;
                case Unity.Netcode.ConnectionEvent.PeerConnected:
                case Unity.Netcode.ConnectionEvent.PeerDisconnected:
                    // Peer events are informational; nothing to react to for now.
                    break;
                default:
                    Debug.LogWarning($"[ConnectionManager] Unhandled ConnectionEvent {data.EventType}.");
                    throw new ArgumentOutOfRangeException(nameof(data.EventType), data.EventType, "Unhandled ConnectionEvent.");
            }
        }

        void OnServerStarted() => m_CurrentState.OnServerStarted();

        void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
            => m_CurrentState.ApprovalCheck(request, response);

        void OnTransportFailure() => m_CurrentState.OnTransportFailure();

        void OnServerStopped(bool _) => m_CurrentState.OnServerStopped();

        // --- Public intent API ---

        public void StartClient(string ipAddress, ushort port, string playerName)
            => m_CurrentState.StartClient(ipAddress, port, playerName);

        public void StartServer(string listenAddress, ushort port)
            => m_CurrentState.StartServer(listenAddress, port);

        public void RequestShutdown()
            => m_CurrentState.OnUserRequestedShutdown();

        public void CancelClientConnectionAttempt()
            => m_CurrentState.OnCancelClientConnectionAttempt();
    }
}
