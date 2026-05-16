using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Tolik.Riftstorm.Runtime.Core
{
    /// <summary>
    /// Generic state machine base for components that must inherit from
    /// <see cref="NetworkBehaviour"/> instead of <see cref="MonoBehaviour"/>.
    /// Behaviourally identisch zu <see cref="StateMachine{TState,TSelf}"/>: states
    /// halten eine Rückreferenz zum konkreten Manager (CRTP), Wechsel laufen über
    /// <see cref="ChangeState"/> mit Exit/Enter-Lifecycle.
    /// </summary>
    /// <typeparam name="TState">Konkreter <see cref="State{TManager}"/>-Typ.</typeparam>
    /// <typeparam name="TSelf">Konkreter Manager-Typ (CRTP).</typeparam>
    public abstract class NetworkStateMachine<TState, TSelf> : NetworkBehaviour
        where TState : State<TSelf>
        where TSelf : NetworkStateMachine<TState, TSelf>
    {
        /// <summary>Aktueller State (für abgeleitete Klassen lesbar/schreibbar).</summary>
        protected TState m_CurrentState;

        /// <summary>Lazy-instantiated EventManager zum Broadcasten von State-Events.</summary>
        public EventManager EventManager
        {
            get
            {
                m_EventManager ??= new EventManager();
                return m_EventManager;
            }
        }

        private EventManager m_EventManager;

        /// <summary>
        /// Verteilt die Manager-Referenz an alle States und setzt den Initial-State,
        /// ohne <c>Enter</c> aufzurufen (siehe <see cref="StateMachine{TState,TSelf}"/>).
        /// </summary>
        protected void InitializeStates(IEnumerable<TState> states, TState initialState)
        {
            foreach (TState state in states)
            {
                state.Manager = (TSelf)this;
            }

            m_CurrentState = initialState;
        }

        /// <summary>
        /// Wechselt in <paramref name="nextState"/>. Ruft <c>Exit</c> am alten und
        /// <c>Enter</c> am neuen State auf.
        /// </summary>
        public void ChangeState(TState nextState)
        {
            Debug.Log($"{name}: Changed state from {m_CurrentState?.GetType().Name} to {nextState.GetType().Name}.");

            if (m_CurrentState != null)
            {
                m_CurrentState.Exit();
            }

            m_CurrentState = nextState;
            m_CurrentState.Enter();
        }
    }
}
