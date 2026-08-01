using System;
using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Spells;
using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using Riftstorm.Gameplay.Combat.Spells.Visuals;
using Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime;
using Riftstorm.Management.SoundManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
    public sealed partial class NpcController
    {
        // -------------------------------------------------------------------
        // Remote-Tick
        // -------------------------------------------------------------------

        private void TickRemoteClient(float dt)
        {
            Vector3 target = m_ServerPosition.Value;
            Vector3 current = transform.position;
            transform.position = Vector3.SmoothDamp(
                current,
                target,
                ref m_SmoothVelocity,
                m_RemoteSmoothTime);
        }

        // -------------------------------------------------------------------
        // Visuals (alle Peers)
        // -------------------------------------------------------------------

        private void UpdateVisuals()
        {
            if (m_Character == null)
            {
                return;
            }

            if (m_Visuals != null && m_Visuals.IsBusy)
            {
                return;
            }
            if (m_ServerDead)
            {
                return;
            }

            // Direction + Moving werden vom Server replizert. Jeder Peer rendert
            // damit dieselbe Octanten-Wahl — kein Octanten-Flackern durch lokales
            // SmoothDamp-Jitter mehr.
            int dir = m_ServerDirection.Value & 7;
            string anim = m_ServerMoving.Value ? m_AnimRun : m_AnimStance;
            m_Character.SetDirection(dir);
            m_Character.Play(anim);
        }

        /// <summary>
        /// Server-only. Pusht die FLARE-Direction in die NetworkVariable, aber nur,
        /// wenn der Winkel die aktuelle Octanten-Mitte um mehr als 22.5° +
        /// <see cref="m_DirectionHysteresisDeg"/> verlaesst. So flippt die Richtung
        /// nicht bei jedem Wackler an der Octanten-Grenze.
        /// </summary>
        private void UpdateServerDirection(Vector3 facingVec)
        {
            facingVec.y = 0f;
            // <2cm Vektor ⇒ Rauschen, Richtung behalten.
            if (facingVec.sqrMagnitude < 0.0004f)
            {
                return;
            }

            int rawDir = ComputeFlareDirection(facingVec);
            byte currentDir = m_ServerDirection.Value;
            if (rawDir == currentDir)
            {
                return;
            }

            // Hysterese: Octanten-Wechsel nur, wenn der Winkel mindestens
            // (22.5° + Hysterese) ueber die Mitte des aktuellen Octanten hinaus
            // gewandert ist. Octanten-Mitte = ((dir - 4) * 45°), Inverse von
            // ComputeFlareDirection.
            float angleDeg = Mathf.Atan2(-facingVec.z, facingVec.x) * Mathf.Rad2Deg;
            float currentCenterDeg = (currentDir - 4) * 45f;
            float deviation = Mathf.Abs(Mathf.DeltaAngle(angleDeg, currentCenterDeg));
            if (deviation < 22.5f + m_DirectionHysteresisDeg)
            {
                return;
            }

            m_ServerDirection.Value = (byte)rawDir;
        }

        /// <summary>
        /// Bildet einen XZ-Bewegungsvektor auf FLARE's 8-Octant-Direction-Index ab
        /// (0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW). Z wird invertiert, weil
        /// FLARE seine Y-Achse aus der Top-Down-Sicht ableitet (Sued = +Z in Unity).
        /// Achtung: dieser Enum-Wert ist Riftstorm-spezifisch und unterscheidet
        /// sich vom source-server-Enum (S=0..SE=7). Atlas-Layout ist hier
        /// massgebend, NICHT die source-Reihenfolge.
        /// </summary>
        private static int ComputeFlareDirection(Vector3 diff)
        {
            float angleDeg = Mathf.Atan2(-diff.z, diff.x) * Mathf.Rad2Deg;
            int octant = Mathf.RoundToInt(angleDeg / 45f);
            return (octant + 4) & 7;
        }

        // -------------------------------------------------------------------
        // ClientRpcs
        // -------------------------------------------------------------------

        [ClientRpc]
        private void PlaySwingClientRpc(FixedString32Bytes animationName)
        {
            if (m_Visuals == null)
            {
                return;
            }
            if (animationName.Length > 0)
            {
                m_Visuals.PlaySwing(animationName.ToString());
            }
            else
            {
                m_Visuals.PlaySwing();
            }
            TryPlayNpcSound("attack");
        }

        [ClientRpc]
        private void PlayCastClientRpc(int spellEntry)
        {
            if (m_Visuals != null)
            {
                m_Visuals.PlayCast();
            }
            // Caster-Particles + Caster-Sound bei Cast-START auf allen Peers
            // (analog Combat.PlayerCombat.BeginCastClientRpc).
            TryTriggerCasterParticles(spellEntry);
            TryTriggerCasterSound(spellEntry);
        }

        /// <summary>
        /// Server-seitiger Fan-Out des Spell-Visuals nach erfolgreichem
        /// <see cref="SpellExecutor.Execute(ICombatUnit,SpellTemplate,ICombatUnit)"/>.
        /// Berechnet Source-/Target-<c>NetworkObjectId</c> und &#8212; fuer
        /// Skillshots &#8212; einen gerichteten Flug-Endpunkt (Forward-Fallback,
        /// identisch zur Spieler-Logik in
        /// <c>Combat.PlayerCombat.ServerCompleteCast</c>) und ruft das
        /// <see cref="PlaySpellVisualClientRpc"/> auf allen Peers (inkl. Host).
        /// NPCs casten ausschliesslich auf Unit-Ziele (keine Boden-Destination),
        /// daher gibt es keinen stationaeren Ground-AoE-Punkt
        /// (<paramref name="castTarget"/>-Transform traegt das Visual).
        /// </summary>
        /// <param name="spellEntry">Katalog-ID des gecasteten Spells.</param>
        /// <param name="spell">Aufgeloestes Spell-Template (fuer Skillshot/Range).</param>
        /// <param name="castTarget">Aufgeloestes Cast-Ziel (kann der NPC selbst sein).</param>
        private void ServerFanSpellVisual(int spellEntry, SpellTemplate spell, UnitStats castTarget)
        {
            if (!IsServer || spell == null || m_Stats == null)
            {
                return;
            }

            ulong sourceNetId = NetworkObject != null ? NetworkObject.NetworkObjectId : 0UL;
            bool selfCast = ReferenceEquals(castTarget, m_Stats);
            ulong targetNetId = (!selfCast && castTarget != null) ? castTarget.NetworkObjectId : 0UL;

            Vector3 groundPoint = Vector3.zero;
            bool hasGroundPoint = false;
            if (spell.IsSkillshot)
            {
                // Skillshots haben kein Unit-Ziel-Transform fuer das Visual:
                // gerichteter Flug-Endpunkt = Caster-Position + Richtung x Range.
                // Richtung primaer zum Ziel, sonst aktuelle Server-Blickrichtung.
                Vector3 origin = m_Stats.transform.position;
                Vector3 dir = castTarget != null
                    ? castTarget.transform.position - origin
                    : m_ServerFacingVec;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) { dir = m_ServerFacingVec; dir.y = 0f; }
                if (dir.sqrMagnitude < 0.0001f) { dir = Vector3.forward; }
                dir.Normalize();
                float rangeMeters = SpellUtils.RangeToMeters(spell.Range);
                if (rangeMeters <= 0f) { rangeMeters = k_FallbackSkillshotVisualRangeMeters; }
                groundPoint = origin + dir * rangeMeters;
                hasGroundPoint = true;
            }

            PlaySpellVisualClientRpc(spellEntry, sourceNetId, targetNetId, groundPoint, hasGroundPoint, 0);
        }

        /// <summary>
        /// Client-seitiges Rendern des Spell-Visuals (Travel/Impact/Aura/Ground)
        /// fuer einen NPC-Cast. Spiegelt
        /// <c>Combat.PlayerCombat.PlaySpellCastClientRpc</c>: resolved Visual-Kit
        /// + Anim-Katalog, leitet die Travel-Speed aus der Spell-Geschwindigkeit
        /// ab und spawnt &#252;ber <see cref="SpellVisualSpawner"/> entweder ein
        /// gerichtetes Skillshot-Visual (<paramref name="hasGroundPoint"/>) oder
        /// ein ziel-gebundenes Visual. Die Caster-Cast-Pose/-Particles laufen
        /// bereits in <see cref="PlayCastClientRpc"/> bei Cast-START.
        /// </summary>
        [ClientRpc]
        private void PlaySpellVisualClientRpc(int spellEntry, ulong sourceNetId, ulong targetNetId, Vector3 groundPoint, bool hasGroundPoint, int groundDurationMs)
        {
            m_VisualKitMappingLoader ??= ServiceLocator.Get<SpellVisualKitMappingCatalogLoader>();
            m_VisualKitDefinitionLoader ??= ServiceLocator.Get<SpellVisualKitDefinitionCatalogLoader>();
            m_AnimationCatalogLoader ??= ServiceLocator.Get<SpellAnimationCatalogLoader>();
            m_ParticleCatalogLoader ??= ServiceLocator.Get<ParticleSystemCatalogLoader>();

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

            SpellVisualDefinition kit = SpellVisualResolver.Resolve(spellEntry, mappings, defs);
            if (kit == null)
            {
                return;
            }

            // Travel-Speed aus der Spell-Geschwindigkeit ableiten (Source-Pixel/
            // Frame -> m/s), damit Client-Travel und Server-Projektil zeitlich
            // konsistent laufen. Fallback auf die Resolver-Konstante.
            SpellTemplate castSpell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (castSpell != null && castSpell.Speed > 0f)
            {
                float travelMps = SpellUtils.ProjectileSpeedToMps(castSpell.Speed);
                if (travelMps > 0f) { kit.TravelSpeed = travelMps; }
            }

            ParticleSystemCatalog particles = m_ParticleCatalogLoader?.GetCached();

            Transform targetTransform = targetNetId != 0UL && targetNetId != sourceNetId
                ? ResolveNetworkTransform(targetNetId)
                : null;

            if (castSpell != null && castSpell.IsSkillshot && hasGroundPoint)
            {
                SpellVisualSpawner.SpawnDirectional(kit, anims, sourceTransform, groundPoint, particles);
            }
            else
            {
                SpellVisualSpawner.Spawn(kit, anims, sourceTransform, targetTransform, particles);
            }

            if (hasGroundPoint && (castSpell == null || !castSpell.IsSkillshot) && kit.Ground.HasAny)
            {
                float lifetime = groundDurationMs > 0 ? groundDurationMs * 0.001f : 0f;
                SpellVisualSpawner.SpawnGround(kit.Ground, anims, groundPoint, lifetime, particles);
            }
        }

        /// <summary>
        /// Loest auf diesem Client das Caster-Partikelsystem des Spells aus.
        /// Resolved <c>spellEntry</c> &#8594; <see cref="SpellVisualKitMapping.CastingKit"/>
        /// &#8594; <see cref="SpellVisualKitDefinition.Psystem"/> &#8594;
        /// <see cref="ParticleSystemCatalog"/> und spawnt das System ueber
        /// <see cref="CasterParticleSpawner"/> am NPC. Stilles No-Op, falls
        /// Mapping/Kit/PSystem-Name fehlt. Spiegelt
        /// <c>Combat.PlayerCombat.TryTriggerCasterParticles</c>.
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
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            m_ActiveCasterParticles = CasterParticleSpawner.Spawn(def, transform, worldYOffset: 0f);
        }

        /// <summary>
        /// Loest auf diesem Client den Caster-Sound des Spells aus. Resolved
        /// <c>spellEntry</c> &#8594; <see cref="SpellVisualKitMapping.CastingKit"/>
        /// &#8594; <see cref="SpellVisualKitDefinition.Sound"/> &#8594;
        /// <see cref="SoundManager.GetClip"/> und spielt einen 3D-One-Shot am NPC.
        /// Stilles No-Op falls Mapping/Kit/Sound-Name fehlt. Spiegelt
        /// <c>Combat.PlayerCombat.TryTriggerCasterSound</c>.
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
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }

        /// <summary>
        /// Loest eine gespawnte <see cref="NetworkObject"/>-Id zu ihrem Transform
        /// auf, oder <c>null</c> wenn es auf diesem Client nicht (mehr) existiert.
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

        /// <summary>
        /// Startet die Overhead-Cast-Bar auf allen Clients (inkl. Host) f&#252;r
        /// einen Cast-Time-Spell. Der Fortschritt wird client-seitig lokal aus
        /// der &#252;bergebenen Dauer interpoliert &#8212; es flie&#223;t kein
        /// laufender Netzwerk-Traffic.
        /// </summary>
        /// <param name="spellId">Katalog-ID des gecasteten Spells.</param>
        /// <param name="durationSeconds">Gesamte Cast-Zeit in Sekunden.</param>
        [ClientRpc]
        private void ShowCastBarClientRpc(int spellId, float durationSeconds)
        {
            m_LocalCastActive = true;
            m_LocalCastSpellId = spellId;
            m_LocalCastStartUnscaled = Time.unscaledTime;
            m_LocalCastDurationSeconds = Mathf.Max(0.01f, durationSeconds);
            EnsureCastBarView().Begin(spellId, durationSeconds);
            LocalCastStarted?.Invoke(spellId, m_LocalCastDurationSeconds);
        }

        /// <summary>
        /// Blendet die Overhead-Cast-Bar auf allen Clients aus (Abschluss,
        /// Abbruch oder Interrupt).
        /// </summary>
        [ClientRpc]
        private void HideCastBarClientRpc()
        {
            m_LocalCastActive = false;
            m_LocalCastSpellId = 0;
            if (m_CastBarView != null)
            {
                m_CastBarView.End();
            }
            // Endlose Caster-PSystems (lifetime = -1) beim Cast-Ende stoppen,
            // damit sie nicht bis zum 8s-Cap weiterglitzern.
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            LocalCastEnded?.Invoke();
        }

        /// <summary>
        /// Liefert die client-seitige <see cref="NpcCastBarView"/> und erg&#228;nzt
        /// sie bei Bedarf zur Laufzeit (kein Prefab-Eintrag n&#246;tig).
        /// </summary>
        private NpcCastBarView EnsureCastBarView()
        {
            if (m_CastBarView == null)
            {
                m_CastBarView = gameObject.GetComponent<NpcCastBarView>();
                if (m_CastBarView == null)
                {
                    m_CastBarView = gameObject.AddComponent<NpcCastBarView>();
                }
            }
            return m_CastBarView;
        }

        [ClientRpc]
        private void PlayDieClientRpc()
        {
            if (m_Visuals != null)
            {
                m_Visuals.PlayDie();
            }
            // Laufende Caster-Particles beim Tod sauber stoppen.
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            TryPlayNpcSound("die");
        }

        [ClientRpc]
        private void PlayAggroClientRpc()
        {
            TryPlayNpcSound("aggro");
        }

        [ClientRpc]
        private void SyncLocalThreatClientRpc(int threatValue, ClientRpcParams _ = default)
        {
            if (m_LocalObservedThreat == threatValue)
            {
                return;
            }

            m_LocalObservedThreat = threatValue;
            try
            {
                LocalObservedThreatChanged?.Invoke(threatValue);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        [ClientRpc]
        private void PlayRespawnClientRpc()
        {
            if (m_Visuals != null)
            {
                m_Visuals.ResetForRespawn();
            }
        }

        // -------------------------------------------------------------------
        // Hit-Reaktion
        // -------------------------------------------------------------------

        private void HandleClientDamageReceived(int amount, HitResult result)
        {
            if (m_Visuals == null)
            {
                return;
            }
            if (result == HitResult.Miss || result == HitResult.Dodge)
            {
                return;
            }
            m_Visuals.PlayHit();
            TryPlayNpcSound("damage");
        }

        /// <summary>
        /// Spielt – falls vorhanden – einen zufaelligen Combat-Sound fuer das
        /// gebundene Model (<see cref="m_ModelName"/>) und das uebergebene
        /// <paramref name="evt"/> (attack/damage/die/aggro) raeumlich an der
        /// NPC-Position ab. Stiller No-op, wenn kein Model gebunden ist, der
        /// <see cref="SoundManager"/> fehlt, kein Eintrag existiert oder der
        /// Clip nicht geladen werden kann. Client-seitige Presentation – wird
        /// ausschliesslich aus ClientRpcs / Client-Hooks gerufen.
        /// </summary>
        /// <param name="evt">Event-Schluessel aus <c>_sounds.json</c>.</param>
        private void TryPlayNpcSound(string evt)
        {
            if (string.IsNullOrEmpty(m_ModelName))
            {
                return;
            }
            if (!NpcSoundCatalogLoader.TryGetRandomSound(m_ModelName, evt, out string soundFile))
            {
                return;
            }

            m_SoundManager ??= ServiceLocator.Get<SoundManager>();
            if (m_SoundManager == null)
            {
                return;
            }

            AudioClip clip = m_SoundManager.GetClip(soundFile);
            if (clip == null)
            {
                return;
            }
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
