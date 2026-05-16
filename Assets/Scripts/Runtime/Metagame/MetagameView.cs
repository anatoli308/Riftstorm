using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tolik.Riftstorm.Runtime.Metagame
{
    /// <summary>
    /// UI root for the metagame scene. Loads the ConnectScreen UIDocument, exposes form values
    /// to the controller, and renders status messages while a connection attempt is in flight.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MetagameView : View<MetagameApplication>
    {
        /// <summary>
        /// Raised when the user clicks the Connect button.
        /// </summary>
        public event Action ConnectClicked;

        TextField m_AddressField;
        TextField m_PortField;
        TextField m_NameField;
        Button m_ConnectButton;
        Label m_StatusLabel;

        void OnEnable()
        {
            VisualElement root = LoadVisualElement();
            if (root == null)
            {
                Debug.LogError("[MetagameView] UIDocument has no rootVisualElement. Assign a Source Asset and PanelSettings on the UIDocument component.");
                return;
            }

            m_AddressField = root.Q<TextField>("address-field");
            m_PortField = root.Q<TextField>("port-field");
            m_NameField = root.Q<TextField>("name-field");
            m_ConnectButton = root.Q<Button>("connect-button");
            m_StatusLabel = root.Q<Label>("status-label");

            if (m_ConnectButton != null)
            {
                m_ConnectButton.clicked += OnConnectButtonClicked;
            }
        }

        void OnDisable()
        {
            if (m_ConnectButton != null)
            {
                m_ConnectButton.clicked -= OnConnectButtonClicked;
            }
        }

        /// <summary>
        /// Initializes field values from the model.
        /// </summary>
        public void Initialize(string serverAddress, ushort serverPort, string playerName)
        {
            m_AddressField?.SetValueWithoutNotify(serverAddress);
            m_PortField?.SetValueWithoutNotify(serverPort.ToString());
            m_NameField?.SetValueWithoutNotify(playerName);
        }

        public string GetServerAddress() => m_AddressField?.value ?? string.Empty;

        public bool TryGetServerPort(out ushort port) => ushort.TryParse(m_PortField?.value, out port);

        public string GetPlayerName() => m_NameField?.value ?? string.Empty;

        /// <summary>
        /// Updates the status line beneath the connect button.
        /// </summary>
        public void SetStatus(string message, StatusKind kind = StatusKind.Info)
        {
            if (m_StatusLabel == null)
            {
                return;
            }

            m_StatusLabel.text = message ?? string.Empty;
            m_StatusLabel.RemoveFromClassList("status-error");
            m_StatusLabel.RemoveFromClassList("status-success");

            switch (kind)
            {
                case StatusKind.Error:
                    m_StatusLabel.AddToClassList("status-error");
                    break;
                case StatusKind.Success:
                    m_StatusLabel.AddToClassList("status-success");
                    break;
            }
        }

        /// <summary>
        /// Enables / disables the connect button while a connection attempt is in flight.
        /// </summary>
        public void SetConnectInteractable(bool interactable)
        {
            m_ConnectButton?.SetEnabled(interactable);
        }

        void OnConnectButtonClicked()
        {
            ConnectClicked?.Invoke();
        }

        public enum StatusKind
        {
            Info,
            Success,
            Error,
        }
    }
}
