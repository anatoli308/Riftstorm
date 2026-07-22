using Riftstorm.Management.FontManagement;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Npc;
using UnityEngine;
using UnityEngine.Serialization;

namespace Riftstorm.Game.Player
{
[DisallowMultipleComponent]
    /// <summary>
    /// Zeichnet den aktuellen <see cref="INameSource.DisplayName"/> als 2D-Label
    /// &#252;ber dem Kopf einer Einheit (Spieler oder NPC) via IMGUI. Bewusst ohne
    /// TextMesh / TextMeshPro, damit keine Asset-Abh&#228;ngigkeit f&#252;r Phase 4 entsteht.
    ///
    /// <para>
    /// Reine Anzeige-Komponente: lauscht auf <see cref="INameSource.DisplayNameChanged"/>
    /// und ben&#246;tigt kein Polling. <c>OnGUI</c> wird vom Unity-Loop nur f&#252;r das
    /// Zeichnen pro Frame aufgerufen &#8212; kein State-Polling.
    /// </para>
    ///
    /// <para>
    /// Nicht <c>sealed</c>: <see cref="PlayerNameTag"/> erbt davon, um die im
    /// Player-Prefab serialisierte Skript-Referenz (GUID) intakt zu halten. NPCs
    /// f&#252;gen <see cref="UnitNameTag"/> direkt an ihrem Prefab an.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(INameSource))]
    [RequireComponent(typeof(UnitStats))]
    [RequireComponent(typeof(NpcCastBarView))]
    [RequireComponent(typeof(Unity.Netcode.NetworkObject))]
    public class UnitNameTag : MonoBehaviour
    {
        [Tooltip("Komponente, die INameSource implementiert (PlayerIdentity, NpcIdentity, ...). " +
                 "Wird beim Awake automatisch via GetComponent<INameSource>() aufgel\u00f6st, falls leer.")]
        [FormerlySerializedAs("m_Identity")]
        [SerializeField] private MonoBehaviour m_IdentitySource;
        [Tooltip("Optional. Wenn gesetzt, wird die Position dieses Transforms direkt als Welt-Anker f\u00fcr " +
                 "das Nametag-Label verwendet (\u00fcblicherweise ein leeres Child 'NameTagAnchor' \u00fcber dem Kopf). " +
                 "Wenn leer, wird der Anker fest aus Root-Transform + HeadHeight gebildet.")]
        [SerializeField] private Transform m_Anchor;
        [Tooltip("Welt-H\u00f6he in Metern \u00fcber der Root-Transform, an der das Label sitzen soll, wenn kein " +
                 "expliziter Anker gesetzt ist. Bewusst KEIN Renderer-Bounds-Lookup, weil FLARE-Sprite-Layer " +
                 "flach auf der XZ-Ebene liegen und ihre Bounds pro Animationsframe wandern " +
                 "(Waffe/Buckler ragen mal raus). Im Inspector pro Charaktergr\u00f6\u00dfe tunbar.")]
        [SerializeField] private float m_HeadHeight = 1.2f;
        [Tooltip("Zus\u00e4tzlicher Offset (Welt-Koordinaten) auf den Anker. " +
                 "Wird zur Laufzeit IGNORIERT \u2014 wir nutzen \u00fcberall den festen Offset (0, -2, 0.8), " +
                 "damit alte Prefab-Werte den Default nicht mehr \u00fcberschreiben k\u00f6nnen.")]
        [SerializeField] private Vector3 m_WorldOffset = new(0f, -2f, 0.8f);

        /// <summary>Fester Offset f&#252;r alle Nametags. Bewusst NICHT serialisiert, damit
        /// Anpassungen in Code nicht von alten Prefab-Werten &#252;berschrieben werden.</summary>
        private static readonly Vector3 s_FixedWorldOffset = new(0f, -2f, 0.8f);
        [SerializeField] private Color m_Color = Color.white;
        [SerializeField] private int m_FontSize = 14;
        [SerializeField] private float m_MaxDistance = 50f;

        private INameSource m_NameSource;
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

