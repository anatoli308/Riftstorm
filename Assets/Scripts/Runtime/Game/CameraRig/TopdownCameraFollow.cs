using UnityEngine;

namespace Riftstorm.Game.CameraRig
{
    /// <summary>
    /// Lässt die Hauptkamera dem Ziel topdown folgen: Kamera blickt entlang -Y
    /// auf die XZ-Ebene, sodass die in der XZ-Ebene liegenden FLARE-Sprites
    /// flach sichtbar sind. Pure <c>LateUpdate</c>-Bewegung, kein Polling.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class TopdownCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform m_Target;

        [Header("Topdown-Rig")]
        [Tooltip("Höhe über dem Ziel auf der Y-Achse.")]
        [SerializeField] private float m_Height = 12f;
        [Tooltip("Optionaler Z-Offset für leichten Schrägblick (0 = pure Vogelperspektive).")]
        [SerializeField] private float m_BackOffset = 0f;
        [SerializeField] private float m_FollowLerp = 10f;

        /// <summary>Setzt das Ziel der Kamera-Verfolgung und richtet sie sofort aus.</summary>
        public void SetTarget(Transform target)
        {
            m_Target = target;
            if (target != null)
            {
                SnapTo(target.position);
            }
        }

        private void LateUpdate()
        {
            if (m_Target == null)
            {
                return;
            }
            Vector3 desired = ComputePosition(m_Target.position);
            float t = 1f - Mathf.Exp(-m_FollowLerp * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.rotation = ComputeRotation();
        }

        private void SnapTo(Vector3 targetPosition)
        {
            transform.position = ComputePosition(targetPosition);
            transform.rotation = ComputeRotation();
        }

        private Vector3 ComputePosition(Vector3 targetPosition)
        {
            return new Vector3(
                targetPosition.x,
                targetPosition.y + m_Height,
                targetPosition.z - m_BackOffset);
        }

        private Quaternion ComputeRotation()
        {
            // BackOffset = 0  → exakt -Y blickend (Euler 90,0,0).
            // BackOffset > 0 → leicht schräg topdown, LookAt liefert die korrekte Neigung.
            if (Mathf.Approximately(m_BackOffset, 0f) || m_Target == null)
            {
                return Quaternion.Euler(90f, 0f, 0f);
            }
            Vector3 forward = m_Target.position - transform.position;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return Quaternion.Euler(90f, 0f, 0f);
            }
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
