using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Unity.Netcode;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Server-only Threat-Tabelle pro NPC. Port von
    /// <c>Server/src/AI/ThreatManager.h</c>. Liefert das Target mit dem
    /// hoechsten Threat-Wert, ersetzt damit das "closest hostile target"-
    /// Picking aus <see cref="NpcController.FindAggroTarget"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Key ist die <see cref="NetworkObjectId"/> der Einheit, NICHT eine
    /// <see cref="UnitStats"/>-Referenz: robust gegen Despawn/Destroy
    /// (Eintrag wird beim naechsten <see cref="GetHighestThreat"/> einfach
    /// wegfallen, wenn der NetworkObject schon nicht mehr im
    /// <c>SpawnManager</c> haengt).
    /// </para>
    /// <para>
    /// Pure C#, keine MonoBehaviour, kein ServiceLocator — Lebensdauer ist
    /// an den besitzenden <see cref="NpcController"/> gekoppelt (analog zu
    /// <c>AuraManager</c> auf <c>UnitStats</c>).
    /// </para>
    /// </remarks>
    public sealed class ThreatManager
    {
        private readonly Dictionary<ulong, int> m_Table = new(8);

        /// <summary>Anzahl Eintraege in der Tabelle.</summary>
        public int Count => m_Table.Count;

        /// <summary>True, wenn ueberhaupt jemand Threat hat.</summary>
        public bool HasThreat => m_Table.Count > 0;

        /// <summary>
        /// Addiert <paramref name="amount"/> Threat auf <paramref name="unitId"/>.
        /// Negative Werte werden geklemmt (kein Threat &lt; 0), die Einheit
        /// bleibt aber in der Tabelle und kann durch weiteren Schaden wieder
        /// hochgezogen werden.
        /// </summary>
        public void AddThreat(ulong unitId, int amount)
        {
            if (unitId == 0UL || amount == 0)
            {
                return;
            }
            m_Table.TryGetValue(unitId, out int current);
            long next = (long)current + amount;
            if (next < 0)
            {
                next = 0;
            }
            m_Table[unitId] = (int)next;
        }

        /// <summary>
        /// Multipliziert den bestehenden Threat-Wert mit <paramref name="multiplier"/>.
        /// Existiert kein Eintrag, no-op. Source-Pendant: <c>ThreatManager::modifyThreat</c>.
        /// </summary>
        public void ModifyThreat(ulong unitId, float multiplier)
        {
            if (!m_Table.TryGetValue(unitId, out int current))
            {
                return;
            }
            int next = UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(current * multiplier));
            m_Table[unitId] = next;
        }

        /// <summary>Entfernt einen Eintrag (z. B. nach Tod / Despawn / Leash).</summary>
        public void RemoveThreat(ulong unitId)
        {
            m_Table.Remove(unitId);
        }

        /// <summary>Liefert den aktuellen Threat-Wert oder 0.</summary>
        public int GetThreat(ulong unitId)
        {
            m_Table.TryGetValue(unitId, out int v);
            return v;
        }

        /// <summary>Kopiert alle aktuell verfolgten Unit-Ids in <paramref name="destination"/>.</summary>
        public void CopyUnitIds(List<ulong> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            foreach (KeyValuePair<ulong, int> kv in m_Table)
            {
                destination.Add(kv.Key);
            }
        }

        /// <summary>
        /// Leert die komplette Tabelle (z. B. bei Evade-Reset oder Match-End).
        /// </summary>
        public void Clear()
        {
            m_Table.Clear();
        }

        /// <summary>
        /// Sucht das Target mit dem hoechsten Threat-Wert und resolved es ueber
        /// den <see cref="NetworkManager.SpawnManager"/>. Tote oder despawnte
        /// Einheiten werden waehrend des Scans aus der Tabelle entfernt.
        /// </summary>
        /// <param name="netManager">Aktiver Netcode-Manager (Server-Instanz).</param>
        /// <returns><see cref="UnitStats"/> mit hoechstem Threat oder <c>null</c>.</returns>
        public UnitStats GetHighestThreat(NetworkManager netManager)
        {
            if (netManager == null || m_Table.Count == 0)
            {
                return null;
            }

            UnitStats best = null;
            int bestThreat = -1;
            List<ulong> stale = null;

            foreach (KeyValuePair<ulong, int> kv in m_Table)
            {
                UnitStats stats = ResolveAlive(netManager, kv.Key);
                if (stats == null)
                {
                    stale ??= new List<ulong>(2);
                    stale.Add(kv.Key);
                    continue;
                }
                if (kv.Value > bestThreat)
                {
                    bestThreat = kv.Value;
                    best = stats;
                }
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    m_Table.Remove(stale[i]);
                }
            }

            return best;
        }

        private static UnitStats ResolveAlive(NetworkManager netManager, ulong unitId)
        {
            if (netManager.SpawnManager == null)
            {
                return null;
            }
            if (!netManager.SpawnManager.SpawnedObjects.TryGetValue(unitId, out NetworkObject no) || no == null)
            {
                return null;
            }
            UnitStats stats = no.GetComponent<UnitStats>();
            if (stats == null || stats.IsDead)
            {
                return null;
            }
            return stats;
        }
    }
}