        // HP-Bar (Overhead, Player + NPC). HP-Quelle ist die geteilte
        // UnitStats-Komponente (NetworkVariable-repliziert), die auf jedem Peer
        // HpChanged feuert. Texturen kommen wie das Hover-Plate datengetrieben
        // aus nametag_config.json und werden einmalig beim ersten OnGUI-Pass
        // aufgeloest. Kein Polling: m_HpCurrent/m_HpMax werden ausschliesslich
        // ueber das HpChanged-Event aktualisiert.
        private UnitStats m_Stats;
        private int m_HpCurrent;
        private int m_HpMax;
        private Texture2D m_NameplateBgTexture;
        private Texture2D m_NameplateHpTexture;
        private bool m_NameplateResolved;

        /// <summary>Vertikaler Abstand (GUI-Pixel) zwischen HP-Bar-Unterkante und Cast-Bar.</summary>
        private const float k_CastBarGap = 4f;

        // Co-lokale NPC-Cast-Bar (nur an NPCs vorhanden, zur Laufzeit per
        // AddComponent ergaenzt). Wird in OnGUI lazy aufgeloest; Spieler-Nametags
        // haben keine und ueberspringen die Cast-Bar dauerhaft.
        private NpcCastBarView m_CastBarView;

        private void Awake()
        {
            ResolveNameSource();
            ResolveStats();

            // Wenn kein expliziter Anker zugewiesen ist, leiten wir die Kopf-H\u00f6he aus den
            // Renderer-Bounds ab. Das funktioniert sowohl f\u00fcr 2D-Sprites (FLARE) als auch
            // f\u00fcr 3D-Meshes, ohne dass der Designer einen Anker setzen muss.
            RefreshRenderersIfNeeded();
        }

        private void ResolveNameSource()
        {
            if (m_IdentitySource is INameSource src)
            {
                m_NameSource = src;
                return;
            }
            // Auto-Resolve: irgendeine Komponente am selben GameObject, die INameSource implementiert.
            m_NameSource = GetComponent<INameSource>();
        }

        /// <summary>
        /// L&#246;st die geteilte <see cref="UnitStats"/>-Komponente auf (Player wie NPC
        /// besitzen genau eine). Liefert die HP-Quelle f&#252;r die Overhead-HP-Bar.
        /// Zuerst am selben GameObject, dann im Parent (Skin-Roots h&#228;ngen UnitStats
        /// teils eine Ebene h&#246;her).
        /// </summary>
        private void ResolveStats()
        {
            m_Stats = GetComponent<UnitStats>();
            if (m_Stats == null)
            {
                m_Stats = GetComponentInParent<UnitStats>();
            }
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
            if (m_NameSource == null)
            {
                ResolveNameSource();
            }
            if (m_NameSource != null)
            {
                m_NameSource.DisplayNameChanged += OnNameChanged;
                m_CachedName = m_NameSource.DisplayName;
            }
            if (m_Stats == null)
            {
                ResolveStats();
            }
            if (m_Stats != null)
            {
                m_Stats.HpChanged += OnHpChanged;
                // Initialwerte ziehen, falls das Spawn-HpChanged bereits gefeuert
                // hat, bevor wir abonniert haben.
                m_HpCurrent = m_Stats.CurrentHp;
                m_HpMax = m_Stats.MaxHp;
            }
        }

        private void OnDisable()
        {
            if (m_NameSource != null)
            {
                m_NameSource.DisplayNameChanged -= OnNameChanged;
            }
            if (m_Stats != null)
            {
                m_Stats.HpChanged -= OnHpChanged;
            }
        }

        private void OnNameChanged(string newName) => m_CachedName = newName;

        /// <summary>
        /// Cacht die aktuellen HP-Werte f&#252;r die Overhead-HP-Bar. Wird auf jedem
        /// Peer vom <see cref="UnitStats.HpChanged"/>-Event getrieben &#8212; kein Polling.
        /// </summary>
        private void OnHpChanged(int current, int max)
        {
            m_HpCurrent = current;
            m_HpMax = max;
        }

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
                // Fester Welt-Y-Offset \u00fcber der Root-Transform. Bewusst KEIN
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
                // aktiven EditorSkin \u2014 und GUI.Label rendert bei Mouse-Over dann den
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
            // garantiert NIE auf hover/active/focused/onXxx umschaltet \u2014 weder
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
            // GUI-Koordinaten: Y ist von oben gez\u00e4hlt, daher invertieren.
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
            // Hover erzeugt KEINE visuelle Aenderung am Nametag \u2014 Hover-Feedback
            // laeuft ueber den Sprite (HoverHighlight). Outline gibt es ausschliesslich
            // fuer das aktive Target-Lock UND niemals fuer das eigene Nametag (du
            // kannst dich selbst nicht als Target locken, ein Self-Outline waere
            // nur visueller L\u00e4rm).
            bool drawOutline = !isLocalPlayer && IsCurrentlyTargeted();

