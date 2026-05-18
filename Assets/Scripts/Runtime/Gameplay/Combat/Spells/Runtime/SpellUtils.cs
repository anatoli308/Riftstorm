using System.Text;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Static-Helper-Klasse für Spell-Klassifikation, Formel-Auswertung und
    /// Cost-/Effect-Berechnung. Read-only / sideeffect-frei.
    /// </summary>
    /// <remarks>
    /// 1:1-Port von <c>source_server/Server/src/Combat/SpellUtils.h/.cpp</c>.
    /// </remarks>
    public static class SpellUtils
    {
        // =====================================================================
        // Klassifikation
        // =====================================================================

        /// <summary>True, wenn der Spell instant ist (keine Cast-Bar).</summary>
        public static bool IsInstant(SpellDefinition spell) => spell != null && spell.CastTimeMs <= 0;

        /// <summary>True, wenn der Spell ein AoE-Effekt-Slot besitzt.</summary>
        public static bool IsAoE(SpellDefinition spell)
        {
            if (spell == null) { return false; }
            foreach (SpellEffectDefinition e in spell.Effects)
            {
                switch (e.TargetType)
                {
                    case SpellTargetType.AreaSrcFriendly:
                    case SpellTargetType.AreaDstFriendly:
                    case SpellTargetType.AreaSrcHostile:
                    case SpellTargetType.AreaDstHostile:
                        return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn der Spell sich ausschließlich an den Caster richtet.</summary>
        public static bool IsSelfOnly(SpellDefinition spell)
        {
            if (spell == null || spell.Effects.Count == 0) { return false; }
            foreach (SpellEffectDefinition e in spell.Effects)
            {
                if (e.TargetType != SpellTargetType.SelfCaster
                    && e.TargetType != SpellTargetType.AreaSrcFriendly
                    && e.TargetType != SpellTargetType.AreaSrcHostile
                    && e.TargetType != SpellTargetType.None)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>True, wenn der Spell ein konkretes Ziel braucht (Single-Target).</summary>
        public static bool RequiresTarget(SpellDefinition spell)
        {
            if (spell == null) { return false; }
            foreach (SpellEffectDefinition e in spell.Effects)
            {
                switch (e.TargetType)
                {
                    case SpellTargetType.FriendlyUnit:
                    case SpellTargetType.HostileUnit:
                    case SpellTargetType.AnyUnit:
                    case SpellTargetType.AreaDstFriendly:
                    case SpellTargetType.AreaDstHostile:
                    case SpellTargetType.GameObject:
                        return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn der Spell Freunde treffen kann (Heal / Buff).</summary>
        public static bool CanTargetFriendly(SpellDefinition spell)
        {
            if (spell == null) { return false; }
            foreach (SpellEffectDefinition e in spell.Effects)
            {
                switch (e.TargetType)
                {
                    case SpellTargetType.SelfCaster:
                    case SpellTargetType.FriendlyUnit:
                    case SpellTargetType.AnyUnit:
                    case SpellTargetType.AreaSrcFriendly:
                    case SpellTargetType.AreaDstFriendly:
                        return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn der Spell Feinde treffen kann (Damage / Debuff).</summary>
        public static bool CanTargetHostile(SpellDefinition spell)
        {
            if (spell == null) { return false; }
            foreach (SpellEffectDefinition e in spell.Effects)
            {
                switch (e.TargetType)
                {
                    case SpellTargetType.HostileUnit:
                    case SpellTargetType.AnyUnit:
                    case SpellTargetType.AreaSrcHostile:
                    case SpellTargetType.AreaDstHostile:
                        return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn mindestens ein Effect-Slot direkten Schaden macht.</summary>
        public static bool IsDamageSpell(SpellDefinition spell)
        {
            if (spell == null) { return false; }
            foreach (SpellEffectDefinition e in spell.Effects)
            {
                if (e.Type == SpellEffectType.SchoolDamage || e.Type == SpellEffectType.WeaponDamage)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn mindestens ein Effect-Slot heilt.</summary>
        public static bool IsHealSpell(SpellDefinition spell)
        {
            if (spell == null) { return false; }
            foreach (SpellEffectDefinition e in spell.Effects)
            {
                if (e.Type == SpellEffectType.Heal || e.Type == SpellEffectType.HealPct)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn mindestens ein Effect-Slot eine Aura applied.</summary>
        public static bool IsAuraSpell(SpellDefinition spell)
        {
            if (spell == null) { return false; }
            foreach (SpellEffectDefinition e in spell.Effects)
            {
                if (e.Type == SpellEffectType.ApplyAura)
                {
                    return true;
                }
            }
            return false;
        }

        // =====================================================================
        // Cost / Value Berechnung
        // =====================================================================

        /// <summary>
        /// Berechnet die effektiven Mana-Kosten unter Berücksichtigung des
        /// Prozentual-Anteils auf <paramref name="maxMana"/>.
        /// </summary>
        public static int CalculateManaCost(SpellDefinition spell, int casterLevel, int maxMana)
        {
            if (spell == null) { return 0; }
            int cost = spell.ManaCost;
            if (spell.ManaCostPct > 0 && maxMana > 0)
            {
                cost += (maxMana * spell.ManaCostPct) / 100;
            }
            // Reserviert für Caster-Level-Skalierung: keine Default-Skalierung,
            // damit JSON die volle Kontrolle behält (1:1 zum Source, in dem
            // unskaliierte Mana-Kosten die Regel sind).
            _ = casterLevel;
            return cost;
        }

        /// <summary>
        /// Berechnet den Basis-Wert (Schaden / Heal) für einen Effekt-Slot
        /// unter Berücksichtigung der optionalen <c>scale_formula</c>.
        /// </summary>
        public static int CalculateEffectValue(SpellDefinition spell, int effectIndex, int casterLevel)
        {
            if (spell == null || (uint)effectIndex >= (uint)spell.Effects.Count)
            {
                return 0;
            }
            SpellEffectDefinition e = spell.Effects[effectIndex];
            if (!string.IsNullOrEmpty(e.ScaleFormula))
            {
                int evaluated = EvaluateFormulaWithBase(e.ScaleFormula, casterLevel, spellLevel: 1, baseValue: e.Data1);
                if (evaluated != 0)
                {
                    return evaluated;
                }
            }
            return e.Data1;
        }

        /// <summary>
        /// Berechnet den Basis-Wert für eine Aura aus ihrer <see cref="AuraDefinition.Data1"/>
        /// (analog zu <see cref="CalculateEffectValue"/>, nur ohne Quell-Spell).
        /// </summary>
        public static int CalculateAuraBaseValue(AuraDefinition aura, int casterLevel)
        {
            if (aura == null) { return 0; }
            _ = casterLevel; // Hook für künftige Aura-spezifische Skalierungs-Formeln.
            return aura.Data1;
        }

        // =====================================================================
        // Formel-Evaluation
        // =====================================================================

        /// <summary>
        /// Wertet eine simple arithmetische Formel (<c>+ - * /</c>, Klammern,
        /// Variable <c>clvl</c> + Konstante <c>sp</c>) zu <see cref="int"/> aus.
        /// </summary>
        /// <remarks>
        /// Minimaler Recursive-Descent-Parser. Server-only, keine User-Input-
        /// Verarbeitung — Formeln stammen aus JSON-Daten, nicht aus Netzwerk-Paketen.
        /// Variablen 1:1 zum Source-Editor (siehe <c>scripts/text/STF_*.txt</c>):
        /// <list type="bullet">
        ///   <item><c>clvl</c> — Caster-Level</item>
        ///   <item><c>splvl</c> — Spell-Level (für Rank-2/3-Spells, default 1)</item>
        ///   <item><c>sp</c> — Spell-Power (Hook für Stats-Pipeline, default 0)</item>
        ///   <item><c>value</c> — Basis-Wert (= <see cref="SpellEffectDefinition.Data1"/>)</item>
        /// </list>
        /// </remarks>
        public static int EvaluateFormula(string formula, int clvl)
        {
            if (string.IsNullOrWhiteSpace(formula))
            {
                return 0;
            }
            try
            {
                FormulaParser parser = new(formula, clvl, spellLevel: 1, spellPower: 0);
                return (int)parser.ParseExpression();
            }
            catch
            {
                UnityEngine.Debug.LogWarning($"[SpellUtils] Formel '{formula}' nicht parsbar — Fallback auf 0.");
                return 0;
            }
        }

        // =====================================================================
        // Minimaler arithmetischer Parser
        // =====================================================================

        /// <summary>
        /// Erweiterte Variante mit Spell-Level und <c>value</c>-Substitution
        /// (für Effect-Scale-Formeln wie <c>value*(clvl+splvl)</c>).
        /// </summary>
        public static int EvaluateFormulaWithBase(string formula, int clvl, int spellLevel, int baseValue)
        {
            if (string.IsNullOrWhiteSpace(formula))
            {
                return baseValue;
            }
            try
            {
                FormulaParser parser = new(formula, clvl, spellLevel, spellPower: 0, baseValue);
                return (int)parser.ParseExpression();
            }
            catch
            {
                UnityEngine.Debug.LogWarning($"[SpellUtils] Formel '{formula}' nicht parsbar — Fallback auf {baseValue}.");
                return baseValue;
            }
        }

        sealed class FormulaParser
        {
            readonly string m_Src;
            readonly int m_Clvl;
            readonly int m_Splvl;
            readonly int m_Sp;
            readonly int m_Value;
            int m_Pos;

            public FormulaParser(string src, int clvl, int spellLevel, int spellPower, int baseValue = 0)
            {
                m_Src = src;
                m_Clvl = clvl;
                m_Splvl = spellLevel;
                m_Sp = spellPower;
                m_Value = baseValue;
            }

            public double ParseExpression()
            {
                double left = ParseTerm();
                while (true)
                {
                    SkipWhitespace();
                    if (m_Pos >= m_Src.Length) { break; }
                    char c = m_Src[m_Pos];
                    if (c != '+' && c != '-') { break; }
                    m_Pos++;
                    double right = ParseTerm();
                    left = c == '+' ? left + right : left - right;
                }
                return left;
            }

            double ParseTerm()
            {
                double left = ParseFactor();
                while (true)
                {
                    SkipWhitespace();
                    if (m_Pos >= m_Src.Length) { break; }
                    char c = m_Src[m_Pos];
                    if (c != '*' && c != '/') { break; }
                    m_Pos++;
                    double right = ParseFactor();
                    left = c == '*' ? left * right : (right != 0 ? left / right : 0);
                }
                return left;
            }

            double ParseFactor()
            {
                SkipWhitespace();
                if (m_Pos >= m_Src.Length)
                {
                    return 0;
                }
                char c = m_Src[m_Pos];
                if (c == '(')
                {
                    m_Pos++;
                    double v = ParseExpression();
                    SkipWhitespace();
                    if (m_Pos < m_Src.Length && m_Src[m_Pos] == ')') { m_Pos++; }
                    return v;
                }
                if (c == '-')
                {
                    m_Pos++;
                    return -ParseFactor();
                }
                if (char.IsLetter(c))
                {
                    StringBuilder sb = new();
                    while (m_Pos < m_Src.Length && char.IsLetter(m_Src[m_Pos]))
                    {
                        sb.Append(m_Src[m_Pos++]);
                    }
                    string id = sb.ToString();
                    return id switch
                    {
                        "clvl" => m_Clvl,
                        "splvl" => m_Splvl,
                        "sp" => m_Sp,
                        "value" => m_Value,
                        _ => 0,
                    };
                }
                if (char.IsDigit(c) || c == '.')
                {
                    StringBuilder sb = new();
                    while (m_Pos < m_Src.Length && (char.IsDigit(m_Src[m_Pos]) || m_Src[m_Pos] == '.'))
                    {
                        sb.Append(m_Src[m_Pos++]);
                    }
                    return double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
                }
                m_Pos++;
                return 0;
            }

            void SkipWhitespace()
            {
                while (m_Pos < m_Src.Length && char.IsWhiteSpace(m_Src[m_Pos])) { m_Pos++; }
            }
        }
    }
}
