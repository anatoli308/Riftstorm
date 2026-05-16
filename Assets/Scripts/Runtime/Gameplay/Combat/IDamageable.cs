namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Vertrag für alles, was Schaden nehmen kann (Spieler, NPCs, zerstörbare
    /// Objekte). Wird ausschließlich server-seitig aufgerufen — die
    /// Implementierung ist verantwortlich für Fan-Out an Clients (z. B. via
    /// <c>ClientRpc</c> für Floating-Text / Hit-Reactions).
    /// </summary>
    public interface IDamageable
    {
        /// <summary>True, wenn das Ziel bereits tot ist und keinen weiteren Schaden mehr akzeptiert.</summary>
        bool IsDead { get; }

        /// <summary>
        /// Wendet einen vom <see cref="CombatFormulas"/> vorbereiteten Schaden an.
        /// Darf nur auf dem Server aufgerufen werden — Implementierungen müssen
        /// das selbst prüfen.
        /// </summary>
        void ApplyDamage(in DamageInfo info);
    }
}
