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
        private Unity.Netcode.NetworkObject m_CachedNetworkObject;

        // Lazy-resolved Hover-Plate-Texturen. Konfig kommt aus
        // StreamingAssets/interface/nametag_config.json via NameTagConfigLoader,
        // Textur-Aufloesung via TextureManager (Pure Service). Wir resolven
        // einmalig beim ersten OnGUI-Pass, damit der TextureManager im
        // ServiceLocator garantiert registriert ist (ApplicationEntryPoint.Awake).
        private Texture2D m_HoverPlateTexture;
        private Texture2D m_IdlePlateTexture;
        private bool m_HoverPlateResolved;

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
                // GUIStyle bewusst OHNE GUI.skin.label-Inheritance bauen. Sonst erbt
                // m_Style die hover/active/focused/onNormal/... State-Slots aus dem
                // aktiven EditorSkin — und GUI.Label rendert bei Mouse-Over dann den
                // hover-State (anderer textColor, fettere Schrift). Genau das war der
                // sichtbare "weiss + etwas fetter bei Hover"-Effekt.
                m_Style = new GUIStyle
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
            // Alle acht State-Slots auf identische Werte zwingen, damit GUI.Label
            // garantiert NIE auf hover/active/focused/onXxx umschaltet — weder
            // Farbe, noch Background, noch Scaled-Backgrounds. Das ist die einzige
            // zuverlaessige Methode, IMGUI-State-Switching am Label abzuschalten.
            m_Style.normal.textColor = m_Color;
            m_Style.normal.background = null;
            m_Style.normal.scaledBackgrounds = System.Array.Empty<Texture2D>();
            m_Style.hover = m_Style.normal;
            m_Style.active = m_Style.normal;
            m_Style.focused = m_Style.normal;
            m_Style.onNormal = m_Style.normal;
            m_Style.onHover = m_Style.normal;
            m_Style.onActive = m_Style.normal;
            m_Style.onFocused = m_Style.normal;

            const float width = 220f;
            const float height = 22f;
            // GUI-Koordinaten: Y ist von oben gez&#228;hlt, daher invertieren.
            float guiY = Screen.height - screen.y - height;
            Rect rect = new(screen.x - width * 0.5f, guiY, width, height);

            // Hover-Test: Event.current.mousePosition liegt in OnGUI bereits in
            // GUI-Koordinaten (oben-links, identisches Space wie rect). Kein Y-Flip,
            // keine Input-System-Abh\u00e4ngigkeit, kein DPI/Game-View-Scale-Risiko.
            // Eigener Nametag wird nie als "hovered" gewertet \u2014 Selbst-Hover macht
            // weder visuell noch gameplay-seitig Sinn (Klick ist bereits No-Op).
            // Self-Detection \u00fcber IsOwner statt IsLocalPlayer, weil Letzteres nur
            // greift, wenn die NetworkObject via NetworkManager PlayerObject markiert
            // wurde (ConnectionApprovalResponse), w\u00e4hrend IsOwner f\u00fcr jeden
            // Client-besessenen NetworkObject zuverl\u00e4ssig true ist.
            bool isLocalPlayer = false;
            Unity.Netcode.NetworkObject selfNo = GetCachedNetworkObject();
            if (selfNo != null && selfNo.IsSpawned && selfNo.IsOwner)
            {
                isLocalPlayer = true;
            }
            // Hover erzeugt KEINE visuelle Aenderung am Nametag — Hover-Feedback
            // laeuft ueber den Sprite (HoverHighlight). Outline gibt es ausschliesslich
            // fuer das aktive Target-Lock UND niemals fuer das eigene Nametag (du
            // kannst dich selbst nicht als Target locken, ein Self-Outline waere
            // nur visueller Lärm).
            bool drawOutline = !isLocalPlayer && IsCurrentlyTargeted();

            // Hover-Plate (datengetrieben via nametag_config.json). Wird VOR dem
            // Text gezeichnet, damit der Text obendrauf sitzt. Nur fuer fremde
            // Nametags — das eigene bleibt nackt. Idle- und Hover-Texturen sind
            // unabhaengig konfigurierbar; leere Keys = keine Plate fuer den jeweiligen Zustand.
            if (!isLocalPlayer)
            {
                NameTagConfig cfg = NameTagConfigLoader.Load();
                if (cfg.hoverPlateEnabled)
                {
                    if (!m_HoverPlateResolved)
                    {
                        m_HoverPlateTexture = NameTagConfigLoader.LoadTextureOrNull(cfg.hoverPlateTexture);
                        m_IdlePlateTexture = NameTagConfigLoader.LoadTextureOrNull(cfg.idlePlateTexture);
                        m_HoverPlateResolved = true;
                    }
                    bool mouseOver = rect.Contains(Event.current.mousePosition);
                    Texture2D plate = mouseOver ? m_HoverPlateTexture : m_IdlePlateTexture;
                    if (plate != null)
                    {
                        Rect plateRect = new(
                            rect.x - cfg.platePaddingX,
                            rect.y - cfg.platePaddingY,
                            rect.width + cfg.platePaddingX * 2f,
                            rect.height + cfg.platePaddingY * 2f);
                        GUI.DrawTexture(plateRect, plate, ScaleMode.StretchToFill, alphaBlend: true);
                    }
                }
            }

            if (isLocalPlayer)
            {
                // Eigenes Nametag: ausschliesslich reiner Text, keine Extra-Passes
                // (kein Drop-Shadow, keine Outline). So gibt es null wahrnehmbare
                // Zustandsaenderung am eigenen Nametag — egal was die Maus tut.
            }
            else if (drawOutline)
            {
                // Schwarze Outline um den weissen Haupttext (nur fuer Target-Lock).
                Color prev = m_Style.normal.textColor;
                m_Style.normal.textColor = Color.black;
                DrawOutline(rect, m_CachedName, m_Style);
                m_Style.normal.textColor = prev;
            }
            else
            {
                // Klassischer 1px-Drop-Shadow als Lesbarkeits-Hilfe ohne Target-Lock.
                Color prev = m_Style.normal.textColor;
                m_Style.normal.textColor = Color.black;
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), m_CachedName, m_Style);
                m_Style.normal.textColor = prev;
            }
            GUI.Label(rect, m_CachedName, m_Style);

            // Klick auf das Label selektiert die Einheit (source-aequivalent zum
            // Overhead-Klick im SoF-Client). Manueller MouseDown-Hit-Test — kein
            // GUI.Button, das einen Hover-Repaint-Pass mit Bold-Strokes erzeugen
            // wuerde. Klick auf eigenes Nametag = No-Op.
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !isLocalPlayer && rect.Contains(e.mousePosition))
            {
                HandleNameTagClicked();
                e.Use();
            }
        }

        /// <summary>
        /// Zeichnet das Label mehrfach versetzt in der aktuellen (schwarzen)
        /// <c>textColor</c> des Styles. ±2px statt ±1px, weil <c>FontStyle.Bold</c>-
        /// Strokes selbst etwa 1-2 Pixel breit sind und einen 1px-Outline-Offset
        /// komplett unter dem Hauptzeichen verschwinden lassen würden. Bewusst kein
        /// TextMeshPro — Outline-Effekt im IMGUI-Pfad.
        /// </summary>
        private static void DrawOutline(Rect rect, string text, GUIStyle style)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }
                    GUI.Label(new Rect(rect.x + dx, rect.y + dy, rect.width, rect.height), text, style);
                }
            }
        }

        /// <summary>
        /// True, wenn die lokale <see cref="Riftstorm.Game.Combat.TargetSelection"/>
        /// genau diese Einheit anvisiert. Dauerhafter Outline für das Target‑Lock.
        /// </summary>
        private bool IsCurrentlyTargeted()
        {
            Riftstorm.Game.Combat.TargetSelection localTs = Riftstorm.Game.Combat.TargetSelection.Local;
            if (localTs == null || !localTs.HasTarget)
            {
                return false;
            }
            Unity.Netcode.NetworkObject myNo = GetCachedNetworkObject();
            if (myNo == null || !myNo.IsSpawned)
            {
                return false;
            }
            return localTs.CurrentTargetId == myNo.NetworkObjectId;
        }

        private Unity.Netcode.NetworkObject GetCachedNetworkObject()
        {
            if (m_CachedNetworkObject != null)
            {
                return m_CachedNetworkObject;
            }
            if (m_Identity == null)
            {
                return null;
            }
            m_CachedNetworkObject = m_Identity.GetComponent<Unity.Netcode.NetworkObject>();
            if (m_CachedNetworkObject == null)
            {
                m_CachedNetworkObject = m_Identity.GetComponentInParent<Unity.Netcode.NetworkObject>();
            }
            return m_CachedNetworkObject;
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
            Unity.Netcode.NetworkObject myNo = GetCachedNetworkObject();
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
            // Bereits selektiertes Target erneut anklicken = No-Op (kein redundanter RPC).
            if (localTs.HasTarget && localTs.CurrentTargetId == myNo.NetworkObjectId)
            {
                return;
            }
            localTs.RequestSelectTargetServerRpc(myNo.NetworkObjectId);
        }
    }
}
