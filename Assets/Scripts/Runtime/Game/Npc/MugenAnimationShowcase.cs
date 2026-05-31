using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Sprites;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Npc
{
[DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    /// <summary>
    /// Test-/Debug-Komponente, die nacheinander <b>alle</b> Animationen des
    /// geladenen FLARE-Atlas auf einem <see cref="FlareCharacter"/> abspielt.
    /// Gedacht fuer eine isolierte SampleScene, in der man visuell pruefen
    /// will, ob jede MUGEN-konvertierte Action sauber laeuft (Frame-Anzahl,
    /// Spiegelung, Dauer).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wird neben einem <see cref="MugenNpcSpawner"/> platziert. Sobald der
    /// Spawner asynchron seinen <see cref="FlareCharacter"/> aufgebaut hat,
    /// liest die Showcase die Animationsnamen aus dem ersten gefundenen
    /// <see cref="FlareLayerAnimator"/>-Atlas und spielt sie sequentiell ab.
    /// </para>
    /// <para>
    /// <b>Konflikte mit Gameplay-Logik:</b> Damit der NPC waehrend des
    /// Showcase-Modus nicht parallel vom <see cref="NpcController"/> oder
    /// <see cref="UnitCombatVisuals"/> in Stance/Run/Swing umgeschaltet wird,
    /// werden diese beiden Komponenten beim Spawn-Abschluss deaktiviert (rein
    /// lokal, ohne Disconnect/NetworkObject-Eingriff).
    /// </para>
    /// <para>
    /// <b>Hotkeys (im Player-Mode):</b>
    /// <list type="bullet">
    /// <item><c>Space</c> &#8212; Auto-Advance pausieren / fortsetzen.</item>
    /// <item><c>RightArrow</c> &#8212; naechste Animation.</item>
    /// <item><c>LeftArrow</c> &#8212; vorherige Animation.</item>
    /// <item><c>R</c> &#8212; aktuelle Animation neu starten.</item>
    /// <item><c>D</c> &#8212; FLARE-Richtung +1 zyklisch (0..7).</item>
    /// </list>
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(MugenNpcSpawner))]
    public sealed class MugenAnimationShowcase : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [Header("Wiedergabe")]
        [Tooltip("Mindest-Haltezeit pro Animation in Sekunden. Looped-Anims (Stance/Run/Block) werden so lange gezeigt; PlayOnce-Anims spielen ggf. laenger, wenn 'UseAtlasDurationIfLonger' aktiv ist.")]
        [Min(0.05f)]
        [SerializeField] private float m_HoldSecondsPerAnim = 1.5f;

        [Tooltip("Wenn aktiv, wird die Haltezeit auf die echte Atlas-Duration der PlayOnce-Anim + Grace verlaengert, damit man jeden Frame sieht.")]
        [SerializeField] private bool m_UseAtlasDurationIfLonger = true;

        [Tooltip("Zusaetzliche Pause nach jeder Animation in Sekunden, damit der visuelle Wechsel sichtbar ist.")]
        [Min(0f)]
        [SerializeField] private float m_PauseBetweenAnims = 0.15f;

        [Tooltip("Wenn aktiv, startet die Showcase automatisch nach dem Spawn-Build. Sonst muss man die naechste Anim mit Pfeiltasten ausloesen.")]
        [SerializeField] private bool m_AutoAdvance = true;

        [Header("Richtung")]
        [Tooltip("Initiale FLARE-Richtung (0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW).")]
        [Range(0, 7)]
        [SerializeField] private int m_StartDirection = 4;

        [Tooltip("Wenn aktiv, wird vor jeder neuen Animation die Richtung um +1 erhoeht.")]
        [SerializeField] private bool m_CycleDirectionPerAnim;

        [Header("Filter")]
        [Tooltip("Optionale Whitelist. Wenn leer, werden alle Atlas-Animationen abgespielt. Sonst nur diese Namen, in dieser Reihenfolge.")]
        [SerializeField] private string[] m_OnlyAnimations;

        [Tooltip("Animationsnamen, die niemals gezeigt werden sollen (z. B. Phantom-Hit-States).")]
        [SerializeField] private string[] m_SkipAnimations;

        [Header("UI")]
        [Tooltip("IMGUI-Overlay mit aktueller Animation/Frame/Hotkeys anzeigen.")]
        [SerializeField] private bool m_ShowGui = true;

        // -------------------------------------------------------------------------
        // Interner State
        // -------------------------------------------------------------------------

        private MugenNpcSpawner m_Spawner;
        private FlareCharacter m_Character;
        private FlareLayerAnimator m_Layer;
        private readonly List<string> m_Names = new();
        private int m_Index = -1;
        private float m_NextAdvanceAt;
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
            // FlareCharacter wird vom Spawner asynchron erst NACH Awake gebaut.
            // Wir pollen einmal pro Frame, bis er da ist, dann initialisieren.
            if (m_Character == null)
            {
                TryAttachCharacter();
                if (m_Character == null)
                {
                    return;
                }
                BuildNameList();
                if (m_Names.Count == 0)
                {
                    Debug.LogWarning(
                        $"[MugenAnimationShowcase] Atlas auf '{name}' enthaelt keine Animationen — Showcase deaktiviert.",
                        this);
                    enabled = false;
                    return;
                }
                AdvanceTo(0, restart: true);
            }

            HandleInput();

            if (!m_AutoAdvance || m_Paused)
            {
                return;
            }
            if (Time.time >= m_NextAdvanceAt)
            {
                AdvanceTo(m_Index + 1, restart: false);
            }
        }

        private void OnGUI()
        {
            if (!m_ShowGui || m_Character == null || m_Index < 0 || m_Index >= m_Names.Count)
            {
                return;
            }

            string current = m_Names[m_Index];
            FlareAnimation anim = ResolveAnimation(current);
            int frame = m_Character.CurrentFrameIndex;
            int frames = anim != null ? anim.FramesCount : 0;
            float duration = anim != null ? anim.DurationSeconds : 0f;
            string type = anim != null ? anim.Type.ToString() : "?";

            GUI.Box(new(10, 10, 360, 150), "Mugen Animation Showcase");
            GUILayout.BeginArea(new(20, 35, 340, 120));
            GUILayout.Label($"[{m_Index + 1}/{m_Names.Count}] {current}");
            GUILayout.Label($"Type: {type}   Frames: {frames}   Duration: {duration:F2}s");
            GUILayout.Label($"Frame: {frame}   Dir: {m_Direction}   Paused: {m_Paused}");
            GUILayout.Label("Space=Pause  Left/Right=Step  R=Restart  D=Dir+1");
            GUILayout.EndArea();
        }

        // -------------------------------------------------------------------------
        // Intern
        // -------------------------------------------------------------------------

        private void TryAttachCharacter()
        {
            FlareCharacter character = m_Spawner != null ? m_Spawner.Character : null;
            if (character == null)
            {
                character = GetComponentInChildren<FlareCharacter>();
            }
            if (character == null)
            {
                return;
            }

            FlareLayerAnimator layer = GetComponentInChildren<FlareLayerAnimator>();
            if (layer == null || layer.Atlas == null)
            {
                return;
            }

            m_Character = character;
            m_Layer = layer;

            DisableConflictingComponents();
        }

        /// <summary>
        /// Deaktiviert <see cref="NpcController"/> und <see cref="UnitCombatVisuals"/>,
        /// damit ihre Update-Loops nicht parallel zur Showcase weitere
        /// <c>Play</c>-Aufrufe absetzen und die gerade gezeigte Anim ueberschreiben.
        /// Idempotent — wird nur einmal pro Spawn-Build ausgefuehrt.
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

        private void BuildNameList()
        {
            m_Names.Clear();
            if (m_Layer == null || m_Layer.Atlas == null || m_Layer.Atlas.Animations == null)
            {
                return;
            }

            // Whitelist hat Vorrang: nur explizit gelistete Namen, in genau dieser Reihenfolge,
            // sofern sie im Atlas existieren.
            if (m_OnlyAnimations != null && m_OnlyAnimations.Length > 0)
            {
                for (int i = 0; i < m_OnlyAnimations.Length; i++)
                {
                    string n = m_OnlyAnimations[i];
                    if (string.IsNullOrEmpty(n))
                    {
                        continue;
                    }
                    if (m_Layer.Atlas.Animations.ContainsKey(n) && !IsSkipped(n))
                    {
                        m_Names.Add(n);
                    }
                }
                return;
            }

            foreach (KeyValuePair<string, FlareAnimation> kvp in m_Layer.Atlas.Animations)
            {
                if (string.IsNullOrEmpty(kvp.Key) || IsSkipped(kvp.Key))
                {
                    continue;
                }
                m_Names.Add(kvp.Key);
            }
            m_Names.Sort();
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

        private void AdvanceTo(int index, bool restart)
        {
            if (m_Names.Count == 0)
            {
                return;
            }
            int wrapped = ((index % m_Names.Count) + m_Names.Count) % m_Names.Count;
            m_Index = wrapped;

            if (m_CycleDirectionPerAnim && !restart)
            {
                m_Direction = (m_Direction + 1) & 7;
            }
            m_Character.SetDirection(m_Direction);

            string name = m_Names[m_Index];
            m_Character.Play(name, force: true);

            float hold = m_HoldSecondsPerAnim;
            if (m_UseAtlasDurationIfLonger)
            {
                float atlas = m_Character.CurrentDurationSeconds;
                if (atlas > hold)
                {
                    hold = atlas;
                }
            }
            m_NextAdvanceAt = Time.time + hold + m_PauseBetweenAnims;
        }

        private FlareAnimation ResolveAnimation(string animationName)
        {
            if (m_Layer == null || m_Layer.Atlas == null)
            {
                return null;
            }
            m_Layer.Atlas.TryGet(animationName, out FlareAnimation anim);
            return anim;
        }

        private void HandleInput()
        {
            // Projekt nutzt ausschliesslich das neue Input System; die alte
            // UnityEngine.Input-API ist deaktiviert. Tastaturzugriff laeuft
            // daher ueber Keyboard.current. Wenn keine Tastatur verbunden
            // ist (z. B. Headless-Server), bleibt der Block ohne Effekt.
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (kb.spaceKey.wasPressedThisFrame)
            {
                m_Paused = !m_Paused;
                if (!m_Paused)
                {
                    // Nach Resume die Haltezeit neu aufziehen, damit man nicht sofort weiterspringt.
                    m_NextAdvanceAt = Time.time + m_HoldSecondsPerAnim + m_PauseBetweenAnims;
                }
            }
            if (kb.rightArrowKey.wasPressedThisFrame)
            {
                AdvanceTo(m_Index + 1, restart: false);
            }
            if (kb.leftArrowKey.wasPressedThisFrame)
            {
                AdvanceTo(m_Index - 1, restart: false);
            }
            if (kb.rKey.wasPressedThisFrame)
            {
                AdvanceTo(m_Index, restart: true);
            }
            if (kb.dKey.wasPressedThisFrame)
            {
                m_Direction = (m_Direction + 1) & 7;
                if (m_Character != null)
                {
                    m_Character.SetDirection(m_Direction);
                }
            }
        }
    }
}
