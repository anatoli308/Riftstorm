using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Unit-agnostische Combat-Anim-Statemachine neben <see cref="FlareCharacter"/>.
    /// Steuert die Priorit&#228;t der Animationsschichten:
    ///
    /// <code>
    /// Die  &gt;  Hit  &gt;  Swing | Shoot | Cast  &gt;  Block  &gt;  Run | Stance
    /// </code>
    ///
    /// Wird vom Spieler (siehe <see cref="PlayerCombatVisuals"/>) und vom NPC
    /// (siehe <see cref="Riftstorm.Game.Npc.NpcController"/>) gleicherma&#223;en benutzt.
    /// Kein NetworkBehaviour &#8212; die Komponente l&#228;uft lokal pro Peer und wird
    /// vom autoritativen State (RPC oder UnitStats-Event) getriggert.
    /// </summary>
    /// <remarks>
    /// Bewusst nicht <c>sealed</c>, damit spieler- oder NPC-spezifische
    /// Subklassen die GUID/Prefab-Anbindung halten k&#246;nnen, ohne dass die
    /// Logik dupliziert wird.
    /// </remarks>
    [DisallowMultipleComponent]
    public class UnitCombatVisuals : MonoBehaviour
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

            /// <summary>Block-Loop l&#228;uft &#8212; Bewegung pausiert visuell.</summary>
            Block,

            /// <summary>PlayOnce-Anim (Swing/Shoot/Cast) l&#228;uft.</summary>
            Action,

            /// <summary>Hit-Reaktion l&#228;uft (&#252;berschreibt aktive Action).</summary>
            Hit,

            /// <summary>Die wurde gespielt &#8212; bleibt latched, kein Wechsel mehr.</summary>
            Dead,
        }

        private VisualState m_State = VisualState.Idle;
        private CombatAnim m_CurrentAnim = CombatAnim.None;
        private bool m_BlockRequested;

        /// <summary>
        /// Absolute Zeit (Time.time), zu der die aktuell laufende Action/Hit-Anim
        /// spätestens beendet wird — Safety-Net für Fälle, in denen
        /// <see cref="FlareCharacter.IsPlayOnceFinished"/> niemals <c>true</c>
        /// liefert (Animation ist Looped, fehlt im Atlas, hat null Layer-Treffer).
        /// Wird in <see cref="StartAction"/> und <see cref="PlayHit"/> gesetzt.
        /// </summary>
        private float m_ActionEndsAt;

        /// <summary>Fallback-Dauer (s), wenn der Atlas die Animation nicht kennt oder Duration 0 ist.</summary>
        private const float k_ActionFallbackSeconds = 0.5f;

        /// <summary>Grace-Puffer (s) oben drauf, damit die natürliche PlayOnce-Erkennung Vorrang hat.</summary>
        private const float k_ActionGraceSeconds = 0.05f;

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// <c>true</c>, solange eine Combat-Anim oder Block-Loop aktiv ist. W&#228;hrend
        /// <c>true</c> darf der Movement-Layer (PlayerMovement / NpcController) keine
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

        /// <summary>
        /// Variante mit explizitem Animationsnamen. Wird vom NPC-AI-Pfad genutzt,
        /// wenn die Combat-Schicht aus dem MUGEN-Skill-Pool einen konkreten
        /// Stand-Normal-Attack ausgewaehlt hat (z. B. "swing_medium", "swing_hard")
        /// und nicht den generischen <see cref="m_AnimSwing"/>-Default spielen soll.
        /// Faellt auf <see cref="m_AnimSwing"/> zurueck, wenn der Aufrufer einen
        /// leeren Namen uebergibt.
        /// </summary>
        public void PlaySwing(string animationName)
        {
            string anim = string.IsNullOrEmpty(animationName) ? m_AnimSwing : animationName;
            StartAction(CombatAnim.Swing, anim);
        }

        /// <summary>Startet die Fernkampf-Anim (Bow/Crossbow/Gun &#8594; PlayOnce).</summary>
        public void PlayShoot() => StartAction(CombatAnim.Shoot, m_AnimShoot);

        /// <summary>Startet die Zauber-Anim (PlayOnce).</summary>
        public void PlayCast() => StartAction(CombatAnim.Cast, m_AnimCast);

        /// <summary>
        /// Spielt die Treffer-Reaktion. &#220;berschreibt eine laufende Action, wird aber
        /// nicht ausgef&#252;hrt, wenn der Charakter bereits tot ist.
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
            ArmActionDeadline();
        }

        /// <summary>
        /// Aktiviert den Block-Loop. W&#228;hrend einer Action/Hit-Anim wird der Block-Request
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
                // Bewegungs-Layer &#252;bernimmt im n&#228;chsten Frame.
            }
        }

        /// <summary>
        /// Spielt die Todes-Anim ab und latched den Zustand. Danach werden keine
        /// weiteren Trigger akzeptiert (au&#223;er einem expliziten Reset durch Respawn-Logik).
        /// </summary>
        public void PlayDie()
        {
            m_State = VisualState.Dead;
            m_CurrentAnim = CombatAnim.Die;
            m_BlockRequested = false;
            PlayInternal(m_AnimDie, force: true);
        }

        /// <summary>
        /// Setzt den Visual-State zur&#252;ck (z. B. nach Respawn). Setzt KEIN Anim.
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
            // Action/Hit laufen bis PlayOnce-Anim fertig ist; danach &#220;bergang
            // zur&#252;ck auf Block (falls aktiv requested) oder Idle.
            if (m_State != VisualState.Action && m_State != VisualState.Hit)
            {
                return;
            }

            // Primärsignal: FlareCharacter meldet PlayOnce-Abschluss.
            // Safety-Net: Wenn die Anim als Looped konvertiert wurde, im Atlas
            // fehlt oder aus anderen Gründen keine PlayOnce-Schicht hat, würde
            // IsPlayOnceFinished niemals true werden und der Owner bliebe für
            // immer in Action — der NPC könnte keine weiteren Auto-Attacks
            // auslösen und der Sprite stünde am letzten Swing-Frame. Die
            // Deadline aus der nominalen Animationsdauer (gesetzt in
            // StartAction/PlayHit) bricht diesen Lock garantiert auf.
            bool playOnceDone = m_Character != null && m_Character.IsPlayOnceFinished;
            bool deadlineDone = Time.time >= m_ActionEndsAt;
            if (!playOnceDone && !deadlineDone)
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
                // Movement-Layer &#252;bernimmt im n&#228;chsten UpdateVisuals().
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
            ArmActionDeadline();
        }

        /// <summary>
        /// Berechnet die Safety-Net-Deadline für den aktuellen Action/Hit-Frame
        /// aus der nominalen Dauer der gerade gestarteten Animation. Wird direkt
        /// nach <see cref="PlayInternal"/> aufgerufen, damit
        /// <see cref="FlareCharacter.CurrentDurationSeconds"/> bereits die neue
        /// Animation reflektiert.
        /// </summary>
        private void ArmActionDeadline()
        {
            float duration = m_Character != null ? m_Character.CurrentDurationSeconds : 0f;
            if (duration <= 0f)
            {
                duration = k_ActionFallbackSeconds;
            }
            m_ActionEndsAt = Time.time + duration + k_ActionGraceSeconds;
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
