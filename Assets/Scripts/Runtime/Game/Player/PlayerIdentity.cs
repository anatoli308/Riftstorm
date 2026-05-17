using System;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Tolik.Riftstorm.Runtime.ConnectionManagement;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Player
{
    /// <summary>
    /// Server-autoritative Identit&#228;t des Spielers (aktuell: Anzeigename).
    /// Der Server liest den w&#228;hrend des Approval-Schritts vom Client
    /// gesendeten Namen aus dem <see cref="ConnectionManager"/> und legt ihn
    /// in einer <see cref="NetworkVariable{T}"/> ab, sodass alle Clients ihn
    /// f&#252;r Nametags / UI auswerten k&#246;nnen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerIdentity : NetworkBehaviour, Riftstorm.Game.Combat.INameSource
    {
        private readonly NetworkVariable<FixedString32Bytes> m_DisplayName = new(
            value: default,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>Aktueller Anzeigename als Managed-String (Allocation bei jedem Lesen \u2014 sparsam nutzen).</summary>
        public string DisplayName => m_DisplayName.Value.ToString();

        /// <summary>Wird ausgel&#246;st, wenn sich der Anzeigename &#228;ndert (auch initial bei Spawn).</summary>
        public event Action<string> DisplayNameChanged;

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            m_DisplayName.OnValueChanged += OnDisplayNameChanged;

            if (IsServer)
            {
                string name = ResolveOwnerName();
                m_DisplayName.Value = new FixedString32Bytes(Clamp32(name));
            }
            else
            {
                // Remote-Clients holen den aktuellen Wert direkt nach Spawn.
                DisplayNameChanged?.Invoke(DisplayName);
            }
        }

        /// <inheritdoc/>
        public override void OnNetworkDespawn()
        {
            m_DisplayName.OnValueChanged -= OnDisplayNameChanged;
        }

        private void OnDisplayNameChanged(FixedString32Bytes _, FixedString32Bytes current)
        {
            DisplayNameChanged?.Invoke(current.ToString());
        }

        private string ResolveOwnerName()
        {
            ApplicationEntryPoint entry = ApplicationEntryPoint.Singleton;
            if (entry == null || entry.ConnectionManager == null)
            {
                return "Player";
            }
            if (entry.ConnectionManager.TryGetApprovedName(OwnerClientId, out string approved) &&
                !string.IsNullOrWhiteSpace(approved))
            {
                return approved;
            }
            return "Player";
        }

        private static string Clamp32(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "Player";
            }
            // FixedString32Bytes h&#228;lt bis zu 29 UTF-8-Bytes Nutzlast.
            const int maxChars = 24;
            return s.Length <= maxChars ? s : s.Substring(0, maxChars);
        }
    }
}
