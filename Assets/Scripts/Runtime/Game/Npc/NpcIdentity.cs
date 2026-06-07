using System;
using Riftstorm.Game.Combat;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
[DisallowMultipleComponent]
    /// <summary>
    /// NPC-Pendant zu <see cref="Player.PlayerIdentity"/>: liefert einen
    /// <see cref="INameSource"/> f&#252;r Nametag / FloatingCombatText / Logger ohne
    /// Netcode-Synchronisation. Der Name ist deterministisch &#252;ber jeden Peer hinweg,
    /// weil <see cref="MugenNpcSpawner"/> ihn auf jedem Peer aus
    /// derselben StreamingAssets-JSON-Stat-Sidecar in <see cref="UnitStats.DisplayName"/>
    /// schreibt &#8212; ein <c>NetworkVariable</c> w&#228;re reine Bandbreitenverschwendung.
    /// </summary>
    [RequireComponent(typeof(UnitStats))]
    public sealed class NpcIdentity : MonoBehaviour, INameSource
    {
        [SerializeField] private UnitStats m_Stats;

        /// <inheritdoc/>
        public string DisplayName => m_Stats != null ? m_Stats.DisplayName : string.Empty;

        /// <inheritdoc/>
        public event Action<string> DisplayNameChanged;

        private void Awake()
        {
            if (m_Stats == null)
            {
                m_Stats = GetComponent<UnitStats>();
            }
        }

        private void OnEnable()
        {
            // NPC-Name ist statisch aus dem Stat-Sidecar; trotzdem feuern, damit
            // sp&#228;t aktivierte Subscriber (Nametag) ihren Cache initial f&#252;llen
            // k&#246;nnen, ohne aktiv pollen zu m&#252;ssen.
            string current = DisplayName;
            if (!string.IsNullOrEmpty(current))
            {
                DisplayNameChanged?.Invoke(current);
            }
        }
    }
}
