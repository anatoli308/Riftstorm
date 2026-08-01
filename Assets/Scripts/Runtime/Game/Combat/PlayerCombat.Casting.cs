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
        // -------------------------------------------------------------------------
        // Spell-Cast (HUD/ActionBar → Server → SpellExecutor)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg, um einen Spell zu casten. HUD/ActionBar ruft das auf,
        /// sobald der Spieler eine Ability-Hotkey betätigt. Lokal nur Sanity-Checks
        /// (lebt, hat Spawn, hat Owner-Rechte, valides Target gewählt) — die
        /// autoritative Validierung passiert serverseitig in
        /// <see cref="RequestCastSpellServerRpc"/> über <see cref="SpellExecutor"/>.
        /// Target-Auswahl folgt der bestehenden <see cref="TargetSelection"/>-Logik;
        /// für Self-Casts darf das Target leer sein.
        /// </summary>
        /// <param name="spellEntry">Numerischer Entry aus <c>spells/_templates.json</c> (z. B. <c>133</c> für Fireball).</param>
        public void TryRequestCastSpell(int spellEntry)
        {
            if (!IsSpawned || !IsOwner || spellEntry <= 0)
            {
                return;
            }
            if (m_Stats != null && m_Stats.IsDead)
            {
                return;
            }

            m_MoveCommands?.InterruptMovementForCast();

            // Rohe TargetId weiterreichen — auch wenn das Ziel inzwischen tot ist.
            // TryGetCurrentTarget filtert tote Targets raus; das wuerde clientseitig
            // zu targetId=0 fuehren und serverseitig zur Selbst-Fallback-Logik
            // verleiten ("Can't target self." statt "Target is dead."). Die echte
            // Validierung passiert ohnehin serverseitig in SpellCaster.Validate.
            ulong targetId = m_TargetSelection != null
                ? m_TargetSelection.CurrentTargetId
                : 0UL;

            SpellTemplate attackSpell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);

            RequestCastSpellServerRpc(spellEntry, targetId, Vector3.zero, false);

            // Waffen-Angriffs-Spells (Melee-Lunge/sofort gecastete Waffen-Spells)
            // sollen nach dem Cast nahtlos in einen Auto-Attack uebergehen. Der Cast
            // hat gerade via InterruptMovementForCast den Move-Intent gecancelt — hier
            // den Follow-/Auto-Attack-Intent auf das Ziel re-engagieren, statt idle
            // stehen zu bleiben. Der Follow-Update feuert den ersten Schlag, sobald
            // GCD frei ist und der Spieler (nach einem etwaigen Charge-Impuls) in
            // Waffenreichweite steht.
            if (targetId != 0UL && SpellChainsIntoAutoAttack(attackSpell))
            {
                m_MoveCommands?.EngageFollowTarget(targetId);
            }
        }

        /// <summary>
        /// Variante mit explizitem Boden-Zielpunkt fuer Ground-Target-Spells
        /// (Blink/Boden-AoE). Wird vom Client-seitigen Ground-Target-Picker
        /// aufgerufen, sobald der Spieler eine Reticle-Position bestaetigt hat
        /// (LMB). Range/LoS werden serverseitig validiert; ein zu weit gesetzter
        /// Punkt wird im SpellExecutor auf <see cref="SpellTemplate.Range"/>
        /// geclampt.
        /// </summary>
        /// <param name="spellEntry">Numerischer Entry aus <c>spells/_templates.json</c>.</param>
        /// <param name="worldDestination">Welt-Koordinate des Boden-Zielpunkts.</param>
        public void TryRequestCastSpellAtGround(int spellEntry, Vector3 worldDestination)
        {
            if (!IsSpawned || !IsOwner || spellEntry <= 0)
            {
                return;
            }
            if (m_Stats != null && m_Stats.IsDead)
            {
                return;
            }

            m_MoveCommands?.InterruptMovementForCast();

            // Rohe TargetId weiterreichen — siehe Kommentar in TryRequestCastSpellAtSelection.
            ulong targetId = m_TargetSelection != null
                ? m_TargetSelection.CurrentTargetId
                : 0UL;

            RequestCastSpellServerRpc(spellEntry, targetId, worldDestination, true);
        }

        /// <summary>
        /// Server-autoritativer Eingang. Resolvt Spell-Template und Primärziel,
        /// delegiert die Annahme-/Ablehnungs-Entscheidung an den aktuellen
        /// Combat-State (Idle akzeptiert, Attacking/Casting/Dead verwerfen).
        /// </summary>
        [ServerRpc]
        private void RequestCastSpellServerRpc(
            int spellEntry,
            ulong targetNetworkObjectId,
            Vector3 castDestination,
            bool hasCastDestination,
            ServerRpcParams rpcParams = default)
        {
            ServerBeginSpellCast(
                spellEntry,
                targetNetworkObjectId,
                castDestination,
                hasCastDestination,
                rpcParams.Receive.SenderClientId,
                reportFailures: true);
        }

        /// <summary>
        /// Gemeinsamer Server-Pfad zum Einleiten eines Casts. Resolvt Spell-Template
        /// und Primaerziel, prueft Self-/Smart-Self-Cast, lehnt waehrend eines
        /// laufenden Casts/Swings ab und fuehrt das Frueh-Validate
        /// (Resourcen/Cooldown/GCD/Target/Range/Faction) aus, bevor an den
        /// <see cref="PlayerCombatIdleState"/> delegiert wird. Wird sowohl vom
        /// Hotbar-Cast (<see cref="RequestCastSpellServerRpc"/>, mit Fehler-Toasts)
        /// als auch vom Auto-Attack (<see cref="RequestAttackServerRpc"/>, still)
        /// genutzt. <paramref name="reportFailures"/> steuert, ob Ablehnungen per
        /// <see cref="ServerNotifyCastFailed"/> an den Owner gemeldet werden — fuer
        /// den pro-Frame nachfeuernden Auto-Attack ist das <c>false</c>, um
        /// Toast-Spam waehrend GCD/Cast zu vermeiden.
        /// </summary>
        private void ServerBeginSpellCast(
            int spellEntry,
            ulong targetNetworkObjectId,
            Vector3 castDestination,
            bool hasCastDestination,
            ulong senderClientId,
            bool reportFailures)
        {
            if (m_Stats == null || m_Stats.IsDead)
            {
                return;
            }

            SpellTemplate spell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (spell == null)
            {
                Debug.LogWarning($"[PlayerCombat] Unbekannter Spell-Entry '{spellEntry}'.");
                if (reportFailures)
                {
                    ServerNotifyCastFailed(senderClientId, CastResult.UnknownSpell);
                }
                return;
            }

            ICombatUnit primaryTarget = null;
            if (targetNetworkObjectId != 0UL
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj)
                && targetObj != null)
            {
                if (targetObj.TryGetComponent<UnitStats>(out var targetStats))
                {
                    primaryTarget = targetStats;
                }
                else
                {
                    primaryTarget = targetObj.GetComponentInChildren<UnitStats>();
                }
            }

            // Self-only Spells (z. B. Buff auf sich selbst) brauchen keinen externen
            // Target-Pick — hier den Caster als Primaerziel einsetzen. Zusaetzlich
            // defaulten reine Friendly-Buffs OHNE explizites Ziel auf den Caster
            // (WoW "self-cast"): Blessing of Health & Co. ohne Target wirken auf
            // den Spieler selbst, statt mit "No target" zu scheitern. Spells, die
            // auch Hostile targeten koennen (z. B. Dispel), bleiben ausgenommen und
            // verlangen weiterhin ein explizites Ziel — sonst gaebe SpellCaster.
            // CheckTarget sauber "No target" / "Target is dead" / "Invalid target"
            // zurueck, statt das Self-Fallback faelschlich "Can't target self."
            // zu triggern.
            if (primaryTarget == null
                && (SpellUtils.IsSelfOnly(spell)
                    || (SpellUtils.CanTargetFriendly(spell) && !SpellUtils.CanTargetHostile(spell))))
            {
                primaryTarget = m_Stats;
            }

            // Smart-Self-Cast: Wenn der Spieler einen Buff/Friendly-Spell wirkt
            // waehrend ein Gegner als Ziel ausgewaehlt ist, soll der Cast nicht
            // scheitern. Stattdessen wird der Caster selbst als Ziel verwendet
            // (WoW-Verhalten "Auto-self-cast on harm-target"). Greift nur fuer
            // reine Friendly-Spells (kein Hostile-Targeting moeglich) — Spells,
            // die sowohl Friendly als auch Hostile targeten koennen (z.B. Dispel),
            // bleiben am ausgewaehlten Ziel.
            if (primaryTarget != null
                && primaryTarget != (ICombatUnit)m_Stats
                && SpellUtils.CanTargetFriendly(spell)
                && !SpellUtils.CanTargetHostile(spell))
            {
                ICombatUnit casterUnit = m_Stats;
                if (casterUnit != null && primaryTarget.FactionId != casterUnit.FactionId)
                {
                    primaryTarget = m_Stats;
                }
            }

            // Wenn der Spieler bereits castet oder swingt, lehnt der State silent ab
            // (Base.OnCastRequested ist no-op in Casting/Attacking/Dead). Damit der
            // Owner trotzdem "Du castest bereits." sieht, hier vor der State-Dispatch
            // explizit reporten.
            if (m_CurrentState != IdleState)
            {
                if (reportFailures)
                {
                    ServerNotifyCastFailed(senderClientId, CastResult.CasterCasting);
                }
                return;
            }

            // Frueh-Validate: Resourcen/Cooldown/Target/Range/Faction werden hier
            // bereits geprueft, damit der Owner sofort eine Fehlermeldung bekommt
            // (kein stilles Verschlucken bei Spell auf Cooldown). Bei Cast-Time-
            // Spells laeuft am Resolve-Ende noch ein zweites Validate in
            // SpellExecutor.Execute &#8212; das kann nochmal scheitern, wenn das Ziel
            // waehrend des Casts aus der Range laeuft (WoW-Verhalten).
            CastResult preValidate = SpellCaster.Validate(m_Stats, spell, primaryTarget);
            if (preValidate != CastResult.Success)
            {
                if (reportFailures)
                {
                    ServerNotifyCastFailed(senderClientId, preValidate);
                }
                return;
            }

            CastDestination destination = hasCastDestination
                ? CastDestination.At(castDestination)
                : CastDestination.None;

            m_CurrentState.OnCastRequested(spellEntry, spell, targetNetworkObjectId, primaryTarget, destination);
        }

        /// <summary>
        /// Server-Pfad zum Einleiten eines Casts. Wird vom <see cref="PlayerCombatIdleState"/>
        /// aufgerufen. Instant-Casts (<c>CastTime &lt;= 0</c>) werden sofort über
        /// <see cref="ServerCompleteCast"/> abgewickelt — der Spieler bleibt im
        /// Idle-State. Cast-Time-Spells transitionieren in den <see cref="CastingState"/>,
        /// der das Awaitable-Gate hält und am Ende ebenfalls <see cref="ServerCompleteCast"/>
        /// aufruft.
        /// </summary>
        internal void BeginCast(int spellEntry, SpellTemplate spell, ulong targetNetId, ICombatUnit primaryTarget, CastDestination destination)
        {
            if (!IsServer || spell == null)
            {
                return;
            }

            // Caster Richtung Ziel ausrichten (XZ), bevor wir den Cast-Timer starten —
            // damit gerichtete VFX/Projectile-Spawns einen sinnvollen Forward haben.
            FaceCurrentTarget();

            if (spell.CastTime <= 0)
            {
                // Instant-Cast: keine State-Transition nötig, aber dennoch die
                // Cast-Pose über BeginCastClientRpc fanned (castSeconds=0 ->
                // CastBar bleibt zu, Pose feuert trotzdem auf allen Peers).
                BeginCastClientRpc(spellEntry, 0f);
                ServerCompleteCast(spellEntry, spell, targetNetId, primaryTarget, destination);
                return;
            }

            // Owner-HUD &#252;ber den startenden Cast informieren — VOR der
            // State-Transition, damit die CastBar parallel zum Server-Timer
            // l&#228;uft. Sekunden statt Millisekunden, weil das HUD direkt
            // gegen <see cref="Time.unscaledTime"/> interpoliert.
            BeginCastClientRpc(spellEntry, spell.CastTime / 1000f);

            CastingState.ConfigureFromCast(spellEntry, spell, targetNetId, primaryTarget, destination);
            ChangeState(CastingState);
        }

        /// <summary>
        /// Server-Pfad zum Abschluss eines Casts (sowohl für Instant- als auch
        /// für Cast-Time-Spells). Führt <see cref="SpellExecutor.Execute"/> aus
        /// und fanned bei Erfolg das Visual via <see cref="PlaySpellCastClientRpc"/>
        /// an alle Peers (inkl. Host) aus. <paramref name="destination"/> wird
        /// fuer Boden-Zielpunkte (Blink, Boden-AoE) durchgereicht.
        /// </summary>
        internal void ServerCompleteCast(int spellEntry, SpellTemplate spell, ulong targetNetId, ICombatUnit primaryTarget, CastDestination destination)
        {
            if (!IsServer || spell == null)
            {
                return;
            }

            ICombatUnit caster = m_Stats;
            if (caster == null)
            {
                return;
            }

            SpellExecutionResult result = SpellExecutor.Execute(caster, spell, primaryTarget, destination);
            if (result.Result != CastResult.Success)
            {
                // CastBar auch bei serverseitig abgelehnter Execute schlie&#223;en
                // (z. B. fehlende Mana / out-of-range zum Zeitpunkt des Resolves).
                EndCastClientRpc(false);
                ServerNotifyCastFailed(OwnerClientId, result.Result);
                return;
            }

            // Erfolgreich abgeschlossen — Owner-HUD die CastBar als completed
            // schlie&#223;en lassen.
            EndCastClientRpc(true);

            // Cooldowns sind server-autoritativ (vgl. <see cref="SpellExecutor.StartCooldowns"/>).
            // Den startenden Cooldown + GCD an den Owner-Client mirroren, damit
            // <see cref="UI.ActionBarHUD"/> die Sweep-Anzeige rendern kann
            // (Cooldown-Replikation pro Cast statt Full-State-Sync).
            int gcdMs = (spell.Attributes & SpellAttributes.Triggered) == 0
                ? CooldownManager.GcdDurationMs
                : 0;
            int cooldownMs = spell.Cooldown > 0 ? spell.Cooldown : 0;
            if (cooldownMs > 0 || gcdMs > 0)
            {
                ClientRpcParams ownerOnly = new()
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[] { OwnerClientId },
                    },
                };
                NotifyCooldownStartedClientRpc(spellEntry, cooldownMs, gcdMs, ownerOnly);
            }

            ulong sourceNetId = NetworkObject != null ? NetworkObject.NetworkObjectId : 0UL;
            ulong resolvedTargetNetId = ReferenceEquals(primaryTarget, caster) ? 0UL : targetNetId;

            // Ground-Visual (FLARE go_kit) braucht die Cast-Destination + die
            // resolvierte Auren-Dauer, damit z. B. der Ice-Patch bei Spell 30
            // genau so lange am Boden liegt wie der Root-Aura. Wenn der Cast
            // keine Destination hat (Single-Target, Self), faellt der Spawner
            // auf One-Shot zurueck.
            Vector3 groundPoint;
            bool hasGroundPoint;
            int groundDurationMs;
            if (spell.IsSkillshot)
            {
                // Skillshots: dem Client IMMER einen konkreten Flug-Endpunkt
                // mitgeben, damit das gerichtete Visual auch dann fliegt, wenn
                // der Owner keinen Boden-Aim geliefert hat (Forward-Fallback,
                // z. B. fehlender GroundTargetPicker am Prefab). Der Endpunkt
                // wird identisch zum server-seitigen <see cref="Spells.Projectiles.ServerProjectile"/>
                // berechnet (Cursor- bzw. Blickrichtung x Reichweite), damit
                // Travel-Visual und Hitscan deckungsgleich laufen.
                Vector3 origin = caster.Position;
                Vector3 dir = destination.HasValue ? destination.Position - origin : caster.Forward;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) { dir = caster.Forward; dir.y = 0f; }
                if (dir.sqrMagnitude < 0.0001f) { dir = Vector3.forward; }
                dir.Normalize();
                float rangeMeters = SpellUtils.RangeToMeters(spell.Range);
                if (rangeMeters <= 0f) { rangeMeters = k_FallbackSkillshotVisualRangeMeters; }
                groundPoint = origin + dir * rangeMeters;
                hasGroundPoint = true;
                groundDurationMs = 0;
            }
            else
            {
                groundPoint = destination.HasValue ? destination.Position : Vector3.zero;
                hasGroundPoint = destination.HasValue;
                groundDurationMs = destination.HasValue ? SpellUtils.CalculateDuration(spell, caster) : 0;
            }
            PlaySpellCastClientRpc(spellEntry, sourceNetId, resolvedTargetNetId, groundPoint, hasGroundPoint, groundDurationMs);
        }

        /// <summary>
        /// Server-Pfad zum harten Abbrechen eines laufenden Casts. Wird vom
        /// <see cref="PlayerMovement.SubmitCommandServerRpc"/> aufgerufen,
        /// sobald der Owner w&#228;hrend des Casts ein Move-Input absetzt
        /// (LoL/WoW-Style "Move-cancels-Cast"). Idempotent: au&#223;erhalb von
        /// <see cref="CastingState"/> ist es ein No-Op. Die State-Transition
        /// nach <see cref="IdleState"/> ruft <see cref="PlayerCombatCastingState.Exit"/>
        /// auf, das den Awaitable-CastTimer canceled — Resource-/Cooldown-Aufwand
        /// ist noch nicht gefallen (passiert erst in <see cref="ServerCompleteCast"/>),
        /// also kein Refund n&#246;tig.
        /// </summary>
        public void ServerInterruptCast()
        {
            if (!IsServer || m_CurrentState != CastingState)
            {
                return;
            }
            ChangeState(IdleState);
            EndCastClientRpc(false);
        }

        // -------------------------------------------------------------------------
        // CastBar-ClientRpcs (Owner-only Event-Fanout)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Wird vom Server gefeuert, sobald ein Cast startet. Triggert auf
        /// ALLEN Peers die Caster-Cast-Pose (FLARE <c>[cast]</c>), damit die
        /// Animation während der gesamten Cast-Zeit zu sehen ist und nicht
        /// erst nach Cast-Ende kurz aufblitzt. Quelle für das Pose-Gate:
        /// <c>spell_visual_kit.unit_cast_animation</c> per Spell-Entry. Da das
        /// Source-Index→State-Mapping nicht recoverable ist, gilt: jeder
        /// Nicht-Null-Index → generische "cast"-Pose. So feuert die Pose auch
        /// für Spells, deren Visual-Kit kein spranim hat (z. B. Spell 133 /
        /// Kit 154 → nur psystem + sound).
        ///
        /// Das <see cref="OwnerCastStarted"/>-Event (CastBar-HUD) bleibt
        /// Owner-only und feuert nur bei echtem Cast-Time-Spell
        /// (<paramref name="castSeconds"/> &gt; 0). Instant-Casts werden mit
        /// <c>castSeconds = 0</c> hier durchgeschleust, damit die Pose
        /// trotzdem auf allen Peers zu sehen ist.
        /// </summary>
        [ClientRpc]
        private void BeginCastClientRpc(int spellEntry, float castSeconds, ClientRpcParams _ = default)
        {
            TryTriggerCasterPose(spellEntry);
            TryTriggerCasterParticles(spellEntry);
            TryTriggerCasterSound(spellEntry);
            TryShowRangedForCast(spellEntry);

            if (!IsOwner || castSeconds <= 0f)
            {
                return;
            }
            OwnerCastStarted?.Invoke(spellEntry, Mathf.Max(0.01f, castSeconds));
        }

        /// <summary>
        /// Wird vom Server gefeuert, sobald ein laufender Cast beendet wird —
        /// entweder erfolgreich (<paramref name="completed"/> = <c>true</c>) oder
        /// unterbrochen (Bewegung, Tod, Execute-Fail). Owner-only Event-Fanout.
        /// </summary>
        [ClientRpc]
        private void EndCastClientRpc(bool completed, ClientRpcParams _ = default)
        {
            // Cast-Particles laufen auf allen Peers (gespawnt in BeginCastClientRpc
            // via TryTriggerCasterParticles), also muss der Stop VOR dem Owner-Check
            // stehen — sonst wuerden Remote-Peers das endlose PSystem bis zum harten
            // 8s-Cap weiter sehen.
            if (m_ActiveCasterParticles != null)
            {
                CasterParticleSpawner.Stop(m_ActiveCasterParticles);
                m_ActiveCasterParticles = null;
            }
            TryHideRangedAfterCast();
            if (!IsOwner)
            {
                return;
            }
            m_MoveCommands?.ResumeHeldMovementAfterCast();
            OwnerCastEnded?.Invoke(completed);
        }

        /// <summary>
        /// Blendet die Ranged-Waffe (Bow/Crossbow/Gun) fuer die Dauer eines
        /// Shoot-Casts auf der FLARE-Schicht <c>"Ranged"</c> ein. Wird auf
        /// jedem Peer aus <see cref="BeginCastClientRpc"/> aufgerufen und ist
        /// strikt an <c>SpellTemplate.RequiredEquipment == 12L</c> gebunden —
        /// alle anderen Spells lassen MainHand/OffHand unangetastet.
        /// MainHand + OffHand bleiben in jedem Frame sichtbar (das Visual ist
        /// kein Stance-Override, sondern eine zusaetzliche Schicht), siehe
        /// <see cref="PlayerEquipmentVisuals.ShowRangedForCast"/>.
        /// </summary>
        private void TryShowRangedForCast(int spellEntry)
        {
            SpellTemplate spell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
            if (spell == null || spell.RequiredEquipment != 12L)
            {
                return;
            }
            if (!TryGetComponent<PlayerEquipmentVisuals>(out var visuals))
            {
                return;
            }
            visuals.ShowRangedForCast(CurrentRangedId);
        }

        /// <summary>
        /// Entfernt die Ranged-Waffe vom FLARE-Layer am Cast-Ende. Idempotent —
        /// wird unkonditional aus <see cref="EndCastClientRpc"/> aufgerufen,
        /// damit auch abgebrochene Casts (Move-cancel, Death-cancel) die Bogen-
        /// Schicht wieder leeren.
        /// </summary>
        private void TryHideRangedAfterCast()
        {
            if (!TryGetComponent<PlayerEquipmentVisuals>(out var visuals))
            {
                return;
            }
            visuals.HideRangedAfterCast();
        }

        /// <summary>
        /// Server-Helper: schickt einen <see cref="CastResult"/>-Fehler nur an den
        /// Cast-anfordernden Client (Owner). HUD (<see cref="UI.CastFailedToastHUD"/>)
        /// rendert daraus &uuml;ber <see cref="CastResultStrings.Get"/> einen
        /// kurzlebigen Screen-Toast. Self-Host (IsServer &amp;&amp; IsOwner) wird
        /// trivial mitversorgt &#8212; ClientRpc liefert auch an den lokalen Client.
        /// </summary>
        internal void ServerNotifyCastFailed(ulong targetClientId, CastResult result)
        {
            if (!IsServer || result == CastResult.Success)
            {
                return;
            }
            ClientRpcParams targetOnly = new()
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { targetClientId },
                },
            };
            NotifyCastFailedClientRpc((byte)result, targetOnly);
        }

        /// <summary>
        /// Owner-only Fanout der Cast-Fehlerursache. Wird vom HUD &uuml;ber
        /// <see cref="OwnerCastFailed"/> konsumiert &#8212; analog zu
        /// <see cref="OwnerCastStarted"/>/<see cref="OwnerCastEnded"/>.
        /// </summary>
        [ClientRpc]
        private void NotifyCastFailedClientRpc(byte resultCode, ClientRpcParams _ = default)
        {
            if (!IsOwner)
            {
                return;
            }
            OwnerCastFailed?.Invoke((CastResult)resultCode);
        }

        /// <summary>
        /// Mirror des frisch gestarteten Spell-Cooldowns + GCD an den Owner-Client.
        /// Server hat die Cooldowns ueber <see cref="SpellExecutor.StartCooldowns"/>
        /// bereits server-autoritativ angelegt &#8212; dieser RPC schiebt dieselben
        /// Daten an den Owner, damit <see cref="UI.ActionBarHUD"/> auch auf
        /// remote Clients den Sweep + Remaining-Sekunden anzeigt.
        /// Cooldowns sind idempotent (<see cref="CooldownManager.StartCooldown"/>
        /// ueberschreibt), daher ist Self-Host (Server == Owner) harmlos.
        /// </summary>
        [ClientRpc]
        private void NotifyCooldownStartedClientRpc(int spellEntry, int cooldownMs, int gcdMs, ClientRpcParams _ = default)
        {
            if (!IsOwner || m_Stats == null)
            {
                return;
            }
            CooldownManager cd = ((ICombatUnit)m_Stats).Cooldowns;
            if (cd == null)
            {
                return;
            }
            if (cooldownMs > 0)
            {
                cd.StartCooldown(spellEntry, cooldownMs);
            }
            if (gcdMs > 0)
            {
                cd.StartGcd(gcdMs);
            }
        }

    }
}
