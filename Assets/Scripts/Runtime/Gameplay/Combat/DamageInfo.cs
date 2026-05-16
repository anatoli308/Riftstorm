namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Ergebnis einer Schadensberechnung. Wird vom <see cref="CombatFormulas"/>
    /// erzeugt und vom Opfer (<see cref="IDamageable"/>) angewendet.
    ///
    /// <para>
    /// Bewusst ein plain-struct ohne <c>INetworkSerializable</c>, damit die
    /// Gameplay-Assembly keine Netcode-Referenz braucht. Für Fan-Out an Clients
    /// werden auf der Victim-Seite primitive Felder via <c>ClientRpc</c>
    /// verteilt.
    /// </para>
    /// </summary>
    public struct DamageInfo
    {
        /// <summary>Schaden vor Armor/Resists/Variance (Roh-Wert aus Waffe + Strength).</summary>
        public int BaseDamage;

        /// <summary>Schaden, der tatsächlich vom Opfer-HP abgezogen wird.</summary>
        public int FinalDamage;

        /// <summary>Durch Armor reduzierter Anteil (BaseDamage − FinalDamage − sonstige).</summary>
        public int Absorbed;

        /// <summary>Hit-Ergebnis (Hit, Crit, Miss, Dodge, …).</summary>
        public HitResult HitResult;

        /// <summary>FinalDamage − verbleibendes HP, falls letaler Treffer.</summary>
        public int Overkill;

        /// <summary>True, wenn dieser Hit das Opfer getötet hat (server-only ausgewertet).</summary>
        public bool KilledTarget;
    }
}
