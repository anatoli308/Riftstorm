using Riftstorm.Game.Input;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Owner-lokaler LoL-Style Boden-Kreis mit Radius = <see cref="PlayerCombat.CurrentWeaponRange"/>.
    /// Wird per <see cref="PlayerInputController.AttackRangeIndicatorPressed"/> ein-/ausgeschaltet.
    /// 
    /// <para>
    /// Rein client-seitiges Visual: keine Netcode-Synchronisation, keine Server-Autoritaet.
    /// Beim Toggle-On wird die Range frisch aus <see cref="PlayerCombat"/> gelesen — Waffen-
    /// wechsel zur Laufzeit aktualisieren den Kreis also automatisch beim naechsten Press.
    /// </para>
    /// <para>
    /// Event-driven, kein Polling: das Material wird nur bei Show/Hide neu geschrieben. Bei
    /// Bewegung folgt der LineRenderer dem Transform automatisch (useWorldSpace = false).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackRangeIndicator : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PlayerCombat m_Combat;
        [SerializeField] private PlayerInputController m_Input;

        [Header("Darstellung")]
        [Tooltip("Farbe des Kreises. Default = LoL-typisches Cyan.")]
        [SerializeField] private Color m_Color = new(0.25f, 0.85f, 1f, 0.9f);
        [SerializeField, Min(0.005f)] private float m_LineWidth = 0.06f;
        [SerializeField, Range(16, 256)] private int m_Segments = 64;
        [Tooltip("Hoehe ueber dem Boden, in der der Kreis gezeichnet wird (verhindert Z-Fighting).")]
        [SerializeField] private float m_GroundOffset = 0.03f;
        [Tooltip("Fallback-Radius, wenn die Waffe noch nicht geladen ist (z.B. wegen Katalog-Latenz).")]
        [SerializeField, Min(0.05f)] private float m_FallbackRadius = 1.5f;

        private LineRenderer m_Line;
        private Material m_RuntimeMaterial;
        private GameObject m_LineHost;
        private bool m_Visible;
        private float m_LastRenderedRadius;

        private void Awake()
        {
            if (m_Combat == null)
            {
                m_Combat = GetComponentInParent<PlayerCombat>();
            }
            if (m_Input == null)
            {
                m_Input = GetComponentInParent<PlayerInputController>();
            }
        }

        private void OnEnable()
        {
            if (m_Input != null)
            {
                m_Input.AttackRangeIndicatorPressed += OnTogglePressed;
            }
        }

        private void OnDisable()
        {
            if (m_Input != null)
            {
                m_Input.AttackRangeIndicatorPressed -= OnTogglePressed;
            }
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (m_RuntimeMaterial != null)
            {
                Destroy(m_RuntimeMaterial);
            }
        }

        /// <summary>
        /// Owner-Toggle. Zeigt den Kreis mit der aktuellen Waffen-Range an, bzw. blendet
        /// ihn wieder aus. Beim Einblenden wird die Range frisch gelesen (Waffenwechsel-safe).
        /// </summary>
        private void OnTogglePressed()
        {
            SetVisible(!m_Visible);
        }

        private void SetVisible(bool visible)
        {
            if (visible)
            {
                EnsureLine();
                float radius = ResolveRadius();
                if (!Mathf.Approximately(radius, m_LastRenderedRadius))
                {
                    BuildCircle(radius);
                    m_LastRenderedRadius = radius;
                }
                m_Line.enabled = true;
                m_Visible = true;
            }
            else
            {
                if (m_Line != null)
                {
                    m_Line.enabled = false;
                }
                m_Visible = false;
            }
        }

        private float ResolveRadius()
        {
            if (m_Combat == null)
            {
                return m_FallbackRadius;
            }
            float r = m_Combat.CurrentWeaponRange;
            return r > 0f ? r : m_FallbackRadius;
        }

        private void EnsureLine()
        {
            if (m_Line != null)
            {
                return;
            }
            // Eigenes Child-GameObject, damit der LineRenderer nicht mit anderen Komponenten
            // am Root kollidiert (HitboxIndicator hat seinen eigenen).
            m_LineHost = new GameObject("AttackRangeIndicatorLine");
            m_LineHost.transform.SetParent(transform, false);
            m_LineHost.transform.localPosition = Vector3.zero;
            m_LineHost.transform.localRotation = Quaternion.identity;
            m_Line = m_LineHost.AddComponent<LineRenderer>();
            m_Line.useWorldSpace = false;
            m_Line.loop = true;
            m_Line.widthMultiplier = m_LineWidth;
            m_Line.alignment = LineAlignment.View;
            m_Line.textureMode = LineTextureMode.Stretch;
            m_Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Line.receiveShadows = false;
            m_Line.startColor = m_Color;
            m_Line.endColor = m_Color;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            m_RuntimeMaterial = new Material(shader) { color = m_Color };
            m_Line.material = m_RuntimeMaterial;
            m_Line.enabled = false;
        }

        private void BuildCircle(float radius)
        {
            if (m_Line == null)
            {
                return;
            }
            m_Line.positionCount = m_Segments;
            var positions = new Vector3[m_Segments];
            for (int i = 0; i < m_Segments; i++)
            {
                float angle = (i / (float)m_Segments) * Mathf.PI * 2f;
                positions[i] = new Vector3(
                    Mathf.Cos(angle) * radius,
                    m_GroundOffset,
                    Mathf.Sin(angle) * radius);
            }
            m_Line.SetPositions(positions);
        }
    }
}
