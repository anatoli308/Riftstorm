using UnityEngine;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// WoW-Style Action-Bar-HUD: eine horizontale Bar unten in der Mitte
    /// (12 Slots + duenne XP-Bar darunter) sowie zwei vertikale Bars am
    /// rechten Bildrand (je 12 Slots). Die Slots sind aktuell reine
    /// visuelle Platzhalter \u2014 das Skill-/Inventory-System haengt
    /// seine Icons spaeter ueber Namens-Querys ("action-slot") ein.
    /// <para>
    /// Bar-Hintergruende und XP-Fill stammen aus <see cref="HudConfig"/>
    /// (Texturen <c>actionbar_base</c> und <c>xp_bar</c>). Die rechten
    /// vertikalen Bars verwenden dieselbe Basis-Textur, lediglich um 90deg
    /// rotiert. Alle Pixel-Masse sind tunbar in <c>hud_config.json</c>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ActionBarHUD : MonoBehaviour
    {
        private const int SlotCount = 12;

        private UIDocument m_Document;
        private VisualElement m_Root;

        private void Awake()
        {
            m_Document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            BuildVisualTree();
        }

        private void BuildVisualTree()
        {
            m_Root = m_Document.rootVisualElement;
            if (m_Root == null)
            {
                return;
            }
            m_Root.Clear();
            m_Root.pickingMode = PickingMode.Ignore;

            HudConfig cfg = HudConfigLoader.Load();
            Texture2D baseTex = HudConfigLoader.LoadTextureOrNull(cfg.actionBarBaseTexture);
            Texture2D xpTex = HudConfigLoader.LoadTextureOrNull(cfg.actionBarXpFillTexture);
            Texture2D slotIdleTex = HudConfigLoader.LoadTextureOrNull(cfg.actionSlotIconIdleTexture);
            Texture2D slotHoverTex = HudConfigLoader.LoadTextureOrNull(cfg.actionSlotIconHoverTexture);
            Texture2D slotPressTex = HudConfigLoader.LoadTextureOrNull(cfg.actionSlotIconPressTexture);

            BuildBottomBar(m_Root, cfg, baseTex, xpTex, slotIdleTex, slotHoverTex, slotPressTex);

            float rightOffset = cfg.actionBarRightMargin;
            for (int i = 0; i < cfg.actionBarRightCount; i++)
            {
                BuildRightBar(m_Root, cfg, baseTex, rightOffset, slotIdleTex, slotHoverTex, slotPressTex);
                rightOffset += cfg.actionBarRightWidth + cfg.actionBarRightSpacing;
            }
        }

        // -------------------------------------------------------------------------
        // Bottom Bar (Action-Bar-Base-Textur + 12 Slots + XP-Bar darunter)
        // -------------------------------------------------------------------------

        private static void BuildBottomBar(VisualElement root, HudConfig cfg, Texture2D baseTex, Texture2D xpTex,
            Texture2D slotIdleTex, Texture2D slotHoverTex, Texture2D slotPressTex)
        {
            float width = cfg.actionBarBottomWidth;
            float height = cfg.actionBarBottomHeight;
            float slotSize = cfg.actionBarBottomSlotSize;
            float xpHeight = cfg.actionBarBottomXpHeight;

            // Aeusserer Wrapper: zentriert, traegt Bar + XP untereinander.
            VisualElement wrapper = new() { name = "action-bar-bottom" };
            wrapper.style.position = Position.Absolute;
            wrapper.style.bottom = cfg.actionBarBottomMargin;
            wrapper.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
            wrapper.style.translate = new StyleTranslate(new Translate(
                new Length(-50f, LengthUnit.Percent), 0f, 0f));
            wrapper.style.width = width;
            wrapper.style.flexDirection = FlexDirection.Column;
            wrapper.style.alignItems = Align.Center;
            wrapper.pickingMode = PickingMode.Ignore;

            // Bar-Hintergrund mit eingebrannten Slot-Wells.
            VisualElement bar = new() { name = "action-bar-bottom-base" };
            bar.style.width = width;
            bar.style.height = height;
            if (baseTex != null)
            {
                bar.style.backgroundImage = new StyleBackground(baseTex);
            }

            // XP-Bar als Overlay INNERHALB der Basis-Textur (League/WoW-Style:
            // duenner Balken unten in die Bar gezeichnet). Wird zuerst dem bar
            // hinzugefuegt, damit die Slot-Reihe danach darueber liegt.
            VisualElement xpRow = HudStyle.BuildTexturedBar(
                "action-bar-xp",
                xpTex,
                width - 2f * cfg.actionBarBottomXpInsetX,
                xpHeight,
                fillFromRight: false,
                out VisualElement xpFill,
                out Label xpValue);
            xpRow.style.position = Position.Absolute;
            xpRow.style.left = cfg.actionBarBottomXpInsetX;
            xpRow.style.bottom = cfg.actionBarBottomXpInsetBottom;
            // Track-Backdrop, damit der XP-Slot auch bei 0% Fuellung sichtbar
            // ist (xp_bar.png ist nur die Fuellung, keine Rinne). Dunkles
            // Semi-Transparent, passt zu den Slot-Wells der Basis-Textur.
            xpRow.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            xpFill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            // Wert-Text einblenden: zeigt initial "0%", bis das XP-System
            // echte Werte liefert. Schrift dezent kleiner, damit sie in den
            // schmalen Balken passt.
            xpValue.text = "0%";
            xpValue.style.fontSize = Mathf.Max(9f, xpHeight * 0.85f);
            bar.Add(xpRow);

            // Slot-Reihe als Container, absolut ueber den eingebrannten Wells.
            // Slots werden INNERHALB der Reihe absolut positioniert (jede Position
            // einzeln auf ganze Pixel gerundet), damit sich keine Subpixel-Fehler
            // aufsummieren wie bei flex/marginLeft. Reihenbreite = N*slotSize +
            // (N-1)*gap; die Reihe wird in der Bar horizontal zentriert.
            float gap = cfg.actionBarBottomSlotGap;
            float rowWidth = SlotCount * slotSize + (SlotCount - 1) * gap;
            float rowLeft = Mathf.Round((width - rowWidth) * 0.5f + cfg.actionBarBottomSlotsRowOffsetX);

            VisualElement slotsRow = new() { name = "action-bar-bottom-slots" };
            slotsRow.style.position = Position.Absolute;
            slotsRow.style.top = cfg.actionBarBottomSlotInsetY;
            slotsRow.style.left = rowLeft;
            slotsRow.style.width = rowWidth;
            slotsRow.style.height = slotSize;

            for (int i = 0; i < SlotCount; i++)
            {
                string bind = GetBottomKeyBind(i);
                VisualElement slot = baseTex != null
                    ? HudStyle.BuildTexturedActionSlot(slotSize, bind, slotIdleTex, slotHoverTex, slotPressTex)
                    : HudStyle.BuildActionSlot((int)slotSize, bind);
                slot.style.position = Position.Absolute;
                slot.style.top = 0f;
                // Jede Position unabhaengig runden -> Fehler bleibt <= 0.5px pro
                // Slot statt sich linear aufzusummieren. Pro-Slot-Korrektur aus
                // Config addieren, falls die eingebrannten Wells der PNG nicht
                // exakt aequidistant sind.
                float perSlotOffset = (cfg.actionBarBottomSlotOffsetsX != null
                                       && i < cfg.actionBarBottomSlotOffsetsX.Length)
                    ? cfg.actionBarBottomSlotOffsetsX[i]
                    : 0f;
                slot.style.left = Mathf.Round(i * (slotSize + gap) + perSlotOffset);
                slotsRow.Add(slot);
            }
            bar.Add(slotsRow);
            wrapper.Add(bar);

            root.Add(wrapper);
        }

        private static readonly string[] BottomKeyBinds =
        {
            "Q", "W", "E", "R",
            "1", "2", "3", "4",
            "5", "6", "7", "8"
        };

        private static string GetBottomKeyBind(int index)
        {
            return index >= 0 && index < BottomKeyBinds.Length
                ? BottomKeyBinds[index]
                : string.Empty;
        }

        // -------------------------------------------------------------------------
        // Right Vertical Bar (12 Slots, Basis-Textur 90deg rotiert)
        // -------------------------------------------------------------------------

        private static void BuildRightBar(VisualElement root, HudConfig cfg, Texture2D baseTex, float rightOffset,
            Texture2D slotIdleTex, Texture2D slotHoverTex, Texture2D slotPressTex)
        {
            float w = cfg.actionBarRightWidth;
            float h = cfg.actionBarRightHeight;
            float slotSize = cfg.actionBarRightSlotSize;

            VisualElement container = new() { name = "action-bar-right" };
            container.style.position = Position.Absolute;
            container.style.right = rightOffset;
            container.style.top = new StyleLength(new Length(50f, LengthUnit.Percent));
            container.style.translate = new StyleTranslate(new Translate(
                0f, new Length(-50f, LengthUnit.Percent), 0f));
            container.style.width = w;
            container.style.height = h;
            container.pickingMode = PickingMode.Ignore;

            // Rotierte Basis-Textur als reine Hintergrund-Ebene.
            // Pre-Rotation hat das Element die Masse der unteren Bar (lange Achse
            // horizontal), wird dann um die Mitte 90deg gedreht und sitzt damit
            // vertikal exakt im Container.
            if (baseTex != null)
            {
                VisualElement bg = new() { name = "action-bar-right-bg" };
                bg.style.position = Position.Absolute;
                // Pre-Rotation: width = lange Achse (= Container-Hoehe),
                //               height = kurze Achse (= Container-Breite).
                bg.style.width = h;
                bg.style.height = w;
                // Zentrieren: pre-rotation linke obere Ecke so platzieren, dass
                // der Mittelpunkt des Bildes mit dem Mittelpunkt des Containers
                // zusammenfaellt.
                bg.style.left = (w - h) * 0.5f;
                bg.style.top = (h - w) * 0.5f;
                bg.style.backgroundImage = new StyleBackground(baseTex);
                bg.style.rotate = new StyleRotate(new Rotate(new Angle(cfg.actionBarRightRotationDegrees, AngleUnit.Degree)));
                bg.pickingMode = PickingMode.Ignore;
                container.Add(bg);
            }

            // Aufrechte Slot-Spalte ueber der rotierten Basis. Slots werden
            // einzeln absolut positioniert (jede Y-Position auf ganze Pixel
            // gerundet), damit sich keine Subpixel-Fehler aufsummieren. Pro-Slot-
            // Korrektur aus Config gleicht Asymmetrien in der gemalten Basis aus
            // (analog zur unteren Bar).
            float rightGap = cfg.actionBarRightSlotGap;
            float colHeight = SlotCount * slotSize + (SlotCount - 1) * rightGap;
            float colTop = Mathf.Round((h - colHeight) * 0.5f + cfg.actionBarRightSlotsColOffsetY);
            float colLeft = Mathf.Round((w - slotSize) * 0.5f);

            VisualElement slotsCol = new() { name = "action-bar-right-slots" };
            slotsCol.style.position = Position.Absolute;
            slotsCol.style.top = colTop;
            slotsCol.style.left = colLeft;
            slotsCol.style.width = slotSize;
            slotsCol.style.height = colHeight;

            for (int i = 0; i < SlotCount; i++)
            {
                VisualElement slot = baseTex != null
                    ? HudStyle.BuildTexturedActionSlot(slotSize, keyBind: null, slotIdleTex, slotHoverTex, slotPressTex)
                    : HudStyle.BuildActionSlot((int)slotSize, keyBind: null);
                slot.style.position = Position.Absolute;
                slot.style.left = 0f;
                float perSlotOffset = (cfg.actionBarRightSlotOffsetsY != null
                                       && i < cfg.actionBarRightSlotOffsetsY.Length)
                    ? cfg.actionBarRightSlotOffsetsY[i]
                    : 0f;
                slot.style.top = Mathf.Round(i * (slotSize + rightGap) + perSlotOffset);
                slotsCol.Add(slot);
            }
            container.Add(slotsCol);

            root.Add(container);
        }
    }
}

