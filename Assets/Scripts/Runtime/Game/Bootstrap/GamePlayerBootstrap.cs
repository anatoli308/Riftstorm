using System.Collections.Generic;
using System.Threading.Tasks;
using Riftstorm.Game.CameraRig;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Input;
using Riftstorm.Game.Movement;
using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
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

            // Equipment-Kataloge sicherstellen (idempotent — cache-first). Wir
            // filtern damit aus m_LayerAtlases all jene Eintraege heraus, die
            // tatsaechlich Waffe oder Offhand sind; sie werden NICHT als Body-
            // Layer aufgebaut, sondern landen unten als initialer Inhalt der
            // dedizierten MainHand-/OffHand-Schichten. So muss das Prefab nicht
            // editiert werden, um den alten "longsword"+"buckler"-Default zu
            // entkoppeln.
            WeaponCatalog weaponCatalog = await EnsureCatalogAsync<WeaponCatalogLoader, WeaponCatalog>(l => l.LoadAsync());
            OffhandCatalog offhandCatalog = await EnsureCatalogAsync<OffhandCatalogLoader, OffhandCatalog>(l => l.LoadAsync());

            List<string> bodyAtlasIds = new(m_LayerAtlases.Length);
            string legacyMainHandFromPrefab = null;
            string legacyOffHandFromPrefab = null;
            for (int i = 0; i < m_LayerAtlases.Length; i++)
            {
                string id = m_LayerAtlases[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                if (weaponCatalog != null && weaponCatalog.TryGet(id, out _))
                {
                    if (string.IsNullOrEmpty(legacyMainHandFromPrefab)) { legacyMainHandFromPrefab = id; }
                    continue;
                }
                if (offhandCatalog != null && offhandCatalog.TryGet(id, out _))
                {
                    if (string.IsNullOrEmpty(legacyOffHandFromPrefab)) { legacyOffHandFromPrefab = id; }
                    continue;
                }
                bodyAtlasIds.Add(id);
            }

            // Body-Layer (Beine, Schuhe, Rumpf, Haende, Kopf) in Inspector-
            // Reihenfolge aufbauen. Sorting-Order entspricht dem Body-Index.
            for (int i = 0; i < bodyAtlasIds.Count; i++)
            {
                string atlasName = bodyAtlasIds[i];
                FlareAtlas atlas = await loader.LoadAsync(atlasName);
                FlareLayerAnimator layer = CreateLayer(visualsRoot.transform, atlasName, i);
                layer.SetAtlas(atlas);
                character.RegisterLayer(layer);
            }

            // Equipment-Schichten als feste, namensbasierte Slots ueber den Body-
            // Layern. Atlas ist initial leer und wird gleich von
            // PlayerEquipmentVisuals.Bind aus den PlayerCombat-NetVars befuellt;
            // legacyMainHand-/legacyOffHand-Werte aus dem Prefab dienen nur als
            // Fallback, falls der Server keinen Default gesetzt hat.
            int mainHandOrder = bodyAtlasIds.Count;
            int offHandOrder = bodyAtlasIds.Count + 1;
            int rangedOrder = bodyAtlasIds.Count + 2;
            FlareLayerAnimator mainHandLayer = CreateLayer(visualsRoot.transform, PlayerEquipmentVisuals.MainHandLayerName, mainHandOrder);
            character.RegisterLayer(mainHandLayer);
            FlareLayerAnimator offHandLayer = CreateLayer(visualsRoot.transform, PlayerEquipmentVisuals.OffHandLayerName, offHandOrder);
            character.RegisterLayer(offHandLayer);
            // Ranged-Schicht (Bow/Crossbow/Gun) liegt ueber MainHand+OffHand, ist
            // initial leer und wird von PlayerEquipmentVisuals.ShowRangedForCast
            // waehrend eines Schuss-Casts mit dem Bogen-Atlas befuellt. Muss
            // hier registriert werden, weil FlareCharacter.SetLayerAtlas eine
            // bereits registrierte Schicht mit passendem GameObject-Namen
            // braucht; ohne Registration bleibt der Bogen unsichtbar.
            FlareLayerAnimator rangedLayer = CreateLayer(visualsRoot.transform, PlayerEquipmentVisuals.RangedLayerName, rangedOrder);
            character.RegisterLayer(rangedLayer);

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

                // Equip-Visuals-Bruecke: lauscht auf PlayerCombat.WeaponChanged /
                // OffhandChanged und tauscht die FLARE-Atlanten der MainHand-/
                // OffHand-Schichten. Wird hier per AddComponent angehaengt, damit
                // das Prefab nicht editiert werden muss; Bind versorgt sie sofort
                // mit dem aktuellen NetVar-Stand (Server-Default).
                PlayerEquipmentVisuals equipVisuals = GetComponent<PlayerEquipmentVisuals>();
                if (equipVisuals == null)
                {
                    equipVisuals = gameObject.AddComponent<PlayerEquipmentVisuals>();
                }
                equipVisuals.Bind(character, loader, combat);
            }
            else
            {
                Debug.LogWarning("[GamePlayerBootstrap] PlayerCombat nicht am Root — Attack-Input wird ignoriert.", this);

                // Ohne PlayerCombat keine Equip-NetVars — wir muessen die Legacy-
                // Werte aus dem Prefab manuell auf die Slot-Layer setzen, sonst
                // bleiben MainHand/OffHand komplett leer.
                if (!string.IsNullOrEmpty(legacyMainHandFromPrefab))
                {
                    FlareAtlas mainAtlas = await loader.LoadAsync(legacyMainHandFromPrefab);
                    character.SetLayerAtlas(PlayerEquipmentVisuals.MainHandLayerName, mainAtlas);
                }
                if (!string.IsNullOrEmpty(legacyOffHandFromPrefab))
                {
                    FlareAtlas offAtlas = await loader.LoadAsync(legacyOffHandFromPrefab);
                    character.SetLayerAtlas(PlayerEquipmentVisuals.OffHandLayerName, offAtlas);
                }
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

        /// <summary>
        /// Holt einen Catalog-Loader aus dem ServiceLocator und stellt sicher,
        /// dass die JSON geladen ist. Liefert <c>null</c>, wenn der Loader fehlt
        /// (z. B. in fruehen Editor-Playmodes ohne ApplicationEntryPoint).
        /// </summary>
        private static async Task<TCatalog> EnsureCatalogAsync<TLoader, TCatalog>(System.Func<TLoader, Task<TCatalog>> load)
            where TLoader : class
            where TCatalog : class
        {
            TLoader loader = ServiceLocator.Get<TLoader>();
            if (loader == null)
            {
                return null;
            }
            return await load(loader);
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
