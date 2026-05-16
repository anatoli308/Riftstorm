using UnityEngine;

namespace Tolik.Riftstorm.Runtime.Gameplay.Combat
{
    /// <summary>
    /// Stellt sicher, dass eine Einheit (Spieler/NPC) einen Collider hat, ueber
    /// den Maus-Picking (Klick / Hover) und Tab-Cycling funktionieren.
    ///
    /// <para>
    /// Liegt am <c>NetworkObject</c>-Root und fuegt im <see cref="Awake"/>
    /// automatisch einen <see cref="CapsuleCollider"/> hinzu, falls noch keiner
    /// existiert. Damit funktioniert die Selektion unabhaengig davon, ob die
    /// Einheit per <see cref="CharacterController"/>, Rigidbody oder gar nicht
    /// physikalisch bewegt wird. Source-treu: Hits werden ueber Ziel-Id und
    /// 2D-Distanz aufgeloest, der Collider hier ist ausschliesslich fuer das
    /// Picking auf der Client-Seite und nicht fuer die Schadensberechnung.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TargetingHitbox : MonoBehaviour
    {
        [Header("Capsule (Y-Achse)")]
        [Tooltip("Radius des Auto-Capsule-Colliders, falls noch keiner am GameObject haengt.")]
        [SerializeField, Min(0.05f)] private float m_Radius = 0.4f;
        [Tooltip("Hoehe des Auto-Capsule-Colliders, falls noch keiner am GameObject haengt.")]
        [SerializeField, Min(0.1f)] private float m_Height = 1.8f;
        [Tooltip("Lokales Zentrum (Y nach oben), passend zur Sprite-Kopfhoehe.")]
        [SerializeField] private Vector3 m_Center = new(0f, 0.9f, 0f);
        [Tooltip("Trigger-Collider werden vom Raycast mit QueryTriggerInteraction.Collide weiterhin getroffen, " +
                 "blockieren aber keine Physik. Empfohlen: true, solange wir manuelle Physik nutzen.")]
        [SerializeField] private bool m_IsTrigger = true;

        private void Awake()
        {
            if (TryGetComponent<Collider>(out _))
            {
                return;
            }

            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1; // Y-Achse
            capsule.radius = m_Radius;
            capsule.height = m_Height;
            capsule.center = m_Center;
            capsule.isTrigger = m_IsTrigger;
        }
    }
}
