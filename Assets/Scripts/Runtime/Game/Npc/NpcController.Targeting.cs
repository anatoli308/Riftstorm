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
        /// <summary>
        /// Port von <c>NpcAI::findAggroTarget</c>: closest hostile player im
        /// Aggro-Radius. Source nutzt eine Entity-Map; wir scannen per
        /// <see cref="Physics.OverlapSphereNonAlloc"/>, was funktional
        /// aequivalent ist, solange Players Collider haben.
        /// </summary>
        private UnitStats FindAggroTarget()
        {
            QueryTriggerInteraction triggerMode = m_IncludeTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            int hits = Physics.OverlapSphereNonAlloc(
                transform.position,
                m_AggroRange,
                s_OverlapBuffer,
                m_TargetLayerMask,
                triggerMode);

            UnitStats closest = null;
            float closestSqr = float.PositiveInfinity;
            Vector3 myPos = transform.position;

            for (int i = 0; i < hits; i++)
            {
                Collider col = s_OverlapBuffer[i];
                s_OverlapBuffer[i] = null;
                if (col == null)
                {
                    continue;
                }

                UnitStats stats = col.GetComponentInParent<UnitStats>();
                if (!IsValidTarget(stats))
                {
                    continue;
                }
                if (stats == m_Stats)
                {
                    continue;
                }
                if (!IsHostileTo(stats))
                {
                    continue;
                }

                float sqr = (stats.transform.position - myPos).sqrMagnitude;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    closest = stats;
                }
            }

            return closest;
        }

        /// <summary>
        /// Port von <c>Entity::isInRange</c> + <c>NpcAI::isInMeleeRange</c>:
        /// rein skalare Distanz. Wir addieren die HitRadii auf beiden Seiten,
        /// damit grosse Modelle sich nicht "ineinanderschieben" muessen, bevor
        /// der Schlag laendet.
        /// </summary>
        private bool IsInMeleeRange(UnitStats target)
        {
            if (target == null)
            {
                return false;
            }
            float effective = m_MeleeRange + m_Stats.HitRadius + target.HitRadius;
            float distSqr = (target.transform.position - transform.position).sqrMagnitude;
            return distSqr <= effective * effective;
        }

        /// <summary>
        /// True, wenn dieser NPC eine Fernkampfwaffe traegt (<c>ranged_weapon_value &gt; 0</c>)
        /// und damit Ranged-Auto-Attacks ausfuehren darf.
        /// </summary>
        private bool HasRangedWeapon => m_RangedWeaponValue > 0;

        /// <summary>
        /// Skalare Reichweiten-Pruefung fuer Ranged-Auto-Attacks. Analog
        /// <see cref="IsInMeleeRange"/>, aber mit <see cref="m_RangedRange"/>.
        /// </summary>
        private bool IsInRangedRange(UnitStats target)
        {
            if (target == null)
            {
                return false;
            }
            float effective = m_RangedRange + m_Stats.HitRadius + target.HitRadius;
            float distSqr = (target.transform.position - transform.position).sqrMagnitude;
            return distSqr <= effective * effective;
        }

        /// <summary>
        /// Port von <c>NpcAI::moveTowardsEntity</c>: einen Schritt Richtung
        /// Target, aber kurz vor Melee-Reichweite stoppen, damit der NPC nicht
        /// ueber den Spieler hinwegrutscht und im naechsten Tick zurueckdreht.
        /// </summary>
        private void MoveTowardsEntity(UnitStats target, float dt, float speed)
        {
            Vector3 selfPos = transform.position;
            Vector3 targetPos = target.transform.position;
            Vector3 diff = targetPos - selfPos;
            diff.y = 0f;
            float dist = diff.magnitude;
            if (dist <= 0.001f)
            {
                return;
            }

            float stopDistance = Mathf.Max(0.1f,
                m_MeleeRange + m_Stats.HitRadius + target.HitRadius - 0.1f);
            float step = Mathf.Max(0f, speed) * dt;
            float moveDist = Mathf.Min(step, Mathf.Max(0f, dist - stopDistance));
            if (moveDist <= 0f)
            {
                return;
            }

            Vector3 dirNorm = diff / dist;
            transform.position = selfPos + dirNorm * moveDist;
            // Source-Parity: orientation wird NUR gesetzt, wenn der NPC tatsaechlich
            // einen Schritt gemacht hat (NpcAI.cpp Z.640-642 in moveTowards). Damit
            // friert die Blickrichtung im Melee-Stand korrekt ein und dreht sich nicht,
            // wenn der Spieler um den NPC herumlaeuft.
            m_ServerFacingVec = diff;
        }

        /// <summary>
        /// Port von <c>NpcAI::returnHome</c>: gerader Lauf Richtung
        /// Home-Position ohne Stop-Distance.
        /// </summary>
        private void MoveTowardsPoint(Vector3 point, float dt, float speed)
        {
            Vector3 selfPos = transform.position;
            Vector3 diff = point - selfPos;
            diff.y = 0f;
            float dist = diff.magnitude;
            if (dist <= 0.001f)
            {
                return;
            }
            float step = Mathf.Min(Mathf.Max(0f, speed) * dt, dist);
            if (step <= 0f)
            {
                return;
            }
            transform.position = selfPos + (diff / dist) * step;
            // Source-Parity: orientation nur bei tatsaechlicher Bewegung. Verhindert
            // dass das Sprite am Home-Punkt (Evade-Ende) noch zappelt.
            m_ServerFacingVec = diff;
        }

        /// <summary>Port von <c>NpcAI::shouldLeash</c>: nur Distanz vom Home.</summary>
        private bool ShouldLeash()
        {
            if (!m_HomeInitialized || m_LeashRange <= 0f)
            {
                return false;
            }
            float distSqr = (transform.position - m_HomePosition).sqrMagnitude;
            return distSqr > m_LeashRange * m_LeashRange;
        }

        /// <summary>Port von <c>NpcAI::isValidTarget</c>: nicht null, nicht tot.</summary>
        private bool IsValidTarget(UnitStats target)
        {
            return target != null && !target.IsDead;
        }

        /// <summary>
        /// Port der <c>UnitDefines::Faction</c>-Logik: Hostile aggrot alles
        /// ausser sich selbst, Neutral/Friendly nur Retaliation. NPC-NPC-
        /// Friendly-Fire ist vorerst aus (analog Source-Default).
        /// </summary>
        /// TODO: Faction Enum usw. implementieren und hier nutzen, statt hardcoded.
        private bool IsHostileTo(UnitStats other)
        {
            if (other == null)
            {
                return false;
            }
            if (other.GetComponent<NpcController>() != null)
            {
                return false;
            }
            return IsHostileFaction(m_Faction);
        }

        private static bool IsHostileFaction(int faction)
        {
            // Shared/UnitDefines.h: PlayerDefault=0, Friendly=1, Neutral=2, Hostile=3, PvP=4
            // TODO: UnitFaction.cs implementieren und hier nutzen, statt hardcoded.
            return faction == 3;
        }

        // -------------------------------------------------------------------
        // Attack
        // -------------------------------------------------------------------

        /// <summary>
        /// NPC-Melee-Auto-Attack. Cooldown ueber <see cref="m_MeleeCooldownSec"/>,
        /// danach werden die Effekte von Spell 81 ("Melee Swing") ueber
        /// <see cref="SpellExecutor.ApplyAllEffectsAtImpact"/> angewendet. Der
        /// Schaden laeuft dadurch durch <see cref="SpellExecutor"/>/<see cref="CombatFormulas"/>
        /// (100%-Waffenschaden, Armor + Dodge/Parry/Block sowie School-Immunity
        /// wie Focused Evasion) — exakt wie der Spieler-Auto-Attack.
        /// <para>
        /// Bewusst <b>kein</b> <see cref="SpellCaster.Validate"/>: Die NPC-AI hat
        /// die Reichweite bereits ueber <see cref="IsInMeleeRange"/> (NPC-eigene
        /// <see cref="m_MeleeRange"/>) gegated. Die spieler-zentrische
        /// Spell-Range-/Equipment-Validierung (Spell 81 traegt nur 2&#160;m
        /// Source-Range, Spell 82 verlangt eine Ranged-Waffe) wuerde den
        /// Auto-Attack sonst faelschlich blocken. Der eigene Attack-Timer bleibt
        /// unabhaengig vom Spell-GCD, genau wie die Auto-Attack-Cadence des Spielers.
        /// </para>
        /// </summary>
        private void TryMeleeAttack(UnitStats target)
        {
            float now = Time.time;
            if (now - m_LastAttackTime < m_MeleeCooldownSec)
            {
                return;
            }
            if (m_Visuals != null && m_Visuals.IsBusy)
            {
                return;
            }
            if (m_Stats == null
                || !SpellCatalogLoader.TryGetTemplate(k_MeleeAutoAttackSpellId, out SpellTemplate spell)
                || spell == null)
            {
                return;
            }

            SpellExecutor.ApplyAllEffectsAtImpact(m_Stats, spell, target, CastDestination.None);
            m_LastAttackTime = now;
            PlaySwingClientRpc(default);
        }

        /// <summary>
        /// NPC-Ranged-Auto-Attack (Riftstorm-Erweiterung; das Original besitzt
        /// keine Ranged-AI). Teilt sich den Attack-Cooldown-Timer mit dem
        /// Melee-Swing, nutzt aber <see cref="m_RangedCooldownSec"/> als Cadence
        /// und wendet die Effekte von Spell 82 ("Ranged Attack") ueber
        /// <see cref="SpellExecutor.ApplyAllEffectsAtImpact"/> an — dieselbe
        /// Schadenspipeline wie der Spieler. Der RangedAtk-Effekt skaliert mit
        /// dem Ranged-Waffenschaden des NPCs (siehe <see cref="UnitStats.SetWeaponDamage"/>).
        /// <para>
        /// Bewusst <b>kein</b> <see cref="SpellCaster.Validate"/>: Die NPC-AI hat
        /// die Reichweite bereits ueber <see cref="IsInRangedRange"/> (NPC-eigene
        /// <see cref="m_RangedRange"/>) gegated, und Spell 82 verlangt in der
        /// Source eine equippte Ranged-Waffe (<c>BaseRangedWeaponDamage&gt;0</c>),
        /// die NPCs nie als Item-Stat fuehren — die regulaere Validierung wuerde
        /// den Schuss daher faelschlich als <c>NoRangedWeapon</c> blocken.
        /// </para>
        /// </summary>
        private void TryRangedAttack(UnitStats target)
        {
            float now = Time.time;
            if (now - m_LastAttackTime < m_RangedCooldownSec)
            {
                return;
            }
            if (m_Visuals != null && m_Visuals.IsBusy)
            {
                return;
            }
            if (m_Stats == null
                || !SpellCatalogLoader.TryGetTemplate(k_RangedAutoAttackSpellId, out SpellTemplate spell)
                || spell == null)
            {
                return;
            }

            SpellExecutor.ApplyAllEffectsAtImpact(m_Stats, spell, target, CastDestination.None);
            m_LastAttackTime = now;
            PlaySwingClientRpc(default);
        }

        // -------------------------------------------------------------------
        // Damage / Threat
        // -------------------------------------------------------------------

    }
}
