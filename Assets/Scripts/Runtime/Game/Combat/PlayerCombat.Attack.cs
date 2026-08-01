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
        // Bindings (vom Bootstrap aufgerufen)
        // -------------------------------------------------------------------------

        /// <summary>Vom Bootstrap aufgerufen, sobald die Visuals-Komponente konstruiert ist.</summary>
        public void BindVisuals(PlayerCombatVisuals visuals)
        {
            m_Visuals = visuals;
        }

        /// <summary>Vom Bootstrap aufgerufen, sobald der Input-Controller verfügbar ist.</summary>
        public void BindInput(PlayerInputController input)
        {
            // Combat selbst horcht nicht mehr auf den Input — wir merken uns die Referenz
            // nur, weil andere Komponenten ggf. ueber Combat darauf zugreifen.
            m_Input = input;
        }

        // -------------------------------------------------------------------------
        // Owner-Input → Server
        // -------------------------------------------------------------------------

        /// <summary>
        /// LoL-Style Auto-Attack-Einstieg fuer den <see cref="MobaCommandController"/>.
        /// Wird per Frame aus dem Follow-Update aufgerufen, sobald der Spieler in
        /// Waffenreichweite zum gelockten Ziel steht. Idempotent gegenueber dem
        /// Prediction-Window — solange der Owner einen Angriff vorhersagt, wird
        /// kein weiterer RPC abgeschickt. Statt einer eigenen Hitscan-Logik castet
        /// der Auto-Attack jetzt den passenden Basis-Spell (Melee 81 / Ranged 82),
        /// damit GCD, Cast-Pose und Schadensaufloesung serverseitig ueber die
        /// regulaere Spell-Pipeline (<see cref="ServerBeginSpellCast"/>) laufen.
        /// </summary>
        public void TryRequestAutoAttack()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            if (m_Stats != null && m_Stats.IsDead)
            {
                return;
            }
            if (m_TargetSelection == null || m_TargetSelection.CurrentTargetId == TargetSelection.NoTarget)
            {
                return;
            }
            // Bereits ein Attack-RPC unterwegs? Dann nichts tun — das verhindert RPC-Spam
            // pro Frame im Follow-Update und ueberlaesst die naechste Anfrage dem Ablauf
            // des Vorhersagefensters. Der Server droppt zusaetzlich still alles, was
            // waehrend GCD/Cast eintrifft (reportFailures=false), sodass kein
            // Fehl-Toast pro Frame entsteht.
            if (IsOwnerPredictingAttack)
            {
                return;
            }

            // Lokale Movement-Vorhersage aktivieren (siehe PlayerMovement.TickOwner).
            m_OwnerPredictedAttackUntil = Time.unscaledTime + k_OwnerAttackPredictionWindow;
            RequestAttackServerRpc(m_TargetSelection.CurrentTargetId);
        }

        [ServerRpc]
        private void RequestAttackServerRpc(ulong targetNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            // Auto-Attack = realer Spell-Cast: im Ranged-Modus (mit ausgeruestetem
            // Bogen) Spell 82 (Ranged Attack), sonst Spell 81 (Melee Swing). Der
            // Cast laeuft durch dieselbe Validate-/GCD-/State-Pipeline wie ein
            // Hotbar-Spell. Fehler (GCD aktiv, Ziel ungueltig, bereits am Casten)
            // werden hier bewusst NICHT an den Owner gemeldet (reportFailures=false),
            // weil der Auto-Attack pro Frame nachfeuert und sonst Toast-Spam erzeugt.
            int spellEntry = m_RangedModeActive.Value && ResolveRangedWeapon() != null
                ? k_RangedAutoAttackSpell
                : k_MeleeAutoAttackSpell;

            ServerBeginSpellCast(
                spellEntry,
                targetNetworkObjectId,
                Vector3.zero,
                hasCastDestination: false,
                rpcParams.Receive.SenderClientId,
                reportFailures: false);
        }

        // -------------------------------------------------------------------------
        // Auto-Attack-Modus-Toggle (Melee <-> Ranged)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg zum Umschalten zwischen Melee- und Ranged-Auto-Attack.
        /// Wird von der Toggle-InputAction (Default: Taste 'T') ueber den
        /// <see cref="MobaCommandController"/> aufgerufen. Lokale Sanity-Checks
        /// (Spawn + Owner); der autoritative Wechsel inkl. Gating auf einen
        /// ausgeruesteten Bogen passiert serverseitig in
        /// <see cref="RequestToggleWeaponModeServerRpc"/>.
        /// </summary>
        public void RequestToggleWeaponMode()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            RequestToggleWeaponModeServerRpc();
        }

        [ServerRpc]
        private void RequestToggleWeaponModeServerRpc(ServerRpcParams _ = default)
        {
            // Auto-Verhalten: nur umschalten, wenn tatsaechlich eine Ranged-Waffe
            // ausgeruestet ist. Ohne Bogen bleibt der Modus zwingend Melee
            // ("sonst immer die vorhandene Waffe"), der Toggle ist dann ein No-Op.
            if (ResolveRangedWeapon() == null)
            {
                if (m_RangedModeActive.Value)
                {
                    m_RangedModeActive.Value = false;
                }
                return;
            }
            m_RangedModeActive.Value = !m_RangedModeActive.Value;
        }

        // -------------------------------------------------------------------------
        // Attack-Cancel (LoL-Style "Move-cancels-Attack")
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg zum Abbrechen einer laufenden Attacke. Wird vom
        /// <see cref="MobaCommandController"/> bei jedem neuen RMB-Move-Command
        /// aufgerufen, damit der Spieler — wie in League of Legends — während
        /// des Auto-Attack-Windups oder -Recovers durch Klicken irgendwo hin
        /// die Attacke abbricht und sich sofort bewegen kann. Lokal wird die
        /// Owner-Prediction-Sperre sofort gelöst, damit das Movement keinen
        /// Frame Verzögerung hat; der Server hebt parallel den
        /// <see cref="IsServerMovementLocked"/>-Lock auf und verhindert den
        /// Damage-Resolve, falls der noch nicht stattgefunden hat.
        /// Idempotent: ohne aktive Attacke ist es ein No-Op.
        /// </summary>
        public void RequestCancelAttack()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            // Owner-lokal: Movement-Prediction sofort freigeben, damit MoveDirection
            // schon im selben Frame greift, ohne auf das Server-ClientRpc zu warten.
            m_OwnerPredictedAttackUntil = 0f;
            RequestCancelAttackServerRpc();
        }

        [ServerRpc]
        private void RequestCancelAttackServerRpc(ServerRpcParams _ = default)
        {
            if (m_CurrentState != AttackingState)
            {
                return;
            }
            // ChangeState → Exit() canceled das CTS im AttackingState, damit der
            // noch nicht aufgelöste Hit nicht mehr landet (sauberer Cancel im Windup)
            // bzw. der Recovery-Teil sofort endet (sauberer Cancel im Wind-down).
            ChangeState(IdleState);
            // Visual-Fanout: Owner-Prediction überall zurücksetzen, falls noch
            // restliche ClientRpc-Latenz hängt. Anim selbst läuft kurz aus —
            // das ist okay (LoL macht es genauso: Anim klingt aus, Gameplay frei).
            NotifyAttackCanceledClientRpc();
        }

        [ClientRpc]
        private void NotifyAttackCanceledClientRpc(ClientRpcParams _ = default)
        {
            if (IsOwner)
            {
                m_OwnerPredictedAttackUntil = 0f;
            }
        }

        // -------------------------------------------------------------------------
        // Server → States
        // -------------------------------------------------------------------------

        /// <summary>
        /// Server-Pfad zum Einleiten einer Attacke. Wird vom Idle-State aufgerufen,
        /// sobald eine valide Attack-Anfrage eingegangen ist.
        /// </summary>
        internal void BeginAttack(WeaponDefinition weapon)
        {
            if (!IsServer)
            {
                return;
            }

            // Stun-Gate: ein gestunnter Spieler kann keine Auto-Attack starten.
            // Roots erlauben Auto-Attack (FLARE-Konvention), daher hier
            // explizit IsStunned, NICHT IsImmobilized.
            if (m_Stats != null && m_Stats.IsStunned)
            {
                return;
            }

            // Server dreht den Angreifer Richtung Ziel (XZ-Ebene), damit Visuals & sp&#228;tere
            // FX-Spawner einen sinnvollen Forward-Vektor haben.
            FaceCurrentTarget();

            float attackCooldown = GetEffectiveAttackCooldown(weapon);

            // 1) An alle Clients (inkl. Host) zum Visual-Trigger fan-out.
            //    Cooldown mitsenden, damit der Owner seinen Movement-Lock für
            //    exakt die gleiche Dauer wie der Server hält (kein Reconciliation-Snap,
            //    wenn die visuelle Anim kürzer ist als der Cooldown).
            PlayAttackClientRpc(weapon.AttackAnim, attackCooldown);

            // 2) Cooldown-State konfigurieren und Transition.
            AttackingState.ConfigureFromWeapon(weapon, attackCooldown);
            ChangeState(AttackingState);
        }

        /// <summary>
        /// Berechnet den server-autoritativ wirksamen Auto-Attack-Cooldown
        /// inklusive Aura-/Stat-Modifikatoren fuer Melee- und Ranged-Angriffe.
        /// </summary>
        private float GetEffectiveAttackCooldown(WeaponDefinition weapon)
        {
            if (weapon == null)
            {
                return 0.05f;
            }

            float cooldown = Mathf.Max(0.05f, weapon.AttackCooldown);
            if (m_Stats == null)
            {
                return cooldown;
            }

            AuraManager auras = ((ICombatUnit)m_Stats).Auras;
            if (auras == null)
            {
                return cooldown;
            }

            int statMask = weapon.IsRanged
                ? 1 << ((int)StatId.RangedCooldown - 1)
                : 1 << ((int)StatId.MeleeCooldown - 1);
            int speedPct = weapon.IsRanged
                ? auras.GetAuraModifierTotal(AuraType.ModifyRangedSpeedPct)
                : auras.GetAuraModifierTotal(AuraType.ModifyMeleeSpeedPct);
            int statPct = auras.GetStatModifierTotal(statMask);
            float multiplier = Mathf.Max(0.05f, 1f + (speedPct + statPct) / 100f);
            return Mathf.Max(0.05f, cooldown * multiplier);
        }

        private void FaceCurrentTarget()
        {
            if (m_TargetSelection == null || !m_TargetSelection.TryGetCurrentTarget(out NetworkObject no, out _))
            {
                return;
            }
            Vector3 to = no.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f)
            {
                return;
            }
            transform.rotation = Quaternion.LookRotation(to, Vector3.up);
        }

        // -------------------------------------------------------------------------
        // Server → Clients (Visual-Fanout)
        // -------------------------------------------------------------------------

        [ClientRpc]
        private void PlayAttackClientRpc(CombatAnim anim, float cooldown, ClientRpcParams _ = default)
        {
            // Server hat die Attack bestätigt: Owner-Vorhersage exakt auf den Server-
            // Cooldown verlängern. Damit ist der Owner-Movement-Lock und der
            // Server-Movement-Lock (IsServerMovementLocked) deckungsgleich —
            // egal wie lang/kurz die visuelle Attack-Anim ist.
            if (IsOwner)
            {
                m_OwnerPredictedAttackUntil = Time.unscaledTime + Mathf.Max(0.05f, cooldown);
            }

            if (m_Visuals == null)
            {
                return;
            }
            switch (anim)
            {
                case CombatAnim.Swing:
                    m_Visuals.PlaySwing();
                    break;
                case CombatAnim.Shoot:
                    m_Visuals.PlayShoot();
                    break;
                case CombatAnim.Cast:
                    m_Visuals.PlayCast();
                    break;
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// True, wenn <paramref name="state"/> aktuell der aktive State ist.
        /// Wird vom Attacking-State benutzt, um Stale-Transitions zu vermeiden.
        /// </summary>
        internal bool IsCurrentState(PlayerCombatState state)
        {
            return m_CurrentState == state;
        }

        /// <summary>
        /// Server-only Damage-Resolve. Wird vom Attacking-State zum
        /// HitResolveProgress-Frame aufgerufen. Source-treuer Single-Target-
        /// Flow: das aktuell gelockte Ziel aus <see cref="TargetSelection"/>
        /// auflösen, gegen <c>weapon.Range</c> auf 2D-Distanz pr&#252;fen, dann
        /// Schaden &#252;ber <see cref="CombatFormulas"/> berechnen und via
        /// <see cref="IDamageable.ApplyDamage"/> anwenden.
        /// </summary>
        internal void ServerResolveMeleeHit(WeaponDefinition weapon)
        {
            if (!IsServer || weapon == null)
            {
                return;
            }
            if (m_Stats == null || m_Stats.IsDead)
            {
                return;
            }
            if (m_TargetSelection == null)
            {
                return;
            }
            if (!m_TargetSelection.TryGetCurrentTarget(out NetworkObject targetObject, out UnitStats victimStats))
            {
                return;
            }
            if (victimStats == null || victimStats.IsDead)
            {
                return;
            }

            // Vor dem FrontArc-Check noch einmal Richtung Ziel drehen: das Ziel
            // konnte sich w&#228;hrend Windup bewegen, sonst l&#228;uft der Treffer ggf.
            // ins Leere obwohl der Spieler "auf" dem Ziel war.
            FaceCurrentTarget();

            // 2D-Distanzprüfung (XZ) — identisch zum SoF-Quellcode (sqrt(dx²+dy²)),
            // erweitert um den HitRadius des Opfers: Treffer landet, sobald die Waffen-
            // reichweite die Körperhülle des Ziels erreicht (nicht erst dessen Mittelpunkt).
            // Damit deckt der Owner-lokale AttackRangeIndicator die echte Reichweite ab,
            // ohne dass der Spieler "in den Gegner hineinlaufen" muss.
            Vector3 d = targetObject.transform.position - transform.position;
            d.y = 0f;
            float distSqr = d.sqrMagnitude;
            float reach = weapon.Range + victimStats.HitRadius;
            if (distSqr > reach * reach)
            {
                return;
            }

            // Front-Arc-Gate — nur sinnvoll, wenn Arc echt einschränkt (<360°)
            // und das Ziel nicht direkt im eigenen Pivot steht (sonst NaN).
            if (weapon.FrontArcDeg < 360f && distSqr > 0.0001f)
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    fwd.Normalize();
                    Vector3 toTarget = d / Mathf.Sqrt(distSqr);
                    float cosHalf = Mathf.Cos(weapon.FrontArcDeg * 0.5f * Mathf.Deg2Rad);
                    if (Vector3.Dot(fwd, toTarget) < cosHalf)
                    {
                        return;
                    }
                }
            }

            // Self-Hit / Allies sp&#228;ter via Hostility-Flag (PvP/Faction) — Phase 4 reicht Single-Target reicht.
            if (victimStats == m_Stats)
            {
                return;
            }

            DamageInfo info = CombatFormulas.CalculateMeleeDamage(m_Stats, victimStats, weapon);
            ((IDamageable)victimStats).ApplyDamage(info);
        }

        /// <summary>
        /// Server-only: prüft, ob das aktuell gelockte Ziel noch ein
        /// reguläres Angriffsziel ist (existiert, lebt, in Range). Wird
        /// vom <see cref="PlayerCombatAttackingState"/> vor
        /// dem Hit-Resolve genutzt, um den Cooldown zu canceln, sobald das
        /// Ziel weg ist (Tod, ServerClearTarget, Despawn, Wegrennen).
        /// </summary>
        internal bool ServerIsTargetStillValid(WeaponDefinition weapon)
        {
            if (!IsServer || weapon == null || m_TargetSelection == null)
            {
                return false;
            }
            // Mid-Windup-Stun: wenn der Angreifer waehrend des Windups gestunnt
            // wird, cancelt das den Hit-Resolve und der AttackingState faellt
            // zurueck in Idle. Roots blocken Auto-Attack NICHT.
            if (m_Stats != null && m_Stats.IsStunned)
            {
                return false;
            }
            if (!m_TargetSelection.TryGetCurrentTarget(out NetworkObject targetObject, out UnitStats victimStats))
            {
                return false;
            }
            if (victimStats == null || victimStats.IsDead || victimStats == m_Stats)
            {
                return false;
            }
            Vector3 d = targetObject.transform.position - transform.position;
            d.y = 0f;
            // Range-Check muss EXAKT zu ServerResolveMeleeHit passen (weapon.Range + HitRadius),
            // sonst cancelt der AttackingState den Cooldown waehrend das Resolve-Frame noch
            // einen Hit landen wuerde.
            float reach = weapon.Range + victimStats.HitRadius;
            return d.sqrMagnitude <= reach * reach;
        }

    }
}
