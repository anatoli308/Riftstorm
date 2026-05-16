using System.Threading.Tasks;
using Riftstorm.Game.CameraRig;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Input;
using Riftstorm.Game.Movement;
using Riftstorm.Game.Sprites;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Bootstrap
{
    /// <summary>
    /// Baut die FLARE-Visuals als Child-Hierarchie unter dem
    /// <c>PlayerCharacter</c>-NetworkObject auf und verdrahtet sie mit dem
    /// <see cref="PlayerMovement"/>, das selbst direkt am Prefab-Root sitzt.
    /// Auf einem reinen Dedicated Server werden weder Sprites noch Kamera erzeugt,
    /// auf Remote-Clients zwar Sprites (damit man andere Spieler sieht), aber
    /// keine Kamera. Nur der Owner-Client bekommt die Topdown-Kamera angehängt.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GamePlayerBootstrap : NetworkBehaviour
    {
        [Header("FLARE Atlas")]
        [Tooltip("Unterordner unter Application.streamingAssetsPath mit den JSON-Atlanten.")]
        [SerializeField] private string m_StreamingSubFolder = "player_male";
        [Tooltip("Unterordner unter Application.dataPath (Editor: Assets/) mit den PNG-Texturen. Leer = gleicher Ordner wie JSON.")]
        [SerializeField] private string m_TextureSubFolder = "Art/player_male";
        [SerializeField] private string[] m_LayerAtlases = { "default_legs", "default_feet", "default_chest", "default_hands", "head_short", "longsword", "buckler" };
        [SerializeField] private string m_InitialAnimation = "stance";

        [Header("Welt")]
        [SerializeField] private bool m_AutoCreateCamera = true;

        public override void OnNetworkSpawn()
        {
            // Auf einem Dedicated Server haben wir keinen Renderer — Sprites/Kamera
            // wären reine Verschwendung. Pure-Server-Branch früh verlassen.
            if (NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsServer
                && !NetworkManager.Singleton.IsClient)
            {
                return;
            }

            _ = BuildVisualsAsync();
        }

        private async Task BuildVisualsAsync()
        {
            // Visuals direkt unter dem NetworkObject-Root erzeugen, damit die
            // server-authoritative Transform-Bewegung sie automatisch mitnimmt.
            GameObject visualsRoot = new("Visuals");
            visualsRoot.transform.SetParent(transform, false);
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

            // PlayerMovement liegt direkt am Root (NetworkBehaviour-Host). Visuals injizieren.
            PlayerMovement movement = GetComponent<PlayerMovement>();
            if (movement != null)
            {
                movement.BindVisuals(character);
            }
            else
            {
                Debug.LogError("[GamePlayerBootstrap] Kein PlayerMovement am Root gefunden — bitte am PlayerCharacter-Prefab hinzufügen.", this);
            }

            // Combat-Visuals (lokal pro Client) + autoritativer Combat-State (NetworkBehaviour)
            // sind beide am Root. Visuals an die FLARE-Layer binden, Manager die Visuals + Input
            // injizieren. Beide Komponenten MÜSSEN auf dem PlayerCharacter-Prefab liegen, weil
            // NetworkBehaviours nicht zur Laufzeit nach OnNetworkSpawn hinzugefügt werden können.
            PlayerCombatVisuals combatVisuals = GetComponent<PlayerCombatVisuals>();
            if (combatVisuals != null)
            {
                combatVisuals.BindCharacter(character);
            }
            else
            {
                Debug.LogWarning("[GamePlayerBootstrap] PlayerCombatVisuals nicht am Root — Combat-Animationen werden nicht abgespielt.", this);
            }

            PlayerCombat combat = GetComponent<PlayerCombat>();
            if (combat != null)
            {
                if (combatVisuals != null)
                {
                    combat.BindVisuals(combatVisuals);
                }
                PlayerInputController input = GetComponent<PlayerInputController>();
                if (input != null)
                {
                    combat.BindInput(input);
                }
            }
            else
            {
                Debug.LogWarning("[GamePlayerBootstrap] PlayerCombat nicht am Root — Attack-Input wird ignoriert.", this);
            }

            // Kamera nur für den lokalen Spieler. Remote-Clients sollen den Owner nicht hijacken.
            if (m_AutoCreateCamera && IsOwner)
            {
                EnsureCamera(transform);
            }

            // HoverHighlight liegt auf JEDEM Spieler (auch Remotes), weil jeder Client jeden
            // anderen anhovern können muss. Auto-add hier, damit das Prefab nicht händisch
            // gepflegt werden muss. RefreshRenderers() ist Pflicht — der HoverHighlight-Awake
            // hat oben (vor BuildVisualsAsync) gefeuert, da gab es die SpriteRenderer noch nicht.
            HoverHighlight hover = GetComponent<HoverHighlight>();
            if (hover == null)
            {
                hover = gameObject.AddComponent<HoverHighlight>();
            }
            hover.RefreshRenderers();

            // AttackRangeIndicator nur für den Owner — Remotes sehen den eigenen Range-Kreis
            // sowieso nicht und müssen ihn auch nicht togglen können.
            if (IsOwner && GetComponent<AttackRangeIndicator>() == null)
            {
                gameObject.AddComponent<AttackRangeIndicator>();
            }
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
    }
}
