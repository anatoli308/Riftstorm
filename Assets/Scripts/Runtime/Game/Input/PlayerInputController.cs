using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Input
{
    /// <summary>
    /// Liest die WASD-/Stick-Bewegung sowie diskrete Combat-Inputs über das
    /// Unity InputSystem-Asset (Action Map "Player"). <c>Move</c> wird als
    /// normalisierter 2D-Vektor abgefragt; <c>Attack</c> wird als Event
    /// (<see cref="AttackPressed"/>) gefeuert — keine Polling-Logik auf
    /// Button-Ebene, kein Frame-Lag durch Edge-Detection.
    /// </summary>
    public sealed class PlayerInputController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset m_InputAsset;
        [SerializeField] private string m_ActionMap = "Player";
        [SerializeField] private string m_MoveAction = "Move";
        [SerializeField] private string m_AttackAction = "Attack";
        [SerializeField] private string m_NextTargetAction = "NextTarget";
        [SerializeField] private string m_ClearTargetAction = "ClearTarget";
        [Tooltip("InputAction-Name für den Attack-Range-Indicator-Toggle. Muss exakt dem " +
                 "Eintrag im InputSystem-Asset entsprechen (Default: 'AttackrangeIndicator').")]
        [SerializeField] private string m_AttackRangeIndicatorAction = "AttackrangeIndicator";

        private InputAction m_Move;
        private InputAction m_Attack;
        private InputAction m_NextTarget;
        private InputAction m_ClearTarget;
        private InputAction m_AttackRangeIndicator;

        /// <summary>Aktueller normalisierter Bewegungsvektor (x = rechts, y = oben).</summary>
        public Vector2 MoveDirection { get; private set; }

        /// <summary>True, sobald die Eingabe einen relevanten Ausschlag hat.</summary>
        public bool IsMoving { get; private set; }

        /// <summary>
        /// Wird einmal pro Tastendruck der <c>Attack</c>-Action gefeuert
        /// (InputSystem-<c>performed</c>-Phase). Subscriber sollten ihre Abos
        /// in <c>OnEnable</c>/<c>OnDisable</c> verwalten.
        /// </summary>
        public event Action AttackPressed;

        /// <summary>
        /// Wird einmal pro Tastendruck der <c>NextTarget</c>-Action (Default: Tab)
        /// gefeuert. Wird vom <c>PlayerTargetingInput</c> zum MMO-typischen
        /// Target-Cycling konsumiert.
        /// </summary>
        public event Action NextTargetPressed;

        /// <summary>
        /// Wird einmal pro Tastendruck der <c>ClearTarget</c>-Action (Default: Escape)
        /// gefeuert. <c>PlayerTargetingInput</c> nutzt das Event, um das aktuelle
        /// Lock-Target wieder freizugeben.
        /// </summary>
        public event Action ClearTargetPressed;

        /// <summary>
        /// Wird einmal pro Tastendruck der <c>AttackrangeIndicator</c>-Action gefeuert.
        /// Konsument ist <see cref="Riftstorm.Game.Combat.AttackRangeIndicator"/>, der die
        /// Sichtbarkeit des Ground-Range-Kreises togglet (LoL-Style).
        /// </summary>
        public event Action AttackRangeIndicatorPressed;

        private void OnEnable()
        {
            if (m_InputAsset == null)
            {
                Debug.LogWarning("[PlayerInputController] Kein InputActionAsset zugewiesen — WASD bleibt inaktiv.");
                return;
            }
            InputActionMap map = m_InputAsset.FindActionMap(m_ActionMap, true);
            m_Move = map.FindAction(m_MoveAction, true);
            m_Attack = map.FindAction(m_AttackAction, false);
            if (m_Attack != null)
            {
                m_Attack.performed += OnAttackPerformed;
            }
            m_NextTarget = map.FindAction(m_NextTargetAction, false);
            if (m_NextTarget != null)
            {
                m_NextTarget.performed += OnNextTargetPerformed;
            }
            m_ClearTarget = map.FindAction(m_ClearTargetAction, false);
            if (m_ClearTarget != null)
            {
                m_ClearTarget.performed += OnClearTargetPerformed;
            }
            else
            {
                Debug.LogWarning($"[PlayerInputController] ClearTarget-Action '{m_ClearTargetAction}' nicht im Asset gefunden. Asset reimporten?");
            }
            m_AttackRangeIndicator = map.FindAction(m_AttackRangeIndicatorAction, false);
            if (m_AttackRangeIndicator != null)
            {
                m_AttackRangeIndicator.performed += OnAttackRangeIndicatorPerformed;
            }
            else
            {
                Debug.LogWarning($"[PlayerInputController] AttackRangeIndicator-Action '{m_AttackRangeIndicatorAction}' nicht im Asset gefunden. Asset reimporten?");
            }
            map.Enable();
        }

        private void OnDisable()
        {
            // WICHTIG: Die ActionMap NICHT disablen — das m_InputAsset ist eine geteilte
            // Asset-Referenz und wird von allen PlayerInputController-Instanzen (Owner + Remotes)
            // gemeinsam genutzt. Wenn ein Remote-Player-Controller hier map.Disable() aufruft,
            // killt das auch den Input des lokalen Owners. Stattdessen lassen wir die Map an
            // und stoppen nur unseren eigenen Read-Loop, indem wir die Referenz droppen.
            m_Move = null;
            if (m_Attack != null)
            {
                m_Attack.performed -= OnAttackPerformed;
                m_Attack = null;
            }
            if (m_NextTarget != null)
            {
                m_NextTarget.performed -= OnNextTargetPerformed;
                m_NextTarget = null;
            }
            if (m_ClearTarget != null)
            {
                m_ClearTarget.performed -= OnClearTargetPerformed;
                m_ClearTarget = null;
            }
            if (m_AttackRangeIndicator != null)
            {
                m_AttackRangeIndicator.performed -= OnAttackRangeIndicatorPerformed;
                m_AttackRangeIndicator = null;
            }
        }

        private void OnAttackPerformed(InputAction.CallbackContext _)
        {
            AttackPressed?.Invoke();
        }

        private void OnNextTargetPerformed(InputAction.CallbackContext _)
        {
            NextTargetPressed?.Invoke();
        }

        private void OnClearTargetPerformed(InputAction.CallbackContext _)
        {
            ClearTargetPressed?.Invoke();
        }

        private void OnAttackRangeIndicatorPerformed(InputAction.CallbackContext _)
        {
            AttackRangeIndicatorPressed?.Invoke();
        }

        private void Update()
        {
            if (m_Move == null)
            {
                MoveDirection = Vector2.zero;
                IsMoving = false;
                return;
            }

            // Self-heal: falls eine andere Instanz die Map deaktiviert hat, re-enable still.
            if (!m_Move.enabled)
            {
                InputActionMap parentMap = m_Move.actionMap;
                if (parentMap != null && !parentMap.enabled)
                {
                    parentMap.Enable();
                }
                else
                {
                    m_Move.Enable();
                }
            }

            Vector2 raw = m_Move.ReadValue<Vector2>();
            // Composite-Vectors sind bereits normalisiert, aber sicherheitshalber clampen.
            float sqr = raw.sqrMagnitude;
            if (sqr > 1f)
            {
                raw /= Mathf.Sqrt(sqr);
            }
            MoveDirection = raw;
            IsMoving = sqr > 0.01f;
        }
    }
}
