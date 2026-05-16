using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Tolik.Riftstorm.Runtime.ConnectionManagement;
using UnityEngine;

namespace Tolik.Riftstorm.Runtime.Metagame
{
    /// <summary>
    /// Bridges the metagame UI to <see cref="ConnectionManager"/>. Subscribes to the view's
    /// ConnectClicked event, pushes form input into the model, and triggers the client connect
    /// flow. Also listens to ConnectionEvent so the UI can reflect connection results.
    /// </summary>
    public class MetagameController : Controller<MetagameApplication>
    {
        ConnectionManager m_ConnectionManager;

        void OnEnable()
        {
            if (App?.View != null)
            {
                App.View.ConnectClicked += HandleConnectClicked;
            }
        }

        void Start()
        {
            MetagameModel model = App.Model;
            MetagameView view = App.View;
            if (model != null && view != null)
            {
                view.Initialize(model.ServerAddress, model.ServerPort, model.PlayerName);
                view.SetStatus("Ready to connect.");
            }

            ApplicationEntryPoint entry = ApplicationEntryPoint.Singleton;
            if (entry != null && entry.ConnectionManager != null)
            {
                m_ConnectionManager = entry.ConnectionManager;
                m_ConnectionManager.EventManager.AddListener<ConnectionEvent>(HandleConnectionEvent);
            }
        }

        void OnDisable()
        {
            if (App?.View != null)
            {
                App.View.ConnectClicked -= HandleConnectClicked;
            }

            if (m_ConnectionManager != null)
            {
                m_ConnectionManager.EventManager.RemoveListener<ConnectionEvent>(HandleConnectionEvent);
                m_ConnectionManager = null;
            }
        }

        /// <summary>
        /// Public entry point if connection should be triggered from outside the UI.
        /// </summary>
        public void RequestConnect()
        {
            HandleConnectClicked();
        }

        void HandleConnectClicked()
        {
            ApplicationEntryPoint entry = ApplicationEntryPoint.Singleton;
            if (entry == null || entry.ConnectionManager == null)
            {
                Debug.LogError("[MetagameController] ApplicationEntryPoint or ConnectionManager not available.");
                App.View?.SetStatus("Connection manager unavailable.", MetagameView.StatusKind.Error);
                return;
            }

            MetagameView view = App.View;
            MetagameModel model = App.Model;
            if (view == null || model == null)
            {
                Debug.LogError("[MetagameController] View or Model missing on MetagameApplication.");
                return;
            }

            string address = view.GetServerAddress();
            if (string.IsNullOrWhiteSpace(address))
            {
                view.SetStatus("Server address is required.", MetagameView.StatusKind.Error);
                return;
            }

            if (!view.TryGetServerPort(out ushort port))
            {
                view.SetStatus("Port must be a number between 0 and 65535.", MetagameView.StatusKind.Error);
                return;
            }

            string playerName = view.GetPlayerName();
            if (string.IsNullOrWhiteSpace(playerName))
            {
                playerName = "Player";
            }

            model.ServerAddress = address;
            model.ServerPort = port;
            model.PlayerName = playerName;

            view.SetConnectInteractable(false);
            view.SetStatus($"Connecting to {address}:{port}...");
            entry.ConnectionManager.StartClient(address, port, playerName);
        }

        void HandleConnectionEvent(ConnectionEvent evt)
        {
            MetagameView view = App?.View;
            if (view == null)
            {
                return;
            }

            switch (evt.status)
            {
                case ConnectStatus.Success:
                    view.SetStatus("Connected. Loading game...", MetagameView.StatusKind.Success);
                    break;
                case ConnectStatus.StartClientFailed:
                    view.SetStatus("Could not reach the server. Check address and port.", MetagameView.StatusKind.Error);
                    view.SetConnectInteractable(true);
                    break;
                case ConnectStatus.ServerFull:
                    view.SetStatus("Server is full.", MetagameView.StatusKind.Error);
                    view.SetConnectInteractable(true);
                    break;
                case ConnectStatus.LoggedInAgain:
                    view.SetStatus("Disconnected: logged in from another client.", MetagameView.StatusKind.Error);
                    view.SetConnectInteractable(true);
                    break;
                case ConnectStatus.ServerEndedSession:
                    view.SetStatus("Server ended the session.", MetagameView.StatusKind.Error);
                    view.SetConnectInteractable(true);
                    break;
                case ConnectStatus.UserRequestedDisconnect:
                    view.SetStatus("Disconnected.");
                    view.SetConnectInteractable(true);
                    break;
                case ConnectStatus.GenericDisconnect:
                    view.SetStatus("Disconnected from server.", MetagameView.StatusKind.Error);
                    view.SetConnectInteractable(true);
                    break;
                case ConnectStatus.Reconnecting:
                    view.SetStatus("Reconnecting...");
                    break;
            }
        }
    }
}
