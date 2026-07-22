namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Logischer Combat-/Animationszustand eines Spielers oder NPCs.
    /// Bildet die Teilmenge der FLARE-Animationen ab, die in Riftstorm tatsächlich
    /// als Player-Sprites vorliegen (siehe <c>referenzen/04-animationen-combat.md</c>).
    /// </summary>
    /// <remarks>
    /// Reihenfolge / Werte sind <b>nicht</b> identisch zum Original-C++-<c>UnitDefines::AnimId</c>;
    /// dort sind Stance=0, Run=1, Attack=2 etc. Hier nur die in Player-Atlanten vorhandenen
    /// Sektionen, plus <see cref="None"/> als Default.
    /// </remarks>
    /// TODO: verwende UnitDefines::AnimId als Basis, dazu existier ein UnitAnimation Enum von mir. 
    /// das ist 1:1 mit den FLARE-Animationen, und dann kann man das hier auf die Riftstorm-Player-Sprites abbilden/ersetzen.
    public enum CombatAnim
    {
        /// <summary>Kein Zustand gesetzt (Default vor Initialisierung).</summary>
        None = 0,
        /// <summary>Idle / Stehen. FLARE-Section <c>[stance]</c>, Loop.</summary>
        Stance = 1,
        /// <summary>Laufen. FLARE-Section <c>[run]</c>, Loop.</summary>
        Run = 2,
        /// <summary>Nahkampfschlag. FLARE-Section <c>[swing]</c>, PlayOnce.</summary>
        Swing = 3,
        /// <summary>Fernkampfschuss. FLARE-Section <c>[shoot]</c>, PlayOnce.</summary>
        Shoot = 4,
        /// <summary>Zauberspruch wirken. FLARE-Section <c>[cast]</c>, PlayOnce.</summary>
        Cast = 5,
        /// <summary>Schildblock halten. FLARE-Section <c>[block]</c>, Loop.</summary>
        Block = 6,
        /// <summary>Treffer empfangen. FLARE-Section <c>[hit]</c>, PlayOnce.</summary>
        Hit = 7,
        /// <summary>Tod. FLARE-Section <c>[die]</c>, PlayOnce, terminaler Zustand.</summary>
        Die = 8,
    }
}
