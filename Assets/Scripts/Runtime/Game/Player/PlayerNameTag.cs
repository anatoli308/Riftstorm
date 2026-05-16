using UnityEngine;

namespace Riftstorm.Game.Player
{
    /// <summary>
    /// Zeichnet den aktuellen <see cref="PlayerIdentity.DisplayName"/> als
    /// 2D-Label &#252;ber dem Kopf des Spielers via IMGUI. Bewusst ohne TextMesh /
    /// TextMeshPro, damit keine Asset-Abh&#228;ngigkeit f&#252;r Phase 4 entsteht.
    ///
    /// <para>
    /// Reine Anzeige-Komponente: lauscht auf <see cref="PlayerIdentity.DisplayNameChanged"/>
    /// und ben&#246;tigt kein Polling. <c>OnGUI</c> wird vom Unity-Loop nur f&#252;r das
    /// Zeichnen pro Frame aufgerufen \u2014 kein State-Polling.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerIdentity))]
    public sealed class PlayerNameTag : MonoBehaviour
    {
        [SerializeField] private PlayerIdentity m_Identity;
        [Tooltip("Optional. Wenn gesetzt, wird die Position dieses Transforms direkt als Welt-Anker f\u00fcr " +
                 "das Nametag-Label verwendet (\u00fcblicherweise ein leeres Child 'NameTagAnchor' \u00fcber dem Kopf). " +
                 "Wenn leer, wird die Anker-H\u00f6he einmalig aus den Renderer-Bounds des Players abgeleitet.")]
        [SerializeField] private Transform m_Anchor;
        [Tooltip("Zus\u00e4tzlicher Offset (Welt-Koordinaten) auf den Anker. Bei Renderer-Bounds-Fallback wird Y " +
                 "automatisch auf die Bounding-Box-Oberkante geschoben und nur dieser Offset addiert.")]
        [SerializeField] private Vector3 m_WorldOffset = new(0f, 0.25f, 0f);
        [SerializeField] private Color m_Color = Color.white;
        [SerializeField] private int m_FontSize = 14;
        [SerializeField] private float m_MaxDistance = 50f;

        private string m_CachedName = string.Empty;
        private GUIStyle m_Style;
        private float m_AutoHeight; // Renderer-Bounds-H\u00f6he, falls m_Anchor leer ist.

        private void Awake()
        {
            if (m_Identity == null)
            {
                m_Identity = GetComponent<PlayerIdentity>();
            }

            // Wenn kein expliziter Anker zugewiesen ist, leiten wir die Kopf-H\u00f6he aus den
            // Renderer-Bounds ab. Das funktioniert sowohl f\u00fcr 2D-Sprites (FLARE) als auch
            // f\u00fcr 3D-Meshes, ohne dass der Designer einen Anker setzen muss.
            if (m_Anchor == null)
            {
                Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactive: false);
                if (renderers.Length > 0)
                {
                    Bounds combined = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        combined.Encapsulate(renderers[i].bounds);
                    }
                    m_AutoHeight = combined.max.y - transform.position.y;
                }
            }
        }

        private void OnEnable()
        {
            if (m_Identity != null)
            {
                m_Identity.DisplayNameChanged += OnNameChanged;
                m_CachedName = m_Identity.DisplayName;
            }
        }

        private void OnDisable()
        {
            if (m_Identity != null)
            {
                m_Identity.DisplayNameChanged -= OnNameChanged;
            }
        }

        private void OnNameChanged(string newName) => m_CachedName = newName;

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(m_CachedName))
            {
                return;
            }
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 worldPos;
            if (m_Anchor != null)
            {
                worldPos = m_Anchor.position + m_WorldOffset;
            }
            else
            {
                worldPos = transform.position + new Vector3(m_WorldOffset.x, m_AutoHeight + m_WorldOffset.y, m_WorldOffset.z);
            }
            Vector3 screen = cam.WorldToScreenPoint(worldPos);
            if (screen.z <= 0f)
            {
                return; // Hinter der Kamera.
            }
            if (m_MaxDistance > 0f && screen.z > m_MaxDistance)
            {
                return;
            }

            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = m_FontSize,
                    fontStyle = FontStyle.Bold,
                };
            }
            m_Style.normal.textColor = m_Color;

            const float width = 220f;
            const float height = 22f;
            // GUI-Koordinaten: Y ist von oben gez&#228;hlt, daher invertieren.
            float guiY = Screen.height - screen.y - height;
            Rect rect = new(screen.x - width * 0.5f, guiY, width, height);

            // Schwarzer Shadow f&#252;r Lesbarkeit (Outline-Effekt).
            Color prev = m_Style.normal.textColor;
            m_Style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), m_CachedName, m_Style);
            m_Style.normal.textColor = prev;
            GUI.Label(rect, m_CachedName, m_Style);
        }
    }
}
