using Riftstorm.Game.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Zeichnet Aggro-, Deaggro- und Attack-Reichweite eines <see cref="NpcController"/>
    /// als sichtbare Ringe in der Spielwelt &#8212; im Gegensatz zu den Editor-Gizmos
    /// auch auf jedem Client und im Game-View. Ziel: Visuelles Tuning der AI ohne
    /// Editor-Selection-Tricks.
    /// </summary>
    /// <remarks>
    /// Erzeugt drei <see cref="LineRenderer"/>-Childs, die als geschlossene
    /// XZ-Kreise um den NPC laufen. Ein gemeinsames Unlit-Material wird statisch
    /// gecached &#8212; pro Ring fallen ausschliesslich die LineRenderer-Allokationen
    /// an. Komponente ist <b>opt-in</b> und sollte nur an Debug-/Tuning-Prefabs
    /// oder per Editor-Toggle aktiviert werden &#8212; bei hunderten NPCs sind 3
    /// LineRenderer pro Instanz nicht produktionstauglich.
    /// <para>
    /// Read-only Bindings:
    /// <list type="bullet">
    ///   <item><see cref="NpcController.AggroRange"/></item>
    ///   <item><see cref="NpcController.LeashRange"/> (FLARE-Port nutzt Leash statt Deaggro)</item>
    ///   <item><see cref="NpcController.MeleeRange"/> + <see cref="UnitStats.HitRadius"/></item>
    /// </list>
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcController))]
    public sealed class NpcDebugVisualizer : MonoBehaviour
    {
        [Header("Referenz")]
        [SerializeField] private NpcController m_Npc;

        [Header("Sichtbarkeit")]
        [Tooltip("Master-Schalter. Aus = saemtliche Ringe versteckt.")]
        [SerializeField] private bool m_VisibilityMaster = true;
        [SerializeField] private bool m_ShowAggro = true;
        [SerializeField] private bool m_ShowDeaggro = true;
        [SerializeField] private bool m_ShowAttackRange = true;

        [Header("Darstellung")]
        [Range(8, 128)]
        [SerializeField] private int m_Segments = 48;

        [Tooltip("Linienstaerke der Ringe in Welt-Metern.")]
        [SerializeField] private float m_LineWidth = 0.04f;

        [Tooltip("Hoehe ueber dem NPC-Pivot, auf der die Ringe gezeichnet werden. Verhindert Z-Fighting mit dem Boden.")]
        [SerializeField] private float m_GroundOffset = 0.02f;

        [SerializeField] private Color m_AggroColor = new(0f, 1f, 0f, 0.9f);
        [SerializeField] private Color m_DeaggroColor = new(1f, 0.5f, 0f, 0.85f);
        [SerializeField] private Color m_AttackColor = new(1f, 1f, 0f, 0.95f);

        private LineRenderer m_AggroRing;
        private LineRenderer m_DeaggroRing;
        private LineRenderer m_AttackRing;

        private static Material s_SharedMaterial;

        private void Awake()
        {
            if (m_Npc == null)
            {
                m_Npc = GetComponent<NpcController>();
            }
        }

        private void OnEnable()
        {
            BuildRings();
            ApplyVisibility();
        }

        private void OnDisable()
        {
            // Komponente ausschalten -> Ringe verstecken, aber nicht zerstoeren,
            // damit ein Re-Enable nicht erneut allokieren muss.
            SetAllEnabled(false);
        }

        private void LateUpdate()
        {
            if (m_Npc == null)
            {
                return;
            }
            ApplyVisibility();
            if (!m_VisibilityMaster)
            {
                return;
            }

            if (m_ShowAggro && m_AggroRing != null)
            {
                FillCircle(m_AggroRing, m_Npc.AggroRange);
            }
            if (m_ShowDeaggro && m_DeaggroRing != null)
            {
                // FLARE kennt kein separates Deaggro — Leash uebernimmt die Rolle
                // (NPC bricht ab, sobald er zu weit vom Spawn entfernt ist).
                FillCircle(m_DeaggroRing, m_Npc.LeashRange);
            }
            if (m_ShowAttackRange && m_AttackRing != null)
            {
                UnitStats selfStats = m_Npc.GetComponent<UnitStats>();
                float selfRadius = selfStats != null ? selfStats.HitRadius : 0f;
                FillCircle(m_AttackRing, m_Npc.MeleeRange + selfRadius);
            }
        }

        private void BuildRings()
        {
            if (m_AggroRing == null)
            {
                m_AggroRing = CreateRing("DebugRing_Aggro", m_AggroColor);
            }
            if (m_DeaggroRing == null)
            {
                m_DeaggroRing = CreateRing("DebugRing_Deaggro", m_DeaggroColor);
            }
            if (m_AttackRing == null)
            {
                m_AttackRing = CreateRing("DebugRing_Attack", m_AttackColor);
            }
        }

        private LineRenderer CreateRing(string childName, Color color)
        {
            GameObject go = new(childName);
            Transform t = go.transform;
            t.SetParent(transform, worldPositionStays: false);
            t.localPosition = new(0f, m_GroundOffset, 0f);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = m_Segments;
            lr.widthMultiplier = m_LineWidth;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.allowOcclusionWhenDynamic = false;
            lr.alignment = LineAlignment.View;
            lr.sharedMaterial = GetSharedMaterial();
            return lr;
        }

        private static Material GetSharedMaterial()
        {
            if (s_SharedMaterial != null)
            {
                return s_SharedMaterial;
            }
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            s_SharedMaterial = new Material(shader)
            {
                name = "NpcDebugVisualizer_SharedRing",
                hideFlags = HideFlags.DontSave,
            };
            return s_SharedMaterial;
        }

        private void FillCircle(LineRenderer lr, float radius)
        {
            if (radius <= 0f)
            {
                lr.enabled = false;
                return;
            }
            if (lr.positionCount != m_Segments)
            {
                lr.positionCount = m_Segments;
            }
            if (!Mathf.Approximately(lr.widthMultiplier, m_LineWidth))
            {
                lr.widthMultiplier = m_LineWidth;
            }
            float step = 2f * Mathf.PI / m_Segments;
            for (int i = 0; i < m_Segments; i++)
            {
                float a = step * i;
                lr.SetPosition(i, new(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
        }

        private void ApplyVisibility()
        {
            if (m_AggroRing != null)
            {
                m_AggroRing.enabled = m_VisibilityMaster && m_ShowAggro;
            }
            if (m_DeaggroRing != null)
            {
                m_DeaggroRing.enabled = m_VisibilityMaster && m_ShowDeaggro;
            }
            if (m_AttackRing != null)
            {
                m_AttackRing.enabled = m_VisibilityMaster && m_ShowAttackRange;
            }
        }

        private void SetAllEnabled(bool active)
        {
            if (m_AggroRing != null)
            {
                m_AggroRing.enabled = active;
            }
            if (m_DeaggroRing != null)
            {
                m_DeaggroRing.enabled = active;
            }
            if (m_AttackRing != null)
            {
                m_AttackRing.enabled = active;
            }
        }
    }
}
