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

        [Header("Debug")]
        [SerializeField] private bool m_LogDiagnostics = true;

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

            if (m_LogDiagnostics && m_Move != null)
            {
                m_Move.started += OnMoveStarted;
                m_Move.performed += OnMovePerformed;
                m_Move.canceled += OnMoveCanceled;
                Debug.Log(
                    $"[Input] OnEnable bound move-action — controls={m_Move.controls.Count} "
                    + $"firstDevice='{(m_Move.controls.Count > 0 ? m_Move.controls[0].device.displayName : "<none>")}'",
                    this);
            }
        }

        private void OnDisable()
        {
            // WICHTIG: Die ActionMap NICHT disablen — das m_InputAsset ist eine geteilte
            // Asset-Referenz und wird von allen PlayerInputController-Instanzen (Owner + Remotes)
            // gemeinsam genutzt. Wenn ein Remote-Player-Controller hier map.Disable() aufruft,
            // killt das auch den Input des lokalen Owners. Stattdessen lassen wir die Map an
            // und stoppen nur unseren eigenen Read-Loop, indem wir die Referenz droppen.
            if (m_Move != null && m_LogDiagnostics)
            {
                m_Move.started -= OnMoveStarted;
                m_Move.performed -= OnMovePerformed;
                m_Move.canceled -= OnMoveCanceled;
            }
            m_Move = null;
        }

        private void OnMoveStarted(InputAction.CallbackContext ctx)
        {
            Debug.Log($"[Input] >>> ACTION STARTED  value={ctx.ReadValue<Vector2>()}  control='{ctx.control?.displayName}'  device='{ctx.control?.device?.displayName}'", this);
        }

        private void OnMovePerformed(InputAction.CallbackContext ctx)
        {
            Debug.Log($"[Input] >>> ACTION PERFORMED value={ctx.ReadValue<Vector2>()}  control='{ctx.control?.displayName}'", this);
        }

        private void OnMoveCanceled(InputAction.CallbackContext ctx)
        {
            Debug.Log($"[Input] <<< ACTION CANCELED", this);
        }

        private void Update()
        {
            // -----------------------------------------------------------------
            // RAW-Keyboard-Check: bypasst InputAction komplett. Wenn das OS
            // eine Taste an Unity liefert, sehen wir es hier — egal ob das
            // InputAction-Asset, Pairing, oder ActionMap kaputt ist.
            // -----------------------------------------------------------------
            if (m_LogDiagnostics)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    bool anyDown = kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame
                                   || kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
                    if (anyDown)
                    {
                        string key = kb.wKey.wasPressedThisFrame ? "W"
                                   : kb.aKey.wasPressedThisFrame ? "A"
                                   : kb.sKey.wasPressedThisFrame ? "S" : "D";

                        bool moveExists = m_Move != null;
                        bool moveEnabled = moveExists && m_Move.enabled;
                        InputActionMap parentMap = moveExists ? m_Move.actionMap : null;
                        bool mapEnabled = parentMap != null && parentMap.enabled;
                        int ctrls = moveExists ? m_Move.controls.Count : -1;

                        Debug.Log(
                            $"[Input] RAW {key} down  |  moveExists={moveExists} moveEnabled={moveEnabled} "
                            + $"mapEnabled={mapEnabled} controls={ctrls}  asset='{(m_InputAsset != null ? m_InputAsset.name : "<null>")}'",
                            this);

                        // Self-heal: wenn die Map disabled wurde, re-enable und loggen.
                        if (moveExists && !moveEnabled)
                        {
                            if (parentMap != null && !parentMap.enabled)
                            {
                                parentMap.Enable();
                                Debug.LogWarning($"[Input] >>> ActionMap war DISABLED — habe sie re-enabled. NeuStatus moveEnabled={m_Move.enabled}", this);
                            }
                            else
                            {
                                m_Move.Enable();
                                Debug.LogWarning($"[Input] >>> Move-Action war DISABLED (Map an) — habe sie re-enabled. NeuStatus moveEnabled={m_Move.enabled}", this);
                            }
                        }
                    }
                }
                else if (Time.frameCount == 120)
                {
                    Debug.LogWarning("[Input] Keyboard.current == null — InputSystem sieht keine Tastatur!", this);
                }
            }

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
