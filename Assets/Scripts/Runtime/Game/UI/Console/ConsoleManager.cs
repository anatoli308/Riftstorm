using Riftstorm.Game.UI.Console.Commands;
using Riftstorm.Game.UI.Console.States;
using Tolik.Riftstorm.Runtime.Core;
using UnityEngine;

namespace Riftstorm.Game.UI.Console
{
    /// <summary>
    /// Persistente StateMachine fuer das Chat-/Console-Subsystem. Lebt eigenstaendig
    /// auf einem <c>DontDestroyOnLoad</c>-GameObject (Auto-Bootstrap via
    /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/>) und ueberlebt Szenenwechsel,
    /// genau wie der <see cref="ConsoleLog"/>-Backlog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zwei States — <see cref="ConsoleInactiveState"/> (Default) und
    /// <see cref="ConsoleActiveState"/> (Chat-Input fokussiert). State-Wechsel
    /// folgt dem <see cref="ChatFocusState.Changed"/>-Event: der ConsoleHUD
    /// fuehrt den Focus-Status, der Manager spiegelt ihn nur.
    /// </para>
    /// <para>
    /// Eingehende Commands kommen ausschliesslich ueber das statische
    /// <see cref="ConsoleLog.CommandSubmitted"/>-Event rein und werden an den
    /// aktuellen State weitergereicht. Antworten gehen ueber
    /// <see cref="ConsoleLog.Add(string, ConsoleChannel)"/> in den Backlog zurueck.
    /// </para>
    /// <para>
    /// Keine Source-Parity: FLARE/Stone&amp;River hat keinerlei Chat-Commands.
    /// </para>
    /// </remarks>
    public sealed class ConsoleManager : StateMachine<State<ConsoleManager>, ConsoleManager>
    {
        private static ConsoleManager s_Instance;

        private ConsoleInactiveState m_InactiveState;
        private ConsoleActiveState m_ActiveState;

        /// <summary>Inactive-State (Default). Verworfene Submits.</summary>
        public ConsoleInactiveState InactiveState => m_InactiveState;

        /// <summary>Active-State. Dispatch an die Command-Registry.</summary>
        public ConsoleActiveState ActiveState => m_ActiveState;

        /// <summary>
        /// Auto-Bootstrap nach dem Laden der ersten Szene. Erzeugt eine einzige,
        /// persistente Instanz — analog zu <see cref="ConsoleLog"/> ohne Abhaengigkeit
        /// vom <c>ApplicationEntryPoint</c>, damit Commands auch im Editor-PlayMode
        /// ohne Boot-Scene verfuegbar sind.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (s_Instance != null)
            {
                return;
            }
            GameObject host = new("ConsoleManager");
            DontDestroyOnLoad(host);
            s_Instance = host.AddComponent<ConsoleManager>();
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;

            m_InactiveState = new ConsoleInactiveState();
            m_ActiveState = new ConsoleActiveState();
            InitializeStates(new State<ConsoleManager>[] { m_InactiveState, m_ActiveState }, m_InactiveState);

            RegisterBuiltInCommands();

            ConsoleLog.CommandSubmitted += OnCommandSubmitted;
            ChatFocusState.Changed += OnChatFocusChanged;
        }

        private void OnDestroy()
        {
            ConsoleLog.CommandSubmitted -= OnCommandSubmitted;
            ChatFocusState.Changed -= OnChatFocusChanged;
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        /// <summary>
        /// Registriert die ab Werk mitgelieferten Console-Commands. Neue Commands
        /// werden hier eingehaengt — bewusst kein Reflection-Scan, damit Reihenfolge
        /// und Abhaengigkeiten explizit bleiben (siehe copilot-instructions: keine
        /// Reflection in Runtime-Gameplay-Systemen).
        /// </summary>
        private void RegisterBuiltInCommands()
        {
            m_ActiveState.RegisterCommand(new WeaponCommand());
            m_ActiveState.RegisterCommand(new OffhandCommand());
            m_ActiveState.RegisterCommand(new GiveCommand());
            m_ActiveState.RegisterCommand(new InventoryCommand());
            m_ActiveState.RegisterCommand(new EquipCommand());
            m_ActiveState.RegisterCommand(new UnequipCommand());
        }

        private void OnCommandSubmitted(string raw)
        {
            // Dispatch an den aktiven State — Inactive verwirft silent, Active parst.
            if (m_CurrentState == m_ActiveState)
            {
                m_ActiveState.HandleCommand(raw);
            }
            else if (m_CurrentState == m_InactiveState)
            {
                m_InactiveState.HandleCommand(raw);
            }
        }

        private void OnChatFocusChanged(bool isTyping)
        {
            State<ConsoleManager> next = isTyping ? (State<ConsoleManager>)m_ActiveState : m_InactiveState;
            if (m_CurrentState == next)
            {
                return;
            }
            ChangeState(next);
        }
    }
}
