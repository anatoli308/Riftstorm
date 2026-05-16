using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// FLARE-Richtungs-Hilfsfunktionen. Konvention nach FLARE-Engine:
    /// Index 0 = West, 1 = Südwest, 2 = Süd, 3 = Südost, 4 = Ost, 5 = Nordost, 6 = Nord, 7 = Nordwest.
    /// </summary>
    public static class FlareDirection
    {
        /// <summary>Anzahl FLARE-Richtungen (immer 8).</summary>
        public const int Count = 8;

        /// <summary>
        /// Wandelt einen 2D-Bewegungsvektor (x = rechts, y = oben) in den FLARE-Richtungsindex 0..7.
        /// Liefert -1, wenn der Vektor näherungsweise null ist.
        /// </summary>
        public static int FromVector(Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f)
            {
                return -1;
            }
            // Winkel in Grad, 0° = Osten, gegen den Uhrzeigersinn.
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (angle < 0f)
            {
                angle += 360f;
            }
            // 8 Sektoren à 45°, Mittelpunkt auf der Kardinalrichtung.
            int sector = Mathf.RoundToInt(angle / 45f) & 7;
            // sector: 0=E, 1=NE, 2=N, 3=NW, 4=W, 5=SW, 6=S, 7=SE   (mathematischer Kreis)
            // FLARE:  0=W, 1=SW, 2=S, 3=SE, 4=E,  5=NE, 6=N, 7=NW
            // Mapping: flare = (sector + 4) & 7
            return (sector + 4) & 7;
        }
    }
}
