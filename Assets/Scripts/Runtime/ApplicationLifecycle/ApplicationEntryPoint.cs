using Riftstorm.ApplicationLifecycle.UI;
using Riftstorm.Gameplay.Combat;
using Riftstorm.Management.TextureManagement;
using Tolik.Riftstorm.Runtime.ConnectionManagement;
using Unity.Multiplayer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using ConnectionEvent = Tolik.Riftstorm.Runtime.ConnectionManagement.ConnectionEvent;

namespace Tolik.Riftstorm.Runtime.ApplicationLifecycle
{
    /// <summary>
    /// Persistent entry point for the application lifecycle. Lives in the Boot scene as a
    /// singleton with DontDestroyOnLoad, registers pure services in the ServiceLocator, listens
    /// to <see cref="ConnectionManager"/> events, and drives the scene-flow:
    /// <list type="bullet">
    ///   <item><b>Server build</b> → StartServer, on success NetworkManager.SceneManager loads <c>Game</c>.</item>
    ///   <item><b>Client build</b> → load <c>Metagame</c>, wait for user to connect; NGO scene sync hands the client over to <c>Game</c>.</item>
    /// </list>
    /// Mirrors the RemakeSoF ApplicationEntryPoint pattern stripped down to NGO essentials.
    /// </summary>
    [MultiplayerRoleRestricted]
    public class ApplicationEntryPoint : MonoBehaviour
    {
        const string k_MetagameSceneName = "Metagame";
        const string k_GameSceneName = "Game";

        public static ApplicationEntryPoint Singleton { get; private set; }

        [SerializeField]
        ConnectionManager m_ConnectionManager;
        public ConnectionManager ConnectionManager => m_ConnectionManager;

        [Header("UI Fonts")]
        [Tooltip("Alle Font-Assets, die UI/HUD per Rolle nutzen darf. Die Zuordnung Rolle\u2192Name kommt aus StreamingAssets/interface/ui_fonts.json.")]
        [SerializeField]
        Font[] m_UIFonts;

        void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }

            Singleton = this;
            DontDestroyOnLoad(gameObject);

            RegisterPureServices();

            if (m_ConnectionManager == null)
            {
                Debug.LogError("[ApplicationEntryPoint] ConnectionManager reference is missing in the Boot scene.");
                return;
            }

            m_ConnectionManager.EventManager.AddListener<ConnectionEvent>(OnConnectionEvent);
        }

        void OnDestroy()
        {
            if (m_ConnectionManager != null)
            {
                m_ConnectionManager.EventManager.RemoveListener<ConnectionEvent>(OnConnectionEvent);
            }

            ServiceLocator.ClearAll();
        }

        [RuntimeInitializeOnLoadMethod]
        static void OnApplicationStarted()
        {
            if (Singleton == null)
            {
                // Happens during PlayMode tests / scenes without the Boot scene loaded.
                return;
            }

            Singleton.InitializeNetworkLogic();
        }

        /// <summary>
        /// Registers pure services (no MonoBehaviour) in the <see cref="ServiceLocator"/>.
        /// Kicks off async preloads (data-driven catalogs from <c>StreamingAssets</c>) without
        /// blocking <c>Awake</c>; first consumers will await the same <c>LoadAsync</c> task.
        /// </summary>
        void RegisterPureServices()
        {
            WeaponCatalogLoader weaponLoader = new();
            ServiceLocator.Register(weaponLoader);
            _ = weaponLoader.LoadAsync();

            OffhandCatalogLoader offhandLoader = new();
            ServiceLocator.Register(offhandLoader);
            _ = offhandLoader.LoadAsync();

            // Scant Application.dataPath/Art rekursiv und indexiert alle Bilddateien
            // mit Keys ohne Extension (z. B. "interface/unit_frame"). Texturen werden
            // erst beim ersten GetTexture(key) lazy geladen.
            TextureManager textureManager = new();
            ServiceLocator.Register(textureManager);

            // UI-Fonts: Inspector-zugewiesene Font-Assets in eine Name->Font-Map packen.
            // Die Rolle->Name-Zuordnung kommt zur Laufzeit aus
            // StreamingAssets/interface/ui_fonts.json (UIFontConfigLoader).
            FontRegistry fontRegistry = new(m_UIFonts);
            ServiceLocator.Register(fontRegistry);
            Debug.Log($"[ApplicationEntryPoint] FontRegistry mit {fontRegistry.Count} Font-Asset(s) registriert.");
        }

        /// <summary>
        /// Reads multiplayer role + CLI args and drives the initial scene-flow.
        /// </summary>
        void InitializeNetworkLogic()
        {
            CommandLineArgumentsParser cli = new();
            ushort port = (ushort)cli.Port;

            switch (MultiplayerRolesManager.ActiveMultiplayerRoleMask)
            {
                case MultiplayerRoleFlags.Server:
                    Application.targetFrameRate = cli.TargetFramerate;
                    QualitySettings.vSyncCount = 0;
                    Debug.Log($"[ApplicationEntryPoint] Server starting on {cli.ListenAddress}:{port} @ {cli.TargetFramerate} Hz.");
                    m_ConnectionManager.StartServer(cli.ListenAddress, port);
                    break;

                case MultiplayerRoleFlags.Client:
                    Debug.Log("[ApplicationEntryPoint] Client mode — loading Metagame scene.");
                    SceneManager.LoadScene(k_MetagameSceneName);
                    break;

                case MultiplayerRoleFlags.ClientAndServer:
                    // Host mode not supported in dedicated-server setups; treat as client for editor convenience.
                    Debug.LogWarning("[ApplicationEntryPoint] ClientAndServer role is not officially supported. Falling back to Client.");
                    SceneManager.LoadScene(k_MetagameSceneName);
                    break;
            }
        }

        void OnConnectionEvent(ConnectionEvent evt)
        {
            if (MultiplayerRolesManager.ActiveMultiplayerRoleMask == MultiplayerRoleFlags.Server)
            {
                HandleServerConnectionEvent(evt);
            }
            else
            {
                HandleClientConnectionEvent(evt);
            }
        }

        void HandleServerConnectionEvent(ConnectionEvent evt)
        {
            switch (evt.status)
            {
                case ConnectStatus.Success:
                    // Server is listening — load the game scene through NGO so connecting clients sync automatically.
                    Debug.Log("[ApplicationEntryPoint] Server up. Loading Game scene via NGO SceneManager.");
                    NetworkManager.Singleton.SceneManager.LoadScene(k_GameSceneName, LoadSceneMode.Single);
                    break;

                case ConnectStatus.StartServerFailed:
                case ConnectStatus.ServerEndedSession:
                case ConnectStatus.GenericDisconnect:
                    Debug.LogWarning($"[ApplicationEntryPoint] Server terminated ({evt.status}). Quitting.");
                    Quit();
                    break;
            }
        }

        void HandleClientConnectionEvent(ConnectionEvent evt)
        {
            switch (evt.status)
            {
                case ConnectStatus.GenericDisconnect:
                case ConnectStatus.UserRequestedDisconnect:
                case ConnectStatus.ServerEndedSession:
                case ConnectStatus.StartClientFailed:
                    Debug.Log($"[ApplicationEntryPoint] Client disconnected ({evt.status}). Returning to Metagame.");
                    SceneManager.LoadScene(k_MetagameSceneName);
                    break;
            }
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
