using System.IO;
using System.Threading.Tasks;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Sprites;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Spawnt einen datengetriebenen FLARE-NPC anhand einer
    /// <see cref="NpcTemplate.Entry"/>-ID aus
    /// <c>StreamingAssets/npc/_templates.json</c>. Sucht das passende
    /// <see cref="NpcModel"/> ueber <see cref="NpcTemplate.ModelId"/>,
    /// laedt dessen FLARE-Atlas, baut den Visuals-Tree auf und schreibt
    /// die Stats in den <see cref="UnitStats"/> am gleichen GameObject.
    /// </summary>
    /// <remarks>
    /// <b>Erwartetes Layout der Atlasse (default):</b>
    /// <list type="bullet">
    /// <item><c>StreamingAssets/{AtlasJsonSubFolder}/{model.Name}.json</c> — FLARE-Manifest, flach im npc-Ordner neben <c>_templates.json</c>.</item>
    /// <item><c>Assets/{AtlasTextureSubFolder}/&lt;image&gt;</c> — vom FLARE-JSON via <c>"image"</c> referenzierte PNG/atlas-Textur.</item>
    /// </list>
    /// <para>
    /// <b>Component-Stack (Prefab "Flare_NPC"):</b> NetworkObject + NetworkTransform +
    /// Collider + <see cref="UnitStats"/> + <see cref="NpcIdentity"/> +
    /// <see cref="NpcController"/> + <see cref="UnitCombatVisuals"/> +
    /// <see cref="FlareNpcSpawner"/> (diese Komponente).
    /// </para>
    /// <para>
    /// <b>Talker-NPCs</b> (siehe <see cref="NpcTemplate.IsPureTalker"/>): der
    /// <see cref="NpcController"/> wird deaktiviert, damit ein Vendor/QuestGiver
    /// nicht in die Combat-AI faellt. Visuals laufen weiter (Stance-Animation).
    /// </para>
    /// <para>
    /// Lebt parallel zum bestehenden <see cref="MugenNpcSpawner"/> — dieser
    /// bleibt unveraendert fuer MUGEN-Single-Atlas-Charaktere.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class FlareNpcSpawner : MonoBehaviour
    {
        // ---- Konfiguration ---------------------------------------------

        [Header("Daten")]
        [Tooltip("npc_template.entry — Schluessel in StreamingAssets/npc/_templates.json.")]
        [SerializeField] private int m_TemplateEntry;

        [Header("Atlas-Pfade")]
        [Tooltip("Unterordner unter Application.streamingAssetsPath, in dem die FLARE-JSONs flach liegen (neben _templates.json). Pfad zur Datei: <StreamingAssets>/<Sub>/<model.Name>.json.")]
        [SerializeField] private string m_AtlasJsonSubFolder = "npc";

        [Tooltip("Unterordner unter Application.dataPath (= Assets/), in dem die zugehoerigen PNG-Texturen liegen. Pfad zur Textur: <Assets>/<Sub>/<image-Feld aus JSON>. Editor-only: bei Builds muessen Texturen via Addressables/Resources kommen.")]
        [SerializeField] private string m_AtlasTextureSubFolder = "Art/npc";

        [Header("Wiedergabe")]
        [Tooltip("Animationsname, der beim Spawn gestartet wird. FLARE-Konvention: 'stance' fuer Idle.")]
        [SerializeField] private string m_InitialAnimation = "stance";

        [Tooltip("FLARE-Richtung beim Spawn (0=W..7=NW; 2=S ist Topdown-Default).")]
        [Range(0, 7)]
        [SerializeField] private int m_InitialFlareDirection = 2;

        [Header("Rendering")]
        [Tooltip("Sorting-Order des erzeugten SpriteRenderer.")]
        [SerializeField] private int m_SortingOrder;

        [Tooltip("Lokale Euler-Rotation des Visual-Childs. (90,0,0) flacht das Sprite fuer eine Topdown-Welt auf den Boden.")]
        [SerializeField] private Vector3 m_VisualEulerAngles = Vector3.zero;

        [Header("Hitbox / Selection")]
        [Tooltip("Hit-Radius (Meter), wenn das geladene Model keine Hoehe liefert oder die Konvertierung 0 ergibt.")]
        [SerializeField] private float m_MinHitRadius = 0.4f;

        [Tooltip("Divisor zur Umrechnung von npc_models.height (FLARE-Pixel/Tile-Einheiten) in Meter. Source hat keine 1:1-Beziehung, aber Height=50 (werewolf) ⇒ ~0.4 m, Height=90 (cursed_grave) ⇒ ~0.7 m bei 128.")]
        [SerializeField] private float m_FlareHeightToMeters = 128f;

        [Header("Combat-Defaults (Meter / m/s)")]
        [Tooltip("Source NpcAI: NPC_MOVE_SPEED=100 px/s. Bei FLARE PPU=64 = 1.56 m/s. Wird in UnitStats.WalkSpeed geschrieben.")]
        [SerializeField, Min(0f)] private float m_WalkSpeed = 1.5f;

        [Tooltip("Source EVADE_SPEED_MULTIPLIER=2.0. Wird hier als RunSpeed propagiert (Evade nutzt Walk * EvadeMul).")]
        [SerializeField, Min(0f)] private float m_RunSpeed = 3.0f;

        [Tooltip("Source DEFAULT_MELEE_RANGE=3. Wird als UnitStats.AttackRange propagiert.")]
        [SerializeField, Min(0f)] private float m_AttackRange = 3f;

        // ---- Runtime ---------------------------------------------------

        private FlareCharacter m_Character;
        private NpcTemplate m_Template;
        private NpcModel m_Model;

        /// <summary>Geladener Character (oder <c>null</c>, solange BuildAsync laeuft).</summary>
        public FlareCharacter Character => m_Character;

        /// <summary>Aufgeloestes Template (oder <c>null</c>, wenn Entry unbekannt).</summary>
        public NpcTemplate Template => m_Template;

        /// <summary>Aufgeloestes Model (oder <c>null</c>, wenn ModelId unbekannt).</summary>
        public NpcModel Model => m_Model;

        // ---- Lifecycle -------------------------------------------------

        private void Awake()
        {
            // Template + Model synchron lesen — UnitStats muss VOR OnNetworkSpawn
            // gesetzt sein, sonst verwirft ApplyBaseStats die Daten.
            m_Template = NpcCatalogLoader.GetTemplateOrNull(m_TemplateEntry);
            if (m_Template == null)
            {
                Debug.LogError(
                    $"[FlareNpcSpawner] Unbekannte npc_template.entry={m_TemplateEntry} — Spawner ohne Daten.",
                    this);
                return;
            }

            m_Model = NpcCatalogLoader.GetModelOrNull(m_Template.ModelId);
            if (m_Model == null)
            {
                Debug.LogError(
                    $"[FlareNpcSpawner] Template entry={m_TemplateEntry} ('{m_Template.Name}') verweist auf " +
                    $"unbekannte model_id={m_Template.ModelId} — Visual wird nicht geladen.",
                    this);
            }

            ApplyStatsToUnitStats(m_Template, m_Model);

            // Talker-NPC (Vendor/Quest/Gossip ohne Combat-Daten) faellt nicht in
            // die Combat-AI. Visuals laufen weiter.
            if (m_Template.IsPureTalker)
            {
                NpcController controller = GetComponent<NpcController>();
                if (controller != null)
                {
                    controller.enabled = false;
                }
            }
        }

        private void Start()
        {
            if (m_Template == null || m_Model == null)
            {
                return;
            }
            _ = BuildAsync();
        }

        // ---- Steps -----------------------------------------------------

        private void ApplyStatsToUnitStats(NpcTemplate tpl, NpcModel model)
        {
            UnitStats unitStats = GetComponent<UnitStats>();
            if (unitStats == null)
            {
                Debug.LogWarning(
                    $"[FlareNpcSpawner] Kein UnitStats am GameObject '{name}' — " +
                    "Stats aus npc_template werden NICHT angewandt. Prefab unvollstaendig?",
                    this);
                return;
            }

            // FLARE-Height liegt in Pixel/Tile-Einheiten (z. B. werewolf=50, cursed_grave=90).
            // Source nutzt sie nur fuers Sprite-Layout, hat selbst keinen Meter-Hitradius;
            // wir mappen mit einem Divisor in Meter, damit Player nicht von einem 12 m grossen
            // Wolf gehittet werden.
            float hitRadius = m_MinHitRadius;
            if (model != null && m_FlareHeightToMeters > 0f && model.Height > 0f)
            {
                hitRadius = Mathf.Max(m_MinHitRadius, model.Height / m_FlareHeightToMeters);
            }

            // npc_template-Sentinels koennen negativ sein (-1 = "DB-Default"); auf
            // safe defaults coercen, sonst verwirft UnitStats die Felder still.
            // Health = -1 ist Source-Sentinel "berechne aus Level/Rank" — siehe
            // Server/src/World/Npc.cpp::initFromTemplate (Zeile 82-89) +
            // calculateHealth (Zeile 130-147): baseHealth = 50 + level*10,
            // x3 fuer Elite, x10 fuer Boss.
            int level = ResolveLevel(tpl);
            int health = tpl.Health > 0 ? tpl.Health : CalculateSourceHealth(level, tpl.IsElite, tpl.IsBoss);
            // Mana=-1 wird wie health behandelt: wenn Sentinel UND intellect>0, berechne;
            // sonst belasse 0 (Source: Npc.cpp Zeile 91-105, calculateMana = 50 + level*5).
            int mana;
            if (tpl.Mana > 0)
            {
                mana = tpl.Mana;
            }
            else if (tpl.Mana < 0 && tpl.Intellect > 0)
            {
                mana = 50 + level * 5;
            }
            else
            {
                mana = 0;
            }
            int strength = Mathf.Max(0, tpl.Strength);
            int armor = Mathf.Max(0, tpl.Armor);

            // UnitStats uebernimmt Speed-/Range-Parameter nur, wenn > 0. Source haelt
            // NPC_MOVE_SPEED=100 px/s als global const; Riftstorm-Erweiterung erlaubt
            // per-Template-Override via tpl.MoveSpeed. Sentinel <=0 ⇒ Inspector-Default
            // (m_WalkSpeed). Range-Felder (m_AttackRange) sind weiter Inspector-getrieben,
            // weil DEFAULT_MELEE_RANGE im NpcController per Template ueberschrieben wird.
            float walkSpeed = tpl.MoveSpeed > 0f ? tpl.MoveSpeed : m_WalkSpeed;
            // npc_template.faction (FLARE: 3 = FACTION_HOSTILE, 0 = NEUTRAL, 1 = ALLY)
            // muss in UnitStats landen, sonst sieht SpellCaster jeden Cast vom Spieler
            // (FactionId aus Inspector) als same-faction → TargetFriendly. Sentinel <0 wird
            // im UnitStats ignoriert; tpl.Faction kann theoretisch <0 sein (DB-Sentinel),
            // dann auf NpcController-Default (=Template-Wert ungeprüft) zurückfallen
            // wäre falsch — coercen auf 0 (neutral) statt "Inspector behalten".
            int faction = tpl.Faction >= 0 ? tpl.Faction : 0;
            unitStats.ApplyBaseStats(
                maxHp: health,
                maxMana: mana,
                strength: strength,
                armor: armor,
                hitRadius: hitRadius,
                selectionRadius: 0f,
                walkSpeed: walkSpeed,
                runSpeed: m_RunSpeed,
                attackRange: m_AttackRange,
                projectileRange: 0f,
                displayName: tpl.Name,
                factionId: faction);
        }

        private async Task BuildAsync()
        {
            if (string.IsNullOrEmpty(m_AtlasJsonSubFolder) || string.IsNullOrEmpty(m_AtlasTextureSubFolder) || string.IsNullOrEmpty(m_Model.Name))
            {
                Debug.LogError(
                    $"[FlareNpcSpawner] AtlasJsonSubFolder ('{m_AtlasJsonSubFolder}'), AtlasTextureSubFolder ('{m_AtlasTextureSubFolder}') oder model.name ('{m_Model?.Name}') leer.",
                    this);
                return;
            }

            string jsonFolder = Path.Combine(Application.streamingAssetsPath, m_AtlasJsonSubFolder);
            string textureFolder = Path.Combine(Application.dataPath, m_AtlasTextureSubFolder);
            FlareAtlasLoader loader = new(jsonFolder, textureFolder);
            FlareAtlas atlas = await loader.LoadAsync(m_Model.Name);
            if (atlas == null)
            {
                Debug.LogWarning(
                    $"[FlareNpcSpawner] Atlas '{m_Model.Name}' konnte aus '{jsonFolder}' (JSON) / '{textureFolder}' (PNG) nicht geladen werden " +
                    $"(Template entry={m_TemplateEntry}).",
                    this);
                return;
            }

            // Visuals-Child + Sprite + Layer (analog MugenNpcSpawner).
            GameObject visualsRoot = new("Visuals");
            visualsRoot.transform.SetParent(transform, false);
            visualsRoot.transform.localRotation = Quaternion.Euler(m_VisualEulerAngles);

            SpriteRenderer renderer = visualsRoot.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = m_SortingOrder;
            FlareLayerAnimator layer = visualsRoot.AddComponent<FlareLayerAnimator>();

            FlareCharacter character = gameObject.AddComponent<FlareCharacter>();
            character.RegisterLayer(layer);
            layer.SetAtlas(atlas);

            character.Play(m_InitialAnimation, true);
            character.SetDirection(m_InitialFlareDirection);
            m_Character = character;

            // FLARE-Character wird async erzeugt. Controller/CombatVisuals haben den
            // beim Awake noch nicht gesehen — explizit wire-uppen, sonst bleiben sie
            // stumm.
            NpcController controller = GetComponent<NpcController>();
            if (controller != null && controller.enabled)
            {
                controller.BindTemplate(m_Template);
                controller.BindCharacter(character);
            }
            else
            {
                // Pure-Talker oder Prefab ohne Controller -> CombatVisuals direkt
                // anbinden, falls vorhanden, damit Schwingungen/Visuals laufen.
                UnitCombatVisuals visuals = GetComponent<UnitCombatVisuals>();
                if (visuals != null)
                {
                    visuals.BindCharacter(character);
                }
            }
        }

        // ---- Source-Stat-Sentinel-Helpers ------------------------------

        /// <summary>
        /// Port von <c>Server/src/World/Npc.cpp::initFromTemplate</c> Zeile 72-79:
        /// uniform random zwischen <c>min_level</c> und <c>max_level</c>, sonst
        /// <c>min_level</c>. Sentinel <c>&lt;=0</c> ⇒ Level 1.
        /// </summary>
        private static int ResolveLevel(NpcTemplate tpl)
        {
            int min = Mathf.Max(1, tpl.MinLevel);
            int max = tpl.MaxLevel > min ? tpl.MaxLevel : min;
            return max > min ? Random.Range(min, max + 1) : min;
        }

        /// <summary>
        /// Port von <c>Server/src/World/Npc.cpp::calculateHealth</c> (Zeile 130-147):
        /// <c>baseHealth = 50 + level*10</c>, ×3 fuer Elite, ×10 fuer Boss.
        /// </summary>
        private static int CalculateSourceHealth(int level, bool isElite, bool isBoss)
        {
            int baseHealth = 50 + level * 10;
            if (isElite) baseHealth *= 3;
            if (isBoss) baseHealth *= 10;
            return baseHealth;
        }
    }
}
