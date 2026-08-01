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
        /// Port von <c>NpcAI::selectSpellToCast</c>. Reihenfolge:
        /// <list type="number">
        /// <item><b>Notfall-Primary</b>: HP <c>&lt;= 30 %</c> ⇒ Primary mit
        /// hoechster Prioritaet (sofern bereit).</item>
        /// <item><b>Regulaere Slots</b>: erster Slot, der Intervall/Cooldown/
        /// Chance/Castbarkeit erfuellt.</item>
        /// <item><b>Fallback-Primary</b>: kein Slot zog ⇒ Primary, falls bereit.</item>
        /// </list>
        /// Rueckgabe: Slot-Index, <see cref="k_PrimarySlotSentinel"/> fuer den
        /// Primary, oder <c>-1</c> (kein Cast in diesem Tick).
        /// </summary>
        private int SelectSpellSlotToCast(UnitStats target)
        {
            if (target == null || m_Stats == null)
            {
                return -1;
            }
            if (m_ActiveSpellSlotCount <= 0 && m_PrimarySpellId <= 0)
            {
                return -1;
            }

            float now = Time.time;

            // Globaler Cooldown: blockt JEDE Spell-Auswahl, solange der GCD
            // laeuft — analog zum Spieler, der waehrend des GCD keinen Spell
            // aktivieren kann. Melee/Chase laufen davon unbeeintraechtigt.
            if (now < m_GcdReadyAt)
            {
                return -1;
            }

            // (1) Notfall-Primary: bei niedriger HP vor allen Slots.
            if (CanCastPrimary(now, target) && IsInEmergencyHealth())
            {
                return k_PrimarySlotSentinel;
            }

            // (2) Regulaere Slots in Template-Reihenfolge.
            for (int i = 0; i < m_SpellSlots.Length; i++)
            {
                NpcSpellSlotRuntime slot = m_SpellSlots[i];
                if (slot.SpellId <= 0)
                {
                    continue;
                }
                if (slot.ChancePct <= 0f)
                {
                    continue;
                }
                if (slot.IntervalSec > 0f && now < slot.NextAttemptAt)
                {
                    continue;
                }

                if (slot.CooldownSec > 0f && now < slot.NextReadyAt)
                {
                    continue;
                }

                float roll = UnityEngine.Random.Range(0f, 100f);
                if (roll > slot.ChancePct)
                {
                    continue;
                }

                if (!CanCastSpell(slot.SpellId, target))
                {
                    continue;
                }

                // Intervall erst bei tatsaechlich erfolgreicher Slot-Wahl
                // verbrauchen. Dadurch entspricht die Frequenz dem Designer-
                // intent (Chance + Conditions + Cooldown) statt bereits bei
                // reinen Fehlversuchen herunterzutakten.
                slot.NextAttemptAt = slot.IntervalSec > 0f ? now + slot.IntervalSec : now;
                m_SpellSlots[i] = slot;

                return i;
            }

            // (3) Fallback-Primary: kein Slot zog in diesem Tick.
            if (CanCastPrimary(now, target))
            {
                return k_PrimarySlotSentinel;
            }

            return -1;
        }

        /// <summary>
        /// True, wenn die aktuelle HP des NPC <c>&lt;= </c>
        /// <see cref="k_EmergencyHealthPct"/> Prozent betraegt. Port der
        /// Notfall-Bedingung aus <c>NpcAI::selectSpellToCast</c>.
        /// </summary>
        private bool IsInEmergencyHealth()
        {
            if (m_Stats == null)
            {
                return false;
            }
            int max = m_Stats.MaxHp;
            if (max <= 0)
            {
                return false;
            }
            float pct = m_Stats.CurrentHp * 100f / max;
            return pct <= k_EmergencyHealthPct;
        }

        /// <summary>
        /// Prueft, ob der Primary-/Notfall-Spell jetzt castbar ist: gesetzt,
        /// eigener Cooldown-Gate abgelaufen und voll castbar gegen das Target.
        /// </summary>
        private bool CanCastPrimary(float now, UnitStats target)
        {
            if (m_PrimarySpellId <= 0 || now < m_PrimaryNextReadyAt)
            {
                return false;
            }
            return CanCastSpell(m_PrimarySpellId, target);
        }

        /// <summary>
        /// Port von <c>NpcAI::canCastSpell</c>. Validiert Spell-Existenz plus
        /// komplette Castbarkeit gegen den aktuellen Target-Kontext. Reine
        /// Friendly-Spells werden vor der Validierung via
        /// <see cref="ResolveCastTarget"/> auf den NPC selbst umgelenkt, damit
        /// z. B. <c>Lesser Heal</c> nicht am feindlichen Aggro-Ziel scheitert.
        /// </summary>
        private bool CanCastSpell(int spellId, UnitStats target)
        {
            if (spellId <= 0 || m_Stats == null)
            {
                return false;
            }
            if (!SpellCatalogLoader.TryGetTemplate(spellId, out SpellTemplate spell) || spell == null)
            {
                return false;
            }
            UnitStats castTarget = ResolveCastTarget(spell, target);
            return SpellCaster.Validate(m_Stats, spell, castTarget) == CastResult.Success;
        }

        /// <summary>
        /// Bestimmt das tatsaechliche Cast-Ziel fuer einen NPC-Spell. Reine
        /// Friendly-Spells (kann nur Verbuendete targeten, nicht Gegner — z. B.
        /// <c>Lesser Heal</c>) wuerden gegen das feindliche Aggro-Ziel an
        /// <c>SpellCaster.CheckTarget</c> mit <c>TargetHostile</c> scheitern.
        /// Analog zum Smart-Self-Cast des Spielers
        /// (<see cref="PlayerCombat"/>) zielt der NPC solche Spells auf
        /// sich selbst. Alle anderen Spells behalten das uebergebene
        /// (feindliche) Aggro-Ziel. Idempotent: ein bereits aufgeloestes
        /// Self-Ziel bleibt Self.
        /// </summary>
        private UnitStats ResolveCastTarget(SpellTemplate spell, UnitStats hostileTarget)
        {
            if (spell == null || m_Stats == null)
            {
                return hostileTarget;
            }
            if (SpellUtils.CanTargetFriendly(spell) && !SpellUtils.CanTargetHostile(spell))
            {
                return m_Stats;
            }
            return hostileTarget;
        }

        /// <summary>
        /// Port von <c>NpcAI::performSpellCast</c>: instant Execute ueber die
        /// vorhandene Spell-Pipeline, danach Cooldown setzen und Cast-Anim
        /// replizieren. <paramref name="slotIndex"/> kann ein regulaerer Slot
        /// oder <see cref="k_PrimarySlotSentinel"/> (Primary-Spell) sein.
        /// </summary>
        private void TryPerformSpellCast(int slotIndex, UnitStats target, bool firePose = true)
        {
            if (target == null || m_Stats == null)
            {
                return;
            }

            if (slotIndex == k_PrimarySlotSentinel)
            {
                TryPerformPrimaryCast(target, firePose);
                return;
            }

            if (slotIndex < 0 || slotIndex >= m_SpellSlots.Length)
            {
                return;
            }

            NpcSpellSlotRuntime slot = m_SpellSlots[slotIndex];
            if (!SpellCatalogLoader.TryGetTemplate(slot.SpellId, out SpellTemplate spell) || spell == null)
            {
                return;
            }

            // Reine Friendly-Spells (z. B. Heal) auf den NPC selbst umlenken,
            // statt sie am feindlichen Aggro-Ziel scheitern zu lassen.
            UnitStats castTarget = ResolveCastTarget(spell, target);
            SpellExecutionResult result = SpellExecutor.Execute(m_Stats, spell, castTarget);
            if (result.Result != CastResult.Success)
            {
                return;
            }

            float spellCdSec = spell.Cooldown > 0 ? spell.Cooldown / 1000f : 0f;
            // Slot-Cooldown aus dem NPC-Template ist ein expliziter Override.
            // Wenn nicht gesetzt (<=0), faellt der Cast auf den Spell-Default
            // aus spell_template.cooldown zurueck.
            float cooldownSec = slot.CooldownSec > 0f ? slot.CooldownSec : spellCdSec;
            slot.NextReadyAt = cooldownSec > 0f ? Time.time + cooldownSec : Time.time;
            m_SpellSlots[slotIndex] = slot;

            // Globaler Cooldown wie beim Spieler: nach jedem erfolgreichen Cast
            // sperrt der GCD den naechsten Spell (cf. CooldownManager.StartGcd).
            m_GcdReadyAt = Time.time + k_GcdSec;

            // Travel-/Impact-/Aura-/Ground-Visual + Caster-Particles an alle
            // Peers fannen (server-autoritativ, Clients rendern nur).
            ServerFanSpellVisual(slot.SpellId, spell, castTarget);

            // Cast-Pose nur fannen, wenn nicht bereits beim Cast-Start gespielt
            // (Cast-Time-Spells fired die Pose in BeginOrResolveSpellCast).
            if (firePose)
            {
                PlayCastClientRpc(slot.SpellId);
            }
        }

        /// <summary>
        /// Fuehrt den Primary-/Notfall-Spell aus und setzt dessen eigenen
        /// Cooldown-Gate. Da der Primary keine Interval/Cooldown-Spalte hat und
        /// <c>SpellCaster.Validate</c> Cooldowns fuer Nicht-Spieler ignoriert,
        /// nutzen wir <c>spell_template.cooldown</c>; ist dieser <c>0</c>, greift
        /// ein <see cref="k_PrimaryGcdFallbackSec"/>-GCD als Anti-Spam-Schutz
        /// (verhindert Casts mit voller Tickrate). <paramref name="firePose"/>
        /// ist <c>false</c>, wenn die Cast-Pose bereits beim Cast-Start lief.
        /// </summary>
        private void TryPerformPrimaryCast(UnitStats target, bool firePose = true)
        {
            if (m_PrimarySpellId <= 0
                || !SpellCatalogLoader.TryGetTemplate(m_PrimarySpellId, out SpellTemplate spell)
                || spell == null)
            {
                return;
            }

            // Reine Friendly-Spells (Notfall-Heal) auf den NPC selbst umlenken.
            UnitStats castTarget = ResolveCastTarget(spell, target);
            SpellExecutionResult result = SpellExecutor.Execute(m_Stats, spell, castTarget);
            if (result.Result != CastResult.Success)
            {
                return;
            }

            float cooldownSec = spell.Cooldown > 0 ? spell.Cooldown / 1000f : k_PrimaryGcdFallbackSec;
            m_PrimaryNextReadyAt = Time.time + cooldownSec;

            // Globaler Cooldown wie beim Spieler.
            m_GcdReadyAt = Time.time + k_GcdSec;

            // Visual-Kit + Caster-Particles fannen (analog regulaerer Slot-Cast).
            ServerFanSpellVisual(m_PrimarySpellId, spell, castTarget);

            if (firePose)
            {
                PlayCastClientRpc(m_PrimarySpellId);
            }
        }

        /// <summary>
        /// Dispatcht einen ausgewaehlten Spell analog zu
        /// <see cref="PlayerCombat.BeginCast"/>: Cast-Time-Spells
        /// (<c>cast_time &gt; 0</c>) gehen in einen gehaltenen Cast (NPC steht
        /// still, channelt, loest erst nach Ablauf der Cast-Time auf),
        /// Instant-Spells (<c>cast_time &lt;= 0</c>) werden sofort aufgeloest.
        /// <paramref name="slotIndex"/> ist ein regulaerer Slot oder
        /// <see cref="k_PrimarySlotSentinel"/>.
        /// </summary>
        private void BeginOrResolveSpellCast(int slotIndex, UnitStats target)
        {
            if (target == null || m_Stats == null)
            {
                return;
            }

            int spellId = slotIndex == k_PrimarySlotSentinel
                ? m_PrimarySpellId
                : (slotIndex >= 0 && slotIndex < m_SpellSlots.Length ? m_SpellSlots[slotIndex].SpellId : 0);
            if (spellId <= 0)
            {
                return;
            }
            if (!SpellCatalogLoader.TryGetTemplate(spellId, out SpellTemplate spell) || spell == null)
            {
                return;
            }

            // Reine Friendly-Spells (Heal/Buff) channeln/zielen auf den NPC
            // selbst — Facing und m_CastTarget muessen daher das aufgeloeste
            // Ziel verwenden, nicht das feindliche Aggro-Ziel.
            UnitStats castTarget = ResolveCastTarget(spell, target);

            if (spell.CastTime > 0)
            {
                // Cast-Time-Spell: Cast-Pose SOFORT fannen (Channel-Animation),
                // Execute/Cooldown/GCD erst bei Abschluss in TickActiveCast.
                m_CastInProgress = true;
                m_CastSlotIndex = slotIndex;
                m_CastSpellId = spellId;
                m_CastTarget = castTarget;
                m_CastEndsAt = Time.time + spell.CastTime / 1000f;

                FaceTargetForCast(castTarget);
                PlayCastClientRpc(spellId);
                ShowCastBarClientRpc(spellId, spell.CastTime / 1000f);
                return;
            }

            // Instant-Cast: sofort aufloesen (setzt GCD, fired Pose).
            TryPerformSpellCast(slotIndex, castTarget);
        }

        /// <summary>
        /// Tickt einen aktiven Cast-Time-Spell: haelt das Facing auf das Ziel,
        /// bricht bei verlorenem/totem Ziel ab und loest den Spell nach Ablauf
        /// der Cast-Time auf (ohne erneute Cast-Pose). Wird nur aus
        /// <see cref="UpdateCombat"/> gerufen, solange
        /// <see cref="m_CastInProgress"/> gesetzt ist.
        /// </summary>
        private void TickActiveCast()
        {
            if (!IsValidTarget(m_CastTarget))
            {
                CancelActiveCast();
                return;
            }

            FaceTargetForCast(m_CastTarget);

            if (Time.time < m_CastEndsAt)
            {
                return; // noch am Channeln
            }

            // Cast abgeschlossen: Slot/Target sichern, State VOR dem Execute
            // zuruecksetzen (kein Re-Entry), dann aufloesen — Pose lief bereits
            // beim Cast-Start, daher firePose: false.
            int slotIndex = m_CastSlotIndex;
            UnitStats target = m_CastTarget;
            ClearActiveCastState();
            HideCastBarClientRpc();
            TryPerformSpellCast(slotIndex, target, firePose: false);
        }

        /// <summary>
        /// Bricht den aktiven Cast ab (Ziel verloren, Stun, harter Reset). Es
        /// werden weder Spell-Effekt noch GCD ausgeloest — ein abgebrochener
        /// Cast verhaelt sich wie ein nie gestarteter (vgl. Spieler-Interrupt).
        /// </summary>
        private void CancelActiveCast()
        {
            ClearActiveCastState();
            HideCastBarClientRpc();
        }

        /// <summary>
        /// Bricht einen laufenden Cast von au&#223;en ab (z. B. durch einen
        /// Spieler-Interrupt-Effekt). L&#228;uft server-autoritativ &#252;ber
        /// <see cref="UnitStats"/>. Liefert <c>true</c>,
        /// wenn tats&#228;chlich ein aktiver Cast abgebrochen wurde &#8212; ein
        /// abgebrochener Cast feuert weder Spell-Effekt noch GCD.
        /// </summary>
        /// <returns><c>true</c>, wenn ein laufender Cast unterbrochen wurde.</returns>
        public bool ServerInterruptCast()
        {
            if (!IsServer || !m_CastInProgress)
            {
                return false;
            }
            CancelActiveCast();
            return true;
        }

        /// <summary>Setzt alle Cast-Tracking-Felder zurueck.</summary>
        private void ClearActiveCastState()
        {
            m_CastInProgress = false;
            m_CastEndsAt = 0f;
            m_CastSpellId = 0;
            m_CastSlotIndex = -1;
            m_CastTarget = null;
        }

        /// <summary>
        /// Richtet die server-seitige Wunsch-Blickrichtung auf das Cast-Ziel aus
        /// (XZ), damit gerichtete VFX/Projectile-Spawns einen sinnvollen Forward
        /// haben — analog zu <c>PlayerCombat.FaceCurrentTarget</c>.
        /// </summary>
        private void FaceTargetForCast(UnitStats target)
        {
            if (target == null)
            {
                return;
            }
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                m_ServerFacingVec = toTarget;
            }
        }

        // -------------------------------------------------------------------
        // Helpers — Port der NpcAI::* Helfer
        // -------------------------------------------------------------------

    }
}
