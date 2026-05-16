using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Synchronisiert den Radius eines Trigger-<see cref="SphereCollider"/> mit
    /// <see cref="UnitStats.SelectionRadius"/>. Dieses Volumen ist <b>nur</b> für
    /// den Maus-Pick (Hover/Klick) gedacht — entspricht dem
    /// <c>selectionRadius</c> aus League of Legends. Es ersetzt nicht den
    /// physikalischen Bewegungs-Kollider der Einheit.
    ///
    /// <para>
    /// Erwartet als Sibling oder Child eines GameObjects mit <see cref="UnitStats"/>.
    /// Der Layer sollte der gleiche sein, den
    /// <c>PlayerTargetingInput.m_TargetMask</c> trifft.
    /// </para>
    /// <para>
    /// Single Source of Truth: Radius kommt ausschließlich aus
    /// <see cref="UnitStats.SelectionRadius"/>. Keine eigene Inspector-Zahl.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class SelectionVolume : MonoBehaviour
    {
        private SphereCollider m_Collider;
        private UnitStats m_Stats;

        private void Awake()
        {
            m_Collider = GetComponent<SphereCollider>();
            m_Stats = GetComponentInParent<UnitStats>();
            m_Collider.isTrigger = true;
            SyncRadius();
        }

        private void OnValidate()
        {
            if (m_Collider == null)
            {
                m_Collider = GetComponent<SphereCollider>();
            }
            if (m_Stats == null)
            {
                m_Stats = GetComponentInParent<UnitStats>();
            }
            if (m_Collider != null)
            {
                m_Collider.isTrigger = true;
            }
            SyncRadius();
        }

        /// <summary>
        /// Setzt den Collider-Radius auf <see cref="UnitStats.SelectionRadius"/>.
        /// Skaliert mit dem maximalen Lossy-Scale-Komponenten gegen, damit der
        /// Welt-Radius unabhängig vom Prefab-Scaling stimmt.
        /// </summary>
        private void SyncRadius()
        {
            if (m_Collider == null || m_Stats == null)
            {
                return;
            }
            float worldRadius = m_Stats.SelectionRadius;
            if (worldRadius <= 0f)
            {
                return;
            }
            Vector3 ls = transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            if (maxScale <= 0.0001f)
            {
                maxScale = 1f;
            }
            m_Collider.radius = worldRadius / maxScale;
        }
    }
}
