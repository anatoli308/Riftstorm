using System.Threading.Tasks;
using Riftstorm.Game.CameraRig;
using Riftstorm.Game.Input;
using Riftstorm.Game.Movement;
using Riftstorm.Game.Sprites;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Bootstrap
{
    /// <summary>
    /// Minimaler Spielstart: spawnt einen FLARE-Spieler mit den vier Standard-Layern
    /// (legs, feet, chest, hands), verdrahtet Input + Movement + Topdown-Kamera.
    /// Dient als Stub bis der vollwertige MVC-<c>GameApplication</c> existiert.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GamePlayerBootstrap : MonoBehaviour
    {
        [Header("FLARE Atlas")]
        [Tooltip("Unterordner unter Application.streamingAssetsPath mit den JSON-Atlanten.")]
        [SerializeField] private string m_StreamingSubFolder = "player_male";
        [Tooltip("Unterordner unter Application.dataPath (Editor: Assets/) mit den PNG-Texturen. Leer = gleicher Ordner wie JSON.")]
        [SerializeField] private string m_TextureSubFolder = "Art/player_male";
        [SerializeField] private string[] m_LayerAtlases = { "default_legs", "default_feet", "default_chest", "default_hands", "head_short", "longsword", "buckler" };
        [SerializeField] private string m_InitialAnimation = "stance";

        [Header("Input")]
        [SerializeField] private InputActionAsset m_InputAsset;

        [Header("Welt")]
        [SerializeField] private Vector3 m_SpawnPosition = Vector3.zero;
        [SerializeField] private bool m_AutoCreateCamera = true;

        private GameObject m_Player;

        private async void Start()
        {
            await BuildPlayerAsync();
        }

        private async Task BuildPlayerAsync()
        {
            ResolveInputAssetFallback();

            // Wurzel-Objekt mit Input + Movement.
            m_Player = new GameObject("Player");
            m_Player.transform.position = m_SpawnPosition;

            // Deaktivieren, damit AddComponent OnEnable nicht vor der Asset-Zuweisung auslöst.
            m_Player.SetActive(false);
            PlayerInputController input = m_Player.AddComponent<PlayerInputController>();
            if (m_InputAsset != null)
            {
                SetInputAsset(input, m_InputAsset);
            }
            m_Player.SetActive(true);

            // Layered Visuals als Kind-Hierarchie.
            GameObject visualsRoot = new("Visuals");
            visualsRoot.transform.SetParent(m_Player.transform, false);
            FlareCharacter character = visualsRoot.AddComponent<FlareCharacter>();

            FlareAtlasLoader loader = BuildLoader();
            for (int i = 0; i < m_LayerAtlases.Length; i++)
            {
                string atlasName = m_LayerAtlases[i];
                FlareAtlas atlas = await loader.LoadAsync(atlasName);
                FlareLayerAnimator layer = CreateLayer(visualsRoot.transform, atlasName, i);
                layer.SetAtlas(atlas);
                character.RegisterLayer(layer);
            }
            character.Play(m_InitialAnimation, true);
            character.SetDirection(2); // FLARE 2 = Süd, Standard-Blickrichtung Topdown.

            // Movement an den fertigen Charakter koppeln.
            PlayerMovement movement = m_Player.AddComponent<PlayerMovement>();
            BindMovement(movement, input, character);

            if (m_AutoCreateCamera)
            {
                EnsureCamera(m_Player.transform);
            }
        }

        private void ResolveInputAssetFallback()
        {
            if (m_InputAsset != null)
            {
                return;
            }
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:InputActionAsset");
            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                m_InputAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                if (m_InputAsset != null)
                {
                    Debug.Log($"[GamePlayerBootstrap] InputActionAsset automatisch geladen: {path}");
                }
            }
#endif
        }

        private FlareAtlasLoader BuildLoader()
        {
            string jsonFolder = System.IO.Path.Combine(Application.streamingAssetsPath, m_StreamingSubFolder);
            if (string.IsNullOrEmpty(m_TextureSubFolder))
            {
                return new FlareAtlasLoader(jsonFolder, jsonFolder);
            }
            string textureFolder = System.IO.Path.Combine(Application.dataPath, m_TextureSubFolder);
            return new FlareAtlasLoader(jsonFolder, textureFolder);
        }

        private static FlareLayerAnimator CreateLayer(Transform parent, string layerName, int order)
        {
            GameObject go = new(layerName);
            go.transform.SetParent(parent, false);
            // Topdown: Sprites entlang der XZ-Ebene rendern, also auf den Boden kippen.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            return go.AddComponent<FlareLayerAnimator>();
        }

        private static void EnsureCamera(Transform target)
        {
            Camera cam = Camera.main;
            GameObject camGo;
            if (cam == null)
            {
                camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            else
            {
                camGo = cam.gameObject;
            }
            TopdownCameraFollow follow = camGo.GetComponent<TopdownCameraFollow>();
            if (follow == null)
            {
                follow = camGo.AddComponent<TopdownCameraFollow>();
            }
            follow.SetTarget(target);
        }

        private static void SetInputAsset(PlayerInputController controller, InputActionAsset asset)
        {
            // Direktes Setzen über das serialisierte Feld via SerializedObject ist nur im Editor verfügbar.
            // Zur Laufzeit nutzen wir Reflection nur für dieses Bootstrap-Stub — bewusst zentral und nicht in Hot Paths.
            System.Reflection.FieldInfo field = typeof(PlayerInputController).GetField(
                "m_InputAsset",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(controller, asset);
        }

        private static void BindMovement(PlayerMovement movement, PlayerInputController input, FlareCharacter character)
        {
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(PlayerMovement).GetField("m_Input", flags)?.SetValue(movement, input);
            typeof(PlayerMovement).GetField("m_Character", flags)?.SetValue(movement, character);
        }
    }
}
