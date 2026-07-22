using Riftstorm.Management.FontManagement;
using Riftstorm.Gameplay.Combat;
using Riftstorm.Gameplay.Combat.Spells.Visuals;
using Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime;
using Riftstorm.Management.SoundManagement;
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

            // Spell-Animations-Bibliothek (StreamingAssets/spells/animations/*.json).
            // Sprite-Sheet-Definitionen; per Name (= Dateistem) gelookupt.
            SpellAnimationCatalogLoader spellAnimLoader = new();
            ServiceLocator.Register(spellAnimLoader);
            _ = spellAnimLoader.LoadAsync();

            // Source-Port der Visual-Pipeline (2 Tabellen, mirror der DB):
            //   _visuals.json      → spell_visual_kit  (per Spell-Entry → Kit-IDs)
            //   _visual_kits.json  → spell_visual      (Kit-Definitionen)
            // Resolver (SpellVisualResolver) baut daraus zur Laufzeit das
            // 3-Phasen-Modell (Casting → Travel → Impact + Aura) fuer den
            // bestehenden WorldSpellAnimation-Player.
            SpellVisualKitMappingCatalogLoader spellVisualMappingLoader = new();
            ServiceLocator.Register(spellVisualMappingLoader);
            _ = spellVisualMappingLoader.LoadAsync();

            SpellVisualKitDefinitionCatalogLoader spellVisualKitLoader = new();
            ServiceLocator.Register(spellVisualKitLoader);
            _ = spellVisualKitLoader.LoadAsync();

            // Partikelsystem-Katalog (StreamingAssets/particles/_particles.json).
            // Source-Port der .psi-Definitionen; vom Client-Visual-Pfad
            // (PlayerCombat.TryTriggerCasterParticles) per Name aus dem Visual-Kit
            // (psystem-Feld) gelookupt und via CasterParticleSpawner instanziiert.
            ParticleSystemCatalogLoader particleCatalogLoader = new();
            ServiceLocator.Register(particleCatalogLoader);
            _ = particleCatalogLoader.LoadAsync();

            // Scant Application.dataPath/Art rekursiv und indexiert alle Bilddateien
            // mit Keys ohne Extension (z. B. "interface/unit_frame"). Texturen werden
            // erst beim ersten GetTexture(key) lazy geladen.
            TextureManager textureManager = new();
            ServiceLocator.Register(textureManager);

            // Resolver für den Gameplay-seitigen Sprite-Cache injizieren. So bleibt
            // Riftstorm.Gameplay frei von Management-/ApplicationLifecycle-Refs.
            SpellSpriteCache.TextureResolver = textureManager.GetTexture;

            // Scant Application.dataPath/Art/sounds und persistentDataPath/CustomSounds.
            // Keys sind Dateinamen inkl. Extension (matched _visual_kits.json -> "sound").
            // AudioClips werden lazy beim ersten GetClip(...) aus der Datei geladen.
            SoundManager soundManager = new();
            ServiceLocator.Register(soundManager);

            // UI-Fonts: laedt alle Fonts aus Assets/Art/fonts. Das Font-Asset
            // wird im Editor ueber die AssetDatabase geladen. Die Rolle->Name-
            // Zuordnung kommt zur Laufzeit aus StreamingAssets/interface/ui_fonts.json
            // (UIFontConfigLoader).
            FontManager fontManager = new();
            ServiceLocator.Register(fontManager);

            // Resolver in den statischen UIFonts-Accessor injizieren. So bleibt
            // Riftstorm.Management frei von ApplicationLifecycle-Refs (Asmdef-Zyklus).
            UIFonts.FontResolver = fontManager.GetFont;
            Debug.Log($"[ApplicationEntryPoint] FontManager mit {fontManager.Count} Font(s) geladen.");
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
                    // Host mode not supported in dedicated-server setups
                    Debug.LogWarning("[ApplicationEntryPoint] ClientAndServer role is not officially supported.");
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
