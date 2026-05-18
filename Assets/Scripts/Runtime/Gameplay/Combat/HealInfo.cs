namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Ergebnis einer Heilungsberechnung. Wird vom <see cref="CombatFormulas"/>
    /// erzeugt und vom <c>SpellExecutor</c> auf das Ziel (<see cref="ICombatUnit.Heal"/>)
    /// angewendet.
    ///
    /// <para>
    /// Spiegelt 1:1 das C++-<c>HealInfo</c>-Struct aus
    /// <c>source_server/Server/src/Combat/CombatFormulas.h</c>.
    /// </para>
    /// <para>
    /// Bewusst ein plain-struct ohne <c>INetworkSerializable</c>, damit die
    /// Gameplay-Assembly keine Netcode-Referenz braucht. Fan-Out an Clients
    /// erfolgt auf der Target-Seite über primitive Felder.
    /// </para>
    /// </summary>
    public struct HealInfo
    {
        /// <summary>Roh-Heilbetrag vor Crit/Variance (z. B. Effect-Data1 + Caster-Bonus).</summary>
        public int BaseHeal;

        /// <summary>Heilung, die tatsächlich auf die Ziel-HP addiert wird (nach Cap, ohne Overheal).</summary>
        public int FinalHeal;

        /// <summary>Überschüssige Heilung über <c>MaxHp</c> hinaus (verfällt).</summary>
        public int Overheal;

        /// <summary>Hit-Ergebnis (Hit, Crit) — Heals dodgen/parrien nicht.</summary>
        public HitResult HitResult;

        /// <summary>True, wenn das Ziel nach diesem Heal auf Vollheilung gebracht wurde.</summary>
        public bool FullHeal;
    }
}
