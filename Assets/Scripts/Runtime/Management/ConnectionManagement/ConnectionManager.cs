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

        /// <summary>
        /// Maximalwert in Zeichen, den ein Spielername w&#228;hrend der Approval-Phase passieren darf.
        /// Schutz vor &#252;berlangen Payloads (FixedString32 fasst ~29 sichtbare ASCII-Zeichen).
        /// </summary>
        public const int MaxPlayerNameLength = 24;

        /// <summary>Server-seitiges Mapping ClientId &#8594; vom Client gesendeter Anzeigename (Approval-Payload).</summary>
        private readonly Dictionary<ulong, string> m_ApprovedPlayerNames = new();

        /// <summary>
        /// Server-only: Speichert den vom Client w&#228;hrend des Approval-Schritts &#252;bertragenen Namen.
        /// Leerwerte werden auf "Player" gemappt, &#252;berlange Namen werden gek&#252;rzt.
        /// </summary>
        internal void SetApprovedName(ulong clientId, string name)
        {
            string sanitized = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
            if (sanitized.Length > MaxPlayerNameLength)
            {
                sanitized = sanitized.Substring(0, MaxPlayerNameLength);
            }
            m_ApprovedPlayerNames[clientId] = sanitized;
        }

        /// <summary>Server-only: Liefert den vom Client &#252;bertragenen Namen, falls Approval ihn gespeichert hat.</summary>
        public bool TryGetApprovedName(ulong clientId, out string name)
            => m_ApprovedPlayerNames.TryGetValue(clientId, out name);

        /// <summary>Server-only: Eintrag entfernen (z. B. nach Disconnect).</summary>
        internal void RemoveApprovedName(ulong clientId)
            => m_ApprovedPlayerNames.Remove(clientId);

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
