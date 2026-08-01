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
        private AuraSnapshot[] m_ClientAuras = Array.Empty<AuraSnapshot>();

        /// <summary>
        /// Aktuelle Aura-Snapshots wie sie der Server zuletzt gebroadcastet hat.
        /// Auf dem Server identisch mit dem Server-State. Niemals <c>null</c>.
        /// </summary>
        public IReadOnlyList<AuraSnapshot> ClientAuras => m_ClientAuras;

        /// <summary>
        /// Feuert auf jedem Peer, sobald ein neuer Aura-Snapshot eintrifft.
        /// HUD-Komponenten (BuffBar, DebuffBar) bauen ihre Icon-Liste hierauf
        /// auf, ohne pollen zu muessen.
        /// </summary>
        public event Action ClientAurasChanged;

        /// <summary>
        /// Server-Hook auf <see cref="AuraManager.OnChanged"/>. Baut einen
        /// kompakten Snapshot der aktuellen Aura-Liste und sendet ihn an alle
        /// Clients (Observer). Filtert <see cref="AuraFlags.Hidden"/> und
        /// <see cref="AuraFlags.Passive"/> heraus &#8212; passive Auren haben per
        /// Konvention kein UI-Icon.
        /// </summary>
        private void ServerOnAurasChanged()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            IReadOnlyList<Aura> all = m_Auras.All;
            int count = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Aura a = all[i];
                if ((a.Flags & (AuraFlags.Hidden | AuraFlags.Passive)) != 0) { continue; }
                count++;
            }

            int[] entries = new int[count];
            byte[] stacks = new byte[count];
            byte[] positive = new byte[count];
            int[] remainingMs = new int[count];
            int[] maxDurationMs = new int[count];
            ulong[] casterIds = new ulong[count];

            int w = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Aura a = all[i];
                if ((a.Flags & (AuraFlags.Hidden | AuraFlags.Passive)) != 0) { continue; }
                entries[w] = a.SourceSpellEntry;
                stacks[w] = (byte)Mathf.Clamp(a.Stacks, 0, 255);
                positive[w] = (byte)(a.IsPositive ? 1 : 0);
                remainingMs[w] = a.RemainingMs;
                maxDurationMs[w] = a.MaxDurationMs;
                casterIds[w] = a.CasterGuid;
                w++;
            }

            BroadcastAurasClientRpc(entries, stacks, positive, remainingMs, maxDurationMs, casterIds);

            // CC-Flags + Move-Speed-Multiplier ebenfalls aus dem AuraManager spiegeln.
            // Wird ueber NetworkVariables (Everyone-Read) automatisch zu allen Peers
            // repliziert &#8212; <see cref="Movement.PlayerMovement"/> liest beide Werte
            // sowohl auf dem Owner (fuer Prediction) als auch auf dem Server.
            byte ccFlags = 0;
            if (m_Auras.IsImmobilized) { ccFlags |= k_CcFlagImmobilized; }
            if (m_Auras.IsSilenced)    { ccFlags |= k_CcFlagSilenced; }
            m_CcFlags.Value = ccFlags;
            float mult = m_Auras.MoveSpeedMultiplier;
            int milli = Mathf.Clamp(Mathf.RoundToInt(mult * 1000f), 0, 5000);
            m_MoveSpeedMultiplierMilli.Value = (short)milli;

            m_Auras.ClearDirty();

            // Aura-Aenderung kann das MaxHp-Cap verschoben haben (z. B. Blessing
            // of Health = ModifyStat auf StatId.Health). CurrentHp herunter-
            // klammern falls das Cap geschrumpft ist und HpChanged mit dem neuen
            // Cap refeuern, damit Player-/Target-Frame den Buff sofort rendern.
            ServerOnStatsChanged();
        }

        /// <summary>
        /// Fanout an alle Observer-Clients (inkl. Host). Parallele Arrays statt
        /// INetworkSerializable-Struct-Array, weil NGO primitive Arrays am
        /// stabilsten serialisiert.
        /// </summary>
        [ClientRpc]
        private void BroadcastAurasClientRpc(
            int[] entries,
            byte[] stacks,
            byte[] positive,
            int[] remainingMs,
            int[] maxDurationMs,
            ulong[] casterIds,
            ClientRpcParams _ = default)
        {
            int n = entries != null ? entries.Length : 0;
            AuraSnapshot[] next = n > 0 ? new AuraSnapshot[n] : Array.Empty<AuraSnapshot>();
            float now = Time.unscaledTime;
            for (int i = 0; i < n; i++)
            {
                next[i] = new AuraSnapshot(
                    spellEntry: entries[i],
                    stacks: stacks[i],
                    isPositive: positive[i] != 0,
                    remainingMs: remainingMs[i],
                    maxDurationMs: maxDurationMs[i],
                    receivedAt: now,
                    casterGuid: casterIds != null && i < casterIds.Length ? casterIds[i] : 0UL);
            }
            m_ClientAuras = next;
            try
            {
                ClientAurasChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }

            // Aura-Snapshot kann das lokal berechnete MaxHp-Cap veraendert haben
            // (ModifyStat auf StatId.Health). HpChanged auf reinen Clients neu
            // feuern, damit Player-/Target-Frame den neuen Max-Wert rendern.
            // Auf dem Host uebernimmt das ServerOnAurasChanged.
            if (!IsServer)
            {
                HpChanged?.Invoke(m_CurrentHp.Value, MaxHp);
            }
        }
    }
}
