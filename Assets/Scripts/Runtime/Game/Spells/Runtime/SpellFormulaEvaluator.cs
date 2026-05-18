namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Variablen-Kontext fuer den Spell-Formel-Evaluator. Reine Wertedaten,
    /// keine Allokationen. Unbekannte Bezeichner im Ausdruck werten zu <c>0</c>.
    /// </summary>
    /// <remarks>
    /// Vokabular aus <c>Assets/StreamingAssets/spells/_templates.json</c>
    /// inventarisiert: <c>clvl, splvl, value, STR, INT, WIL, CUR</c>. Source-Referenz:
    /// <c>source_server/Server/src/Combat/SpellUtils.cpp:19</c>.
    /// </remarks>
    public readonly struct SpellFormulaContext
    {
        /// <summary>Caster-Level (<c>clvl</c>).</summary>
        public readonly int Clvl;
        /// <summary>Spell-Rank (<c>splvl</c>). Bis ein Rank-System existiert: <c>0</c>.</summary>
        public readonly int Splvl;
        /// <summary>Basis-Wert (<c>value</c>) — i.d.R. <see cref="SpellTemplateEffect.Data1"/>.</summary>
        public readonly int Value;
        /// <summary>Staerke (<c>STR</c>).</summary>
        public readonly int Str;
        /// <summary>Intelligenz (<c>INT</c>).</summary>
        public readonly int Int;
        /// <summary>Willenskraft (<c>WIL</c>).</summary>
        public readonly int Wil;
        /// <summary>Ausdauer / Konstitution (<c>CUR</c>).</summary>
        public readonly int Cur;

        /// <summary>Konstruktor.</summary>
        public SpellFormulaContext(int clvl, int splvl = 0, int value = 0,
            int str = 0, int intStat = 0, int wil = 0, int cur = 0)
        {
            Clvl = clvl;
            Splvl = splvl;
            Value = value;
            Str = str;
            Int = intStat;
            Wil = wil;
            Cur = cur;
        }
    }

    /// <summary>
    /// Stateless Integer-Ausdrucks-Evaluator fuer Spell-Formeln aus den
    /// JSON-Templates. Unterstuetzt <c>+ - * /</c>, Klammern, unaeres Minus,
    /// Integer-Literale und Variablen (<c>clvl, splvl, value, STR, INT, WIL, CUR</c>).
    /// Allokationsfrei (recursive descent ueber String-Index).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fehlertoleranz: <c>null</c>/leerer String, Syntaxfehler oder Division-durch-Null
    /// liefern <c>0</c>. Damit bleibt z.B. das fehlerhafte Template
    /// <c>"2+((clvl*75)/10"</c> (fehlende Klammer in <c>_templates.json</c>) ohne
    /// Cast-Pipeline-Crash.
    /// </para>
    /// <para>
    /// Source-Aequivalent: <c>SpellUtils::calculateFormula</c>
    /// (<c>source_server/Server/src/Combat/SpellUtils.cpp:19</c>) — dort
    /// Shunting-Yard. Recursive descent ist semantisch identisch und braucht
    /// keinen Heap-Buffer.
    /// </para>
    /// </remarks>
    public static class SpellFormulaEvaluator
    {
        /// <summary>
        /// Evaluiert <paramref name="formula"/> im gegebenen Kontext. Liefert
        /// <c>0</c> bei null/leer/Syntaxfehler/Div-by-Zero.
        /// </summary>
        public static int Evaluate(string formula, SpellFormulaContext ctx)
        {
            if (string.IsNullOrEmpty(formula)) { return 0; }
            int pos = 0;
            int result = ParseExpression(formula, ref pos, ctx, out bool ok);
            if (!ok) { return 0; }
            SkipWhitespace(formula, ref pos);
            // Trailing Garbage = Syntaxfehler.
            if (pos != formula.Length) { return 0; }
            return result;
        }

        // ----- Grammar -------------------------------------------------------
        // Expression := Term (('+'|'-') Term)*
        // Term       := Factor (('*'|'/') Factor)*
        // Factor     := '-' Factor | '(' Expression ')' | Number | Identifier
        // ---------------------------------------------------------------------

        static int ParseExpression(string s, ref int pos, in SpellFormulaContext ctx, out bool ok)
        {
            int left = ParseTerm(s, ref pos, ctx, out ok);
            if (!ok) { return 0; }
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) { return left; }
                char c = s[pos];
                if (c != '+' && c != '-') { return left; }
                pos++;
                int right = ParseTerm(s, ref pos, ctx, out ok);
                if (!ok) { return 0; }
                left = c == '+' ? left + right : left - right;
            }
        }

        static int ParseTerm(string s, ref int pos, in SpellFormulaContext ctx, out bool ok)
        {
            int left = ParseFactor(s, ref pos, ctx, out ok);
            if (!ok) { return 0; }
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) { return left; }
                char c = s[pos];
                if (c != '*' && c != '/') { return left; }
                pos++;
                int right = ParseFactor(s, ref pos, ctx, out ok);
                if (!ok) { return 0; }
                if (c == '*')
                {
                    left *= right;
                }
                else
                {
                    if (right == 0) { ok = false; return 0; }
                    left /= right;
                }
            }
        }

        static int ParseFactor(string s, ref int pos, in SpellFormulaContext ctx, out bool ok)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) { ok = false; return 0; }
            char c = s[pos];

            if (c == '-')
            {
                pos++;
                int inner = ParseFactor(s, ref pos, ctx, out ok);
                return ok ? -inner : 0;
            }
            if (c == '+')
            {
                pos++;
                return ParseFactor(s, ref pos, ctx, out ok);
            }
            if (c == '(')
            {
                pos++;
                int inner = ParseExpression(s, ref pos, ctx, out ok);
                if (!ok) { return 0; }
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ')') { ok = false; return 0; }
                pos++;
                return inner;
            }
            if (c >= '0' && c <= '9')
            {
                return ParseNumber(s, ref pos, out ok);
            }
            if (IsIdentStart(c))
            {
                return ResolveIdentifier(s, ref pos, ctx, out ok);
            }
            ok = false;
            return 0;
        }

        static int ParseNumber(string s, ref int pos, out bool ok)
        {
            int value = 0;
            int start = pos;
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c < '0' || c > '9') { break; }
                // Overflow-tolerant — clamp statt throw.
                value = value * 10 + (c - '0');
                pos++;
            }
            ok = pos > start;
            return value;
        }

        static int ResolveIdentifier(string s, ref int pos, in SpellFormulaContext ctx, out bool ok)
        {
            int start = pos;
            while (pos < s.Length && IsIdentCont(s[pos])) { pos++; }
            int len = pos - start;
            ok = true;
            // Case-sensitiver Vergleich gegen das Source-Vokabular. Unbekannt = 0.
            if (Match(s, start, len, "clvl")) { return ctx.Clvl; }
            if (Match(s, start, len, "splvl")) { return ctx.Splvl; }
            if (Match(s, start, len, "value")) { return ctx.Value; }
            if (Match(s, start, len, "STR")) { return ctx.Str; }
            if (Match(s, start, len, "INT")) { return ctx.Int; }
            if (Match(s, start, len, "WIL")) { return ctx.Wil; }
            if (Match(s, start, len, "CUR")) { return ctx.Cur; }
            // Legacy-Alias: das alte SpellUtils-Doc nannte 'sp' — wir leiten auf Splvl um.
            if (Match(s, start, len, "sp")) { return ctx.Splvl; }
            return 0;
        }

        static bool Match(string s, int start, int len, string token)
        {
            if (len != token.Length) { return false; }
            for (int i = 0; i < len; i++)
            {
                if (s[start + i] != token[i]) { return false; }
            }
            return true;
        }

        static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t')) { pos++; }
        }

        static bool IsIdentStart(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

        static bool IsIdentCont(char c)
            => IsIdentStart(c) || (c >= '0' && c <= '9');
    }
}
