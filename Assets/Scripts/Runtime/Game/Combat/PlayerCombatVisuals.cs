using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Lokale Combat-Anim-Statemachine neben <see cref="FlareCharacter"/>. Steuert die
    /// Priorität der Animationsschichten:
    ///
    /// <code>
    /// Die  >  Hit  >  Swing | Shoot | Cast  >  Block  >  Run | Stance
    /// </code>
    ///
    /// Alle Combat-Visuals laufen über diese Komponente; die Movement-Schicht
    /// (<see cref="Riftstorm.Game.Movement.PlayerMovement"/>) konsultiert <see cref="IsBusy"/>,
    /// bevor sie <c>stance</c>/<c>run</c> einschiebt. Diese Klasse ist kein NetworkBehaviour —
    /// sie wird vom autoritativen <c>PlayerCombat</c>-State (NetworkVariable) oder lokal vom
    /// Editor-Test aus getriggert (siehe Phase 4).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCombatVisuals : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [SerializeField] private FlareCharacter m_Character;

        [Header("Animationsnamen (FLARE-Atlas)")]
        [SerializeField] private string m_AnimSwing = "swing";
        [SerializeField] private string m_AnimShoot = "shoot";
        [SerializeField] private string m_AnimCast = "cast";
        [SerializeField] private string m_AnimBlock = "block";
        [SerializeField] private string m_AnimHit = "hit";
        [SerializeField] private string m_AnimDie = "die";

        // -------------------------------------------------------------------------
        // Interner State
        // -------------------------------------------------------------------------

        /// <summary>Top-Level-Zustand der Combat-Visuals.</summary>
        private enum VisualState
        {
            /// <summary>Bewegungs-Layer (Stance/Run) darf zeichnen.</summary>
            Idle,

            /// <summary>Block-Loop läuft — Bewegung pausiert visuell.</summary>
            Block,

            /// <summary>PlayOnce-Anim (Swing/Shoot/Cast) läuft.</summary>
            Action,

            /// <summary>Hit-Reaktion läuft (überschreibt aktive Action).</summary>
            Hit,

            /// <summary>Die wurde gespielt — bleibt latched, kein Wechsel mehr.</summary>
            Dead,
        }

        private VisualState m_State = VisualState.Idle;
        private CombatAnim m_CurrentAnim = CombatAnim.None;
        private bool m_BlockRequested;

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// <c>true</c>, solange eine Combat-Anim oder Block-Loop aktiv ist. Während
        /// <c>true</c> darf <see cref="Riftstorm.Game.Movement.PlayerMovement"/> keine
        /// Stance/Run-Anim erzwingen. Richtung darf weiterhin gesetzt werden.
        /// </summary>
        public bool IsBusy => m_State != VisualState.Idle;

        /// <summary>Aktuell visualisierte Combat-Anim (<see cref="CombatAnim.None"/> im Idle).</summary>
        public CombatAnim CurrentAnim => m_CurrentAnim;

        /// <summary>
        /// Wird vom Bootstrap aufgerufen, sobald der FLARE-Charakter aufgebaut ist.
        /// </summary>
        public void BindCharacter(FlareCharacter character)
        {
            m_Character = character;
        }

        /// <summary>Startet die waffenspezifische Nahkampf-Anim (PlayOnce).</summary>
        public void PlaySwing() => StartAction(CombatAnim.Swing, m_AnimSwing);

        /// <summary>Startet die Fernkampf-Anim (Bow/Crossbow/Gun → PlayOnce).</summary>
        public void PlayShoot() => StartAction(CombatAnim.Shoot, m_AnimShoot);

        /// <summary>Startet die Zauber-Anim (PlayOnce).</summary>
        public void PlayCast() => StartAction(CombatAnim.Cast, m_AnimCast);

        /// <summary>
        /// Spielt die Treffer-Reaktion. Überschreibt eine laufende Action, wird aber
        /// nicht ausgeführt, wenn der Charakter bereits tot ist.
        /// </summary>
        public void PlayHit()
        {
            if (m_State == VisualState.Dead)
            {
                return;
            }
            m_State = VisualState.Hit;
            m_CurrentAnim = CombatAnim.Hit;
            PlayInternal(m_AnimHit, force: true);
        }

        /// <summary>
        /// Aktiviert den Block-Loop. Während einer Action/Hit-Anim wird der Block-Request
        /// gemerkt und erst beim Abschluss der PlayOnce-Anim eingeblendet.
        /// </summary>
        public void PlayBlockEnter()
        {
            if (m_State == VisualState.Dead)
            {
                return;
            }
            m_BlockRequested = true;
            if (m_State == VisualState.Idle)
            {
                EnterBlock();
            }
        }

        /// <summary>Beendet den Block-Loop.</summary>
        public void PlayBlockExit()
        {
            m_BlockRequested = false;
            if (m_State == VisualState.Block)
            {
                m_State = VisualState.Idle;
                m_CurrentAnim = CombatAnim.None;
                // Bewegungs-Layer übernimmt im nächsten Frame.
            }
        }

        /// <summary>
        /// Spielt die Todes-Anim ab und latched den Zustand. Danach werden keine
        /// weiteren Trigger akzeptiert (außer einem expliziten Reset durch Respawn-Logik).
        /// </summary>
        public void PlayDie()
        {
            m_State = VisualState.Dead;
            m_CurrentAnim = CombatAnim.Die;
            m_BlockRequested = false;
            PlayInternal(m_AnimDie, force: true);
        }

        /// <summary>
        /// Setzt den Visual-State zurück (z. B. nach Respawn). Setzt KEIN Anim.
        /// Der Aufrufer entscheidet, ob direkt eine Idle-Anim folgen soll.
        /// </summary>
        public void ResetForRespawn()
        {
            m_State = VisualState.Idle;
            m_CurrentAnim = CombatAnim.None;
            m_BlockRequested = false;
        }

        // -------------------------------------------------------------------------
        // Unity
        // -------------------------------------------------------------------------

        private void Awake()
        {
            if (m_Character == null)
            {
                m_Character = GetComponentInChildren<FlareCharacter>();
            }
        }

        private void Update()
        {
            // Action/Hit laufen bis PlayOnce-Anim fertig ist; danach Übergang
            // zurück auf Block (falls aktiv requested) oder Idle.
            if (m_State != VisualState.Action && m_State != VisualState.Hit)
            {
                return;
            }
            if (m_Character == null || !m_Character.IsPlayOnceFinished)
            {
                return;
            }

            if (m_BlockRequested)
            {
                EnterBlock();
            }
            else
            {
                m_State = VisualState.Idle;
                m_CurrentAnim = CombatAnim.None;
                // PlayerMovement übernimmt im nächsten UpdateVisuals().
            }
        }

        // -------------------------------------------------------------------------
        // Intern
        // -------------------------------------------------------------------------

        private void StartAction(CombatAnim anim, string animationName)
        {
            if (m_State == VisualState.Dead)
            {
                return;
            }
            m_State = VisualState.Action;
            m_CurrentAnim = anim;
            PlayInternal(animationName, force: true);
        }

        private void EnterBlock()
        {
            m_State = VisualState.Block;
            m_CurrentAnim = CombatAnim.Block;
            PlayInternal(m_AnimBlock, force: false);
        }

        private void PlayInternal(string animationName, bool force)
        {
            if (m_Character == null || string.IsNullOrEmpty(animationName))
            {
                return;
            }
            m_Character.Play(animationName, force);
        }
    }
}
