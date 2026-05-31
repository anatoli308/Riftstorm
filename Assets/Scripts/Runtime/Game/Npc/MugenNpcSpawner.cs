using System.IO;
using System.Threading.Tasks;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Sprites;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Lädt einen aus MUGEN konvertierten FLARE-NPC (eine Sprite-Schicht) aus
    /// <c>StreamingAssets/Custom_Characters/&lt;name&gt;/</c> und spielt die
    /// gewählte Animation ab. Pro NPC-Instanz im Scene-Graph eine Komponente.
    /// </summary>
    /// <remarks>
    /// Erwartet folgende Dateien im konfigurierten Unterordner:
    /// <list type="bullet">
    /// <item><c>&lt;AtlasName&gt;.json</c> — FLARE-Manifest (per <c>mugen_to_flare.py</c> erzeugt).</item>
    /// <item><c>atlas.png</c> — Atlas-Textur, im JSON als <c>"image"</c> referenziert.</item>
    /// <item><c>&lt;AtlasName&gt;.stats.json</c> — <i>optional</i>, MUGEN-abgeleitete Base-Stats.</item>
    /// </list>
    /// Liegt sowohl JSON als auch PNG im selben Ordner, ist keine getrennte
    /// Texturpfad-Konfiguration nötig.
    /// <para>
    /// <b>Erwarteter Component-Stack (Prefab "Mugen_NPC"):</b>
    /// <list type="bullet">
    /// <item><see cref="Unity.Netcode.NetworkObject"/> — Netcode-Identity.</item>
    /// <item><see cref="UnitStats"/> — Single Source of Truth für HP/Mana/STR/ARM/Speed/Range/DisplayName.</item>
    /// <item><see cref="MugenNpcSpawner"/> (diese Komponente) — lädt Atlas + füttert UnitStats.</item>
    /// <item>Collider passend zu <see cref="UnitStats.HitRadius"/> (z. B. CapsuleCollider).</item>
    /// <item>Targetable / NavAgent / AI nach Bedarf.</item>
    /// </list>
    /// Pro NPC-Variante (Mudpenis, Gallon, …) wird das Prefab instanziiert und
    /// nur <c>StreamingSubFolder</c> + <c>AtlasName</c> umkonfiguriert.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class MugenNpcSpawner : MonoBehaviour
    {
        [Header("Asset")]
        [Tooltip("Unterordner unter Application.streamingAssetsPath, z. B. 'Custom_Characters/Mudpenis'.")]
        [SerializeField] private string m_StreamingSubFolder = "Custom_Characters/Mudpenis";

        [Tooltip("Dateiname (ohne .json) des FLARE-Atlas, z. B. 'Mudpenis'.")]
        [SerializeField] private string m_AtlasName = "Mudpenis";

        [Header("Wiedergabe")]
        [Tooltip("Animationsname, der beim Spawn gestartet wird. MUGEN-Action 0 -> 'stance'.")]
        [SerializeField] private string m_InitialAnimation = "stance";

        [Tooltip("FLARE-Richtung (0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW). MUGEN-Sprites sind 2D-Sideview, daher 4=E sinnvoll.")]
        [Range(0, 7)]
        [SerializeField] private int m_InitialFlareDirection = 4;

        [Header("Rendering")]
        [Tooltip("Sorting-Order des erzeugten SpriteRenderer.")]
        [SerializeField] private int m_SortingOrder;

        [Tooltip("Lokale Euler-Rotation des Visual-Childs. (90,0,0) flacht das Sprite für eine Topdown-Welt auf den Boden, (0,0,0) lässt es aufrecht.")]
        [SerializeField] private Vector3 m_VisualEulerAngles = Vector3.zero;

        [Header("MUGEN-Stats")]
        [Tooltip("Wenn aktiv und eine <Atlas>.stats.json existiert, werden HP/Mana/STR/ARM/HitRadius/Speeds/Ranges/DisplayName auf einem UnitStats am gleichen GameObject überschrieben und visualScale an den Visuals-Child angewandt.")]
        [SerializeField] private bool m_ApplyStatsFromSidecar = true;

        private FlareCharacter m_Character;

        /// <summary>
        /// Geladene MUGEN-Stats-DTO, gecacht aus <see cref="Awake"/>. Wird in
        /// <see cref="BuildAsync"/> an den <see cref="MugenHitboxRuntime"/> weitergegeben,
        /// damit dieser <c>PixelsPerMeter</c> aus dem Sidecar liest statt Default 100.
        /// </summary>
        private MugenCharacterStats m_Stats;

        /// <summary>
        /// Visuelle Sprite-Skalierung aus der MUGEN-Stats-Sidecar (xscale/yscale).
        /// Wird in <see cref="Awake"/> aus der DTO entnommen und in
        /// <see cref="BuildAsync"/> auf den Visuals-Child angewandt. <c>null</c> =
        /// keine Stats geladen ⇒ kein Scale-Override.
        /// </summary>
        private Vector2? m_VisualScaleOverride;

        /// <summary>Liefert den erzeugten <see cref="FlareCharacter"/> oder <c>null</c>, solange noch geladen wird.</summary>
        public FlareCharacter Character => m_Character;

        private void Awake()
        {
            // Stats synchron laden, damit UnitStats VOR OnNetworkSpawn überschrieben wird.
            // Die DTO ist transient — nur die VisualScale wird für BuildAsync gemerkt.
            // Combat/Movement/Range/Name landen direkt in UnitStats (Single Source of Truth).
            if (!m_ApplyStatsFromSidecar)
            {
                return;
            }
            MugenCharacterStats stats = MugenCharacterStatsLoader.LoadOrNull(m_StreamingSubFolder, m_AtlasName);
            if (stats == null)
            {
                return;
            }
            m_Stats = stats;

            Vector2 scale = stats.VisualScaleVector;
            if (scale.x > 0f && scale.y > 0f)
            {
                m_VisualScaleOverride = scale;
            }

            if (TryGetComponent<UnitStats>(out var unitStats))
            {
                unitStats.ApplyBaseStats(
                    maxHp: stats.MaxHp,
                    maxMana: stats.MaxMana,
                    strength: stats.Strength,
                    armor: stats.Armor,
                    hitRadius: stats.HitRadius,
                    selectionRadius: 0f,
                    walkSpeed: stats.WalkSpeed,
                    runSpeed: stats.RunSpeed,
                    attackRange: stats.AttackRange,
                    projectileRange: stats.ProjectileRange,
                    displayName: stats.DisplayName);
            }
            else
            {
                Debug.LogWarning(
                    $"[MugenNpcSpawner] Kein UnitStats am GameObject '{name}' — " +
                    "MUGEN-Stats werden NICHT angewandt. Prefab unvollständig?",
                    this);
            }
        }

        private void Start()
        {
            _ = BuildAsync();
        }

        private async Task BuildAsync()
        {
            if (string.IsNullOrEmpty(m_StreamingSubFolder) || string.IsNullOrEmpty(m_AtlasName))
            {
                Debug.LogError("[MugenNpcSpawner] StreamingSubFolder und AtlasName müssen gesetzt sein.", this);
                return;
            }

            string folder = Path.Combine(Application.streamingAssetsPath, m_StreamingSubFolder);
            FlareAtlasLoader loader = new(folder, folder);
            FlareAtlas atlas = await loader.LoadAsync(m_AtlasName);
            if (atlas == null)
            {
                // Loader hat bereits ein Warning geloggt. Hier nur den Kontext markieren.
                Debug.LogWarning($"[MugenNpcSpawner] Atlas '{m_AtlasName}' konnte aus '{folder}' nicht geladen werden.", this);
                return;
            }

            // Eine Sprite-Schicht reicht für MUGEN-Single-Atlas-Charaktere. FlareCharacter
            // dient als gemeinsamer Steuerungs-Entrypoint (Play / SetDirection) — analog
            // zum Player-Bootstrap.
            GameObject visualsRoot = new("Visuals");
            visualsRoot.transform.SetParent(transform, false);
            visualsRoot.transform.localRotation = Quaternion.Euler(m_VisualEulerAngles);

            // Sprite-Scale aus MUGEN size.xscale/yscale anwenden (z. B. 0.5 für Mudpenis).
            // Wert wurde in Awake aus der transient geladenen Stats-DTO gecacht.
            if (m_VisualScaleOverride.HasValue)
            {
                Vector2 scale = m_VisualScaleOverride.Value;
                visualsRoot.transform.localScale = new(scale.x, scale.y, 1f);
            }

            SpriteRenderer renderer = visualsRoot.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = m_SortingOrder;
            FlareLayerAnimator layer = visualsRoot.AddComponent<FlareLayerAnimator>();

            FlareCharacter character = gameObject.AddComponent<FlareCharacter>();
            character.RegisterLayer(layer);
            layer.SetAtlas(atlas);

            character.Play(m_InitialAnimation, true);
            character.SetDirection(m_InitialFlareDirection);
            m_Character = character;

            // Combat-Adapter für per-Frame MUGEN-Hitboxen. Reines MonoBehaviour, kein
            // NetworkBehaviour: liest nur transform.position + FlareCharacter-State und
            // wird vom NpcController server-seitig via Physics.OverlapBox abgefragt.
            if (!TryGetComponent<MugenHitboxRuntime>(out var hitboxRuntime))
            {
                hitboxRuntime = gameObject.AddComponent<MugenHitboxRuntime>();
            }
            hitboxRuntime.BindCharacter(character);
            if (m_Stats != null)
            {
                hitboxRuntime.BindStats(m_Stats);
            }

            // FLARE-Character wird async erzeugt. NpcController.Awake hat den noch nicht
            // gesehen (GetComponentInChildren liefert null vor BuildAsync). Ohne dieses
            // Wire-Up bleibt UpdateVisuals/CombatVisuals stumm und der NPC steht in der
            // initialen Richtung/Anim fest.
            if (TryGetComponent<NpcController>(out var controller))
            {
                controller.BindCharacter(character);
                // Mugen-Skill-Pool wurde mit dem FLARE-Port aus NpcController entfernt.
                // MugenNpcSpawner laeuft hier nur noch fuer Visuals; AI nutzt FLARE-Defaults.
            }
            else
            {
                // Kein NpcController -> dennoch CombatVisuals direkt anbinden, falls
                // vorhanden (z. B. fuer Test-Dummies ohne AI).
                if (TryGetComponent<UnitCombatVisuals>(out var visuals))
                {
                    visuals.BindCharacter(character);
                }
            }
        }
    }
}

