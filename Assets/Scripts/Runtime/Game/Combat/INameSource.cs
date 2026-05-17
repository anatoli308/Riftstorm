using System;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Minimaler Vertrag f&#252;r alles, was einen Anzeigenamen liefert (Spieler, NPC, Pet,
    /// Boss). Bewusst Netcode-frei: die Quelle entscheidet selbst, ob der Name aus einer
    /// <c>NetworkVariable</c> kommt (Spieler) oder lokal aus dem geladenen Stat-Block
    /// (NPC). Die Konsumenten (Nametag, FloatingCombatText, Logger) sehen nur einen
    /// <see cref="DisplayName"/> + Change-Event.
    /// </summary>
    public interface INameSource
    {
        /// <summary>Aktueller Anzeigename. Kann leer sein, solange noch nicht resolved.</summary>
        string DisplayName { get; }

        /// <summary>Wird ausgel&#246;st, sobald sich <see cref="DisplayName"/> &#228;ndert
        /// (auch f&#252;r das initiale Setzen bei Spawn).</summary>
        event Action<string> DisplayNameChanged;
    }
}
