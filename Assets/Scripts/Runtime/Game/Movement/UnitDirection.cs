using UnityEngine;

namespace Riftstorm.Game.Movement
{
    /// <summary>
    /// Kanonisches 8-Richtungs-System fuer Unit-Facing/Bewegung — 1:1 aus
    /// <c>source_server/Shared/UnitDefines.h</c> (<c>UnitDefines::Direction</c>),
    /// im Uhrzeigersinn ab Sued. Dies ist die <b>Gameplay-Wahrheit</b>; die
    /// Umrechnung auf den FLARE-Atlas-Zeilenindex passiert ausschliesslich zentral
    /// in <see cref="UnitDirectionUtil"/>.
    /// </summary>
    public enum UnitDirection
    {
        South = 0,
        SouthWest = 1,
        West = 2,
        NorthWest = 3,
        North = 4,
        NorthEast = 5,
        East = 6,
        SouthEast = 7,
    }

    /// <summary>
    /// Zentrale Umrechnungen fuer <see cref="UnitDirection"/>. Haelt die
    /// <b>einzige</b> Stelle, an der die kanonische UnitDefines-Richtung auf den
    /// FLARE-Atlas-Zeilenindex abgebildet wird (FLARE-Reihenfolge:
    /// 0=West, 1=SW, 2=Sued, 3=SO, 4=Ost, 5=NO, 6=Nord, 7=NW). Damit entfallen
    /// verstreute „+4 &amp; 7"-Korrekturen im Gameplay-Code.
    /// </summary>
    public static class UnitDirectionUtil
    {
        /// <summary>FLARE-Atlas-Zeilenindex je <see cref="UnitDirection"/>-Wert.</summary>
        private static readonly int[] k_FlareByUnit = { 2, 1, 0, 7, 6, 5, 4, 3 };

        /// <summary><see cref="UnitDirection"/> je FLARE-Atlas-Zeilenindex (Inverse).</summary>
        private static readonly UnitDirection[] k_UnitByFlare =
        {
            UnitDirection.West,      // FLARE 0
            UnitDirection.SouthWest, // FLARE 1
            UnitDirection.South,     // FLARE 2
            UnitDirection.SouthEast, // FLARE 3
            UnitDirection.East,      // FLARE 4
            UnitDirection.NorthEast, // FLARE 5
            UnitDirection.North,     // FLARE 6
            UnitDirection.NorthWest, // FLARE 7
        };

        /// <summary>
        /// Mappt die kanonische Richtung auf den FLARE-Atlas-Zeilenindex (0..7).
        /// Einzige Konvertierstelle Gameplay → Sprite.
        /// </summary>
        public static int ToFlareIndex(UnitDirection dir)
        {
            int i = (int)dir;
            return (uint)i < (uint)k_FlareByUnit.Length ? k_FlareByUnit[i] : k_FlareByUnit[0];
        }

        /// <summary>Inverse zu <see cref="ToFlareIndex"/>: FLARE-Index → kanonische Richtung.</summary>
        public static UnitDirection FromFlareIndex(int flareIndex)
        {
            return k_UnitByFlare[flareIndex & 7];
        }

        /// <summary>
        /// Bestimmt die kanonische Blickrichtung aus einem 2D-Vektor
        /// (<c>x</c> = Ost/rechts, <c>y</c> = Nord/oben). Nahe-Null-Vektoren
        /// liefern <see cref="UnitDirection.South"/> (Topdown-Default).
        /// </summary>
        public static UnitDirection FromMovement(Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f)
            {
                return UnitDirection.South;
            }
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg; // Ost=0°, Nord=+90°
            // UnitDefines laeuft im Uhrzeigersinn ab Sued: idx = round((270-angle)/45).
            int idx = Mathf.RoundToInt((270f - angle) / 45f) & 7;
            return (UnitDirection)idx;
        }
    }
}