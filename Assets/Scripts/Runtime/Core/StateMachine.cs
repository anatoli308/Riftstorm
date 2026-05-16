using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tolik.Riftstorm.Runtime.Core
{
    /// <summary>
    /// Generic state machine base class. Managers can inherit from this to get a common
    /// ChangeState implementation and basic state initialization.
    /// TState: the concrete State type
    /// TSelf: the concrete manager type (CRTP pattern) so states can reference back to the concrete manager
    /// </summary>
    public abstract class StateMachine<TState, TSelf> : MonoBehaviour
        where TState : State<TSelf>
        where TSelf : StateMachine<TState, TSelf>
    {
        // current state accessible to derived managers and their logic
        protected TState m_CurrentState;

        public EventManager EventManager
        {
            get
            {
                m_EventManager ??= new EventManager();

                return m_EventManager;
            }
        }

        EventManager m_EventManager;

        /// <summary>
        /// Initializes states by assigning their Manager reference and setting initial state.
        /// Does not call Enter on the initial state to preserve previous behaviour.
        /// </summary>
        protected void InitializeStates(IEnumerable<TState> states, TState initialState)
        {
            foreach (var state in states)
            {
                state.Manager = (TSelf)this;
            }

            m_CurrentState = initialState;
        }

        /// <summary>
        /// Change the current state to <paramref name="nextState"/> invoking Exit/Enter accordingly.
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
