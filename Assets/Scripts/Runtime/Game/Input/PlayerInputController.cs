using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Input
{
    /// <summary>
    /// Liest die WASD-/Stick-Bewegung über das Unity InputSystem-Asset
    /// (Action Map "Player", Action "Move") und stellt sie als normalisierten
    /// 2D-Vektor zur Verfügung. Aktiviert das Action Map beim Enable
    /// und deaktiviert es beim Disable; kein Polling auf Tastaturebene.
    /// </summary>
    public sealed class PlayerInputController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset m_InputAsset;
        [SerializeField] private string m_ActionMap = "Player";
        [SerializeField] private string m_MoveAction = "Move";

        private InputAction m_Move;

        /// <summary>Aktueller normalisierter Bewegungsvektor (x = rechts, y = oben).</summary>
        public Vector2 MoveDirection { get; private set; }

        /// <summary>True, sobald die Eingabe einen relevanten Ausschlag hat.</summary>
        public bool IsMoving { get; private set; }

        private void OnEnable()
        {
            if (m_InputAsset == null)
            {
                Debug.LogWarning("[PlayerInputController] Kein InputActionAsset zugewiesen — WASD bleibt inaktiv.");
                return;
            }
            InputActionMap map = m_InputAsset.FindActionMap(m_ActionMap, true);
            m_Move = map.FindAction(m_MoveAction, true);
            map.Enable();
        }

        private void OnDisable()
        {
            if (m_Move != null)
            {
                m_Move.actionMap?.Disable();
                m_Move = null;
            }
        }

        private void Update()
        {
            if (m_Move == null)
            {
                MoveDirection = Vector2.zero;
                IsMoving = false;
                return;
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
