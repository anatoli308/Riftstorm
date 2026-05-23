using System;
using Riftstorm.Game.UI.Console;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Input
{
    /// <summary>
    /// Liest diskrete Combat- und LoL-Style-MOBA-Inputs ueber das Unity
    /// InputSystem-Asset (Action Map "Player"). Reine Event-Quelle — keine
    /// kontinuierlichen Move-Vektoren mehr (WASD wurde durch RMB-Klick ersetzt,
    /// siehe <see cref="MobaCommandController"/>).
    /// </summary>
    public sealed class PlayerInputController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset m_InputAsset;
        [SerializeField] private string m_ActionMap = "Player";
        [SerializeField] private string m_AttackAction = "Attack";
        [SerializeField] private string m_NextTargetAction = "NextTarget";
        [SerializeField] private string m_ClearTargetAction = "ClearTarget";
        [Tooltip("InputAction-Name fuer den Attack-Range-Indicator-Toggle. Muss exakt dem " +
                 "Eintrag im InputSystem-Asset entsprechen (Default: 'AttackrangeIndicator').")]
        [SerializeField] private string m_AttackRangeIndicatorAction = "AttackrangeIndicator";
        [Tooltip("InputAction-Name fuer den LoL-Style Move-Command (RMB-Klick). " +
                 "Wird vom MobaCommandController in einen Bewegungs-Intent uebersetzt.")]
        [SerializeField] private string m_MoveCommandAction = "MoveCommand";

        /// <summary>
        /// Anzahl der WoW-Style Spell-Hotkey-Slots. Bindings sind hartverdrahtet auf
        /// die Zahlentasten 1..9 + 0 (Slot 0 = Taste '1', Slot 9 = Taste '0'). Wird
        /// als Konstante gefuehrt, weil die zugehoerigen <see cref="InputAction"/>s
        /// code-erzeugt sind (kein Asset-Eintrag) und der Konsument
        /// (<see cref="PlayerSpellInput"/>) ein passendes Loadout-Array gleicher Laenge
        /// verwaltet.
        /// </summary>
        public const int SpellSlotCount = 10;

        private static readonly string[] k_SpellSlotBindings =
        {
            "<Keyboard>/1",
            "<Keyboard>/2",
            "<Keyboard>/3",
            "<Keyboard>/4",
            "<Keyboard>/5",
            "<Keyboard>/6",
            "<Keyboard>/7",
            "<Keyboard>/8",
            "<Keyboard>/9",
            "<Keyboard>/0",
        };

        private InputAction m_Attack;
        private InputAction m_NextTarget;
        private InputAction m_ClearTarget;
        private InputAction m_AttackRangeIndicator;
        private InputAction m_MoveCommand;
        private InputAction[] m_SpellSlots;

        /// <summary>
        /// Wird einmal pro Tastendruck der <c>Attack</c>-Action gefeuert
        /// (InputSystem-<c>performed</c>-Phase). LoL-Style: LMB-Klick dient primaer
        /// der Ziel-Selektion (siehe <c>PlayerTargetingInput</c>); Attacken laufen
        /// ueber den RMB-Move-Command bzw. Hotkeys.
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
        /// Lock-Target wieder freizugeben und der <c>MobaCommandController</c>, um
        /// einen laufenden Bewegungs-Intent abzubrechen.
        /// </summary>
        public event Action ClearTargetPressed;

        /// <summary>
        /// Wird einmal pro Tastendruck der <c>AttackrangeIndicator</c>-Action gefeuert.
        /// Konsument ist <see cref="Riftstorm.Game.Combat.AttackRangeIndicator"/>, der die
        /// Sichtbarkeit des Ground-Range-Kreises togglet (LoL-Style).
        /// </summary>
        public event Action AttackRangeIndicatorPressed;

        /// <summary>
        /// Wird einmal pro Tastendruck der <c>MoveCommand</c>-Action (Default: RMB)
        /// gefeuert. Konsument ist <see cref="MobaCommandController"/>, der die
        /// aktuelle Maus-Position in einen Move-To-Point- oder Follow-Target-Intent
        /// uebersetzt.
        /// </summary>
        public event Action MoveCommandPressed;

        /// <summary>
        /// Wird einmal pro Tastendruck eines Spell-Hotkey-Slots (Tasten 1..9, 0)
        /// gefeuert. Der uebergebene Index ist 0-basiert (Taste '1' = 0, Taste '0' = 9)
        /// und entspricht dem Slot-Index im Loadout-Array von
        /// <see cref="PlayerSpellInput"/>, das den Index in einen Spell-Entry aufloest
        /// und an <see cref="Combat.PlayerCombat.TryRequestCastSpell"/> weiterreicht.
        /// Owner-Filter passiert dort autoritativ; das Event feuert wie alle anderen
        /// hier auf jeder PlayerInputController-Instanz (shared Keyboard-Device).
        /// </summary>
        public event Action<int> SpellSlotPressed;

        private void OnEnable()
        {
            if (m_InputAsset == null)
            {
                Debug.LogWarning("[PlayerInputController] Kein InputActionAsset zugewiesen \u2014 Inputs bleiben inaktiv.");
                return;
            }
            InputActionMap map = m_InputAsset.FindActionMap(m_ActionMap, true);
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
            m_MoveCommand = map.FindAction(m_MoveCommandAction, false);
            if (m_MoveCommand != null)
            {
                m_MoveCommand.performed += OnMoveCommandPerformed;
            }
            else
            {
                Debug.LogWarning($"[PlayerInputController] MoveCommand-Action '{m_MoveCommandAction}' nicht im Asset gefunden. Asset reimporten?");
            }
            map.Enable();

            // Spell-Hotkey-Slots: code-erzeugte InputActions, nicht aus dem geteilten
            // Asset-Map. Damit pro Controller-Instanz eigene Actions existieren, die
            // wir in OnDisable gefahrlos wieder deaktivieren koennen (im Gegensatz zur
            // shared ActionMap oben). Die Bindings sind hartverdrahtet auf die
            // Zahlentasten 1..0 (siehe <see cref="k_SpellSlotBindings"/>).
            m_SpellSlots = new InputAction[SpellSlotCount];
            for (int i = 0; i < SpellSlotCount; i++)
            {
                InputAction action = new(name: $"SpellSlot{i + 1}", binding: k_SpellSlotBindings[i]);
                int capturedIndex = i;
                action.performed += ctx => OnSpellSlotPerformed(capturedIndex);
                action.Enable();
                m_SpellSlots[i] = action;
            }
        }

        private void OnDisable()
        {
            // WICHTIG: Die ActionMap NICHT disablen \u2014 das m_InputAsset ist eine geteilte
            // Asset-Referenz und wird von allen PlayerInputController-Instanzen (Owner + Remotes)
            // gemeinsam genutzt. Wenn ein Remote-Player-Controller hier map.Disable() aufruft,
            // killt das auch den Input des lokalen Owners. Stattdessen lassen wir die Map an
            // und stoppen nur unseren eigenen Read-Loop, indem wir die Referenzen droppen.
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
            if (m_MoveCommand != null)
            {
                m_MoveCommand.performed -= OnMoveCommandPerformed;
                m_MoveCommand = null;
            }
            if (m_SpellSlots != null)
            {
                // Spell-Slot-Actions sind code-erzeugt und gehoeren dieser Instanz —
                // disabled werden sie hier explizit, damit beim Despawn / Owner-Wechsel
                // keine Phantom-Listener weiterlaufen.
                for (int i = 0; i < m_SpellSlots.Length; i++)
                {
                    InputAction action = m_SpellSlots[i];
                    if (action == null)
                    {
                        continue;
                    }
                    action.Disable();
                    action.Dispose();
                    m_SpellSlots[i] = null;
                }
                m_SpellSlots = null;
            }
        }

        /// <summary>
        /// Globaler Suppress-Gate fuer Gameplay-Inputs, waehrend der Spieler im
        /// Chat-Input tippt. Verhindert, dass z. B. eine "1" im Chatfenster den
        /// Action-Bar-Slot 0 castet oder ESC zusaetzlich den Target-Lock loest.
        /// Bewusst keine ActionMap.Disable() — die Map ist asset-shared (Owner +
        /// Remotes); ein Disable wuerde alle Controller killen.
        /// </summary>
        private static bool IsSuppressedByChat() => ChatFocusState.IsTyping;

        private void OnAttackPerformed(InputAction.CallbackContext _)
        {
            if (IsSuppressedByChat()) { return; }
            AttackPressed?.Invoke();
        }

        private void OnNextTargetPerformed(InputAction.CallbackContext _)
        {
            if (IsSuppressedByChat()) { return; }
            NextTargetPressed?.Invoke();
        }

        private void OnClearTargetPerformed(InputAction.CallbackContext _)
        {
            if (IsSuppressedByChat()) { return; }
            ClearTargetPressed?.Invoke();
        }

        private void OnAttackRangeIndicatorPerformed(InputAction.CallbackContext _)
        {
            if (IsSuppressedByChat()) { return; }
            AttackRangeIndicatorPressed?.Invoke();
        }

        private void OnMoveCommandPerformed(InputAction.CallbackContext _)
        {
            if (IsSuppressedByChat()) { return; }
            MoveCommandPressed?.Invoke();
        }

        private void OnSpellSlotPerformed(int slotIndex)
        {
            if (IsSuppressedByChat()) { return; }
            SpellSlotPressed?.Invoke(slotIndex);
        }
    }
}
