using System.Collections.Generic;
using Riftstorm.Management.FontManagement;
using Riftstorm.Management.TextureManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Riftstorm.Game.UI.Console
{
    /// <summary>
    /// In-Game Server-Chat-Fenster (Source-Style, unten-links). Mirror von
    /// <c>source_client/ConsoleWindow.cpp</c>: 9-Slice-Frame, scrollbarer Backlog,
    /// TextField fuer Eingabe, Close-Button (Escape), Enter-Button (Return),
    /// Scroll-Up/Down-Buttons. Datenquelle: <see cref="ConsoleConfig"/> aus
    /// <c>StreamingAssets/interface/console_config.json</c>. Texturen via
    /// <see cref="TextureManager"/> (ServiceLocator). Toggle-Input via
    /// code-erzeugte <see cref="InputAction"/> auf <c>&lt;Keyboard&gt;/enter</c>
    /// (Open) - Close passiert ueber Button-Click oder ESC im fokussierten TextField,
    /// damit ESC ausserhalb der Konsole weiterhin den <c>ClearTarget</c>-Input frei laesst.
    /// </summary>
    /// <remarks>
    /// Reines UI mit lokalem <see cref="ConsoleLog"/> als Datenquelle und
    /// <see cref="ConsoleLog.SubmitCommand(string)"/> als Submission-Hook. Der
    /// <see cref="ConsoleManager"/> abonniert <see cref="ConsoleLog.CommandSubmitted"/>
    /// und antwortet ueber <see cref="ConsoleLog.Add(string, ConsoleChannel)"/>.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ConsoleHUD : MonoBehaviour
    {
        private static readonly Color k_ColorStandard = new(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color k_ColorError = new(0.95f, 0.30f, 0.30f, 1f);
        private static readonly Color k_ColorWarning = new(0.95f, 0.85f, 0.30f, 1f);
        private static readonly Color k_ColorSystem = new(0.70f, 0.70f, 0.70f, 1f);
        private static readonly Color k_ColorCommand = new(0.45f, 0.85f, 0.95f, 1f);
        private static readonly Color k_ColorChat = new(0.60f, 0.75f, 1.00f, 1f);

        private UIDocument m_Document;
        private ConsoleConfig m_Config;
        private TextureManager m_TextureManager;

        private VisualElement m_Root;
        private VisualElement m_Panel;
        private ScrollView m_LogView;
        private VisualElement m_InputRow;
        private TextField m_InputField;
        private VisualElement m_EnterButton;

        private InputAction m_ToggleAction;
        private bool m_InputActive;
        // True direkt nach OnToggleActionPerformed, damit der GLEICHE Enter-Press,
        // der die Input-Row geoeffnet hat, nicht sofort vom fokussierten TextField
        // als Submit/Close interpretiert wird (Open+Close im selben Frame).
        private bool m_SwallowNextEnterKeyDown;

        private void OnEnable()
        {
            m_Document = GetComponent<UIDocument>();
            m_Root = m_Document != null ? m_Document.rootVisualElement : null;
            if (m_Root == null)
            {
                Debug.LogError("[ConsoleHUD] UIDocument hat kein rootVisualElement. Source Asset + PanelSettings am UIDocument-Component pruefen.");
                return;
            }

            m_Config = ConsoleConfigLoader.Load();
            ConsoleLog.SetMaxLines(m_Config.logMaxLines);
            m_TextureManager = ServiceLocator.Get<TextureManager>();

            BuildVisualTree();
            HydrateBacklog();
            ConsoleLog.LineAppended += OnLineAppended;
            ConsoleLog.Cleared += OnLogCleared;

            // Enter/NumpadEnter togglet den Chat-Input. Im aktiven Zustand wird
            // dieselbe Aktion vom TextField-KeyDown-Hook konsumiert (Submit/Close),
            // damit das InputAction-Performed hier nichts mehr macht.
            m_ToggleAction = new(name: "ServerChatToggle", binding: "<Keyboard>/enter");
            m_ToggleAction.AddBinding("<Keyboard>/numpadEnter");
            m_ToggleAction.performed += OnToggleActionPerformed;
            m_ToggleAction.Enable();

            // Persistente Chat-Optik: Panel + Log sind immer sichtbar, nur die
            // Input-Row togglet (WoW-Style). m_Config.openOnStart bleibt nur als
            // Hinweis, ob der Cursor direkt im Eingabefeld stehen soll.
            SetInputActive(m_Config.openOnStart);
        }

        private void OnDisable()
        {
            ConsoleLog.LineAppended -= OnLineAppended;
            ConsoleLog.Cleared -= OnLogCleared;

            if (m_ToggleAction != null)
            {
                m_ToggleAction.performed -= OnToggleActionPerformed;
                m_ToggleAction.Disable();
                m_ToggleAction.Dispose();
                m_ToggleAction = null;
            }

            // Falls beim Despawn noch ein Typing-Flag haengt, sauber freigeben,
            // sonst bleibt PlayerInputController dauerhaft suppressed.
            ChatFocusState.SetTyping(false);

            if (m_Root != null)
            {
                m_Root.Clear();
            }
            m_Panel = null;
            m_LogView = null;
            m_InputRow = null;
            m_InputField = null;
            m_EnterButton = null;
        }

        // ---------------------------------------------------------------------
        // Build
        // ---------------------------------------------------------------------

        private void BuildVisualTree()
        {
            m_Root.Clear();
            m_Root.pickingMode = PickingMode.Ignore;

            m_Panel = new() { name = "console-panel" };
            m_Panel.style.position = Position.Absolute;
            m_Panel.style.left = m_Config.anchorLeft;
            m_Panel.style.bottom = m_Config.anchorBottom;
            m_Panel.style.width = m_Config.panelWidth;
            m_Panel.style.height = m_Config.panelHeight;
            m_Panel.pickingMode = PickingMode.Position;
            m_Root.Add(m_Panel);

            BuildNineSliceFrame(m_Panel);
            BuildLogView(m_Panel);
            BuildInputField(m_Panel);
            BuildButtons(m_Panel);
        }

        private void BuildNineSliceFrame(VisualElement parent)
        {
            // Source-Layout: 4 Ecken mit fester cornerWidth x cornerHeight,
            // 4 Kanten stretchen zwischen den Ecken (Dicke = cornerHeight bzw.
            // cornerWidth, damit Kantengrafik nahtlos an die Ecken anschliesst),
            // Center fuellt den Innenbereich. Edgethickness aus dem JSON wird
            // bewusst ignoriert, weil die Ecken-PNGs die optische Rahmendicke
            // bereits vorgeben — andernfalls entstehen sichtbare Luecken.
            float cw = m_Config.cornerWidth;
            float ch = m_Config.cornerHeight;

            // Center (liegt unter den Kanten, fuellt den Innenbereich exakt)
            AddSliceStretch(parent, "console-center", m_Config.centerTexture, cw, cw, ch, ch);

            // Kanten (stretchen zwischen den Ecken)
            AddSliceStretchHorizontal(parent, "console-edge-top", m_Config.edgeTopTexture, cw, cw, top: 0f, height: ch);
            AddSliceStretchHorizontal(parent, "console-edge-bottom", m_Config.edgeBottomTexture, cw, cw, bottom: 0f, height: ch);
            AddSliceStretchVertical(parent, "console-edge-left", m_Config.edgeLeftTexture, ch, ch, left: 0f, width: cw);
            AddSliceStretchVertical(parent, "console-edge-right", m_Config.edgeRightTexture, ch, ch, right: 0f, width: cw);

            // Ecken (feste Groesse, in den vier Panel-Ecken verankert)
            AddSliceCorner(parent, "console-corner-tl", m_Config.cornerTopLeftTexture,     left: 0f,        right: float.NaN, top: 0f,         bottom: float.NaN, cw, ch);
            AddSliceCorner(parent, "console-corner-tr", m_Config.cornerTopRightTexture,    left: float.NaN, right: 0f,        top: 0f,         bottom: float.NaN, cw, ch);
            AddSliceCorner(parent, "console-corner-bl", m_Config.cornerBottomLeftTexture,  left: 0f,        right: float.NaN, top: float.NaN,  bottom: 0f,        cw, ch);
            AddSliceCorner(parent, "console-corner-br", m_Config.cornerBottomRightTexture, left: float.NaN, right: 0f,        top: float.NaN,  bottom: 0f,        cw, ch);
        }

        /// <summary>
        /// Center-Slice: stretcht in beide Achsen zwischen den vier Insets.
        /// </summary>
        private void AddSliceStretch(VisualElement parent, string elementName, string textureKey,
            float left, float right, float top, float bottom)
        {
            VisualElement el = CreateSlice(elementName, textureKey);
            el.style.left = left;
            el.style.right = right;
            el.style.top = top;
            el.style.bottom = bottom;
            parent.Add(el);
        }

        /// <summary>
        /// Horizontale Kante (Top/Bottom): stretcht zwischen left/right,
        /// feste Hoehe, an top ODER bottom verankert.
        /// </summary>
        private void AddSliceStretchHorizontal(VisualElement parent, string elementName, string textureKey,
            float left, float right, float height,
            float top = float.NaN, float bottom = float.NaN)
        {
            VisualElement el = CreateSlice(elementName, textureKey);
            el.style.left = left;
            el.style.right = right;
            el.style.height = height;
            if (!float.IsNaN(top)) el.style.top = top;
            if (!float.IsNaN(bottom)) el.style.bottom = bottom;
            parent.Add(el);
        }

        /// <summary>
        /// Vertikale Kante (Left/Right): stretcht zwischen top/bottom,
        /// feste Breite, an left ODER right verankert.
        /// </summary>
        private void AddSliceStretchVertical(VisualElement parent, string elementName, string textureKey,
            float top, float bottom, float width,
            float left = float.NaN, float right = float.NaN)
        {
            VisualElement el = CreateSlice(elementName, textureKey);
            el.style.top = top;
            el.style.bottom = bottom;
            el.style.width = width;
            if (!float.IsNaN(left)) el.style.left = left;
            if (!float.IsNaN(right)) el.style.right = right;
            parent.Add(el);
        }

        /// <summary>
        /// Ecke: feste width x height, an je einer horizontalen + vertikalen
        /// Kante verankert (genau eines von left/right und eines von top/bottom
        /// ist gesetzt, das jeweils andere ist <see cref="float.NaN"/>).
        /// </summary>
        private void AddSliceCorner(VisualElement parent, string elementName, string textureKey,
            float left, float right, float top, float bottom, float width, float height)
        {
            VisualElement el = CreateSlice(elementName, textureKey);
            if (!float.IsNaN(left)) el.style.left = left;
            if (!float.IsNaN(right)) el.style.right = right;
            if (!float.IsNaN(top)) el.style.top = top;
            if (!float.IsNaN(bottom)) el.style.bottom = bottom;
            el.style.width = width;
            el.style.height = height;
            parent.Add(el);
        }

        /// <summary>
        /// Gemeinsame Basis fuer alle Slice-Elemente: absolute Positionierung,
        /// pickingMode = Ignore (Klicks gehen ans Panel), Textur per
        /// <see cref="TextureManager"/> geladen.
        /// </summary>
        private VisualElement CreateSlice(string elementName, string textureKey)
        {
            VisualElement el = new() { name = elementName };
            el.style.position = Position.Absolute;
            el.pickingMode = PickingMode.Ignore;
            Texture2D tex = LoadTexture(textureKey);
            if (tex != null)
            {
                el.style.backgroundImage = new StyleBackground(tex);
            }
            else
            {
                Debug.LogWarning($"[ConsoleHUD] Missing texture for slice '{elementName}': key='{textureKey}'");
            }
            return el;
        }

        private void BuildLogView(VisualElement parent)
        {
            m_LogView = new() { name = "console-log" };
            m_LogView.style.position = Position.Absolute;
            m_LogView.style.left = m_Config.logInsetLeft;
            m_LogView.style.right = m_Config.logInsetRight;
            m_LogView.style.top = m_Config.logInsetTop;
            m_LogView.style.bottom = m_Config.logInsetBottom;
            m_LogView.mode = ScrollViewMode.Vertical;
            // Unity rendert sonst ZWEI Scrollbars: einmal die ScrollView-eigene
            // und einmal unsere dekorative Scroll-Lane mit Up/Down-Buttons (Source-
            // Style). Beide ausblenden, gescrollt wird ueber Mausrad +
            // Scroll-Up/Down-Buttons.
            m_LogView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_LogView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            parent.Add(m_LogView);
        }

        private void BuildInputField(VisualElement parent)
        {
            // Wrapper-Row: nur dieses Element wird ein-/ausgeblendet, damit Log
            // und Frame dauerhaft sichtbar bleiben (WoW-Style persistent chat).
            m_InputRow = new() { name = "console-input-row" };
            m_InputRow.style.position = Position.Absolute;
            m_InputRow.style.left = m_Config.inputInsetLeft;
            m_InputRow.style.right = m_Config.inputInsetRight;
            m_InputRow.style.bottom = m_Config.inputInsetBottom;
            m_InputRow.style.height = m_Config.inputHeight;
            m_InputRow.pickingMode = PickingMode.Ignore;
            parent.Add(m_InputRow);

            m_InputField = new() { name = "console-input" };
            m_InputField.style.position = Position.Absolute;
            m_InputField.style.left = 0f;
            m_InputField.style.right = 0f;
            m_InputField.style.top = 0f;
            m_InputField.style.bottom = 0f;
            m_InputField.style.fontSize = m_Config.inputFontSize;
            m_InputField.style.color = new StyleColor(k_ColorStandard);
            m_InputField.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            UIFonts.Apply(m_InputField, UIFonts.Body);
            // UIToolkit-Quirk: das aeussere TextField rendert keinen Text; die
            // tatsaechliche TextElement-Instanz liegt im Child
            // ".unity-text-field__input" und erbt color/font NICHT vom Parent.
            // Ohne expliziten Override bleibt der Cursor/Text weiss auf hellem
            // Hintergrund. Wir warten einen Frame, bis das Template instanziiert
            // ist, und setzen die visuellen Properties direkt am Inner-Element.
            // Zusaetzlich Border/Margin/Padding 0 setzen, sonst beschneidet der
            // Unity-Default-Style die Baseline und der Text wird oben/unten
            // abgeschnitten.
            m_InputField.schedule.Execute(() =>
            {
                VisualElement inner = m_InputField.Q(className: "unity-text-field__input");
                if (inner == null) { return; }
                inner.style.color = new StyleColor(k_ColorStandard);
                inner.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
                inner.style.fontSize = m_Config.inputFontSize;
                inner.style.unityTextAlign = TextAnchor.MiddleLeft;
                inner.style.whiteSpace = WhiteSpace.NoWrap;
                inner.style.overflow = Overflow.Hidden;
                inner.style.paddingLeft = 6;
                inner.style.paddingRight = 6;
                inner.style.paddingTop = 0;
                inner.style.paddingBottom = 0;
                inner.style.marginTop = 0;
                inner.style.marginBottom = 0;
                inner.style.borderTopWidth = 0;
                inner.style.borderBottomWidth = 0;
                inner.style.borderLeftWidth = 0;
                inner.style.borderRightWidth = 0;
                UIFonts.Apply(inner, UIFonts.Body);
            });
            // Focus-Events koppeln das ChatFocusState-Gate, damit Spell-Hotkeys
            // (1..0), Attack, NextTarget, ClearTarget und MoveCommand waehrend
            // des Tippens NICHT feuern. Siehe PlayerInputController.IsSuppressedByChat.
            m_InputField.RegisterCallback<FocusInEvent>(_ => ChatFocusState.SetTyping(true));
            m_InputField.RegisterCallback<FocusOutEvent>(_ => ChatFocusState.SetTyping(false));
            m_InputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            m_InputRow.Add(m_InputField);
        }

        private void BuildButtons(VisualElement parent)
        {
            // Close-Button (top-right) — schliesst NUR die Input-Row, das Chat-
            // Fenster selbst bleibt persistent sichtbar (WoW-Style).
            VisualElement close = BuildStatefulButton(
                "console-close",
                m_Config.closeButtonIdleTexture,
                m_Config.closeButtonHoverTexture,
                m_Config.closeButtonPressTexture,
                m_Config.closeButtonSize, m_Config.closeButtonSize,
                onClick: () => SetInputActive(false));
            close.style.right = m_Config.closeButtonInsetRight;
            close.style.top = m_Config.closeButtonInsetTop;
            parent.Add(close);

            // Enter-Button (bottom-right). Wird zusammen mit der Input-Row
            // ein-/ausgeblendet, damit er nicht ohne Eingabefeld stehen bleibt.
            m_EnterButton = BuildStatefulButton(
                "console-enter",
                m_Config.enterButtonIdleTexture,
                m_Config.enterButtonHoverTexture,
                m_Config.enterButtonPressTexture,
                m_Config.enterButtonWidth, m_Config.enterButtonHeight,
                onClick: SubmitFromInput);
            m_EnterButton.style.right = m_Config.enterButtonInsetRight;
            m_EnterButton.style.bottom = m_Config.enterButtonInsetBottom;
            parent.Add(m_EnterButton);

            // ScrollBar-Hintergrund (dekorativ)
            Texture2D scrollBarTex = LoadTexture(m_Config.scrollBarTexture);
            if (scrollBarTex != null)
            {
                VisualElement scrollBar = new() { name = "console-scrollbar" };
                scrollBar.style.position = Position.Absolute;
                scrollBar.style.right = m_Config.scrollBarInsetRight;
                scrollBar.style.top = m_Config.scrollBarInsetTop;
                scrollBar.style.bottom = m_Config.scrollBarInsetBottom;
                scrollBar.style.width = m_Config.scrollButtonSize;
                scrollBar.style.backgroundImage = new StyleBackground(scrollBarTex);
                scrollBar.pickingMode = PickingMode.Ignore;
                parent.Add(scrollBar);
            }

            // Scroll-Up (top right of scroll lane)
            VisualElement scrollUp = BuildStatefulButton(
                "console-scroll-up",
                m_Config.scrollUpIdleTexture,
                m_Config.scrollUpHoverTexture,
                m_Config.scrollUpPressTexture,
                m_Config.scrollButtonSize, m_Config.scrollButtonSize,
                onClick: () => ScrollBy(-m_Config.logFontSize * 3f));
            scrollUp.style.right = m_Config.scrollBarInsetRight;
            scrollUp.style.top = m_Config.scrollBarInsetTop - m_Config.scrollButtonSize - 2f;
            parent.Add(scrollUp);

            // Scroll-Down (bottom right of scroll lane)
            VisualElement scrollDown = BuildStatefulButton(
                "console-scroll-down",
                m_Config.scrollDownIdleTexture,
                m_Config.scrollDownHoverTexture,
                m_Config.scrollDownPressTexture,
                m_Config.scrollButtonSize, m_Config.scrollButtonSize,
                onClick: () => ScrollBy(m_Config.logFontSize * 3f));
            scrollDown.style.right = m_Config.scrollBarInsetRight;
            scrollDown.style.bottom = m_Config.scrollBarInsetBottom - m_Config.scrollButtonSize - 2f;
            parent.Add(scrollDown);
        }

        private VisualElement BuildStatefulButton(
            string elementName, string idleKey, string hoverKey, string pressKey,
            float width, float height, System.Action onClick)
        {
            Texture2D idle = LoadTexture(idleKey);
            Texture2D hover = LoadTexture(hoverKey) ?? idle;
            Texture2D press = LoadTexture(pressKey) ?? idle;

            VisualElement btn = new() { name = elementName };
            btn.style.position = Position.Absolute;
            btn.style.width = width;
            btn.style.height = height;
            btn.pickingMode = PickingMode.Position;
            if (idle != null)
            {
                btn.style.backgroundImage = new StyleBackground(idle);
            }

            btn.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (hover != null) btn.style.backgroundImage = new StyleBackground(hover);
            });
            btn.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (idle != null) btn.style.backgroundImage = new StyleBackground(idle);
            });
            btn.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (press != null) btn.style.backgroundImage = new StyleBackground(press);
            });
            btn.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (hover != null) btn.style.backgroundImage = new StyleBackground(hover);
                if (evt.button == 0)
                {
                    onClick?.Invoke();
                }
            });
            return btn;
        }

        // ---------------------------------------------------------------------
        // State + Events
        // ---------------------------------------------------------------------

        /// <summary>
        /// Aktiviert/Deaktiviert die Chat-Input-Row. Das Chat-Panel und der Log
        /// bleiben dauerhaft sichtbar — nur das Eingabefeld + der Enter-Button
        /// togglen (WoW-Style). Setzt zusaetzlich das ChatFocusState-Gate,
        /// damit Gameplay-Hotkeys waehrend des Tippens unterdrueckt werden.
        /// </summary>
        private void SetInputActive(bool active)
        {
            m_InputActive = active;
            if (m_InputRow != null)
            {
                m_InputRow.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (m_EnterButton != null)
            {
                m_EnterButton.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (active)
            {
                ScrollToBottom();
                m_InputField?.Focus();
            }
            else
            {
                m_InputField?.SetValueWithoutNotify(string.Empty);
                m_InputField?.Blur();
                ChatFocusState.SetTyping(false);
            }
        }

        private void OnToggleActionPerformed(InputAction.CallbackContext _)
        {
            // Wenn der Input bereits aktiv ist, hat der TextField-KeyDown-Hook
            // Enter schon konsumiert (Submit/Close). Hier nur der Open-Fall.
            if (m_InputActive)
            {
                return;
            }
            // Den naechsten Enter-KeyDown auf dem TextField verwerfen — sonst
            // schliesst der eben aufgemachte Input sich sofort wieder, weil
            // dieselbe Enter-Taste auch durch das UIToolkit-Eventsystem laeuft.
            m_SwallowNextEnterKeyDown = true;
            SetInputActive(true);
        }

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                if (m_SwallowNextEnterKeyDown)
                {
                    m_SwallowNextEnterKeyDown = false;
                    evt.StopPropagation();
                    return;
                }
                SubmitFromInput();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                // Cancel ohne Submit + Propagation stoppen, damit der globale
                // ClearTarget-Input (ESC) nicht zusaetzlich den Target-Lock loest.
                SetInputActive(false);
                evt.StopPropagation();
            }
        }

        /// <summary>
        /// Submit-Logik: leerer (getrimmter) Text schliesst die Input-Row
        /// (Toggle-off via Enter), gefuellter Text wird gesendet und die Row
        /// schliesst danach — naechstes Enter oeffnet sie wieder.
        /// </summary>
        private void SubmitFromInput()
        {
            if (m_InputField == null)
            {
                return;
            }
            string text = m_InputField.value;
            if (string.IsNullOrWhiteSpace(text))
            {
                SetInputActive(false);
                return;
            }
            m_InputField.SetValueWithoutNotify(string.Empty);
            ConsoleLog.SubmitCommand(text);
            SetInputActive(false);
        }

        private void ScrollBy(float delta)
        {
            if (m_LogView == null)
            {
                return;
            }
            Vector2 off = m_LogView.scrollOffset;
            off.y = Mathf.Max(0f, off.y + delta);
            m_LogView.scrollOffset = off;
        }

        private void ScrollToBottom()
        {
            if (m_LogView == null)
            {
                return;
            }
            m_LogView.schedule.Execute(() =>
            {
                Vector2 off = m_LogView.scrollOffset;
                off.y = float.MaxValue;
                m_LogView.scrollOffset = off;
            }).ExecuteLater(1);
        }

        // ---------------------------------------------------------------------
        // Log-Rendering
        // ---------------------------------------------------------------------

        private void HydrateBacklog()
        {
            IReadOnlyCollection<ConsoleLine> snapshot = ConsoleLog.Snapshot();
            foreach (ConsoleLine line in snapshot)
            {
                AppendLineLabel(line);
            }
            ScrollToBottom();
        }

        private void OnLineAppended(ConsoleLine line)
        {
            AppendLineLabel(line);
            ScrollToBottom();
        }

        private void OnLogCleared()
        {
            if (m_LogView == null)
            {
                return;
            }
            m_LogView.Clear();
        }

        private void AppendLineLabel(ConsoleLine line)
        {
            if (m_LogView == null)
            {
                return;
            }
            Label label = new(line.Text) { name = "console-line" };
            label.style.color = new StyleColor(ColorFor(line.Channel));
            label.style.fontSize = m_Config.logFontSize;
            label.style.whiteSpace = WhiteSpace.Normal;
            UIFonts.Apply(label, UIFonts.Body);
            m_LogView.Add(label);
        }

        private static Color ColorFor(ConsoleChannel channel) => channel switch
        {
            ConsoleChannel.Error => k_ColorError,
            ConsoleChannel.Warning => k_ColorWarning,
            ConsoleChannel.System => k_ColorSystem,
            ConsoleChannel.Command => k_ColorCommand,
            ConsoleChannel.Chat => k_ColorChat,
            _ => k_ColorStandard,
        };

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private Texture2D LoadTexture(string key)
        {
            if (string.IsNullOrEmpty(key) || m_TextureManager == null)
            {
                return null;
            }
            return m_TextureManager.GetTexture(key);
        }
    }
}
