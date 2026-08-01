using System;
using System.Collections.Generic;
using Riftstorm.Game.Movement;
using Riftstorm.Game.Npc;
using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    public sealed partial class UnitStats
    {
        /// <summary>Server-only: setzt die HP zurück (z. B. bei Respawn).</summary>
        public void ServerResetHp()
        {
            if (!IsServer)
            {
                return;
            }
            m_CurrentHp.Value = MaxHp;
        }

        /// <summary>Server-only: setzt die Mana zurück (z. B. bei Respawn).</summary>
        public void ServerResetMana()
        {
            if (!IsServer)
            {
                return;
            }
            m_CurrentMana.Value = m_MaxMana;
        }
        
        /// <summary>
        /// Server-only: belebt eine tote Unit an Ort und Stelle wieder und
        /// hebt den Dead-State der zugehoerigen Combat-/AI-Komponenten auf.
        /// </summary>
        /// <param name="health">Absolute HP nach dem Revive. Werte &lt;= 0 werden verworfen.</param>
        /// <param name="mana">Absolute Mana nach dem Revive. Bei Einheiten ohne Mana ignoriert.</param>
        /// <returns><c>true</c>, wenn ein Dead-Target erfolgreich revived wurde.</returns>
        public bool ServerRevive(int health, int mana)
        {
            if (!IsServer || !IsDead || health <= 0 || MaxHp <= 0)
            {
                return false;
            }

            m_CurrentHp.Value = Mathf.Clamp(health, 1, MaxHp);
            if (HasMana)
            {
                m_CurrentMana.Value = Mathf.Clamp(mana, 0, m_MaxMana);
            }

            if (m_PlayerCombat != null)
            {
                m_PlayerCombat.ServerHandleExternalRevive();
            }

            if (m_NpcController != null)
            {
                m_NpcController.ServerHandleExternalRevive();
            }

            return true;
        }

        /// <summary>
        /// Server-only: Manaverbrauch fuer Spells. Gibt true zurueck, wenn genug Mana
        /// vorhanden war (analog SoF-SpellCaster::checkResources). Zieht keinen Mana ab,
        /// wenn die Einheit keine Mana hat (<see cref="HasMana"/> == false).
        /// </summary>
        public bool ServerTryConsumeMana(int amount)
        {
            if (!IsServer || amount <= 0 || !HasMana)
            {
                return amount <= 0;
            }
            if (m_CurrentMana.Value < amount)
            {
                return false;
            }
            m_CurrentMana.Value -= amount;
            return true;
        }

        // -------------------------------------------------------------------------
        // Client-Fanout
        // -------------------------------------------------------------------------

        /// <summary>
        /// Feuert auf jedem Client (inkl. Host), sobald ein Schadens-Ereignis
        /// reinkommt. Parameter: <c>amount</c> = bereits gemilderter Final-
        /// Schaden, <c>result</c> = Hit-Klassifikation (Hit/Crit/Miss/…).
        /// Wird vom <see cref="FloatingCombatText"/> und ggf. Hit-Reactions
        /// abonniert. Keine Polling-Logik n&#246;tig.
        /// </summary>
        public event Action<int, HitResult> ClientDamageReceived;

        /// <summary>
        /// Verteilt das Schadens-Ereignis an alle Clients und l&#246;st das
        /// <see cref="ClientDamageReceived"/>-Event lokal aus.
        /// </summary>
        [ClientRpc]
        private void BroadcastDamageClientRpc(int amount, byte hitResult, ClientRpcParams _ = default)
        {
            HitResult result = (HitResult)hitResult;
            try
            {
                ClientDamageReceived?.Invoke(amount, result);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }
        
        /// <summary>
        /// Prueft, ob eine aktive Reincarnation-Aura den gerade eingetretenen
        /// Tod konsumiert und die Unit an Ort und Stelle wiederbelebt.
        /// </summary>
        private void TryTriggerReincarnationRevive()
        {
            if (!IsServer || !IsDead || m_Auras == null || m_Auras.Count == 0 || MaxHp <= 0)
            {
                return;
            }

            IReadOnlyList<Aura> activeAuras = m_Auras.All;
            for (int auraIndex = 0; auraIndex < activeAuras.Count; auraIndex++)
            {
                Aura aura = activeAuras[auraIndex];
                if (aura == null)
                {
                    continue;
                }

                for (int effectIndex = 0; effectIndex < aura.Effects.Count; effectIndex++)
                {
                    AuraEffect effect = aura.Effects[effectIndex];
                    if (effect == null || effect.AuraType != AuraType.RepopOntopOfSelf)
                    {
                        continue;
                    }

                    int reviveHealthPct = effect.BaseValue > 0 ? (int)effect.BaseValue : 40;
                    int reviveManaPct = effect.MiscValue > 0 ? (int)effect.MiscValue : 20;
                    int reviveHealth = (int)((long)MaxHp * reviveHealthPct / 100);
                    if (reviveHealth <= 0)
                    {
                        reviveHealth = 1;
                    }

                    int reviveMana = 0;
                    if (HasMana && m_MaxMana > 0)
                    {
                        reviveMana = (int)((long)m_MaxMana * reviveManaPct / 100);
                    }

                    if (!ServerRevive(reviveHealth, reviveMana))
                    {
                        return;
                    }

                    m_Auras.RemoveAura(aura.SourceSpellEntry, aura.CasterGuid);
                    return;
                }
            }
        }

        // -------------------------------------------------------------------------
        // ICombatUnit
        // -------------------------------------------------------------------------

        /// <inheritdoc/>
        ulong ICombatUnit.Guid => NetworkObject != null ? NetworkObject.NetworkObjectId : 0UL;

        /// <inheritdoc/>
        int ICombatUnit.Health => m_CurrentHp.Value;

        /// <inheritdoc/>
        int ICombatUnit.MaxHealth => MaxHp;

        /// <inheritdoc/>
        int ICombatUnit.Mana => m_CurrentMana.Value;

        /// <inheritdoc/>
        int ICombatUnit.MaxMana => m_MaxMana;

        /// <inheritdoc/>
        Vector3 ICombatUnit.Position => transform.position;

        /// <inheritdoc/>
        Vector3 ICombatUnit.Forward
        {
            get
            {
                // Topdown-Projektion auf XZ-Plane &#8212; verhindert Schraeg-Slides,
                // wenn die Visuals leicht geneigt aufgesetzt sind. Fallback auf
                // <c>Vector3.forward</c>, falls die Transform-Forward zu klein wird
                // (z. B. unmittelbar nach Spawn ohne Facing-Update).
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                float sqr = fwd.sqrMagnitude;
                if (sqr < 1e-4f)
                {
                    return Vector3.forward;
                }
                return fwd / Mathf.Sqrt(sqr);
            }
        }

        /// <inheritdoc/>
        bool ICombatUnit.IsStunned => m_Auras.IsStunned;

        /// <inheritdoc/>
        bool ICombatUnit.IsSilenced => m_Auras.IsSilenced;

        /// <inheritdoc/>
        bool ICombatUnit.IsRooted => m_Auras.IsRooted;

        /// <summary>
        /// Replizierte Sicht von <see cref="AuraManager.IsImmobilized"/> (Stun ODER Root).
        /// Auf jedem Peer lesbar &#8212; auf dem Server identisch mit dem Live-Aura-State,
        /// auf den Clients gespiegelt ueber <see cref="m_CcFlags"/>. Wird vom
        /// <see cref="PlayerMovement"/> sowohl in der Owner-
        /// Prediction als auch im Server-Authority-Pfad konsultiert, sodass beide
        /// Seiten denselben Bewegungs-Block sehen (keine Reconciliation-Rucker).
        /// </summary>
        public bool IsImmobilized =>
            IsServer ? m_Auras.IsImmobilized : (m_CcFlags.Value & k_CcFlagImmobilized) != 0;

        /// <summary>
        /// Server-autoritative Sicht auf den Stun-Zustand. Wird ausschliesslich
        /// fuer server-seitige Action-Gates (Auto-Attack-Gate in
        /// <see cref="PlayerCombat"/>, NPC-AI in
        /// <see cref="NpcController"/>) genutzt. Auf Clients
        /// stets <c>false</c> &#8212; Clients fragen
        /// <see cref="IsImmobilized"/> ab, das Stun ODER Root abdeckt.
        /// </summary>
        public bool IsStunned => IsServer && m_Auras != null && m_Auras.IsStunned;

        /// <summary>
        /// Server-autoritative Sicht auf den Fear-Zustand. Nur auf dem Server
        /// aussagekraeftig (Clients stets <c>false</c>) &#8212; die NPC-AI
        /// (<see cref="NpcController"/>) laeuft server-seitig und laesst die
        /// Einheit vom Fear-Verursacher wegfliehen. Ein zusaetzlicher
        /// Stun/Root (<see cref="IsStunned"/>/<see cref="IsImmobilized"/>)
        /// friert die Flucht ein.
        /// </summary>
        public bool IsFeared => IsServer && m_Auras != null && m_Auras.IsFeared;

    }
}
