using Riftstorm.Game.UI;
using Tolik.Riftstorm.Runtime.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Initialisiert <see cref="SelectionIndicatorAssets"/> aus
    /// <see cref="HudConfig"/> direkt nach dem Szenen-Load. Laeuft per
    /// <c>[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]</c> nach allen
    /// Awakes / OnEnables (also auch nach <c>ApplicationEntryPoint.Awake</c>,
    /// in dem der <c>TextureManager</c> im <c>ServiceLocator</c> registriert
    /// wird) und vor dem ersten <c>Start()</c> aller Szenen-Objekte —
    /// genau in dem Fenster, das <see cref="HitboxIndicator"/> nutzt, um
    /// seine Visualisierung lazy in <c>Start()</c> aufzubauen.
    /// </summary>
    /// <remarks>
    /// Bewusst in der <c>Riftstorm.Game</c>-Assembly, damit
    /// <see cref="HudConfigLoader"/> zugreifbar ist. Das Zielfeld liegt in
    /// <c>Riftstorm.Gameplay</c>, wo der <see cref="HitboxIndicator"/> es
    /// ohne Cross-Assembly-Reference auf <c>Riftstorm.Game</c> auslesen kann.
    /// </remarks>
    internal static class SelectionIndicatorBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Populate()
        {
            HudConfig cfg = HudConfigLoader.Load();
            SelectionIndicatorAssets.Scale = cfg.selectionIndicatorScale > 0f ? cfg.selectionIndicatorScale : 1f;
            SelectionIndicatorAssets.Texture = string.IsNullOrEmpty(cfg.selectionIndicatorTexture)
                ? null
                : HudConfigLoader.LoadTextureOrNull(cfg.selectionIndicatorTexture);
        }
    }
}
