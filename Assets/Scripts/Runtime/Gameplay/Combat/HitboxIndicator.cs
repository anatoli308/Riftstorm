using UnityEngine;

namespace Tolik.Riftstorm.Runtime.Gameplay.Combat
{
    /// <summary>
    /// Zeichnet einen Kreis am Boden um eine Einheit (League-of-Legends-Style Hitbox-Anzeige).
    /// Rein visuell, lokal pro Client. Kein Netcode, keine Server-Autorit&#228;t n&#246;tig.
    /// Sichtbarkeit wird extern per <see cref="Show"/> / <see cref="Hide"/> geschaltet
    /// — typischerweise vom Owner-Client als Reaktion auf das LOCK-Target
    /// (<c>TargetSelection.CurrentTargetIdChanged</c>). Kein Hover-Auto-Toggle mehr.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class HitboxIndicator : MonoBehaviour
    {
        [Header("Geometrie")]
        [Tooltip("Wenn gesetzt, wird der Radius automatisch aus diesem CapsuleCollider gelesen " +
                 "(inkl. lossyScale). Bleibt das Feld leer, sucht der Indicator beim Awake im Parent " +
                 "nach einem CapsuleCollider. Greift keiner, wird m_Radius verwendet.")]
        [SerializeField] private CapsuleCollider m_MatchCollider;
        [SerializeField, Min(0.05f)] private float m_Radius = 0.5f;
        [SerializeField, Range(8, 128)] private int m_Segments = 48;
        [SerializeField] private float m_GroundOffset = 0.02f;

        [Header("Darstellung")]
        [SerializeField] private Color m_Color = new(0.25f, 1f, 0.45f, 0.95f);
        [SerializeField, Min(0.005f)] private float m_Width = 0.05f;
        [SerializeField] private bool m_AlwaysVisible = false;

        private LineRenderer m_Line;
        private Material m_RuntimeMaterial;

        private void Awake()
        {
            m_Line = GetComponent<LineRenderer>();
            if (m_MatchCollider == null)
            {
                m_MatchCollider = GetComponentInParent<CapsuleCollider>();
            }
            SyncRadiusFromCollider();
            ConfigureLineRenderer();
            RebuildCircle();
            m_Line.enabled = m_AlwaysVisible;
        }

        /// <summary>
        /// Uebernimmt den Radius vom referenzierten <see cref="CapsuleCollider"/>
        /// (inkl. world-space Scale). Ohne Collider bleibt <c>m_Radius</c> erhalten.
        /// </summary>
        private void SyncRadiusFromCollider()
        {
            if (m_MatchCollider == null)
            {
                return;
            }
            Vector3 scale = m_MatchCollider.transform.lossyScale;
            float scaleXZ = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            m_Radius = Mathf.Max(0.05f, m_MatchCollider.radius * scaleXZ);
        }

        private void OnDestroy()
        {
            if (m_RuntimeMaterial != null)
            {
                Destroy(m_RuntimeMaterial);
            }
        }

        private void ConfigureLineRenderer()
        {
            m_Line.useWorldSpace = false;
            m_Line.loop = true;
            m_Line.widthMultiplier = m_Width;
            m_Line.positionCount = m_Segments;
            m_Line.alignment = LineAlignment.View;
            m_Line.textureMode = LineTextureMode.Stretch;
            m_Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Line.receiveShadows = false;
            m_Line.startColor = m_Color;
            m_Line.endColor = m_Color;

            if (m_Line.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }
                m_RuntimeMaterial = new Material(shader) { color = m_Color };
                m_Line.material = m_RuntimeMaterial;
            }
        }

        private void RebuildCircle()
        {
            var positions = new Vector3[m_Segments];
            for (int i = 0; i < m_Segments; i++)
            {
                float angle = (i / (float)m_Segments) * Mathf.PI * 2f;
                positions[i] = new Vector3(
                    Mathf.Cos(angle) * m_Radius,
                    m_GroundOffset,
                    Mathf.Sin(angle) * m_Radius);
            }
            m_Line.SetPositions(positions);
        }

        /// <summary>Zeigt den Kreis an (z.B. bei Hover oder Selection).</summary>
        public void Show()
        {
            if (m_Line != null)
            {
                m_Line.enabled = true;
            }
        }

        /// <summary>Versteckt den Kreis, sofern nicht <see cref="m_AlwaysVisible"/> aktiv ist.</summary>
        public void Hide()
        {
            if (m_Line != null && !m_AlwaysVisible)
            {
                m_Line.enabled = false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_Line == null)
            {
                m_Line = GetComponent<LineRenderer>();
            }
            if (m_MatchCollider == null)
            {
                m_MatchCollider = GetComponentInParent<CapsuleCollider>();
            }
            SyncRadiusFromCollider();
            if (m_Line != null && m_Line.positionCount > 0)
            {
                ConfigureLineRenderer();
                RebuildCircle();
                m_Line.enabled = m_AlwaysVisible || m_Line.enabled;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(m_Color.r, m_Color.g, m_Color.b, 0.4f);
            Vector3 c = transform.position + Vector3.up * m_GroundOffset;
            const int n = 48;
            Vector3 prev = c + new Vector3(m_Radius, 0f, 0f);
            for (int i = 1; i <= n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                Vector3 next = c + new Vector3(Mathf.Cos(a) * m_Radius, 0f, Mathf.Sin(a) * m_Radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
