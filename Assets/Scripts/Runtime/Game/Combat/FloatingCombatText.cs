using System.Collections.Generic;
using Riftstorm.Management.FontManagement;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Client-seitiger Floating-Combat-Text: zeigt eingehenden Schaden
    /// (oder Miss/Dodge/Block) als kurz aufsteigende, ausblendende Zahl
    /// &#252;ber der Einheit. Abonniert <see cref="UnitStats.ClientDamageReceived"/>
    /// auf jedem Peer (inkl. Host) und braucht keinen Server-Code.
    ///
    /// <para>
    /// Bewusst IMGUI-basiert (wie <see cref="Player.PlayerNameTag"/>), damit
    /// keine TextMeshPro- / Canvas-Prefab-Abh&#228;ngigkeit entsteht. Aktive
    /// Eintr&#228;ge werden in einer Liste (kein GC pro Frame) verwaltet und
    /// laufen ohne Coroutines/Timer per <c>Time.time</c>-Diff aus.
    /// </para>
    /// <para>
    /// Setup: Komponente auf das Player- bzw. Enemy-Prefab legen
    /// (RequireComponent UnitStats). Optionaler <c>Anchor</c>-Transform
    /// definiert den Welt-Ankerpunkt &#252;ber dem Kopf; ist er leer, wird er
    /// einmalig aus den Renderer-Bounds abgeleitet.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnitStats))]
    public sealed class FloatingCombatText : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [Header("Refs")]
        [SerializeField] private UnitStats m_Stats;
        [Tooltip("Optionaler Welt-Anker (\u00fcblicherweise ein leeres Child \u00fcber dem Kopf). " +
                 "Wenn leer, wird die H\u00f6he einmalig aus den Renderer-Bounds abgeleitet.")]
        [SerializeField] private Transform m_Anchor;
        [SerializeField] private Vector3 m_WorldOffset = new(0f, 0.5f, 0f);

        [Header("Animation")]
        [SerializeField, Min(0.1f)] private float m_LifetimeSeconds = 1.1f;
        [Tooltip("Welteinheiten pro Sekunde, die der Text nach oben steigt.")]
        [SerializeField] private float m_RiseSpeed = 1.4f;
        [Tooltip("Zuf\u00e4lliger horizontaler Offset in Screen-Pixeln, damit mehrere Treffer nicht \u00fcberlappen.")]
        [SerializeField] private float m_HorizontalJitter = 22f;
        [Tooltip("Max. gleichzeitig aktive Floats pro Einheit. \u00c4ltere werden verworfen.")]
        [SerializeField, Min(1)] private int m_MaxActive = 16;

        [Header("Style")]
        [SerializeField, Min(8)] private int m_FontSize = 18;
        [Tooltip("Maximale Render-Entfernung (Welt-Einheiten). 0 = unbegrenzt.")]
        [SerializeField, Min(0f)] private float m_MaxDistance = 50f;
        [SerializeField] private Color m_HitColor = new(1f, 0.92f, 0.4f, 1f);
        [SerializeField] private Color m_CritColor = new(1f, 0.45f, 0.2f, 1f);
        [SerializeField] private Color m_MitigatedColor = new(0.8f, 0.8f, 0.85f, 1f);
        [SerializeField] private Color m_HealColor = new(0.45f, 1f, 0.6f, 1f);

        // -------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------

        private struct Entry
        {
            public float SpawnTime;
            public int Amount;
            public HitResult Result;
            public float JitterX;
            public bool IsHeal;
        }

        private readonly List<Entry> m_Active = new(16);
        private GUIStyle m_Style;
        private Renderer[] m_CachedRenderers;
        private System.Random m_Rng;

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        private void Awake()
        {
            if (m_Stats == null)
            {
                m_Stats = GetComponent<UnitStats>();
            }
            m_Rng = new(GetInstanceID());
            RefreshRenderersIfNeeded();
        }

        /// <summary>
        /// Cache der Renderer verwerfen, z. B. nach einem Skin-Swap. Wird in
        /// <see cref="OnGUI"/> bei Bedarf neu befüllt.
        /// </summary>
        public void InvalidateRendererCache()
        {
            m_CachedRenderers = null;
        }

        private void RefreshRenderersIfNeeded()
        {
            if (m_Anchor != null)
            {
                return;
            }
            if (Player.UnitNameTag.IsCacheStale(m_CachedRenderers))
            {
                m_CachedRenderers = GetComponentsInChildren<Renderer>(includeInactive: false);
            }
        }

        private void OnEnable()
        {
            if (m_Stats != null)
            {
                m_Stats.ClientDamageReceived += OnDamageReceived;
                m_Stats.ClientHealReceived += OnHealReceived;
            }
        }

        private void OnDisable()
        {
            if (m_Stats != null)
            {
                m_Stats.ClientDamageReceived -= OnDamageReceived;
                m_Stats.ClientHealReceived -= OnHealReceived;
            }
            m_Active.Clear();
        }

        // -------------------------------------------------------------------------
        // Event-Handler
        // -------------------------------------------------------------------------

        private void OnDamageReceived(int amount, HitResult result)
        {
            if (m_Active.Count >= m_MaxActive)
            {
                m_Active.RemoveAt(0); // ältesten Eintrag verwerfen.
            }
            m_Active.Add(new()
            {
                SpawnTime = Time.time,
                Amount = amount,
                Result = result,
                JitterX = ((float)m_Rng.NextDouble() * 2f - 1f) * m_HorizontalJitter,
            });
        }

        private void OnHealReceived(int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            if (m_Active.Count >= m_MaxActive)
            {
                m_Active.RemoveAt(0);
            }
            m_Active.Add(new()
            {
                SpawnTime = Time.time,
                Amount = amount,
                Result = HitResult.Hit,
                JitterX = ((float)m_Rng.NextDouble() * 2f - 1f) * m_HorizontalJitter,
                IsHeal = true,
            });
        }

        // -------------------------------------------------------------------------
        // Render
        // -------------------------------------------------------------------------

        private void OnGUI()
        {
            if (m_Active.Count == 0)
            {
                return;
            }
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            float now = Time.time;

            // Abgelaufene Einträge entfernen (rückwärts iteriert).
            for (int i = m_Active.Count - 1; i >= 0; i--)
            {
                if (now - m_Active[i].SpawnTime >= m_LifetimeSeconds)
                {
                    m_Active.RemoveAt(i);
                }
            }
            if (m_Active.Count == 0)
            {
                return;
            }

            Vector3 baseWorld;
            if (m_Anchor != null)
            {
                baseWorld = m_Anchor.position + m_WorldOffset;
            }
            else
            {
                RefreshRenderersIfNeeded();
                if (Player.UnitNameTag.TryComputeWorldTop(m_CachedRenderers, cam, out Vector3 top))
                {
                    baseWorld = top + m_WorldOffset;
                }
                else
                {
                    baseWorld = transform.position + m_WorldOffset;
                }
            }

            if (m_Style == null)
            {
                m_Style = new(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = m_FontSize,
                    fontStyle = FontStyle.Bold,
                };
                Font font = UIFonts.Numeric;
                if (font != null)
                {
                    m_Style.font = font;
                }
            }

            const float width = 120f;
            const float height = 24f;

            for (int i = 0; i < m_Active.Count; i++)
            {
                Entry e = m_Active[i];
                float age = now - e.SpawnTime;
                float t = age / m_LifetimeSeconds; // 0..1

                Vector3 worldPos = baseWorld + Vector3.up * (m_RiseSpeed * age);
                Vector3 screen = cam.WorldToScreenPoint(worldPos);
                if (screen.z <= 0f)
                {
                    continue;
                }
                if (m_MaxDistance > 0f && screen.z > m_MaxDistance)
                {
                    continue;
                }

                float alpha = 1f - t * t; // Quadratisches Fade-Out.
                Color color = PickColor(e.Result, e.IsHeal);
                color.a *= alpha;

                string label = FormatLabel(e.Amount, e.Result, e.IsHeal);

                float guiY = Screen.height - screen.y - height;
                Rect rect = new(screen.x - width * 0.5f + e.JitterX, guiY, width, height);

                // Schwarzer Schatten für Lesbarkeit.
                Color shadow = Color.black;
                shadow.a = alpha * 0.85f;
                m_Style.normal.textColor = shadow;
                GUI.Label(new(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, m_Style);

                m_Style.normal.textColor = color;
                GUI.Label(rect, label, m_Style);
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private Color PickColor(HitResult result, bool isHeal)
        {
            if (isHeal)
            {
                return m_HealColor;
            }

            return result switch
            {
                HitResult.Crit => m_CritColor,
                HitResult.Miss
                    or HitResult.Dodge
                    or HitResult.Parry
                    or HitResult.Block
                    or HitResult.GlancingBlow
                    or HitResult.Resist
                    or HitResult.Immune
                    or HitResult.Absorb => m_MitigatedColor,
                _ => m_HitColor,
            };
        }

        private static string FormatLabel(int amount, HitResult result, bool isHeal)
        {
            if (isHeal)
            {
                return $"+{amount}";
            }

            return result switch
            {
                HitResult.Miss => "Miss",
                HitResult.Dodge => "Dodge",
                HitResult.Parry => "Parry",
                HitResult.Block => amount > 0 ? $"Block {amount}" : "Block",
                HitResult.Resist => "Resist",
                HitResult.Immune => "Immune",
                HitResult.Absorb => amount > 0 ? $"Absorb {amount}" : "Absorb",
                HitResult.Crit => $"{amount}!",
                HitResult.GlancingBlow => $"~{amount}",
                _ => amount.ToString(),
            };
        }
    }
}
