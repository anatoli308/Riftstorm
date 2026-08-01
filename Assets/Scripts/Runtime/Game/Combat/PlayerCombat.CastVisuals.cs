using System.Threading;
using Riftstorm.Game.Combat.CombatStates;
using Riftstorm.Game.Input;
using Riftstorm.Game.Items;
using Riftstorm.Game.Movement;
using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using Riftstorm.Gameplay.Combat.Spells.Visuals;
using Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime;
using Riftstorm.Management.SoundManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Tolik.Riftstorm.Runtime.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    public sealed partial class PlayerCombat
    {
        /// <summary>
        /// Client-seitiger Visual-Handler. Loest aus den Source-Tabellen
        /// <c>spell_visual_kit</c> (per-Spell Kit-IDs, <c>_visuals.json</c>) und
        /// <c>spell_visual</c> (Kit-Definitionen, <c>_visual_kits.json</c>) eine
        /// Phasen-Anim auf (Casting, Travel, Impact, Aura) und spawnt den
        /// <see cref="WorldSpellAnimation"/> ueber <see cref="SpellVisualSpawner"/>.
        /// Wenn der Cast eine Boden-Destination hat
        /// (<paramref name="hasGroundPoint"/> = true) und das Visual-Kit ein
        /// <c>go_kit</c> definiert, wird zusaetzlich eine Ground-Animation
        /// an <paramref name="groundPoint"/> platziert (FLARE-style Eis-Patch
        /// bei Spell 30 / Ice Blast). <paramref name="groundDurationMs"/>
        /// entspricht der server-seitig resolvten Auren-Dauer; bei 0 laeuft
        /// die Anim als One-Shot.
        /// </summary>
        [ClientRpc]
        private void PlaySpellCastClientRpc(int spellEntry, ulong sourceNetId, ulong targetNetId, Vector3 groundPoint, bool hasGroundPoint, int groundDurationMs)
        {
            m_VisualKitMappingLoader ??= ServiceLocator.Get<SpellVisualKitMappingCatalogLoader>();
            m_VisualKitDefinitionLoader ??= ServiceLocator.Get<SpellVisualKitDefinitionCatalogLoader>();
            m_AnimationCatalogLoader ??= ServiceLocator.Get<SpellAnimationCatalogLoader>();

            SpellVisualKitMappingCatalog mappings = m_VisualKitMappingLoader?.GetCached();
            SpellVisualKitDefinitionCatalog defs = m_VisualKitDefinitionLoader?.GetCached();
            SpellAnimationCatalog anims = m_AnimationCatalogLoader?.GetCached();
            if (mappings == null || defs == null || anims == null)
            {
                return;
            }

            Transform sourceTransform = ResolveNetworkTransform(sourceNetId);
            if (sourceTransform == null)
            {
                return;
            }

            // Caster-Cast-Pose wird bereits in BeginCastClientRpc bei Cast-START
            // auf allen Peers ausgelöst (sowohl Cast-Time- als auch Instant-Casts).
            // Hier nur noch der Spawner-Pfad für Travel/Impact/Aura-Visuals.

            SpellVisualDefinition kit = SpellVisualResolver.Resolve(spellEntry, mappings, defs);
            if (kit == null)
            {
                return;
            }

            // Quick-Fix: Travel-Speed des Visuals aus der Spell-Geschwindigkeit
            // ableiten (Source-Pixel/Frame → m/s), statt der Resolver-Konstante
            // DefaultTravelSpeed. Haelt Client-Travel-Phase und Server-Projektil
            // zeitlich konsistent. Faellt auf die Konstante zurueck, falls der
            // Spell kein Speed-Feld traegt (Hitscan/Instant).
            SpellTemplate castSpell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (castSpell != null && castSpell.Speed > 0f)
            {
                float travelMps = SpellUtils.ProjectileSpeedToMps(castSpell.Speed);
                if (travelMps > 0f) { kit.TravelSpeed = travelMps; }
            }

            Transform targetTransform = targetNetId != 0UL && targetNetId != sourceNetId
                ? ResolveNetworkTransform(targetNetId)
                : null;

            // Partikel-Katalog fuer die Phasen-Partikelsysteme (Travel/Impact/
            // Ground) aufloesen. Bei fehlendem Loader bleibt es bei Sprites+Sound.
            m_ParticleCatalogLoader ??= ServiceLocator.Get<ParticleSystemCatalogLoader>();
            ParticleSystemCatalog phaseParticles = m_ParticleCatalogLoader?.GetCached();

            // Skillshots haben kein Unit-Ziel: das Visual fliegt gerichtet zum
            // uebergebenen Aim-Punkt (groundPoint) statt zu einem Target-Transform,
            // spiegelt damit das server-seitige gerichtete ServerProjectile.
            if (castSpell != null && castSpell.IsSkillshot && hasGroundPoint)
            {
                SpellVisualSpawner.SpawnDirectional(kit, anims, sourceTransform, groundPoint, phaseParticles);
            }
            else
            {
                SpellVisualSpawner.Spawn(kit, anims, sourceTransform, targetTransform, phaseParticles);
            }

            // Ground-Visual (FLARE go_kit) — nur wenn der Server eine Cast-
            // Destination mitgeschickt hat UND das Visual-Kit eine Ground-Phase
            // aufgeloest hat. Beispiel: Spell 30 / Ice Blast hat go_kit=35
            // (nova_001.sa) und liefert eine TargetsGround-Destination.
            // Skillshots sind ausgenommen: deren hasGroundPoint markiert nur den
            // Flug-Endpunkt fuer das gerichtete Travel-Visual, nicht eine
            // stationaere Boden-AoE.
            if (hasGroundPoint && (castSpell == null || !castSpell.IsSkillshot) && kit.Ground.HasAny)
            {
                float lifetime = groundDurationMs > 0 ? groundDurationMs * 0.001f : 0f;
                SpellVisualSpawner.SpawnGround(kit.Ground, anims, groundPoint, lifetime, phaseParticles);
            }
        }

        /// <summary>
        /// Löst auf diesem Client die Cast-Pose des Casters (FLARE <c>[cast]</c>)
        /// aus. Wird sowohl für Cast-Time- als auch für Instant-Casts aus
        /// <see cref="BeginCastClientRpc"/> aufgerufen — also bei Cast-START,
        /// nicht erst bei Cast-Resolve.
        /// <para>
        /// Routing nach Spell-Typ + aktuell ausgeruesteter Waffe:
        /// <list type="bullet">
        ///   <item><description><see cref="SpellEffect.WeaponDamage"/>-Spells nutzen die
        ///     Attack-Pose der equippten Waffe — Ranged-Waffe ⇒ <see cref="UnitCombatVisuals.PlayShoot"/>
        ///     (Aimed Shot), Melee-Waffe ⇒ <see cref="UnitCombatVisuals.PlaySwing"/>
        ///     (Sinister Strike). So liest der Spieler den Spell visuell als
        ///     verstaerkten Auto-Attack statt als Magier-Cast.</description></item>
        ///   <item><description>Alle anderen Spells (SchoolDamage, Heal, Aura, ...) fallen
        ///     auf die generische Cast-Pose (<see cref="UnitCombatVisuals.PlayCast"/>)
        ///     zurueck.</description></item>
        /// </list>
        /// FLARE-Charaktere haben nur eine generische Cast-Animation; der
        /// <c>unit_cast_animation</c>-Index im Visual-Kit-Mapping ist daher
        /// visuell bedeutungslos und wird hier bewusst nicht konsumiert.
        /// </para>
        /// </summary>
        private void TryTriggerCasterPose(int spellEntry)
        {
            UnitCombatVisuals visuals = m_Visuals != null
                ? m_Visuals
                : GetComponent<UnitCombatVisuals>();
            if (visuals == null)
            {
                visuals = GetComponentInChildren<UnitCombatVisuals>();
            }
            if (visuals == null)
            {
                return;
            }

            // Spell-Effekte abfragen, um zu entscheiden, ob es ein
            // Waffen-Spell ist (Pose folgt der Waffe) oder ein klassischer
            // Cast (generische Cast-Pose).
            SpellTemplate spell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (spell != null && SpellUsesWeaponAttack(spell))
            {
                // Spell-aware Auflfoesung: Ranged-Spells nutzen die Bogen-Waffe
                // aus dem Ranged-Slot (CurrentRangedWeapon), alle anderen die
                // Main-Hand. Damit spielt Aimed Shot zuverlaessig Shoot, auch
                // wenn die Main-Hand ein Longsword fuehrt.
                WeaponDefinition weapon = ResolveWeaponFor(spell);
                if (weapon != null)
                {
                    if (weapon.IsRanged) { visuals.PlayShoot(); }
                    else { visuals.PlaySwing(); }
                    return;
                }
            }

            // Melee-Lunges (Charge & Co., Effect 37) schliessen die Distanz zum
            // Ziel und enden mit einem Nahkampf-Treffer — immer die Swing-Pose,
            // unabhaengig von der equippten Waffe, statt der generischen Cast-Pose.
            if (spell != null && SpellUsesMeleeLunge(spell))
            {
                visuals.PlaySwing();
                return;
            }

            visuals.PlayCast();
        }

        /// <summary>True, sobald der Spell mindestens einen aktiven Effekt-Slot
        /// vom Typ <see cref="SpellEffect.WeaponDamage"/>, <see cref="SpellEffect.MeleeAtk"/>
        /// oder <see cref="SpellEffect.RangedAtk"/> hat. Solche Spells skalieren
        /// mit dem Waffenschaden (verstaerkte Spezial-Schlaege wie Aimed Shot
        /// ebenso wie die Basis-Auto-Attacks <c>Melee Swing</c> (81) und
        /// <c>Ranged Attack</c> (82)) und sollen die Attack-Pose der equippten
        /// Waffe statt der generischen Magier-Cast-Pose spielen.</summary>
        private static bool SpellUsesWeaponAttack(SpellTemplate spell)
        {
            if (spell == null)
            {
                return false;
            }

            int effectCount = spell.EffectCount;
            for (int slot = 1; slot <= effectCount; slot++)
            {
                SpellTemplateEffect eff = spell.GetEffect(slot);
                if (!eff.IsActive)
                {
                    continue;
                }

                if (eff.Effect == SpellEffect.WeaponDamage
                    || eff.Effect == SpellEffect.MeleeAtk
                    || eff.Effect == SpellEffect.RangedAtk)
                {
                    return true;
                }

                if (eff.Effect == SpellEffect.TriggerSpell)
                {
                    SpellTemplate triggeredSpell = SpellCatalogLoader.GetTemplateOrNull((int)eff.Data1);
                    if (SpellHasDirectWeaponDamage(triggeredSpell))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool SpellHasDirectWeaponDamage(SpellTemplate spell)
        {
            if (spell == null)
            {
                return false;
            }

            int effectCount = spell.EffectCount;
            for (int slot = 1; slot <= effectCount; slot++)
            {
                SpellTemplateEffect eff = spell.GetEffect(slot);
                if (eff.IsActive && eff.Effect == SpellEffect.WeaponDamage)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True, wenn der Spell einen aktiven <see cref="SpellEffect.Charge"/>-
        /// Effekt-Slot hat. Solche Melee-Lunges (z. B. Charge, Entry 4) schliessen die
        /// Distanz zum Ziel und enden mit einem Nahkampf-Treffer — sie spielen daher
        /// die Swing-Pose (<see cref="UnitCombatVisuals.PlaySwing"/>) statt der
        /// generischen Magier-Cast-Pose.</summary>
        private static bool SpellUsesMeleeLunge(SpellTemplate spell)
        {
            if (spell == null)
            {
                return false;
            }

            int effectCount = spell.EffectCount;
            for (int slot = 1; slot <= effectCount; slot++)
            {
                SpellTemplateEffect eff = spell.GetEffect(slot);
                if (eff.IsActive && eff.Effect == SpellEffect.Charge)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True, wenn der Spell nach dem Cast nahtlos in einen Auto-Attack
        /// uebergehen soll: Melee-Lunges (<see cref="SpellEffect.Charge"/>) sowie
        /// waffenbasierte Angriffs-Spells (<see cref="SpellUsesWeaponAttack"/>, z. B.
        /// Sinister Strike / Aimed Shot). Reine Buffs/Heals/Nukes ohne Waffen- oder
        /// Charge-Effekt bleiben ausgenommen und lassen den Spieler nach dem Cast
        /// stehen.</summary>
        private static bool SpellChainsIntoAutoAttack(SpellTemplate spell)
        {
            return spell != null
                && (SpellUsesMeleeLunge(spell) || SpellUsesWeaponAttack(spell));
        }

        /// <summary>
        /// Loest auf diesem Client das Caster-Partikelsystem des Spells aus.
        /// Resolved <c>spellEntry</c> → <see cref="SpellVisualKitMapping.CastingKit"/>
        /// → <see cref="SpellVisualKitDefinition.Psystem"/> (z. B. <c>casting_holy.psi</c>)
        /// → <see cref="ParticleSystemCatalog"/> und spawnt das System ueber
        /// <see cref="CasterParticleSpawner"/>. Wird parallel zur Cast-Pose aus
        /// <see cref="BeginCastClientRpc"/> bei Cast-START auf allen Peers gefeuert
        /// (sowohl Cast-Time- als auch Instant-Casts). Stilles No-Op, falls
        /// Mapping/Kit/PSystem-Name nicht im Katalog vorhanden.
        /// </summary>
        private void TryTriggerCasterParticles(int spellEntry)
        {
            m_VisualKitMappingLoader ??= ServiceLocator.Get<SpellVisualKitMappingCatalogLoader>();
            m_VisualKitDefinitionLoader ??= ServiceLocator.Get<SpellVisualKitDefinitionCatalogLoader>();
            m_ParticleCatalogLoader ??= ServiceLocator.Get<ParticleSystemCatalogLoader>();

            SpellVisualKitMappingCatalog mappings = m_VisualKitMappingLoader?.GetCached();
            SpellVisualKitDefinitionCatalog defs = m_VisualKitDefinitionLoader?.GetCached();
            ParticleSystemCatalog particles = m_ParticleCatalogLoader?.GetCached();
            if (mappings == null || defs == null || particles == null)
            {
                return;
            }

            if (!mappings.TryGet(spellEntry, out SpellVisualKitMapping mapping)
                || mapping == null
                || mapping.CastingKit == 0)
            {
                return;
            }
            if (!defs.TryGet(mapping.CastingKit, out SpellVisualKitDefinition kit)
                || kit == null
                || string.IsNullOrEmpty(kit.Psystem))
            {
                return;
            }
            string psName = ParticleSystemCatalog.StripPsi(kit.Psystem);
            if (!particles.TryGet(psName, out ParticleSystemDefinition def) || def == null)
            {
                return;
            }
            // Falls aus irgendeinem Grund noch ein altes Cast-PSystem laeuft
            // (z. B. zweiter Cast-Start ohne dazwischenliegendes Cast-End),
            // sauber stoppen bevor wir das neue spawnen.
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            m_ActiveCasterParticles = CasterParticleSpawner.Spawn(def, transform, worldYOffset: 0f);
        }

        /// <summary>
        /// Loest auf diesem Client den Caster-Sound des Spells aus.
        /// Resolved <c>spellEntry</c> → <see cref="SpellVisualKitMapping.CastingKit"/>
        /// → <see cref="SpellVisualKitDefinition.Sound"/> (Dateiname inkl. Extension,
        /// z. B. <c>"skill_heal.wav"</c>) → <see cref="SoundManager.GetClip"/>. Wird
        /// parallel zu Cast-Pose / Cast-Particles aus <see cref="BeginCastClientRpc"/>
        /// bei Cast-START auf allen Peers gefeuert. Stilles No-Op falls Mapping/Kit/
        /// Sound-Name nicht im Index vorhanden.
        /// </summary>
        private void TryTriggerCasterSound(int spellEntry)
        {
            m_VisualKitMappingLoader ??= ServiceLocator.Get<SpellVisualKitMappingCatalogLoader>();
            m_VisualKitDefinitionLoader ??= ServiceLocator.Get<SpellVisualKitDefinitionCatalogLoader>();
            m_SoundManager ??= ServiceLocator.Get<SoundManager>();

            SpellVisualKitMappingCatalog mappings = m_VisualKitMappingLoader?.GetCached();
            SpellVisualKitDefinitionCatalog defs = m_VisualKitDefinitionLoader?.GetCached();
            if (mappings == null || defs == null || m_SoundManager == null)
            {
                return;
            }

            if (!mappings.TryGet(spellEntry, out SpellVisualKitMapping mapping)
                || mapping == null
                || mapping.CastingKit == 0)
            {
                return;
            }
            if (!defs.TryGet(mapping.CastingKit, out SpellVisualKitDefinition kit)
                || kit == null
                || string.IsNullOrEmpty(kit.Sound))
            {
                return;
            }
            AudioClip clip = m_SoundManager.GetClip(kit.Sound);
            if (clip == null)
            {
                return;
            }
            // 3D one-shot am Caster; PlayClipAtPoint zerstoert das temporäre GO automatisch
            // nach Clip-Ende. Event-Frequenz (Cast-Start) ist niedrig genug fuer dieses Pattern.
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }

        /// <summary>
        /// Resolves a spawned <see cref="NetworkObject"/> by id to its transform,
        /// oder <c>null</c> wenn es auf diesem Client nicht (mehr) existiert.
        /// </summary>
        private Transform ResolveNetworkTransform(ulong netId)
        {
            if (netId == 0UL || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return null;
            }
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(netId, out NetworkObject no) || no == null)
            {
                return null;
            }
            return no.transform;
        }
    }
}
