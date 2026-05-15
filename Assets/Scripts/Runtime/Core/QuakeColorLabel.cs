using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tolik.Riftstorm.Runtime.Core
{
    /// <summary>
    /// Custom VisualElement that renders Quake/SoF2 color-coded text using the bigchars atlas.
    /// Supports ^0-^9 and ^a-^z (case-insensitive) color escape sequences = 36 colors.
    /// Supports \XX hex escapes for atlas symbols (e.g. \01 = char 1, \FF = char 255).
    /// Each character is a child VisualElement showing a cropped sprite from the 16x16 atlas grid.
    /// Use ^^ to render a literal ^ character, \\ to render a literal backslash.
    /// </summary>
    [UxmlElement]
    internal partial class QuakeColorLabel : VisualElement
    {
        /// <summary>Atlas grid dimensions (16x16 = 256 ASCII characters).</summary>
        const int k_AtlasColumns = 16;
        const int k_AtlasRows = 16;

        /// <summary>SoF2/Quake III color escape character.</summary>
        const char k_ColorEscape = '^';

        /// <summary>
        /// Extended Quake color table: ^0-^9 + ^a-^z (case-insensitive) = 36 colors.
        /// Lookup by lowercase char key.
        /// </summary>
        static readonly Dictionary<char, Color> s_ColorMap = new()
        {
            // ^0-^9: Original Quake III + Extended
            { '0', new(0f, 0f, 0f, 1f) },            // Black
            { '1', new(1f, 0.2f, 0.2f, 1f) },        // Red
            { '2', new(0.2f, 1f, 0.2f, 1f) },        // Green
            { '3', new(1f, 1f, 0.2f, 1f) },          // Yellow
            { '4', new(0.3f, 0.3f, 1f, 1f) },        // Blue
            { '5', new(0.2f, 1f, 1f, 1f) },          // Cyan
            { '6', new(1f, 0.2f, 1f, 1f) },          // Magenta
            { '7', new(1f, 1f, 1f, 1f) },            // White
            { '8', new(1f, 0.5f, 0f, 1f) },          // Orange
            { '9', new(0.5f, 0.5f, 0.5f, 1f) },     // Grey
            // ^a-^z: Extended palette
            { 'a', new(1f, 0.5f, 0.5f, 1f) },       // Salmon
            { 'b', new(0.5f, 1f, 0.5f, 1f) },       // Light Green
            { 'c', new(0.5f, 0.5f, 1f, 1f) },       // Light Blue
            { 'd', new(0.6f, 0.6f, 0f, 1f) },       // Olive
            { 'e', new(1f, 0.4f, 0.7f, 1f) },       // Pink
            { 'f', new(0.6f, 0.2f, 0.8f, 1f) },     // Purple
            { 'g', new(0f, 0.5f, 0f, 1f) },          // Dark Green
            { 'h', new(0.6f, 0.1f, 0.1f, 1f) },     // Maroon
            { 'i', new(0.6f, 0.4f, 0.2f, 1f) },     // Brown
            { 'j', new(1f, 0.84f, 0f, 1f) },         // Gold
            { 'k', new(0.3f, 0.3f, 0.3f, 1f) },     // Dark Grey
            { 'l', new(0.8f, 0.8f, 0.8f, 1f) },     // Light Grey
            { 'm', new(0.6f, 0.6f, 0.6f, 1f) },     // Medium Grey
            { 'n', new(0f, 0.5f, 0.5f, 1f) },        // Teal
            { 'o', new(0.5f, 0.6f, 0.2f, 1f) },     // Olive Green
            { 'p', new(1f, 0.8f, 0.6f, 1f) },       // Peach
            { 'q', new(1f, 0.4f, 0.4f, 1f) },       // Rose
            { 'r', new(0.1f, 0.1f, 0.5f, 1f) },     // Navy
            { 's', new(0.75f, 0.75f, 0.75f, 1f) },   // Silver
            { 't', new(0.82f, 0.71f, 0.55f, 1f) },   // Tan
            { 'u', new(0.3f, 0f, 0.5f, 1f) },        // Indigo
            { 'v', new(0.56f, 0f, 1f, 1f) },          // Violet
            { 'w', new(0.96f, 0.87f, 0.7f, 1f) },    // Wheat
            { 'x', new(0f, 0.4f, 0.4f, 1f) },        // Dark Cyan
            { 'y', new(0.6f, 1f, 0.2f, 1f) },        // Lime
            { 'z', new(1f, 0.5f, 0.31f, 1f) },       // Coral
        };

        /// <summary>Default color (white, same as ^7).</summary>
        static readonly Color s_DefaultColor = new(1f, 1f, 1f, 1f);

        /// <summary>Cursor blink interval in milliseconds.</summary>
        const long k_CursorBlinkMs = 530;

        /// <summary>Parsed glyph info for single-mesh rendering.</summary>
        struct GlyphInfo
        {
            public int AsciiCode;
            public Color32 Tint;
        }

        Texture2D m_Atlas;
        string m_Text = "";
        float m_CharWidth = 24f;
        float m_CharHeight = 24f;
        float m_MaxWidth;
        bool m_ShowCursor;
        VisualElement m_CursorElement;
        IVisualElementScheduledItem m_CursorBlink;
        bool m_CursorVisible;

        /// <summary>Cached parsed glyphs for mesh generation. Rebuilt only when text or atlas changes.</summary>
        readonly List<GlyphInfo> m_Glyphs = new();

        /// <summary>Bigchars atlas texture (16x16 grid of ASCII glyphs, white on transparent).</summary>
        public Texture2D Atlas
        {
            get => m_Atlas;
            set
            {
                m_Atlas = value;
                RebuildGlyphCache();
            }
        }

        /// <summary>Raw text with ^X color codes (e.g. "^1[EU] ^3Warzone ^7#1").</summary>
        public string Text
        {
            get => m_Text;
            set
            {
                m_Text = value ?? "";
                RebuildGlyphCache();
            }
        }

        /// <summary>Shows a blinking cursor at the end of the text (for input overlay).</summary>
        public bool ShowCursor
        {
            get => m_ShowCursor;
            set
            {
                m_ShowCursor = value;
                UpdateCursor();
            }
        }

        /// <summary>Rendered width of each character in pixels.</summary>
        public float CharWidth
        {
            get => m_CharWidth;
            set
            {
                m_CharWidth = value;
                RecalculateSize();
                UpdateCursor();
                MarkDirtyRepaint();
            }
        }

        /// <summary>Rendered height of each character in pixels.</summary>
        public float CharHeight
        {
            get => m_CharHeight;
            set
            {
                m_CharHeight = value;
                RecalculateSize();
                UpdateCursor();
                MarkDirtyRepaint();
            }
        }

        /// <summary>Maximum width in pixels. When set (greater than 0), glyphs wrap to the next line. 0 = no limit (single line).</summary>
        public float MaxWidth
        {
            get => m_MaxWidth;
            set
            {
                m_MaxWidth = value;
                RecalculateSize();
                UpdateCursor();
                MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// Initializes the label with overflow clipping and registers custom mesh rendering.
        /// </summary>
        public QuakeColorLabel()
        {
            style.overflow = Overflow.Hidden;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>
        /// Parses the text into a cached list of GlyphInfo entries and triggers a repaint.
        /// Called when text or atlas changes. Does not create child VisualElements.
        /// </summary>
        void RebuildGlyphCache()
        {
            m_Glyphs.Clear();

            if (m_Atlas != null && !string.IsNullOrEmpty(m_Text))
            {
                Color currentColor = s_DefaultColor;

                for (int i = 0; i < m_Text.Length; i++)
                {
                    // ^^ = literal ^ character
                    if (i + 1 < m_Text.Length && m_Text[i] == k_ColorEscape && m_Text[i + 1] == k_ColorEscape)
                    {
                        i++;
                        // fall through to add '^' glyph
                    }
                    else if (IsColorCode(m_Text, i))
                    {
                        char key = char.ToLowerInvariant(m_Text[i + 1]);
                        currentColor = s_ColorMap[key];
                        i++;
                        continue;
                    }
                    // \\ = literal backslash
                    else if (i + 1 < m_Text.Length && m_Text[i] == '\\' && m_Text[i + 1] == '\\')
                    {
                        i++;
                        // fall through to add '\\' glyph
                    }
                    // \XX = hex escape for bigchars atlas symbol (00-FF)
                    else if (TryParseHexEscape(m_Text, i, out int hexChar))
                    {
                        m_Glyphs.Add(new GlyphInfo { AsciiCode = hexChar, Tint = currentColor });
                        i += 2;
                        continue;
                    }

                    char c = m_Text[i];
                    int asciiCode = c & 0xFF;
                    m_Glyphs.Add(new GlyphInfo { AsciiCode = asciiCode, Tint = currentColor });
                }
            }

            RecalculateSize();
            UpdateCursor();
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Calculates the number of glyphs per line based on MaxWidth. Returns 0 if no limit.
        /// </summary>
        int GlyphsPerLine()
        {
            if (m_MaxWidth <= 0 || m_CharWidth <= 0)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.FloorToInt(m_MaxWidth / m_CharWidth));
        }

        /// <summary>
        /// Recalculates element width and height based on glyph count, char size and MaxWidth.
        /// </summary>
        void RecalculateSize()
        {
            int count = m_Glyphs.Count;
            int perLine = GlyphsPerLine();

            if (perLine > 0 && count > 0)
            {
                int lines = Mathf.CeilToInt((float)count / perLine);
                style.width = Mathf.Min(count, perLine) * m_CharWidth;
                style.height = lines * m_CharHeight;
            }
            else
            {
                style.width = count * m_CharWidth;
                style.height = m_CharHeight;
            }
        }

        /// <summary>
        /// Generates a single textured mesh with one quad per glyph. All characters are
        /// rendered in one draw call instead of creating N child VisualElements.
        /// Supports multi-line layout when MaxWidth is set.
        /// </summary>
        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (m_Atlas == null || m_Glyphs.Count == 0)
            {
                return;
            }

            int count = m_Glyphs.Count;
            int perLine = GlyphsPerLine();
            MeshWriteData mwd = mgc.Allocate(count * 4, count * 6, m_Atlas);

            float cellU = 1f / k_AtlasColumns;
            float cellV = 1f / k_AtlasRows;

            for (int i = 0; i < count; i++)
            {
                GlyphInfo g = m_Glyphs[i];
                int col = g.AsciiCode % k_AtlasColumns;
                int row = g.AsciiCode / k_AtlasColumns;

                int lineIndex = (perLine > 0) ? i % perLine : i;
                int lineNumber = (perLine > 0) ? i / perLine : 0;

                float x0 = lineIndex * m_CharWidth;
                float x1 = x0 + m_CharWidth;
                float y0 = lineNumber * m_CharHeight;
                float y1 = y0 + m_CharHeight;

                float u0 = col * cellU;
                float u1 = u0 + cellU;
                // Standard Unity UV: (0,0) = bottom-left; atlas row 0 = top of texture image
                float vTop = 1f - row * cellV;
                float vBot = vTop - cellV;

                ushort vi = (ushort)(i * 4);

                mwd.SetNextVertex(new Vertex
                {
                    position = new Vector3(x0, y0, Vertex.nearZ),
                    tint = g.Tint,
                    uv = new Vector2(u0, vTop)
                });
                mwd.SetNextVertex(new Vertex
                {
                    position = new Vector3(x1, y0, Vertex.nearZ),
                    tint = g.Tint,
                    uv = new Vector2(u1, vTop)
                });
                mwd.SetNextVertex(new Vertex
                {
                    position = new Vector3(x1, y1, Vertex.nearZ),
                    tint = g.Tint,
                    uv = new Vector2(u1, vBot)
                });
                mwd.SetNextVertex(new Vertex
                {
                    position = new Vector3(x0, y1, Vertex.nearZ),
                    tint = g.Tint,
                    uv = new Vector2(u0, vBot)
                });

                mwd.SetNextIndex(vi);
                mwd.SetNextIndex((ushort)(vi + 1));
                mwd.SetNextIndex((ushort)(vi + 2));
                mwd.SetNextIndex(vi);
                mwd.SetNextIndex((ushort)(vi + 2));
                mwd.SetNextIndex((ushort)(vi + 3));
            }
        }

        /// <summary>
        /// Removes and re-creates the cursor element based on current state.
        /// </summary>
        void UpdateCursor()
        {
            if (m_CursorElement != null && m_CursorElement.parent == this)
            {
                Remove(m_CursorElement);
            }

            m_CursorElement = null;
            m_CursorBlink?.Pause();

            if (m_ShowCursor && m_Atlas != null)
            {
                AppendCursor();
            }
        }

        /// <summary>
        /// Appends a blinking cursor element positioned after the last glyph.
        /// </summary>
        void AppendCursor()
        {
            m_CursorElement = new VisualElement();
            m_CursorElement.style.position = Position.Absolute;
            m_CursorElement.style.left = m_Glyphs.Count * m_CharWidth;
            m_CursorElement.style.top = 0;
            m_CursorElement.style.width = 2;
            m_CursorElement.style.height = m_CharHeight;
            m_CursorElement.style.backgroundColor = new StyleColor(s_DefaultColor);
            Add(m_CursorElement);

            m_CursorVisible = true;
            m_CursorBlink = schedule.Execute(() =>
            {
                if (m_CursorElement == null) return;
                m_CursorVisible = !m_CursorVisible;
                m_CursorElement.style.opacity = m_CursorVisible ? 1f : 0f;
            }).Every(k_CursorBlinkMs);
        }

        /// <summary>
        /// Tries to parse a \XX hex escape at the given index.
        /// Returns true if text[index] == '\' and text[index+1..index+2] are valid hex digits.
        /// </summary>
        static bool TryParseHexEscape(string text, int index, out int charCode)
        {
            charCode = 0;
            if (index + 2 >= text.Length || text[index] != '\\')
            {
                return false;
            }

            char h1 = text[index + 1];
            char h2 = text[index + 2];
            if (!IsHexDigit(h1) || !IsHexDigit(h2))
            {
                return false;
            }

            charCode = (HexVal(h1) << 4) | HexVal(h2);
            return true;
        }

        /// <summary>Returns true if the character is a valid hexadecimal digit (0-9, a-f, A-F).</summary>
        static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        /// <summary>Converts a single hex character to its integer value.</summary>
        static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return c - 'A' + 10;
        }

        /// <summary>
        /// Checks if the character at the given index is a Quake color escape sequence
        /// (^ followed by 0-9 or a-z/A-Z that exists in the color map).
        /// </summary>
        static bool IsColorCode(string text, int index)
        {
            if (index + 1 >= text.Length || text[index] != k_ColorEscape)
            {
                return false;
            }

            char next = char.ToLowerInvariant(text[index + 1]);
            return s_ColorMap.ContainsKey(next);
        }

        /// <summary>
        /// Strips all ^X color codes and resolves \XX hex escapes from a Quake-formatted string.
        /// Handles ^^ as literal ^ and \\ as literal \. Returns only the visible text.
        /// </summary>
        public static string StripColorCodes(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            System.Text.StringBuilder sb = new(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (i + 1 < text.Length && text[i] == k_ColorEscape && text[i + 1] == k_ColorEscape)
                {
                    sb.Append(k_ColorEscape);
                    i++;
                    continue;
                }

                if (IsColorCode(text, i))
                {
                    i++;
                    continue;
                }

                if (i + 1 < text.Length && text[i] == '\\' && text[i + 1] == '\\')
                {
                    sb.Append('\\');
                    i++;
                    continue;
                }

                if (TryParseHexEscape(text, i, out int hexChar))
                {
                    sb.Append((char)hexChar);
                    i += 2;
                    continue;
                }

                sb.Append(text[i]);
            }

            return sb.ToString();
        }
    }
}
