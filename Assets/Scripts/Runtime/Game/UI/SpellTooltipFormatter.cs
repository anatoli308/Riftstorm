using System.Text;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Spells;
using UnityEngine;

namespace Riftstorm.Game.UI
{
    /// <summary>
    /// Loest die WoW-/FLARE-Platzhalter in <see cref="SpellTemplate.Description"/>
    /// (und <see cref="SpellTemplate.AuraDescription"/>) gegen einen konkreten
    /// Caster-Kontext auf. Stateless, allokiert nur den Result-String.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unterstuetzte Tokens (Inventar aus <c>StreamingAssets/spells/_templates.json</c>):
    /// </para>
    /// <list type="bullet">
    ///   <item><c>$E1min</c>, <c>$E2min</c>, <c>$E3min</c> &#8212; Min-Wert (Data1) des Effekts, ueber <see cref="SpellTemplate.Effect1ScaleFormula"/> skaliert.</item>
    ///   <item><c>$E1max</c>, <c>$E2max</c>, <c>$E3max</c> &#8212; Max-Wert (Data2; Fallback Data1), skaliert.</item>
    ///   <item><c>$E1D1</c>..<c>$E3D3</c> &#8212; Rohwert eines beliebigen Data-Feldes (ungescalt).</item>
    ///   <item><c>$E1</c>, <c>$E2</c>, <c>$E3</c> &#8212; Median (min+max)/2, skaliert. Praktisch wenn die DB nur einen kombinierten Token nutzt.</item>
    ///   <item><c>$DUR</c> &#8212; Dauer aus <see cref="SpellTemplate.Duration"/>/<see cref="SpellTemplate.DurationFormula"/>, formatiert als &quot;X sec&quot; oder &quot;Y min&quot;.</item>
    /// </list>
    /// <para>
    /// Unbekannte Tokens bleiben unveraendert im String &#8212; FLARE-tolerant,
    /// damit ein neuer Token nicht silent "" rendert.
    /// </para>
    /// <para>
    /// Source-Aequivalent: <c>SpellMgr::formatDescription</c> &#8212; im Riftstorm-Port
    /// liegt das im Client, weil Tooltip nur auf dem lokalen Spieler relevant ist.
    /// </para>
    /// </remarks>
    public static class SpellTooltipFormatter
    {
        /// <summary>
        /// Formatiert <paramref name="template"/> gegen den gegebenen Spell und Caster.
        /// Null/leerer Input liefert <see cref="string.Empty"/>.
        /// </summary>
        /// <param name="template">Roh-Template-Text mit <c>$</c>-Platzhaltern.</param>
        /// <param name="spell">Spell-Template (liefert Effekt-Daten + Dauer).</param>
        /// <param name="caster">Caster-Kontext fuer Scale-Formeln. <c>null</c> = Stats als 0 behandeln.</param>
        public static string Format(string template, SpellTemplate spell, ICombatUnit caster)
        {
            if (string.IsNullOrEmpty(template)) { return string.Empty; }
            if (spell == null || template.IndexOf('$') < 0) { return template; }

            StringBuilder sb = new(template.Length + 16);
            int pos = 0;
            while (pos < template.Length)
            {
                char c = template[pos];
                if (c != '$')
                {
                    sb.Append(c);
                    pos++;
                    continue;
                }
                // '$' am Ende ohne Token = einfach behalten.
                int tokenEnd = pos + 1;
                while (tokenEnd < template.Length && IsTokenChar(template[tokenEnd]))
                {
                    tokenEnd++;
                }
                if (tokenEnd == pos + 1)
                {
                    sb.Append('$');
                    pos++;
                    continue;
                }
                string token = template.Substring(pos + 1, tokenEnd - pos - 1);
                if (TryResolve(token, spell, caster, out string value))
                {
                    sb.Append(value);
                }
                else
                {
                    // Unbekannter Token bleibt literal stehen (inkl. '$').
                    sb.Append('$');
                    sb.Append(token);
                }
                pos = tokenEnd;
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------------

        private static bool IsTokenChar(char c)
        {
            return (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9');
        }

        private static bool TryResolve(string token, SpellTemplate spell, ICombatUnit caster, out string value)
        {
            // $DUR (case-insensitive)
            if (EqualsIgnoreCase(token, "DUR") || EqualsIgnoreCase(token, "d"))
            {
                value = FormatDuration(SpellUtils.CalculateDuration(spell, caster));
                return true;
            }

            // $E<N>min | $E<N>max | $E<N>D<K> | $E<N>
            if ((token[0] == 'E' || token[0] == 'e') && token.Length >= 2 && token[1] >= '1' && token[1] <= '3')
            {
                int slot = token[1] - '0';
                SpellTemplateEffect eff = spell.GetEffect(slot);

                if (token.Length == 2)
                {
                    int min = SpellUtils.CalculateEffectValueMin(eff, caster);
                    int max = SpellUtils.CalculateEffectValueMax(eff, caster);
                    // Negative Aura-Magnitudes (z.B. -50% Cooldown) im Tooltip
                    // als Absolutwert anzeigen &#8212; der Vorzeichen-Kontext
                    // ist im Beschreibungstext ("Reduces ... by") enthalten.
                    int avg = (min + max) / 2;
                    value = System.Math.Abs(avg).ToString();
                    return true;
                }
                string suffix = token.Substring(2);
                if (EqualsIgnoreCase(suffix, "min"))
                {
                    value = System.Math.Abs(SpellUtils.CalculateEffectValueMin(eff, caster)).ToString();
                    return true;
                }
                if (EqualsIgnoreCase(suffix, "max"))
                {
                    value = System.Math.Abs(SpellUtils.CalculateEffectValueMax(eff, caster)).ToString();
                    return true;
                }
                // $E<N>D<K> (K = 1..3) -> ungescalter Rohwert
                if ((suffix[0] == 'D' || suffix[0] == 'd') && suffix.Length == 2
                    && suffix[1] >= '1' && suffix[1] <= '3')
                {
                    long raw = suffix[1] switch
                    {
                        '1' => eff.Data1,
                        '2' => eff.Data2,
                        '3' => eff.Data3,
                        _ => 0L,
                    };
                    value = raw.ToString();
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool EqualsIgnoreCase(string a, string b)
        {
            if (a.Length != b.Length) { return false; }
            for (int i = 0; i < a.Length; i++)
            {
                char ca = a[i];
                char cb = b[i];
                if (ca >= 'A' && ca <= 'Z') { ca = (char)(ca + 32); }
                if (cb >= 'A' && cb <= 'Z') { cb = (char)(cb + 32); }
                if (ca != cb) { return false; }
            }
            return true;
        }

        /// <summary>
        /// Formatiert Millisekunden in WoW-Stil: &quot;X sec&quot; (1..59s),
        /// &quot;Y min&quot; (&gt;=60s). 0 oder negativ liefert &quot;0 sec&quot; (selten genutzt,
        /// weil <c>$DUR</c>-Tokens nur in Auren auftauchen).
        /// </summary>
        private static string FormatDuration(int durationMs)
        {
            if (durationMs <= 0) { return "0 sec"; }
            int totalSec = Mathf.Max(1, Mathf.RoundToInt(durationMs / 1000f));
            if (totalSec < 60)
            {
                return totalSec + " sec";
            }
            int min = totalSec / 60;
            int rem = totalSec % 60;
            return rem == 0 ? (min + " min") : (min + " min " + rem + " sec");
        }
    }
}