            // Hover-Plate (datengetrieben via nametag_config.json). Wird VOR dem
            // Text gezeichnet, damit der Text obendrauf sitzt. Nur fuer fremde
            // Nametags \u2014 das eigene bleibt nackt. Idle- und Hover-Texturen sind
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
                // Zustandsaenderung am eigenen Nametag \u2014 egal was die Maus tut.
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

            // Overhead-HP-Bar (nameplate_bg + nameplate_hp) unter dem Nametag.
            // Datengetrieben via nametag_config.json, fuer Player wie NPC. Das
            // eigene Nametag bleibt per Default ohne Bar (eigenes PlayerFrameUI).
            DrawHealthBar(rect, isLocalPlayer);

            // NPC-Cast-Bar direkt UNTER der HP-Bar. Teilt sich dasselbe nameRect,
            // damit sie nie gegenueber Name + HP-Bar verrutscht.
            DrawCastBar(rect, isLocalPlayer);

            // Klick auf das Label selektiert die Einheit (source-aequivalent zum
            // Overhead-Klick im SoF-Client). Manueller MouseDown-Hit-Test \u2014 kein
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
        /// <c>textColor</c> des Styles. &#177;2px statt &#177;1px, weil <c>FontStyle.Bold</c>-
        /// Strokes selbst etwa 1-2 Pixel breit sind und einen 1px-Outline-Offset
        /// komplett unter dem Hauptzeichen verschwinden lassen w&#252;rden. Bewusst kein
        /// TextMeshPro &#8212; Outline-Effekt im IMGUI-Pfad.
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
        /// Zeichnet die Overhead-HP-Bar unter dem Nametag mittels der
        /// datengetriebenen Texturen <c>nameplate_bg</c> (Hintergrund) und
        /// <c>nameplate_hp</c> (Fill). Der Fill wird horizontal auf den HP-Anteil
        /// (<see cref="m_HpCurrent"/> / <see cref="m_HpMax"/>) skaliert. Reiner
        /// Zeichen-Pass &#8212; die HP-Werte kommen ausschlie&#223;lich aus dem
        /// <see cref="UnitStats.HpChanged"/>-Event (kein Polling).
        /// </summary>
        /// <param name="nameRect">Das bereits berechnete Rect des Nametag-Labels.</param>
        /// <param name="isLocalPlayer">True, wenn dies das eigene Nametag ist.</param>
        private void DrawHealthBar(Rect nameRect, bool isLocalPlayer)
        {
            NameTagConfig cfg = NameTagConfigLoader.Load();
            if (!cfg.healthBarEnabled)
            {
                return;
            }
            if (isLocalPlayer && !cfg.healthBarShowSelf)
            {
                return;
            }
            if (m_HpMax <= 0)
            {
                return; // Keine gueltige HP-Quelle (Pre-Spawn) oder MaxHp noch 0.
            }

            if (!m_NameplateResolved)
            {
                m_NameplateBgTexture = NameTagConfigLoader.LoadTextureOrNull(cfg.nameplateBackgroundTexture);
                m_NameplateHpTexture = NameTagConfigLoader.LoadTextureOrNull(cfg.nameplateHpTexture);
                m_NameplateResolved = true;
            }

            float width = cfg.healthBarWidth;
            float height = cfg.healthBarHeight;
            float barX = nameRect.x + (nameRect.width - width) * 0.5f;
            float barY = nameRect.yMax + cfg.healthBarOffsetY;
            Rect bgRect = new(barX, barY, width, height);

            if (m_NameplateBgTexture != null)
            {
                GUI.DrawTexture(bgRect, m_NameplateBgTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }

            float fraction = Mathf.Clamp01((float)m_HpCurrent / m_HpMax);
            if (m_NameplateHpTexture != null && fraction > 0f)
            {
                Rect fillRect = new(barX, barY, width * fraction, height);
                GUI.DrawTexture(fillRect, m_NameplateHpTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }
        }

        /// <summary>
        /// Zeichnet die NPC-Cast-Bar direkt UNTER der Overhead-HP-Bar. Die
        /// Cast-Daten (Fortschritt, Spell-Name, Texturen, Style) kommen von der
        /// co-lokalen <see cref="NpcCastBarView"/>; die Position wird aus demselben
        /// <paramref name="nameRect"/> abgeleitet wie die HP-Bar, sodass die Bar nie
        /// gegen&#252;ber Name und HP-Bar verrutscht. Spieler-Nametags besitzen keine
        /// <see cref="NpcCastBarView"/> und &#252;berspringen den Pass. Reiner
        /// Zeichen-Pass &#8212; kein Polling.
        /// </summary>
        /// <param name="nameRect">Das bereits berechnete Rect des Nametag-Labels.</param>
        /// <param name="isLocalPlayer">True, wenn dies das eigene Nametag ist.</param>
        private void DrawCastBar(Rect nameRect, bool isLocalPlayer)
        {
            if (m_CastBarView == null)
            {
                m_CastBarView = GetComponent<NpcCastBarView>();
                if (m_CastBarView == null)
                {
                    return; // Keine NPC-Cast-Bar an dieser Einheit (z. B. Spieler).
                }
            }

            if (!m_CastBarView.TryGetActiveCast(
                    out float progress,
                    out string spellName,
                    out Texture2D bg,
                    out Texture2D fill,
                    out GUIStyle nameStyle))
            {
                return;
            }

            NameTagConfig cfg = NameTagConfigLoader.Load();
            float width = cfg.healthBarWidth;
            float height = cfg.healthBarHeight;
            float barX = nameRect.x + (nameRect.width - width) * 0.5f;

            // Direkt unter der HP-Bar stapeln. Ist die HP-Bar (de)aktiviert oder
            // ohne gueltige HP-Quelle, sitzt die Cast-Bar entsprechend direkt unter
            // dem Namen — identische Bedingungen wie DrawHealthBar.
            bool hpShown = cfg.healthBarEnabled
                           && !(isLocalPlayer && !cfg.healthBarShowSelf)
                           && m_HpMax > 0;
            float barTop = hpShown
                ? nameRect.yMax + cfg.healthBarOffsetY + cfg.healthBarHeight + k_CastBarGap
                : nameRect.yMax + k_CastBarGap;

            Rect bgRect = new(barX, barTop, width, height);
            if (bg != null)
            {
                GUI.DrawTexture(bgRect, bg, ScaleMode.StretchToFill, alphaBlend: true);
            }
            if (fill != null && progress > 0f)
            {
                Rect fillRect = new(barX, barTop, width * progress, height);
                GUI.DrawTexture(fillRect, fill, ScaleMode.StretchToFill, alphaBlend: true);
            }
            if (nameStyle != null && !string.IsNullOrEmpty(spellName))
            {
                GUI.Label(bgRect, spellName, nameStyle);
            }
        }

        /// <summary>
        /// True, wenn die lokale <see cref="TargetSelection"/>
        /// genau diese Einheit anvisiert. Dauerhafter Outline f&#252;r das Target-Lock.
        /// </summary>
        private bool IsCurrentlyTargeted()
        {
            TargetSelection localTs = Riftstorm.Game.Combat.TargetSelection.Local;
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
            m_CachedNetworkObject = GetComponent<Unity.Netcode.NetworkObject>();
            if (m_CachedNetworkObject == null)
            {
                m_CachedNetworkObject = GetComponentInParent<Unity.Netcode.NetworkObject>();
            }
            return m_CachedNetworkObject;
        }

        /// <summary>
        /// Sendet einen Lock-Wunsch fuer DIESE Einheit an den Server. Lookup des
        /// lokalen <see cref="TargetSelection"/> erfolgt
        /// statisch (Owner registriert sich beim NetworkSpawn), kein FindObject.
        /// </summary>
        private void HandleNameTagClicked()
        {
            Unity.Netcode.NetworkObject myNo = GetCachedNetworkObject();
            if (myNo == null || !myNo.IsSpawned)
            {
                return;
            }
            TargetSelection localTs = Riftstorm.Game.Combat.TargetSelection.Local;
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
