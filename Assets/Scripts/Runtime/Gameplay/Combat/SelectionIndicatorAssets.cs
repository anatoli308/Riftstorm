using UnityEngine;

namespace Tolik.Riftstorm.Runtime.Gameplay.Combat
{
    /// <summary>
    /// Statischer Asset-Provider fuer den Selection-Indicator (Boden-Quad um
    /// gelockte Einheiten). Bewusst hier in der <c>Riftstorm.Gameplay</c>-
    /// Assembly, damit <see cref="HitboxIndicator"/> die Textur lesen kann,
    /// ohne den hoeherliegenden <c>HudConfigLoader</c> aus <c>Riftstorm.Game</c>
    /// zu referenzieren (asmdef-Zyklen-Schutz).
    /// </summary>
    /// <remarks>
    /// Befuellt wird das Pair von einer Init-Routine in der Game-Assembly
    /// (siehe <c>SelectionIndicatorBootstrap</c>) via
    /// <c>[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]</c> — also bevor
    /// <c>Start()</c> auf Szenen-Objekten laeuft. Bleibt <see cref="Texture"/>
    /// <c>null</c> (z. B. fehlende PNG), faellt <c>HitboxIndicator</c>
    /// automatisch auf den vektorbasierten <c>LineRenderer</c>-Kreis zurueck.
    /// </remarks>
    public static class SelectionIndicatorAssets
    {
        /// <summary>Geladene Textur fuer das Indicator-Quad. <c>null</c> = Fallback aktiv.</summary>
        public static Texture2D Texture;

        /// <summary>Skalierungsfaktor relativ zum Hit-Durchmesser. Default 1 = exakt am Hitradius.</summary>
        public static float Scale = 1f;
    }
}
