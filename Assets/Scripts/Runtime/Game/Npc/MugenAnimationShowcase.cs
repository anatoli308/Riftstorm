using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Sprites;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Debug-Showcase für einen aus MUGEN konvertierten FLARE-Charakter. Zeigt
    /// entweder jede Einzelanimation des Atlas oder — als Move-Modus — jeden aus
    /// der State-Machine abgeleiteten Move so, wie die Engine ihn darstellt:
    /// Body-Kette, Effekte und <c>PlaySnd</c> gleichzeitig auf einer Zeitachse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wird neben einem <see cref="MugenNpcSpawner"/> platziert. Sobald dieser
    /// asynchron seinen <see cref="FlareCharacter"/> gebaut hat, übernimmt die
    /// Showcase die Steuerung und deaktiviert <see cref="NpcController"/> sowie
    /// <see cref="UnitCombatVisuals"/>, damit niemand parallel Animationen setzt.
    /// </para>
    /// <para>
    /// <b>Hotkeys:</b> <c>Tab</c> Modus wechseln · <c>Left/Right</c> ein Move ·
    /// <c>Up/Down</c> Kategorie · <c>PageUp/PageDown</c> zehn Moves · <c>Space</c> Pause ·
    /// <c>R</c> Neustart · <c>D</c> Richtung +1 · <c>F</c> Vollbild-Effekte ·
    /// <c>G</c> Trainingspuppe · <c>B</c> Stage-Hintergrund · <c>M</c> Ton.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MugenNpcSpawner))]
    public sealed class MugenAnimationShowcase : MonoBehaviour
    {
        /// <summary>Schrittweite von PageUp/PageDown in der Move-Liste.</summary>
        private const int PageStep = 10;

        /// <summary>Sammelkategorie ohne Filter.</summary>
        private const string AllCategories = "Alle";

        /// <summary>
        /// Kategorie für Atlas-Animationen, die keine einzige Sequenz anspricht.
        /// MUGEN-Autoren legen Opfer-, Partner- und Bonus-Posen häufig in eigene
        /// Nummernbänder, die nur ein fremder Charakter oder eine externe Datei
        /// aufruft — ohne diese Kategorie blieben sie im Move-Modus unsichtbar.
        /// </summary>
        private const string OrphanCategory = "Nur Atlas";

        /// <summary>Umrechnung MUGEN-Pixel → Unity-Einheiten (muss zum Atlas passen).</summary>
        private const float MugenPixelsPerUnit = 64f;

        // MUGENs eigene Vorgaben, falls keine Stage geladen ist: localcoord 320×240,
        // Boden am unteren Bildrand, Kämpfer 140 Pixel auseinander.
        private const float DefaultScreenWidth = 320f;
        private const float DefaultScreenHeight = 240f;
        private const float DefaultPlayerDistance = 140f;

        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [Header("Modus")]
        [Tooltip("Startet im Move-Modus (Body + Effekte + Sound gleichzeitig). Aus = Liste aller Einzelanimationen.")]
        [SerializeField] private bool m_StartInMoveMode = true;

        [Header("Einzelanimationen")]
        [Tooltip("Mindest-Haltezeit pro Animation in Sekunden.")]
        [Min(0.05f)]
        [SerializeField] private float m_HoldSecondsPerAnim = 1.5f;

        [Tooltip("Verlängert die Haltezeit auf die echte Atlas-Dauer, damit jeder Frame sichtbar wird.")]
        [SerializeField] private bool m_UseAtlasDurationIfLonger = true;

        [Tooltip("Zusätzliche Pause nach jeder Animation in Sekunden.")]
        [Min(0f)]
        [SerializeField] private float m_PauseBetweenAnims = 0.15f;

        [Tooltip("Schaltet ohne Tastendruck zur nächsten Animation weiter.")]
        [SerializeField] private bool m_AutoAdvance = true;

        [Tooltip("Animationsnamen, die nie gezeigt werden sollen.")]
        [SerializeField] private string[] m_SkipAnimations;

        [Header("Move-Kategorien")]
        [Tooltip("Reihenfolge der Kategorien hinter 'Alle'. Kategorien ohne Moves werden übersprungen.")]
        [SerializeField] private string[] m_Categories =
        {
            "Normal", "Special", "Hyper", "Command", "Hit", "Custom", "System", "Helper", "Effect",
            "Global"
        };

        [Tooltip("Atlas-Animationen, die keine Sequenz anspricht, als eigene Kategorie 'Nur Atlas' anbieten.")]
        [SerializeField] private bool m_ShowOrphanAnimations = true;

        [Header("Richtung")]
        [Tooltip("Initiale FLARE-Richtung (0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW).")]
        [Range(0, 7)]
        [SerializeField] private int m_StartDirection = 4;

        [Header("Bühne")]
        [Tooltip("Ordner unter StreamingAssets/Custom_Stages mit der konvertierten MUGEN-Stage, z. B. 'Agony'. Sie liefert Bodenhöhe, Bildausschnitt und die Standplätze beider Kämpfer.")]
        [SerializeField] private string m_StageFolder = "Agony";

        [Tooltip("Eigene orthografische Kamera, die genau den MUGEN-Bildausschnitt (localcoord) zeigt — so groß wie im Original. Aus = die Kamera der Szene bleibt.")]
        [SerializeField] private bool m_UseStageCamera = true;

        [Tooltip("Bildschirmverankerte Effekte (postype left/right/front/back) zeigen — Vollbild-Flashes, Balken und Hintergrund-Panels eines Hypers.")]
        [SerializeField] private bool m_ShowScreenEffects = true;

        [Tooltip("Trainingspuppe auf p2start zeigen. Sie erscheint nur bei Moves mit HitDef und liefert den Bezugspunkt für postype=p2 und Trefferfunken.")]
        [SerializeField] private bool m_ShowDummy = true;

        [Tooltip("Mindest-Sichtdauer eines Effekts in Sekunden. 0 = exakte MUGEN-Zeiten; höhere Werte halten sehr kurze Glows (removetime=1) länger sichtbar.")]
        [Min(0f)]
        [SerializeField] private float m_MinEffectVisibleSeconds;

        [Header("Ton")]
        [Tooltip("PlaySnd-Aufrufe des Moves tickgenau abspielen.")]
        [SerializeField] private bool m_PlaySounds = true;

        [Range(0f, 1f)]
        [SerializeField] private float m_SoundVolume = 0.6f;

        [Header("UI")]
        [Tooltip("IMGUI-Overlay mit Move-Liste, Timeline und Hotkeys anzeigen.")]
        [SerializeField] private bool m_ShowGui = true;

        // -------------------------------------------------------------------------
        // Interner State
        // -------------------------------------------------------------------------

        private MugenNpcSpawner m_Spawner;
        private FlareCharacter m_Character;
        private FlareLayerAnimator m_Layer;
        private MugenMoveStage m_Stage;
        private MugenSoundBank m_SoundBank;
        private AudioSource m_AudioSource;
        private MugenStage m_StageDefinition;
        private MugenStageBackground m_Background;
        private Transform m_ShowcaseRoot;
        private Camera m_StageCamera;
        private bool m_ShowStage = true;

        private readonly List<string> m_AnimNames = new();
        private int m_AnimIndex = -1;
        private float m_NextAdvanceAt;

        private readonly List<FlareSequence> m_AllMoves = new();
        private readonly List<FlareSequence> m_Moves = new();
        private readonly List<string> m_AvailableCategories = new();
        private int m_CategoryIndex;
        private int m_MoveIndex = -1;
        private bool m_MoveBuildPending;

        private bool m_MoveMode;
        private bool m_Paused;
        private int m_Direction;
        private bool m_ConflictingComponentsDisabled;

        // -------------------------------------------------------------------------
        // Unity
        // -------------------------------------------------------------------------

        private void Awake()
        {
            m_Spawner = GetComponent<MugenNpcSpawner>();
            m_Direction = Mathf.Clamp(m_StartDirection, 0, 7);
        }

        private void Update()
        {
            // Der Spawner baut den FlareCharacter asynchron erst nach Awake.
            if (m_Character == null && !TryInitialize())
            {
                return;
            }

            HandleInput();

            if (m_Paused)
            {
                return;
            }
            m_Background?.Tick(Time.deltaTime);
            if (m_MoveMode)
            {
                m_Stage.Tick(Time.deltaTime);
                return;
            }
            if (m_AutoAdvance && Time.time >= m_NextAdvanceAt)
            {
                PlayAnimation(m_AnimIndex + 1);
            }
        }

        private void OnDisable()
        {
            m_Stage?.Clear();
        }

        private void OnDestroy()
        {
            m_Background?.Dispose();
            m_Stage?.Dispose();
            m_SoundBank?.ClearCache();
            if (m_ShowcaseRoot != null)
            {
                Destroy(m_ShowcaseRoot.gameObject);
            }
        }

        private void OnGUI()
        {
            if (!m_ShowGui || m_Character == null)
            {
                return;
            }
            if (m_MoveMode)
            {
                DrawMoveGui();
            }
            else
            {
                DrawAnimationGui();
            }
        }

        // -------------------------------------------------------------------------
        // Initialisierung
        // -------------------------------------------------------------------------

        /// <summary>
        /// Hängt sich an den fertig gebauten Charakter, liest Animationen und Moves
        /// aus dem Atlas und startet den konfigurierten Modus. Liefert false,
        /// solange der Spawner noch lädt.
        /// </summary>
        private bool TryInitialize()
        {
            FlareCharacter character = m_Spawner != null ? m_Spawner.Character : null;
            if (character == null)
            {
                character = GetComponentInChildren<FlareCharacter>();
            }
            FlareLayerAnimator layer = GetComponentInChildren<FlareLayerAnimator>();
            if (character == null || layer == null || layer.Atlas == null)
            {
                return false;
            }

            m_Character = character;
            m_Layer = layer;
            DisableConflictingComponents();

            BuildAnimationList();
            if (m_AnimNames.Count == 0)
            {
                Debug.LogWarning(
                    $"[MugenAnimationShowcase] Atlas auf '{name}' enthält keine Animationen — Showcase aus.", this);
                enabled = false;
                return false;
            }

            m_AudioSource = gameObject.AddComponent<AudioSource>();
            m_AudioSource.playOnAwake = false;
            m_AudioSource.spatialBlend = 0f;
            m_AudioSource.volume = m_PlaySounds ? m_SoundVolume : 0f;
            m_SoundBank = new MugenSoundBank(m_Spawner.SoundFolder);
            m_ShowcaseRoot = BuildShowcaseRoot();
            m_Stage = new MugenMoveStage(m_Character, m_Layer, m_SoundBank, m_AudioSource);
            _ = LoadStageAsync();

            BuildMoveCatalog();
            if (m_StartInMoveMode && m_Moves.Count > 0)
            {
                m_MoveMode = true;
                SelectMove(0);
            }
            else
            {
                PlayAnimation(0);
            }
            return true;
        }

        /// <summary>
        /// Schaltet <see cref="NpcController"/> und <see cref="UnitCombatVisuals"/> ab,
        /// damit ihre Update-Loops nicht parallel Animationen setzen.
        /// </summary>
        private void DisableConflictingComponents()
        {
            if (m_ConflictingComponentsDisabled)
            {
                return;
            }
            m_ConflictingComponentsDisabled = true;
            if (TryGetComponent<NpcController>(out var controller))
            {
                controller.enabled = false;
            }
            if (TryGetComponent<UnitCombatVisuals>(out var visuals))
            {
                visuals.enabled = false;
            }
        }

        /// <summary>Sammelt alle Atlas-Animationen abzüglich der Skip-Liste.</summary>
        private void BuildAnimationList()
        {
            m_AnimNames.Clear();
            foreach (KeyValuePair<string, FlareAnimation> entry in m_Layer.Atlas.Animations)
            {
                if (!string.IsNullOrEmpty(entry.Key) && !IsSkipped(entry.Key))
                {
                    m_AnimNames.Add(entry.Key);
                }
            }
            m_AnimNames.Sort();
        }

        private bool IsSkipped(string animationName)
        {
            if (m_SkipAnimations == null)
            {
                return false;
            }
            for (int i = 0; i < m_SkipAnimations.Length; i++)
            {
                if (m_SkipAnimations[i] == animationName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Sammelt jede Sequenz des Atlas, hängt die von keiner Sequenz benutzten
        /// Atlas-Animationen als Ersatz-Moves an und leitet daraus die belegten
        /// Kategorien ab. Sortiert wird nach Statedef-Nummer — dieselbe Reihenfolge,
        /// in der die Zustände in den CNS-Dateien stehen.
        /// </summary>
        private void BuildMoveCatalog()
        {
            m_AllMoves.Clear();
            var referenced = new HashSet<string>();
            foreach (FlareSequence sequence in m_Layer.Atlas.Sequences)
            {
                if (sequence == null || !HasVisibleContent(sequence))
                {
                    continue;
                }
                m_AllMoves.Add(sequence);
                CollectReferencedAnimations(sequence, referenced);
            }
            if (m_ShowOrphanAnimations)
            {
                AppendOrphanMoves(referenced);
            }
            m_AllMoves.Sort((a, b) => a.State.CompareTo(b.State));

            BuildCategoryList();
            ApplyCategoryFilter();
        }

        /// <summary>
        /// True, wenn die Sequenz überhaupt etwas darstellt. Ein Statedef ohne
        /// eigene Body-Animation ist kein leerer Zustand: Wurf-Opferposen leben
        /// allein in <c>victim</c>, Partikelquellen allein in <c>effects</c> und
        /// Projektil-Zustände allein in <c>helpers</c>.
        /// </summary>
        private static bool HasVisibleContent(FlareSequence sequence)
        {
            return sequence.Anims.Length > 0
                || sequence.Effects.Length > 0
                || sequence.Victim.Length > 0
                || sequence.Helpers.Length > 0;
        }

        /// <summary>Trägt jeden Animationsschlüssel ein, den die Sequenz irgendwo verwendet.</summary>
        private static void CollectReferencedAnimations(FlareSequence sequence, HashSet<string> target)
        {
            foreach (FlareSequenceAnim anim in sequence.Anims)
            {
                target.Add(anim.Anim);
            }
            foreach (FlareSequenceAnim victim in sequence.Victim)
            {
                target.Add(victim.Anim);
            }
            foreach (FlareSequenceEffect effect in sequence.Effects)
            {
                target.Add(effect.Anim);
            }
            foreach (FlareSequenceHit hit in sequence.Hits)
            {
                if (!string.IsNullOrEmpty(hit.Spark))
                {
                    target.Add(hit.Spark);
                }
            }
            foreach (FlareSequenceHelper helper in sequence.Helpers)
            {
                foreach (FlareSequenceAnim anim in helper.Anims)
                {
                    target.Add(anim.Anim);
                }
                foreach (FlareSequenceEffect effect in helper.Effects)
                {
                    target.Add(effect.Anim);
                }
            }
        }

        /// <summary>Baut aus jeder unreferenzierten Atlas-Animation einen Ein-Anim-Move.</summary>
        private void AppendOrphanMoves(HashSet<string> referenced)
        {
            foreach (KeyValuePair<string, FlareAnimation> entry in m_Layer.Atlas.Animations)
            {
                if (string.IsNullOrEmpty(entry.Key) || referenced.Contains(entry.Key) || IsSkipped(entry.Key))
                {
                    continue;
                }
                m_AllMoves.Add(new FlareSequence
                {
                    State = ActionNumber(entry.Key),
                    Label = string.IsNullOrEmpty(entry.Value?.Label) ? entry.Key : entry.Value.Label,
                    Group = entry.Key,
                    Category = OrphanCategory,
                    Anims = new[] { new FlareSequenceAnim(entry.Key, 0) }
                });
            }
        }

        /// <summary>Zieht die Nummer aus einem <c>action_&lt;n&gt;</c>-Schlüssel; sonst 0.</summary>
        private static int ActionNumber(string animationKey)
        {
            int underscore = animationKey.LastIndexOf('_');
            return underscore >= 0 && int.TryParse(animationKey[(underscore + 1)..], out int number)
                ? number
                : 0;
        }

        /// <summary>Behaelt aus der Inspector-Reihenfolge nur die Kategorien mit Moves.</summary>
        private void BuildCategoryList()
        {
            m_AvailableCategories.Clear();
            m_AvailableCategories.Add(AllCategories);
            foreach (string category in m_Categories)
            {
                if (!string.IsNullOrEmpty(category) && HasCategory(category))
                {
                    m_AvailableCategories.Add(category);
                }
            }
            // Kategorien, die in den Daten stehen, aber im Inspector fehlen — sonst
            // wären ihre Moves über "Alle" hinaus nicht erreichbar.
            foreach (FlareSequence move in m_AllMoves)
            {
                if (!string.IsNullOrEmpty(move.Category) && !m_AvailableCategories.Contains(move.Category))
                {
                    m_AvailableCategories.Add(move.Category);
                }
            }
            m_CategoryIndex = Mathf.Clamp(m_CategoryIndex, 0, m_AvailableCategories.Count - 1);
        }

        private bool HasCategory(string category)
        {
            foreach (FlareSequence move in m_AllMoves)
            {
                if (move.Category == category)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Spiegelt die aktive Kategorie in <see cref="m_Moves"/>.</summary>
        private void ApplyCategoryFilter()
        {
            string category = m_AvailableCategories.Count > 0
                ? m_AvailableCategories[m_CategoryIndex]
                : AllCategories;
            m_Moves.Clear();
            foreach (FlareSequence move in m_AllMoves)
            {
                if (category == AllCategories || move.Category == category)
                {
                    m_Moves.Add(move);
                }
            }
        }

        /// <summary>Wechselt die Kategorie (mit Wrap) und startet ihren ersten Move.</summary>
        private void SelectCategory(int index)
        {
            if (m_AvailableCategories.Count == 0)
            {
                return;
            }
            m_CategoryIndex = Wrap(index, m_AvailableCategories.Count);
            ApplyCategoryFilter();
            SelectMove(0);
        }

        // -------------------------------------------------------------------------
        // Move-Modus
        // -------------------------------------------------------------------------

        /// <summary>Baut den Move am Index (mit Wrap) auf der Bühne auf.</summary>
        private void SelectMove(int index)
        {
            if (m_Moves.Count == 0)
            {
                m_MoveIndex = -1;
                m_Stage.Clear();
                return;
            }
            m_MoveIndex = Wrap(index, m_Moves.Count);
            _ = BuildCurrentMoveAsync(m_MoveIndex);
        }

        /// <summary>
        /// Baut einen Move auf. Die Bühne lädt dabei Sounds nach, deshalb asynchron;
        /// ein zwischenzeitlicher Wechsel lässt das Ergebnis verfallen.
        /// </summary>
        private async Task BuildCurrentMoveAsync(int requested)
        {
            m_MoveBuildPending = true;
            await m_Stage.BuildAsync(m_Moves[requested], m_Direction, CurrentSettings());
            if (m_MoveIndex == requested)
            {
                m_MoveBuildPending = false;
            }
        }

        /// <summary>
        /// Bühnen-Parameter des aktuellen Moves. Alle Bezugswerte stammen aus der
        /// geladenen MUGEN-Stage; fehlt sie, gelten MUGENs eigene Vorgaben
        /// (<c>localcoord</c> 320×240, Boden am unteren Bildrand).
        /// </summary>
        private MugenMoveStage.Settings CurrentSettings()
        {
            MugenStage stage = m_StageDefinition;
            return new MugenMoveStage.Settings
            {
                MinEffectVisibleSeconds = m_MinEffectVisibleSeconds,
                ScreenWidth = stage?.ScreenWidth ?? DefaultScreenWidth,
                GroundLevel = stage?.GroundLevel ?? DefaultScreenHeight,
                CameraOffsetX = stage?.P1Start.x ?? 0f,
                ShowScreenEffects = m_ShowScreenEffects,
                DummyDistance = !m_ShowDummy
                    ? 0f
                    : stage?.PlayerDistance ?? DefaultPlayerDistance,
            };
        }

        /// <summary>
        /// Lädt die konfigurierte Stage nach und baut ihren Hintergrund auf. Der
        /// laufende Move wird danach neu gebaut, damit er die neuen Bezugswerte
        /// bekommt. Fehlt die Stage, bleibt die Showcase bei ihren Inspector-Werten.
        /// </summary>
        private async Task LoadStageAsync()
        {
            if (string.IsNullOrEmpty(m_StageFolder))
            {
                return;
            }
            MugenStage stage = await new MugenStageLoader(m_StageFolder).LoadAsync();
            if (stage == null || this == null || m_Layer == null)
            {
                return;
            }

            m_StageDefinition = stage;
            m_Background = new MugenStageBackground(stage, m_ShowcaseRoot);
            m_Background.SetVisible(m_ShowStage);
            if (m_UseStageCamera)
            {
                BuildStageCamera(stage);
            }
            if (m_MoveMode && m_MoveIndex >= 0)
            {
                SelectMove(m_MoveIndex);
            }
        }

        /// <summary>
        /// Hängt die Charakter-Visuals in einen aufrechten, ungedrehten und
        /// unskalierten Bezugsraum. MUGEN zeichnet in einer flachen 2D-Ebene; die
        /// Topdown-Ausrichtung der Spielwelt würde Stage und Effekte verkippen.
        /// </summary>
        private Transform BuildShowcaseRoot()
        {
            GameObject root = new("MugenShowcase");
            root.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            Transform visuals = m_Layer.transform;
            visuals.SetParent(root.transform, worldPositionStays: false);
            visuals.localPosition = Vector3.zero;
            visuals.localRotation = Quaternion.identity;
            return root.transform;
        }

        /// <summary>
        /// Setzt eine orthografische Kamera auf, die genau den MUGEN-Bildausschnitt
        /// zeigt: <c>localcoord</c> Pixel hoch, Bildmitte auf der Stage-Mitte. Der
        /// Charakter steht dabei auf <c>p1startx</c> — also nicht mittig, genau wie
        /// im Original.
        /// </summary>
        private void BuildStageCamera(MugenStage stage)
        {
            if (m_StageCamera == null)
            {
                GameObject go = new("MugenStageCamera");
                go.transform.SetParent(m_ShowcaseRoot, worldPositionStays: false);
                m_StageCamera = go.AddComponent<Camera>();
                m_StageCamera.clearFlags = CameraClearFlags.SolidColor;
                m_StageCamera.backgroundColor = Color.black;
                m_StageCamera.orthographic = true;
                // Über der Szenenkamera, damit der MUGEN-Ausschnitt das Bild bestimmt.
                m_StageCamera.depth = 100f;
            }
            m_StageCamera.orthographicSize = stage.ScreenHeight * 0.5f / MugenPixelsPerUnit;
            m_StageCamera.transform.localPosition = new Vector3(
                -stage.P1Start.x / MugenPixelsPerUnit,
                (stage.GroundLevel - stage.ScreenHeight * 0.5f) / MugenPixelsPerUnit,
                -10f);
            m_StageCamera.transform.localRotation = Quaternion.identity;
        }

        /// <summary>Wechselt zwischen Einzelanimationen und Move-Modus.</summary>
        private void ToggleMode()
        {
            if (m_MoveMode)
            {
                m_Stage.Clear();
                m_MoveMode = false;
                PlayAnimation(Mathf.Max(0, m_AnimIndex));
                return;
            }
            if (m_Moves.Count == 0)
            {
                Debug.LogWarning(
                    $"[MugenAnimationShowcase] Atlas auf '{name}' enthält keine Move-Daten (states.json).", this);
                return;
            }
            m_MoveMode = true;
            SelectMove(Mathf.Max(0, m_MoveIndex));
        }

        // -------------------------------------------------------------------------
        // Einzelanimations-Modus
        // -------------------------------------------------------------------------

        /// <summary>Spielt die Animation am Index (mit Wrap) und zieht die Haltezeit neu auf.</summary>
        private void PlayAnimation(int index)
        {
            if (m_AnimNames.Count == 0)
            {
                return;
            }
            m_AnimIndex = Wrap(index, m_AnimNames.Count);
            m_Character.SetDirection(m_Direction);
            m_Character.Play(m_AnimNames[m_AnimIndex], force: true);

            float hold = m_HoldSecondsPerAnim;
            if (m_UseAtlasDurationIfLonger)
            {
                hold = Mathf.Max(hold, m_Character.CurrentDurationSeconds);
            }
            m_NextAdvanceAt = Time.time + hold + m_PauseBetweenAnims;
        }

        // -------------------------------------------------------------------------
        // Eingabe
        // -------------------------------------------------------------------------

        private void HandleInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                ToggleMode();
            }
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                m_Paused = !m_Paused;
                if (!m_Paused && !m_MoveMode)
                {
                    m_NextAdvanceAt = Time.time + m_HoldSecondsPerAnim + m_PauseBetweenAnims;
                }
            }
            if (keyboard.dKey.wasPressedThisFrame)
            {
                m_Direction = (m_Direction + 1) & 7;
                if (m_MoveMode)
                {
                    m_Stage.SetDirection(m_Direction);
                }
                else
                {
                    m_Character.SetDirection(m_Direction);
                }
            }
            if (keyboard.mKey.wasPressedThisFrame)
            {
                m_PlaySounds = !m_PlaySounds;
                m_AudioSource.volume = m_PlaySounds ? m_SoundVolume : 0f;
            }
            if (keyboard.bKey.wasPressedThisFrame)
            {
                m_ShowStage = !m_ShowStage;
                m_Background?.SetVisible(m_ShowStage);
            }

            if (m_MoveMode)
            {
                HandleMoveInput(keyboard);
            }
            else
            {
                HandleAnimationInput(keyboard);
            }
        }

        private void HandleMoveInput(Keyboard keyboard)
        {
            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                SelectMove(m_MoveIndex + 1);
            }
            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                SelectMove(m_MoveIndex - 1);
            }
            if (keyboard.downArrowKey.wasPressedThisFrame)
            {
                SelectCategory(m_CategoryIndex + 1);
            }
            if (keyboard.upArrowKey.wasPressedThisFrame)
            {
                SelectCategory(m_CategoryIndex - 1);
            }
            if (keyboard.pageDownKey.wasPressedThisFrame)
            {
                SelectMove(m_MoveIndex + PageStep);
            }
            if (keyboard.pageUpKey.wasPressedThisFrame)
            {
                SelectMove(m_MoveIndex - PageStep);
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                m_Stage.Restart();
            }
            if (keyboard.fKey.wasPressedThisFrame)
            {
                m_ShowScreenEffects = !m_ShowScreenEffects;
                SelectMove(m_MoveIndex);
            }
            if (keyboard.gKey.wasPressedThisFrame)
            {
                m_ShowDummy = !m_ShowDummy;
                SelectMove(m_MoveIndex);
            }
        }

        private void HandleAnimationInput(Keyboard keyboard)
        {
            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                PlayAnimation(m_AnimIndex + 1);
            }
            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                PlayAnimation(m_AnimIndex - 1);
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                PlayAnimation(m_AnimIndex);
            }
        }

        private static int Wrap(int index, int count)
        {
            return ((index % count) + count) % count;
        }

        // -------------------------------------------------------------------------
        // HUD
        // -------------------------------------------------------------------------

        private void DrawAnimationGui()
        {
            if (m_AnimIndex < 0 || m_AnimIndex >= m_AnimNames.Count)
            {
                return;
            }
            string current = m_AnimNames[m_AnimIndex];
            m_Layer.Atlas.TryGet(current, out FlareAnimation anim);
            string label = anim != null && !string.IsNullOrEmpty(anim.Label) ? anim.Label : "(unbenannt)";

            GUI.Box(new Rect(10, 10, 380, 150), "Einzelanimationen");
            GUILayout.BeginArea(new Rect(20, 35, 360, 120));
            GUILayout.Label($"[{m_AnimIndex + 1}/{m_AnimNames.Count}] {current}");
            GUILayout.Label($"Move: {label}");
            GUILayout.Label(anim != null
                ? $"Typ: {anim.Type}   Frames: {anim.FramesCount}   Dauer: {anim.DurationSeconds:F2}s"
                : "Typ: ?");
            GUILayout.Label($"Frame: {m_Character.CurrentFrameIndex}   Richtung: {m_Direction}   Pause: {m_Paused}");
            GUILayout.Label("Tab=Moves  Left/Right=Blättern  R=Neustart  D=Richtung");
            GUILayout.EndArea();
        }

        private void DrawMoveGui()
        {
            if (m_MoveIndex < 0 || m_MoveIndex >= m_Moves.Count)
            {
                GUI.Box(new Rect(10, 10, 380, 60), "Moves");
                GUILayout.BeginArea(new Rect(20, 35, 360, 30));
                GUILayout.Label("Keine Moves in dieser Kategorie. Up/Down = wechseln.");
                GUILayout.EndArea();
                return;
            }

            FlareSequence move = m_Moves[m_MoveIndex];
            IReadOnlyList<MugenMoveStage.EffectLayer> layers = m_Stage.Layers;
            int rows = Mathf.Min(layers.Count, 14);
            int height = 188 + rows * 18;

            GUI.Box(new Rect(10, 10, 660, height), "Moves — Body + Effekte + Sound");
            GUILayout.BeginArea(new Rect(20, 35, 640, height - 30));
            GUILayout.Label(
                $"Kategorie [{m_CategoryIndex + 1}/{m_AvailableCategories.Count}] " +
                $"{m_AvailableCategories[m_CategoryIndex]}    " +
                $"Move [{m_MoveIndex + 1}/{m_Moves.Count}]    von {m_AllMoves.Count} gesamt");
            GUILayout.Label(
                $"State {move.State} — {(string.IsNullOrEmpty(move.Label) ? "(unbenannt)" : move.Label)}" +
                (string.IsNullOrEmpty(move.Group) ? "" : $"   [{move.Group}]") +
                (m_MoveBuildPending ? "   (lädt …)" : ""));
            GUILayout.Label($"Body: {DescribeBody(move)}");
            GUILayout.Label(
                $"t={m_Stage.Elapsed:F2}s / {m_Stage.CycleSeconds:F2}s    " +
                $"Effekte: {layers.Count}/{move.Effects.Length}    Sounds: {move.Sounds.Length}    " +
                $"PalFX: {move.PalFx.Length}    Pausen: {move.Pauses.Length}    " +
                $"Bewegung: {move.Moves.Length}{(move.Airborne ? " (Luft)" : "")}    " +
                $"Helfer: {move.Helpers.Length}    Treffer: {CountHits(move)}    " +
                $"Opfer-Anims: {move.Victim.Length}");

            for (int i = 0; i < rows; i++)
            {
                MugenMoveStage.EffectLayer layer = layers[i];
                FlareSequenceEffect fx = layer.Spec;
                string mark = layer.Done ? "x" : (layer.Visible ? "*" : "-");
                string blend = fx.Blend.IsBlended
                    ? $" {fx.Blend.Trans}({fx.Blend.AlphaSrc}/{fx.Blend.AlphaDst})"
                    : "";
                string scale = Mathf.Approximately(fx.ScaleX, 1f) && Mathf.Approximately(fx.ScaleY, 1f)
                    ? ""
                    : $" sc=({fx.ScaleX:0.##},{fx.ScaleY:0.##})";
                GUILayout.Label(
                    $"{mark} {fx.Anim}  t={fx.Time} p={fx.Period} pos=({fx.PosX},{fx.PosY}) " +
                    $"{fx.PosType} prio={fx.SprPriority} rm={fx.RemoveTime}{scale}{blend}");
            }
            if (layers.Count > rows)
            {
                GUILayout.Label($"… und {layers.Count - rows} weitere");
            }

            GUILayout.Label(
                $"Richtung: {m_Direction}   Pause: {m_Paused}   " +
                $"Vollbild-FX: {(m_ShowScreenEffects ? "an" : "aus")}   Ton: {(m_PlaySounds ? "an" : "aus")}   " +
                $"Dummy: {(m_ShowDummy ? "an" : "aus")}   " +
                $"Stage: {(m_StageDefinition == null ? "—" : m_StageDefinition.Name)}" +
                $"{(m_StageDefinition != null && !m_ShowStage ? " (aus)" : "")}");
            if (m_StageDefinition != null)
            {
                GUILayout.Label(
                    $"localcoord {m_StageDefinition.ScreenWidth:0}×{m_StageDefinition.ScreenHeight:0}   " +
                    $"zoffset {m_StageDefinition.GroundLevel:0}   " +
                    $"p1 {m_StageDefinition.P1Start.x:0}   " +
                    $"Abstand {m_StageDefinition.PlayerDistance:0}");
            }
            GUILayout.Label(
                "Tab=Einzelanims  Left/Right=1 Move  Up/Down=Kategorie  PageUp/Down=10 Moves  " +
                "R=Neustart  F=Vollbild-FX  G=Dummy  B=Stage  M=Ton  D=Richtung");
            GUILayout.EndArea();
        }

        /// <summary>Treffer des Moves inklusive der auf seinen Helfern liegenden.</summary>
        private static int CountHits(FlareSequence move)
        {
            int total = move.Hits.Length;
            foreach (FlareSequenceHelper helper in move.Helpers)
            {
                total += helper.Hits.Length;
            }
            return total;
        }

        /// <summary>Formatiert die Body-Kette als <c>anim@tick</c>-Folge mit markiertem Glied.</summary>
        private string DescribeBody(FlareSequence move)
        {
            StringBuilder builder = new();
            for (int i = 0; i < move.Anims.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(" > ");
                }
                string token = $"{move.Anims[i].Anim}@{move.Anims[i].Time}";
                builder.Append(i == m_Stage.BodyStep ? $"[{token}]" : token);
            }
            return builder.Length > 0 ? builder.ToString() : "-";
        }
    }
}
