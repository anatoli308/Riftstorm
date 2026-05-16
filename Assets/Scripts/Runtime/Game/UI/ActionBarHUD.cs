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
    /// Komplett programmatisch aufgebaut (kein UXML). Genuegt ein
    /// GameObject mit <see cref="UIDocument"/> + dieser Komponente.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ActionBarHUD : MonoBehaviour
    {
        private const int SlotCount = 12;
        private const int BottomSlotSize = 44;
        private const int SideSlotSize = 36;

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

            BuildBottomBar(m_Root);
            BuildRightBar(m_Root, rightOffset: 16f);
            BuildRightBar(m_Root, rightOffset: 16f + SideSlotSize + 8f);
        }

        // -------------------------------------------------------------------------
        // Bottom Bar (12 Slots + XP-Bar darunter)
        // -------------------------------------------------------------------------

        private static void BuildBottomBar(VisualElement root)
        {
            VisualElement container = new() { name = "action-bar-bottom" };
            container.style.position = Position.Absolute;
            container.style.bottom = 16f;
            // Horizontal zentriert via translate -50% (Unity UIToolkit: TranslateOperand).
            container.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
            container.style.translate = new StyleTranslate(new Translate(
                new Length(-50f, LengthUnit.Percent), 0f, 0f));
            container.style.flexDirection = FlexDirection.Column;
            container.style.alignItems = Align.Center;

            // XP-Bar: schmaler Streifen unter den Slots (WoW-Layout).
            VisualElement xpRow = HudStyle.BuildBarRow(
                "action-bar-xp",
                new Color(0.55f, 0.35f, 0.85f, 1f),
                out VisualElement xpFill,
                out Label xpValue);
            xpRow.style.height = 8f;
            xpRow.style.width = (BottomSlotSize + 4) * SlotCount;
            xpRow.style.marginBottom = 4f;
            xpFill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            xpValue.style.display = DisplayStyle.None; // Wert-Text erst, wenn XP-System existiert.
            container.Add(xpRow);

            // Slot-Reihe
            VisualElement slotsRow = new() { name = "action-bar-bottom-slots" };
            slotsRow.style.flexDirection = FlexDirection.Row;
            slotsRow.style.justifyContent = Justify.Center;

            for (int i = 0; i < SlotCount; i++)
            {
                string bind = GetBottomKeyBind(i);
                VisualElement slot = HudStyle.BuildActionSlot(BottomSlotSize, bind);
                slotsRow.Add(slot);
            }
            container.Add(slotsRow);

            root.Add(container);
        }

        private static string GetBottomKeyBind(int index)
        {
            // 1,2,3,4,5,6,7,8,9,0,-,=  (WoW Default)
            return index switch
            {
                < 9 => (index + 1).ToString(),
                9 => "0",
                10 => "-",
                11 => "=",
                _ => string.Empty,
            };
        }

        // -------------------------------------------------------------------------
        // Right Vertical Bar (12 Slots)
        // -------------------------------------------------------------------------

        private static void BuildRightBar(VisualElement root, float rightOffset)
        {
            VisualElement container = new() { name = "action-bar-right" };
            container.style.position = Position.Absolute;
            container.style.right = rightOffset;
            // Vertikal mittig.
            container.style.top = new StyleLength(new Length(50f, LengthUnit.Percent));
            container.style.translate = new StyleTranslate(new Translate(
                0f, new Length(-50f, LengthUnit.Percent), 0f));
            container.style.flexDirection = FlexDirection.Column;
            container.style.alignItems = Align.Center;

            for (int i = 0; i < SlotCount; i++)
            {
                VisualElement slot = HudStyle.BuildActionSlot(SideSlotSize, keyBind: null);
                container.Add(slot);
            }
            root.Add(container);
        }
    }
}
