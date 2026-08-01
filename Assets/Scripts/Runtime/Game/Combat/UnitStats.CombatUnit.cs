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
        /// <summary>
        /// Server-only: liefert die Weltposition des Fear-Verursachers ueber den
        /// <see cref="AuraManager"/>. Auf Clients stets <c>false</c>.
        /// </summary>
        /// <param name="position">Ausgabe: Weltposition des Fear-Casters.</param>
        public bool TryGetFearSourcePosition(out Vector3 position)
        {
            if (IsServer && m_Auras != null)
            {
                return m_Auras.TryGetFearSourcePosition(out position);
            }
            position = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Replizierte Sicht von <see cref="AuraManager.MoveSpeedMultiplier"/>. 1.0 = neutral,
        /// &lt; 1 = Snare/Slow, &gt; 1 = Haste. Auf dem Server live aus dem AuraManager,
        /// auf Clients aus <see cref="m_MoveSpeedMultiplierMilli"/> (FixedPoint &#215; 1000).
        /// </summary>
        public float MoveSpeedMultiplier =>
            IsServer ? m_Auras.MoveSpeedMultiplier : m_MoveSpeedMultiplierMilli.Value / 1000f;

        /// <inheritdoc/>
        bool ICombatUnit.IsPlayer => m_IsPlayer;

        /// <inheritdoc/>
        int ICombatUnit.FactionId => m_FactionId;

        /// <inheritdoc/>
        AuraManager ICombatUnit.Auras => m_Auras;

        /// <inheritdoc/>
        CooldownManager ICombatUnit.Cooldowns => m_Cooldowns;

        /// <inheritdoc/>
        IUnitStats ICombatUnit.Stats => this;

        /// <inheritdoc/>
        void ICombatUnit.TakeDamage(int amount, ICombatUnit attacker)
        {
            if (!IsServer || amount <= 0)
            {
                return;
            }
            // Schaden l\u00e4uft \u00fcber denselben Pfad wie Melee \u2014 ApplyDamage zieht HP ab,
            // feuert Death-Event und broadcastet an die Clients. Attacker wird
            // an die Overload weitergereicht, damit ThreatManager Quelle zuordnet.
            DamageInfo info = new()
            {
                BaseDamage = amount,
                FinalDamage = amount,
                HitResult = HitResult.Hit,
            };
            ApplyDamage(attacker as UnitStats, in info);
        }

        /// <inheritdoc/>
        void ICombatUnit.ApplyDamageInfo(in DamageInfo info, ICombatUnit attacker)
        {
            if (!IsServer)
            {
                return;
            }
            // Direkt in den autoritativen Schadenspfad: ApplyDamage broadcastet
            // auch FinalDamage <= 0 (Miss/Dodge/Parry/Immune/Resist/Absorb) an
            // alle Clients und erhaelt die HitResult-Klassifikation — damit der
            // FloatingCombatText die Avoidance-Labels wieder anzeigt. Kein
            // amount-Filter wie bei TakeDamage.
            ApplyDamage(attacker as UnitStats, in info);
        }

        /// <inheritdoc/>
        void ICombatUnit.Heal(int amount, ICombatUnit source)
        {
            if (!IsServer || amount <= 0 || IsDead)
            {
                return;
            }
            int previousHp = m_CurrentHp.Value;
            int newHp = Mathf.Min(MaxHp, previousHp + amount);
            if (newHp == previousHp)
            {
                return;
            }
            m_CurrentHp.Value = newHp;
            // Heal an alle Clients fanouten \u2014 reuse des Damage-Pfads mit
            // HitResult.Hit (Floating-Text-Renderer kann anhand des negativen
            // Vorzeichens unterscheiden, falls wir das Event sp\u00e4ter doch
            // splitten).
            BroadcastHealClientRpc(newHp - previousHp);
        }

        /// <inheritdoc/>
        void ICombatUnit.SetMana(int amount)
        {
            if (!IsServer)
            {
                return;
            }
            m_CurrentMana.Value = Mathf.Clamp(amount, 0, m_MaxMana);
        }

        /// <inheritdoc/>
        void ICombatUnit.ServerTeleportTo(Vector3 position)
        {
            if (!IsServer)
            {
                return;
            }
            if (m_PlayerMovement != null)
            {
                m_PlayerMovement.ServerTeleportTo(position);
                return;
            }
            if (m_NpcController != null)
            {
                m_NpcController.ServerTeleportTo(position);
                return;
            }
            // Fallback: keine Movement-Komponente &#8212; schreibt die Transform
            // direkt. Replikation an Remote-Clients erfolgt dann nicht (NPCs ohne
            // NpcController + Spieler ohne PlayerMovement existieren in der Praxis
            // nicht), wir loggen damit Setup-Fehler auffallen.
            Debug.LogWarning(
                "[UnitStats] ServerTeleportTo ohne Movement-Sibling &#8212; " +
                "Replikation entfaellt.",
                this);
            transform.position = position;
        }

        /// <inheritdoc/>
        void ICombatUnit.ServerApplyImpulse(Vector3 direction, float meters, float durationSec)
        {
            if (!IsServer)
            {
                return;
            }
            if (m_PlayerMovement != null)
            {
                m_PlayerMovement.ServerApplyImpulse(direction, meters, durationSec);
                return;
            }
            if (m_NpcController != null)
            {
                m_NpcController.ServerApplyImpulse(direction, meters, durationSec);
                return;
            }
            Debug.LogWarning(
                "[UnitStats] ServerApplyImpulse ohne Movement-Sibling &#8212; " +
                "Effekt wird verworfen.",
                this);
        }

        /// <inheritdoc/>
        bool ICombatUnit.IsImpulseActive
        {
            get
            {
                if (m_PlayerMovement != null) { return m_PlayerMovement.IsImpulseActive; }
                if (m_NpcController != null) { return m_NpcController.IsImpulseActive; }
                return false;
            }
        }

        /// <inheritdoc/>
        void ICombatUnit.RegisterImpulseCompletion(Action onComplete)
        {
            if (!IsServer) { return; }
            if (m_PlayerMovement != null)
            {
                m_PlayerMovement.RegisterImpulseCompletion(onComplete);
                return;
            }
            if (m_NpcController != null)
            {
                m_NpcController.RegisterImpulseCompletion(onComplete);
            }
        }

        /// <inheritdoc/>
        void ICombatUnit.ServerInterruptCast()
        {
            if (!IsServer) { return; }
            // Spieler-Casts laufen ueber PlayerCombat; NPC-Casts ueber deren AI.
            // Genau eine der beiden Komponenten ist pro Einheit gesetzt.
            if (m_PlayerCombat != null)
            {
                m_PlayerCombat.ServerInterruptCast();
                return;
            }
            if (m_NpcController != null)
            {
                m_NpcController.ServerInterruptCast();
            }
        }

        /// <inheritdoc/>
        void ICombatUnit.AddIncomingThreat(ICombatUnit source, int amount)
        {
            if (!IsServer || amount == 0 || source == null) { return; }
            // Nur NPCs fuehren ThreatTables &#8212; Spieler ignorieren den Eintrag.
            if (m_NpcController != null)
            {
                m_NpcController.AddIncomingThreat(source.Guid, amount);
            }
        }

        /// <summary>
        /// Feuert auf jedem Client (inkl. Host), sobald eine Heilung gelandet ist.
        /// Parameter: <c>amount</c> = tats\u00e4chlich applizierte HP (nach Overheal-Cap).
        /// </summary>
        public event Action<int> ClientHealReceived;

        /// <summary>
        /// Verteilt das Heal-Ereignis an alle Clients und l\u00f6st das
        /// <see cref="ClientHealReceived"/>-Event lokal aus.
        /// </summary>
        [ClientRpc]
        private void BroadcastHealClientRpc(int amount, ClientRpcParams _ = default)
        {
            try
            {
                ClientHealReceived?.Invoke(amount);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }
        // -------------------------------------------------------------------------
        // Aura-Replikation (Server -> Client Snapshot bei Aenderung)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Client-sichtbarer Schnappschuss einer einzelnen aktiven Aura. Wird
        /// vom Server bei jeder strukturellen Aenderung der Aura-Liste
        /// (Apply/Refresh/Stack/Remove/Expire) gebroadcastet. Die HUD-Schicht
        /// rechnet die verbleibende Dauer lokal aus
        /// (<see cref="MaxDurationMs"/> minus seit <see cref="ReceivedAt"/>
        /// vergangene Zeit), damit kein Per-Frame-Netcode noetig ist.
        /// </summary>
        public readonly struct AuraSnapshot
        {
            /// <summary>Source-Spell-Entry der Aura (Verweis auf SpellTemplate).</summary>
            public readonly int SpellEntry;
            /// <summary>Aktuelle Stack-Anzahl (1..MaxStacks).</summary>
            public readonly int Stacks;
            /// <summary>True = Buff, false = Debuff.</summary>
            public readonly bool IsPositive;
            /// <summary>Verbleibende Dauer in ms zum Broadcast-Zeitpunkt. -1 = permanent.</summary>
            public readonly int RemainingMs;
            /// <summary>Gesamtdauer in ms (0 = permanent).</summary>
            public readonly int MaxDurationMs;
            /// <summary>Client-Zeitstempel <c>Time.unscaledTime</c> beim Empfang. Fuer Cooldown-Sweep.</summary>
            public readonly float ReceivedAt;
            /// <summary>NetworkObjectId des Casters (0 = ohne Quelle). Erlaubt reinen
            /// Clients, caster-abhaengige Effekt-Formeln (z. B. Blessing of Health mit
            /// WIL-Skalierung) mit den echten Caster-Stats zu rekonstruieren statt mit
            /// Null-Kontext (alle Stats 0).</summary>
            public readonly ulong CasterGuid;

            /// <summary>Vollstaendiger Konstruktor.</summary>
            public AuraSnapshot(int spellEntry, int stacks, bool isPositive, int remainingMs, int maxDurationMs, float receivedAt, ulong casterGuid)
            {
                SpellEntry = spellEntry;
                Stacks = stacks;
                IsPositive = isPositive;
                RemainingMs = remainingMs;
                MaxDurationMs = maxDurationMs;
                ReceivedAt = receivedAt;
                CasterGuid = casterGuid;
            }
        }

    }
}
