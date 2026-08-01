using Riftstorm.Game.Movement;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// FLARE-Richtungs-Hilfsfunktion. Die kanonische Richtung ist
    /// <see cref="UnitDirection"/>; diese Methode liefert nur den FLARE-Atlas-
    /// Zeilenindex und delegiert die Berechnung an <see cref="UnitDirectionUtil"/>,
    /// damit es keine zweite „Korrektur"-Formel gibt.
    /// </summary>
    public static class FlareDirection
    {
        /// <summary>Anzahl FLARE-Richtungen (immer 8).</summary>
        public const int Count = 8;

        /// <summary>
        /// Wandelt einen 2D-Bewegungsvektor (x = rechts, y = oben) in den FLARE-
        /// Richtungsindex 0..7. Liefert -1, wenn der Vektor näherungsweise null ist.
        /// </summary>
        public static int FromVector(Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f)
            {
                return -1;
            }
            return UnitDirectionUtil.ToFlareIndex(UnitDirectionUtil.FromMovement(dir));
        }
    }
}
