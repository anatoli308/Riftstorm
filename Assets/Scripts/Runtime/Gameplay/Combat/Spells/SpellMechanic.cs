using System;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Crowd-Control- / Effekt-Mechanik. Bitmask, damit ein Unit
    /// mehrere Mechaniken gleichzeitig tragen oder gegen mehrere immun sein kann.
    /// </summary>
    /// <remarks>
    /// Subset aus <c>SpellDefines.h::Mechanics</c>. JSON-seitig wird das als
    /// Komma-getrennter String oder als Integer-Bitmask gespeichert (Newtonsoft
    /// kann beides). Werte sind <b>nicht</b> deckungsgleich mit dem Original,
    /// dafür aufgeräumt und MOBA-relevant.
    /// </remarks>
    [Flags]
    public enum SpellMechanic
    {
        /// <summary>Keine Mechanik.</summary>
        None = 0,
        /// <summary>Stun (kein Movement, kein Cast).</summary>
        Stun = 1 << 0,
        /// <summary>Root (kein Movement, Casts erlaubt).</summary>
        Root = 1 << 1,
        /// <summary>Silence (keine Casts, Movement erlaubt).</summary>
        Silence = 1 << 2,
        /// <summary>Fear (laufen panisch weg, kein Cast).</summary>
        Fear = 1 << 3,
        /// <summary>Sleep (CC bis Schaden).</summary>
        Sleep = 1 << 4,
        /// <summary>Snare / Slow (reduziertes Movement).</summary>
        Snare = 1 << 5,
        /// <summary>Polymorph (Modell-Tausch + CC).</summary>
        Polymorph = 1 << 6,
        /// <summary>Pacify (Auto-Attacks aus, Casts erlaubt).</summary>
        Pacify = 1 << 7,
        /// <summary>Charm (anderer Spieler / KI steuert das Unit).</summary>
        Charm = 1 << 8,
        /// <summary>Disorient (zufällige Move-Richtung).</summary>
        Disorient = 1 << 9,
        /// <summary>Disarm (keine Auto-Attacks, ggf. keine Weapon-Spells).</summary>
        Disarm = 1 << 10,
        /// <summary>Knockback / Knock-Up (kurze Anim-Lock-Phase).</summary>
        Knockback = 1 << 11,
        /// <summary>Charging-Phase (Charge-Spell läuft, blockiert andere Movement-Calls).</summary>
        Charging = 1 << 12,
    }
}
