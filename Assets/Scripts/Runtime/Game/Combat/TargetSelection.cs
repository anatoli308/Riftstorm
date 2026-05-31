using System;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Server-autoritative Ziel-Auswahl pro Einheit — &#228;quivalent zum
    /// <c>target</c>-Feld im SoF/SpellCaster-Quellcode. H&#228;lt die
    /// <c>NetworkObjectId</c> des aktuell anvisierten Ziels in einer
    /// <see cref="NetworkVariable{T}"/> (Server schreibt, alle lesen).
    ///
    /// <para>
    /// Es wird ausschlie&#223;lich Ziel-ID + 2D-Distanz verwendet — kein
    /// Hitbox-Overlap, keine Cone-Checks. Skillshots/AoE bekommen ihre
    /// eigenen Resolver in sp&#228;teren Phasen.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TargetSelection : NetworkBehaviour
    {
        /// <summary>Sentinel-Wert f&#252;r "kein Ziel".</summary>
        public const ulong NoTarget = 0UL;

        private readonly NetworkVariable<ulong> m_CurrentTargetId = new(
            value: NoTarget,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>Aktuell anvisierte NetworkObject-Id (0 = kein Ziel).</summary>
        public ulong CurrentTargetId => m_CurrentTargetId.Value;

        /// <summary>True, wenn aktuell ein Ziel gesetzt ist (rein wertbasiert).</summary>
        public bool HasTarget => m_CurrentTargetId.Value != NoTarget;

        /// <summary>
        /// Feuert auf jedem Peer (Server + Clients), sobald sich das anvisierte Ziel
        /// aendert. Erster Parameter = vorheriger Wert, zweiter = neuer Wert.
        /// Wird beim <see cref="OnNetworkSpawn"/> einmalig mit dem Initialwert gefeuert,
        /// damit UI-Schichten ihre Anfangsanzeige korrekt setzen koennen.
        /// </summary>
        public event Action<ulong, ulong> CurrentTargetIdChanged;

        /// <summary>
        /// Statischer Zugriff auf die <see cref="TargetSelection"/> des lokalen Owner-Spielers.
        /// Wird in <see cref="OnNetworkSpawn"/> nur fuer den Owner gesetzt und in
        /// <see cref="OnNetworkDespawn"/> wieder geloescht. Erlaubt UI-Komponenten
        /// (z. B. das Nametag-Label eines anderen Spielers) den Local-Player ohne
        /// teure FindObject-Suche zu erreichen, um per ServerRpc ein Lock zu setzen.
        /// </summary>
        public static TargetSelection Local { get; private set; }

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_CurrentTargetId.OnValueChanged += OnTargetIdChangedInternal;
            if (IsOwner)
            {
                Local = this;
            }
            // Initialen Wert einmal feuern, damit Listener (UI) korrekt initialisieren.
            CurrentTargetIdChanged?.Invoke(NoTarget, m_CurrentTargetId.Value);
        }

        /// <inheritdoc/>
        public override void OnNetworkDespawn()
        {
            m_CurrentTargetId.OnValueChanged -= OnTargetIdChangedInternal;
            if (Local == this)
            {
                Local = null;
            }
            base.OnNetworkDespawn();
        }

        private void OnTargetIdChangedInternal(ulong previous, ulong current)
        {
            CurrentTargetIdChanged?.Invoke(previous, current);
        }

        // -------------------------------------------------------------------------
        // Client → Server
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Client fordert eine Ziel-Aenderung an (z. B. via Tab-Cycle oder
        /// Klick). Server validiert die Id und schreibt in die NetworkVariable.
        /// 0 = Ziel aufheben.
        /// </summary>
        // RequireOwnership=true ist Default und seit NGO 2.x als explizites Property obsolet —
        // deshalb hier weggelassen. Verhalten unverändert: nur der Owner-Client darf rufen.
        [ServerRpc]
        public void RequestSelectTargetServerRpc(ulong targetNetworkObjectId, ServerRpcParams _ = default)
        {
            ServerSetTarget(targetNetworkObjectId);
        }

        // -------------------------------------------------------------------------
        // Server-Helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Server-only: Setzt das aktuelle Ziel. Validiert, dass das
        /// NetworkObject existiert; ung&#252;ltige IDs werden auf <see cref="NoTarget"/>
        /// gemappt.
        /// </summary>
        public void ServerSetTarget(ulong targetNetworkObjectId)
        {
            if (!IsServer)
            {
                return;
            }
            if (targetNetworkObjectId == NoTarget)
            {
                m_CurrentTargetId.Value = NoTarget;
                return;
            }
            if (NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(targetNetworkObjectId))
            {
                m_CurrentTargetId.Value = NoTarget;
                return;
            }
            m_CurrentTargetId.Value = targetNetworkObjectId;
        }

        /// <summary>Server-only: Ziel l&#246;schen.</summary>
        public void ServerClearTarget()
        {
            if (!IsServer)
            {
                return;
            }
            m_CurrentTargetId.Value = NoTarget;
        }

        /// <summary>
        /// Server-only Lookup: Liefert das aktuelle Ziel-NetworkObject und die
        /// daran h&#228;ngenden <see cref="UnitStats"/>, falls vorhanden und am Leben.
        /// </summary>
        public bool TryGetCurrentTarget(out NetworkObject targetObject, out UnitStats targetStats)
        {
            targetObject = null;
            targetStats = null;

            ulong id = m_CurrentTargetId.Value;
            if (id == NoTarget || NetworkManager.Singleton == null)
            {
                return false;
            }
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject no) || no == null)
            {
                return false;
            }
            if (!no.TryGetComponent<UnitStats>(out var stats))
            {
                stats = no.GetComponentInChildren<UnitStats>();
            }
            if (stats == null || stats.IsDead)
            {
                return false;
            }
            targetObject = no;
            targetStats = stats;
            return true;
        }

        /// <summary>
        /// True, wenn das aktuelle Ziel innerhalb von <paramref name="range"/>
        /// (XZ-Ebene, 2D-Distanz wie im SoF-Quellcode) zum &#252;bergebenen
        /// Ursprung liegt.
        /// </summary>
        public bool IsCurrentTargetInRange2D(Vector3 from, float range)
        {
            if (!TryGetCurrentTarget(out NetworkObject no, out _))
            {
                return false;
            }
            Vector3 d = no.transform.position - from;
            d.y = 0f;
            return d.sqrMagnitude <= range * range;
        }
    }
}
