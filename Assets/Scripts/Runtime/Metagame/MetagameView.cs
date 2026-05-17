using System;
using Riftstorm.ApplicationLifecycle.UI;
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
        /// Maximum number of characters allowed in the player name input.
        /// </summary>
        public const int MaxPlayerNameLength = 20;

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

            if (m_NameField != null)
            {
                // Hard cap via UI Toolkit, damit der Nutzer gar nicht mehr
                // tippen kann. GetPlayerName() clamped zusaetzlich als Safety
                // Net, falls die UXML einen abweichenden Wert vorgibt.
                m_NameField.maxLength = MaxPlayerNameLength;
            }

            if (m_ConnectButton != null)
            {
                m_ConnectButton.clicked += OnConnectButtonClicked;
            }

            ApplyFonts(root);
        }

        /// <summary>
        /// Wendet die ueber <c>StreamingAssets/interface/ui_fonts.json</c>
        /// konfigurierten Fonts auf die statisch in der UXML/USS definierten
        /// Elemente des Login-Screens an. USS kann das JSON nicht lesen,
        /// deshalb erfolgt das Binding hier per Code anhand der USS-Klassen.
        /// </summary>
        void ApplyFonts(VisualElement root)
        {
            UIFonts.Apply(root.Q<Label>(className: "title"), UIFonts.Title);
            UIFonts.Apply(root.Q<Label>(className: "subtitle"), UIFonts.Small);
            UIFonts.Apply(m_ConnectButton, UIFonts.Heading);
            UIFonts.Apply(m_StatusLabel, UIFonts.Small);

            root.Query<Label>(className: "field-label").ForEach(label => UIFonts.Apply(label, UIFonts.Small));
            root.Query<TextField>().ForEach(field => UIFonts.Apply(field, UIFonts.Body));
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
            m_NameField?.SetValueWithoutNotify(ClampName(playerName));
        }

        public string GetServerAddress() => m_AddressField?.value ?? string.Empty;

        public bool TryGetServerPort(out ushort port) => ushort.TryParse(m_PortField?.value, out port);

        public string GetPlayerName() => ClampName(m_NameField?.value);

        static string ClampName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }
            return raw.Length > MaxPlayerNameLength ? raw[..MaxPlayerNameLength] : raw;
        }

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
