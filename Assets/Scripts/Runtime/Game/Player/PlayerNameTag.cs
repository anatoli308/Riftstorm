using Riftstorm.ApplicationLifecycle.UI;
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
                 "Wenn leer, wird der Anker fest aus Root-Transform + HeadHeight gebildet.")]
        [SerializeField] private Transform m_Anchor;
        [Tooltip("Welt-H\u00f6he in Metern \u00fcber der Root-Transform, an der das Label sitzen soll, wenn kein " +
                 "expliziter Anker gesetzt ist. Bewusst KEIN Renderer-Bounds-Lookup, weil FLARE-Sprite-Layer " +
                 "flach auf der XZ-Ebene liegen und ihre Bounds pro Animationsframe wandern " +
                 "(Waffe/Buckler ragen mal raus). Im Inspector pro Charaktergr\u00f6\u00dfe tunbar.")]
        [SerializeField] private float m_HeadHeight = 1.2f;
        [Tooltip("Zusätzlicher Offset (Welt-Koordinaten) auf den Anker. " +
                 "Wird zur Laufzeit IGNORIERT — wir nutzen überall den festen Offset (0, -2, 0.8), " +
                 "damit alte Prefab-Werte den Default nicht mehr überschreiben können.")]
        [SerializeField] private Vector3 m_WorldOffset = new(0f, -2f, 0.8f);

        /// <summary>Fester Offset für alle Nametags. Bewusst NICHT serialisiert, damit
        /// Anpassungen in Code nicht von alten Prefab-Werten überschrieben werden.</summary>
        private static readonly Vector3 s_FixedWorldOffset = new(0f, -2f, 0.8f);
        [SerializeField] private Color m_Color = Color.white;
        [SerializeField] private int m_FontSize = 14;
        [SerializeField] private float m_MaxDistance = 50f;

        private string m_CachedName = string.Empty;
        private GUIStyle m_Style;
        private Renderer[] m_CachedRenderers;

        private void Awake()
        {
            if (m_Identity == null)
            {
                m_Identity = GetComponent<PlayerIdentity>();
            }

            // Wenn kein expliziter Anker zugewiesen ist, leiten wir die Kopf-H\u00f6he aus den
            // Renderer-Bounds ab. Das funktioniert sowohl f\u00fcr 2D-Sprites (FLARE) als auch
            // f\u00fcr 3D-Meshes, ohne dass der Designer einen Anker setzen muss.
            RefreshRenderersIfNeeded();
        }

        /// <summary>
        /// Soll aufgerufen werden, wenn sich die Renderer-Hierarchie aendert (z.B.
        /// nach einem Skin-Apply). Der Cache wird bei Bedarf in OnGUI erneuert,
        /// falls er leer wurde.
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
            if (IsCacheStale(m_CachedRenderers))
            {
                m_CachedRenderers = GetComponentsInChildren<Renderer>(includeInactive: false);
            }
        }

        /// <summary>
        /// Cache gilt als veraltet, wenn er leer ist oder mindestens ein Eintrag
        /// zerstoert wurde (z. B. durch einen Skin-Swap, der die Renderer-Hierarchie
        /// austauscht). Unity's '==' meldet zerstoerte Objekte als null.
        /// </summary>
        internal static bool IsCacheStale(Renderer[] cache)
        {
            if (cache == null || cache.Length == 0)
            {
                return true;
            }
            for (int i = 0; i < cache.Length; i++)
            {
                if (cache[i] == null)
                {
                    return true;
                }
            }
            return false;
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

        /// <summary>
        /// Liefert die Welt-Position des Renderer-Bounds-Eckpunkts, der unter der
        /// gegebenen Kamera am weitesten OBEN am Bildschirm landet. Funktioniert
        /// fuer aufrechte 3D-Meshes ebenso wie fuer FLARE-Sprite-Layer, die um 90 Grad
        /// auf die XZ-Ebene gekippt sind (dort liegt der visuelle Kopf nicht bei
        /// bounds.max.y, sondern bei bounds.max.z). Es werden alle 8 AABB-Ecken
        /// jedes aktiven Renderers ausgewertet und der mit dem groessten screen.y
        /// gewinnt. Liefert <c>false</c>, wenn kein gueltiger Eckpunkt vor der
        /// Kamera liegt.
        /// </summary>
        internal static bool TryComputeWorldTop(Renderer[] renderers, Camera cam, out Vector3 worldTop)
        {
            worldTop = default;
            if (renderers == null || cam == null)
            {
                return false;
            }
            bool hasAny = false;
            float bestScreenY = float.NegativeInfinity;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Bounds b = r.bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            Vector3 corner = new(c.x + sx * e.x, c.y + sy * e.y, c.z + sz * e.z);
                            Vector3 screen = cam.WorldToScreenPoint(corner);
                            if (screen.z <= 0f)
                            {
                                continue;
                            }
                            if (!hasAny || screen.y > bestScreenY)
                            {
                                bestScreenY = screen.y;
                                worldTop = corner;
                                hasAny = true;
                            }
                        }
                    }
                }
            }
            return hasAny;
        }

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

            // Fester Offset \u2014 ignoriert serialisierte Prefab-Werte (siehe s_FixedWorldOffset).
            Vector3 worldPos;
            if (m_Anchor != null)
            {
                worldPos = m_Anchor.position + s_FixedWorldOffset;
            }
            else
            {
                // Fester Welt-Y-Offset über der Root-Transform. Bewusst KEIN
                // Renderer-Bounds-Lookup: FLARE-Layer liegen flach auf der XZ-Ebene
                // und ihre Bounds wandern pro Animationsframe.
                worldPos = transform.position
                           + (Vector3.up * m_HeadHeight)
                           + s_FixedWorldOffset;
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
                Font font = UIFonts.Small;
                if (font != null)
                {
                    m_Style.font = font;
                }
            }
            // m_Color für alle Button-States setzen, damit GUI.Button beim Hover/Click
            // NICHT auf die weiße Skin-Hover-Farbe umschaltet. Source-treu: der Nametag
            // hat keine Hover-Aufhellung — die übernimmt der Sprite via HoverHighlight.
            m_Style.normal.textColor = m_Color;
            m_Style.hover.textColor = m_Color;
            m_Style.active.textColor = m_Color;
            m_Style.focused.textColor = m_Color;
            m_Style.onNormal.textColor = m_Color;
            m_Style.onHover.textColor = m_Color;
            m_Style.onActive.textColor = m_Color;
            m_Style.onFocused.textColor = m_Color;

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

            // Klick auf das Label selektiert die Einheit (source-aequivalent zum
            // Overhead-Klick im SoF-Client). Wir nutzen GUI.Button mit dem gleichen
            // Label-Style (kein Hintergrund) — visuell identisch zum vorherigen GUI.Label,
            // aber klickbar. Klick aufs eigene Nametag ist ein No-Op.
            if (GUI.Button(rect, m_CachedName, m_Style))
            {
                HandleNameTagClicked();
            }
        }

        /// <summary>
        /// Sendet einen Lock-Wunsch fuer DIESE Einheit an den Server. Lookup des
        /// lokalen <see cref="Riftstorm.Game.Combat.TargetSelection"/> erfolgt
        /// statisch (Owner registriert sich beim NetworkSpawn), kein FindObject.
        /// </summary>
        private void HandleNameTagClicked()
        {
            if (m_Identity == null)
            {
                return;
            }
            Unity.Netcode.NetworkObject myNo = m_Identity.GetComponent<Unity.Netcode.NetworkObject>();
            if (myNo == null)
            {
                myNo = m_Identity.GetComponentInParent<Unity.Netcode.NetworkObject>();
            }
            if (myNo == null || !myNo.IsSpawned)
            {
                return;
            }
            Riftstorm.Game.Combat.TargetSelection localTs = Riftstorm.Game.Combat.TargetSelection.Local;
            if (localTs == null)
            {
                return;
            }
            // Eigenes Nametag klicken = No-Op (kein Selbst-Lock).
            if (localTs.NetworkObjectId == myNo.NetworkObjectId)
            {
                return;
            }
            localTs.RequestSelectTargetServerRpc(myNo.NetworkObjectId);
        }
    }
}
