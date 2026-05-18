using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Statischer Spawn-Helper für <see cref="WorldSpellAnimation"/>s.
    /// Erzeugt ein leeres GameObject mit <see cref="SpriteRenderer"/> +
    /// <see cref="SpellAnimationPlayer"/> + <see cref="WorldSpellAnimation"/>,
    /// startet die Sequenz und überlässt das Aufräumen dem
    /// <see cref="WorldSpellAnimation"/> selbst (Destroy nach
    /// <see cref="WorldSpellAnimation.Phase.Done"/>).
    /// </summary>
    /// <remarks>
    /// Vermeidet Prefab-Abhängigkeiten — der ClientRpc-Handler in
    /// <c>PlayerCombat</c> ruft schlicht <see cref="Spawn"/> auf. Pooling
    /// kann später als Drop-in-Ersatz hinzukommen, ohne Aufrufer zu ändern.
    /// </remarks>
    public static class SpellVisualSpawner
    {
        /// <summary>
        /// Spawnt ein Visual für <paramref name="kit"/> und startet sofort
        /// die Casting-Phase. Liefert die erzeugte Komponente (oder
        /// <c>null</c>, wenn <paramref name="kit"/> komplett leer ist).
        /// </summary>
        /// <param name="kit">Per-Spell-Visual-Kit (typisch aus dem <see cref="SpellVisualCatalog"/>).</param>
        /// <param name="anims">Animations-Katalog zur Namensauflösung.</param>
        /// <param name="source">Caster-Transform (nicht <c>null</c>).</param>
        /// <param name="target">Ziel-Transform; bei <c>null</c> wird Self-Target am Caster gespielt.</param>
        public static WorldSpellAnimation Spawn(
            SpellVisualDefinition kit,
            SpellAnimationCatalog anims,
            Transform source,
            Transform target)
        {
            if (kit == null || !kit.HasAny || source == null)
            {
                return null;
            }

            GameObject go = new("SpellVisual_" + (kit.SpellId ?? "unknown"));
            go.transform.position = source.position;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 100;

            go.AddComponent<SpellAnimationPlayer>();
            WorldSpellAnimation world = go.AddComponent<WorldSpellAnimation>();
            world.Play(kit, anims, source, target);
            return world;
        }
    }
}
