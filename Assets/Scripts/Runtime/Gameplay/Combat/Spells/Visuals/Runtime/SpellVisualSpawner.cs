using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Statischer Spawn-Helper für <see cref="WorldSpellAnimation"/>s und
    /// <see cref="SpellVisualPhase"/>-Boden-Effekte. Erzeugt leichte
    /// GameObject-Hierarchien (Root + bis zu 2 Sprite-Layer + optionale Glow-
    /// Light), startet Animationen + Sound und ueberlaesst das Aufraeumen dem
    /// erzeugten GameObject selbst.
    /// </summary>
    /// <remarks>
    /// Vermeidet Prefab-Abhaengigkeiten &#8212; der ClientRpc-Handler in
    /// <c>PlayerCombat</c> ruft schlicht <see cref="Spawn"/> /
    /// <see cref="SpawnGround"/> auf. Pooling kann spaeter als Drop-in-Ersatz
    /// hinzukommen, ohne Aufrufer zu aendern.
    /// </remarks>
    public static class SpellVisualSpawner
    {
        /// <summary>Source-Pixel pro Welt-Einheit (FLARE-Konvention).</summary>
        public const float SourcePixelsPerUnit = 64f;

        private const int k_GroundPrimarySortingOrder = 50;
        private const int k_GroundSecondaryTopSortingOrder = 51;
        private const int k_GroundSecondaryBottomSortingOrder = 49;
        private const float k_GlowLightBaseIntensity = 1.5f;
        private const float k_GlowLightRange = 3f;

        private static readonly HashSet<string> s_LoggedParticleSystems = new();

        /// <summary>
        /// Spawnt ein Visual für <paramref name="kit"/> und startet sofort
        /// die Casting-Phase. Liefert die erzeugte Komponente (oder
        /// <c>null</c>, wenn <paramref name="kit"/> komplett leer ist).
        /// </summary>
        /// <param name="kit">Per-Spell-Visual-Plan (typisch aus dem
        ///   <see cref="SpellVisualResolver"/>).</param>
        /// <param name="anims">Animations-Katalog zur Namensaufloesung.</param>
        /// <param name="source">Caster-Transform (nicht <c>null</c>).</param>
        /// <param name="target">Ziel-Transform; bei <c>null</c> wird Self-Target am Caster gespielt.</param>
        /// <param name="particles">Optionaler Partikel-Katalog zum Aufloesen der
        ///   Phasen-Partikelsysteme (Travel/Impact). <c>null</c> = nur Sprites + Sound.</param>
        public static WorldSpellAnimation Spawn(
            SpellVisualDefinition kit,
            SpellAnimationCatalog anims,
            Transform source,
            Transform target,
            ParticleSystemCatalog particles = null)
        {
            if (kit == null || !kit.HasAny || source == null)
            {
                return null;
            }

            GameObject go = new("SpellVisual_" + (kit.SpellId ?? "unknown"));
            go.transform.position = source.position;

            WorldSpellAnimation world = go.AddComponent<WorldSpellAnimation>();
            world.Play(kit, anims, source, target, particles);
            return world;
        }

        /// <summary>
        /// Spawnt ein gerichtetes Skillshot-Visual: das Projektil fliegt
        /// geradlinig vom <paramref name="source"/> in Richtung des festen
        /// Welt-Zielpunkts <paramref name="worldTarget"/> (Cursor-Richtung),
        /// unabhaengig von einem Unit-Transform. Spiegelt das server-seitige
        /// gerichtete <c>ServerProjectile</c>.
        /// </summary>
        /// <param name="kit">Per-Spell-Visual-Plan (typisch aus dem
        ///   <see cref="SpellVisualResolver"/>).</param>
        /// <param name="anims">Animations-Katalog zur Namensaufloesung.</param>
        /// <param name="source">Caster-Transform (nicht <c>null</c>).</param>
        /// <param name="worldTarget">Fester Welt-Zielpunkt (Travel-Ende/Impact).</param>
        /// <param name="particles">Optionaler Partikel-Katalog zum Aufloesen der
        ///   Phasen-Partikelsysteme (Travel/Impact). <c>null</c> = nur Sprites + Sound.</param>
        public static WorldSpellAnimation SpawnDirectional(
            SpellVisualDefinition kit,
            SpellAnimationCatalog anims,
            Transform source,
            Vector3 worldTarget,
            ParticleSystemCatalog particles = null)
        {
            if (kit == null || !kit.HasAny || source == null)
            {
                return null;
            }

            GameObject go = new("SpellVisual_" + (kit.SpellId ?? "unknown"));
            go.transform.position = source.position;

            WorldSpellAnimation world = go.AddComponent<WorldSpellAnimation>();
            world.PlayDirectional(kit, anims, source, worldTarget, particles);
            return world;
        }

        /// <summary>
        /// Spawnt eine Boden-Phase (FLARE <c>go_kit</c>) an einer festen
        /// Welt-Position. Im Gegensatz zu <see cref="Spawn"/> ist diese Phase
        /// nicht an Caster/Target gebunden &#8212; sie bleibt stationaer am
        /// Cast-Destinationspunkt (z. B. Eis-Patch bei Ice Blast). Renderpfad:
        /// Root-GO + bis zu 2 Sprite-Layer (Primary/Secondary) + optionales
        /// Glow-Light + Phase-Sound.
        /// </summary>
        /// <param name="phase">Aufgeloeste Boden-Phase (<see cref="SpellVisualDefinition.Ground"/>).</param>
        /// <param name="anims">Animations-Katalog zur Namensaufloesung.</param>
        /// <param name="groundPoint">Welt-Position, an der die Phase platziert wird.</param>
        /// <param name="lifetimeSeconds">Lebensdauer in Sekunden. &gt; 0 = Loop fuer diese
        ///   Dauer, dann Destroy. &lt;= 0 = One-Shot (Anim laeuft einmal, dann Destroy).</param>
        /// <param name="particles">Optionaler Partikel-Katalog zum Aufloesen des
        ///   Boden-Partikelsystems (<see cref="SpellVisualPhase.ParticleSystemName"/>).
        ///   <c>null</c> = nur Sprites + Glow + Sound (Partikel werden geloggt-uebersprungen).</param>
        /// <returns>Das erzeugte Root-GameObject oder <c>null</c>, wenn nichts gespawnt wurde.</returns>
        public static GameObject SpawnGround(
            SpellVisualPhase phase,
            SpellAnimationCatalog anims,
            Vector3 groundPoint,
            float lifetimeSeconds,
            ParticleSystemCatalog particles = null)
        {
            if (phase == null || !phase.HasAny || anims == null)
            {
                return null;
            }

            string rootName = !string.IsNullOrEmpty(phase.PrimaryAnim)
                ? "SpellGroundVisual_" + phase.PrimaryAnim
                : "SpellGroundVisual";
            GameObject root = new(rootName);
            root.transform.position = groundPoint;
            // Boden-Phase flach auf den Boden legen (Topdown: Sprite blickt
            // nach oben). Wenn der Camera-Setup spaeter Billboarding fuer
            // Ground erfordert, kann hier eine eigene Komponente nachruesten.
            root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            bool primaryLoops = false;
            SpellAnimationPlayer primaryPlayer = null;

            if (phase.HasPrimary
                && anims.TryGet(phase.PrimaryAnim, out SpellAnimationDefinition primaryAnim)
                && primaryAnim != null)
            {
                primaryPlayer = CreateSpriteLayer(
                    root.transform,
                    "Primary",
                    phase.EffectivePrimaryOffsetPx(primaryAnim.CanvasSize),
                    phase.PrimaryTint,
                    phase.PrimaryBlend,
                    k_GroundPrimarySortingOrder);
                primaryLoops = lifetimeSeconds > 0f && primaryAnim.HasLoop;
                primaryPlayer.Play(primaryAnim, primaryLoops);
            }

            if (phase.HasSecondary
                && anims.TryGet(phase.SecondaryAnim, out SpellAnimationDefinition secondaryAnim)
                && secondaryAnim != null)
            {
                int sortingOrder = phase.SecondaryTopmost
                    ? k_GroundSecondaryTopSortingOrder
                    : k_GroundSecondaryBottomSortingOrder;
                SpellAnimationPlayer secondaryPlayer = CreateSpriteLayer(
                    root.transform,
                    "Secondary",
                    phase.EffectiveSecondaryOffsetPx(secondaryAnim.CanvasSize),
                    phase.SecondaryTint,
                    phase.SecondaryBlend,
                    sortingOrder);
                bool secondaryLoops = lifetimeSeconds > 0f && secondaryAnim.HasLoop;
                secondaryPlayer.Play(secondaryAnim, secondaryLoops);
            }

            if (phase.GroundGlowColor.a > 0f)
            {
                CreateGlowLight(root.transform, phase.GroundGlowColor);
            }

            PlayPhaseSound(phase, groundPoint);
            TrySpawnPhaseParticles(phase, root.transform, particles);

            if (lifetimeSeconds > 0f)
            {
                Object.Destroy(root, lifetimeSeconds);
            }
            else if (primaryPlayer != null && !primaryLoops)
            {
                // One-Shot: nach Anim-Ende selbst zerstoeren.
                primaryPlayer.OnFinished = () => Object.Destroy(root);
            }
            else
            {
                // Fallback: ohne Lifetime & ohne primaeren Anim-Trigger sofort
                // ein Sicherheitsnetz von 5s setzen, damit nichts ewig lebt.
                Object.Destroy(root, 5f);
            }
            return root;
        }

        // ---- Layer/Light/Sound Helpers --------------------------------

        /// <summary>Erzeugt ein Sprite-Layer-Child mit Renderer + Player.</summary>
        public static SpellAnimationPlayer CreateSpriteLayer(
            Transform parent,
            string name,
            Vector2 offsetPx,
            Color tint,
            SpellVisualBlend blend,
            int sortingOrder)
        {
            GameObject layer = new(name);
            layer.transform.SetParent(parent, worldPositionStays: false);
            // FLARE-Offsets sind Pixel (X rechts, Y abwaerts). Unity-Y zeigt
            // nach oben &#8594; Y invertieren.
            layer.transform.localPosition = new Vector3(
                offsetPx.x / SourcePixelsPerUnit,
                -offsetPx.y / SourcePixelsPerUnit,
                0f);
            layer.transform.localRotation = Quaternion.identity;

            SpriteRenderer renderer = layer.AddComponent<SpriteRenderer>();
            renderer.color = tint;
            renderer.sortingOrder = sortingOrder;
            Material mat = SpellMaterialCache.Get(blend);
            if (mat != null)
            {
                renderer.sharedMaterial = mat;
            }

            return layer.AddComponent<SpellAnimationPlayer>();
        }

        /// <summary>Erzeugt ein Point-Light-Child fuer den FLARE-Glow.</summary>
        public static Light CreateGlowLight(Transform parent, Color glow)
        {
            GameObject lightGo = new("Glow");
            lightGo.transform.SetParent(parent, worldPositionStays: false);
            lightGo.transform.localPosition = Vector3.zero;

            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(glow.r, glow.g, glow.b, 1f);
            light.range = k_GlowLightRange;
            light.intensity = k_GlowLightBaseIntensity * Mathf.Clamp01(glow.a);
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>Spielt den Phase-Sound an <paramref name="worldPosition"/>.</summary>
        public static void PlayPhaseSound(SpellVisualPhase phase, Vector3 worldPosition)
        {
            if (phase == null || string.IsNullOrEmpty(phase.SoundFile))
            {
                return;
            }
            System.Func<string, AudioClip> resolver = SpellVisualAudioHook.ClipResolver;
            if (resolver == null)
            {
                return;
            }
            AudioClip clip = resolver(phase.SoundFile);
            if (clip == null)
            {
                return;
            }
            AudioSource.PlayClipAtPoint(clip, worldPosition);
        }

        /// <summary>
        /// Spawnt das Phasen-Partikelsystem (<see cref="SpellVisualPhase.ParticleSystemName"/>)
        /// am <paramref name="anchor"/> (folgt dem bewegten Visual-Root bzw. liegt am
        /// Boden-Punkt). Resolved den <c>.psi</c>-Namen ueber
        /// <see cref="ParticleSystemCatalog.StripPsi"/> + <see cref="ParticleSystemCatalog.TryGet"/>
        /// und delegiert das eigentliche Erzeugen an <see cref="CasterParticleSpawner.Spawn"/>.
        /// Stilles No-Op, wenn die Phase kein Partikelsystem traegt. Faellt auf die
        /// einmalige Log-Warnung zurueck, wenn kein <paramref name="particles"/>-Katalog
        /// vorliegt (Aufrufer ohne Partikel-Loader).
        /// </summary>
        /// <param name="phase">Visual-Phase mit optionalem Partikelsystem-Namen.</param>
        /// <param name="anchor">Transform, an das das Partikelsystem gehaengt wird.</param>
        /// <param name="particles">Partikel-Katalog zum Aufloesen; <c>null</c> = Log-Fallback.</param>
        public static void TrySpawnPhaseParticles(
            SpellVisualPhase phase,
            Transform anchor,
            ParticleSystemCatalog particles)
        {
            if (phase == null || string.IsNullOrEmpty(phase.ParticleSystemName))
            {
                return;
            }
            if (particles == null || anchor == null)
            {
                WarnParticleSystemOnce(phase);
                return;
            }
            string psName = ParticleSystemCatalog.StripPsi(phase.ParticleSystemName);
            if (!particles.TryGet(psName, out ParticleSystemDefinition def) || def == null)
            {
                return;
            }
            CasterParticleSpawner.Spawn(def, anchor, worldYOffset: 0f);
        }

        /// <summary>Loggt einmalig pro Partikelsystem-Name eine Warnung (kein Partikel-Katalog verfuegbar).</summary>
        private static void WarnParticleSystemOnce(SpellVisualPhase phase)
        {
            if (phase == null || string.IsNullOrEmpty(phase.ParticleSystemName))
            {
                return;
            }
            if (s_LoggedParticleSystems.Add(phase.ParticleSystemName))
            {
                Debug.LogWarning(
                    $"[SpellVisualSpawner] ParticleSystem '{phase.ParticleSystemName}' is set on a "
                    + "spell visual phase but the Unity particle pipeline is not wired yet. "
                    + "Sprite layers + sound are spawned; particles are skipped.");
            }
        }
    }
}
