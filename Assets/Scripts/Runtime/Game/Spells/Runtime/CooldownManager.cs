using System.Collections.Generic;
using System.Diagnostics;

namespace Riftstorm.Game.Spells
{
    /// <summary>
    /// Eintrag in der Cooldown-Map.
    /// </summary>
    public sealed class CooldownEntry
    {
        /// <summary>Spell-Entry (1:1 zur DB-Spalte <c>id</c>).</summary>
        public int SpellEntry;
        /// <summary>Ende-Zeitpunkt in Millisekunden (steady-clock-Domäne).</summary>
        public long EndTimeMs;
        /// <summary>Geteilte Cooldown-Kategorie (0 = eigener Slot).</summary>
        public int CategoryId;
    }

    /// <summary>
    /// Per-Unit Cooldown-Tracking für Spells und Kategorien (Shared CDs) plus
    /// globalem Cooldown (GCD). Schlüssel sind <see cref="int"/>-Entries
    /// (analog <c>SpellTemplate.Entry</c>).
    /// </summary>
    /// <remarks>
    /// Server-only — Cooldowns leben auf dem Server, Clients sehen sie
    /// optional über separate Sync-Pfade. Zeitquelle: monotone
    /// <see cref="Stopwatch"/>, prozessweit geteilt (entspricht
    /// <c>std::chrono::steady_clock</c>).
    /// </remarks>
    public sealed class CooldownManager
    {
        /// <summary>Default-GCD-Dauer in Millisekunden (1.5 s).</summary>
        public const int GcdDurationMs = 1500;
        /// <summary>Cooldowns unter diesem Wert nicht an Clients pushen.</summary>
        public const int MinCooldownToSendMs = 100;

        static readonly Stopwatch s_Clock = Stopwatch.StartNew();
        static long Now => s_Clock.ElapsedMilliseconds;

        readonly Dictionary<int, CooldownEntry> m_Cooldowns = new();
        readonly Dictionary<int, long> m_CategoryCooldowns = new();
        long m_GcdEndTime;

        /// <summary>Anzahl aktiver Spell-Cooldowns (für Debug-Anzeige).</summary>
        public int ActiveCount => m_Cooldowns.Count;

        /// <summary>Setzt einen Cooldown für <paramref name="spellEntry"/>.</summary>
        public void StartCooldown(int spellEntry, int durationMs, int categoryId = 0)
        {
            if (durationMs <= 0 || spellEntry <= 0)
            {
                return;
            }
            long endTime = Now + durationMs;

            if (!m_Cooldowns.TryGetValue(spellEntry, out CooldownEntry entry))
            {
                entry = new() { SpellEntry = spellEntry };
                m_Cooldowns[spellEntry] = entry;
            }
            entry.EndTimeMs = endTime;
            entry.CategoryId = categoryId;

            if (categoryId != 0)
            {
                if (!m_CategoryCooldowns.TryGetValue(categoryId, out long existing) || existing < endTime)
                {
                    m_CategoryCooldowns[categoryId] = endTime;
                }
            }
        }

        /// <summary>Triggert den globalen Cooldown (GCD).</summary>
        public void StartGcd() => m_GcdEndTime = Now + GcdDurationMs;

        /// <summary>Triggert einen benutzerdefinierten GCD-Wert.</summary>
        public void StartGcd(int durationMs) => m_GcdEndTime = Now + durationMs;

        /// <summary>True, wenn <paramref name="spellEntry"/> noch nicht bereit ist.</summary>
        public bool IsOnCooldown(int spellEntry)
        {
            if (!m_Cooldowns.TryGetValue(spellEntry, out CooldownEntry entry))
            {
                return false;
            }
            return Now < entry.EndTimeMs;
        }

        /// <summary>True, wenn der globale Cooldown läuft.</summary>
        public bool IsOnGcd() => Now < m_GcdEndTime;

        /// <summary>True, wenn die geteilte Kategorie noch nicht bereit ist.</summary>
        public bool IsCategoryOnCooldown(int categoryId)
        {
            if (categoryId == 0)
            {
                return false;
            }
            if (!m_CategoryCooldowns.TryGetValue(categoryId, out long endTime))
            {
                return false;
            }
            return Now < endTime;
        }

        /// <summary>Verbleibende Cooldown-Zeit in Millisekunden (0 = bereit).</summary>
        public int GetRemainingCooldown(int spellEntry)
        {
            if (!m_Cooldowns.TryGetValue(spellEntry, out CooldownEntry entry))
            {
                return 0;
            }
            long now = Now;
            return now >= entry.EndTimeMs ? 0 : (int)(entry.EndTimeMs - now);
        }

        /// <summary>Verbleibende GCD-Zeit in Millisekunden.</summary>
        public int GetRemainingGcd()
        {
            long now = Now;
            return now >= m_GcdEndTime ? 0 : (int)(m_GcdEndTime - now);
        }

        /// <summary>Setzt alle Cooldowns + GCD zurück.</summary>
        public void ClearAll()
        {
            m_Cooldowns.Clear();
            m_CategoryCooldowns.Clear();
            m_GcdEndTime = 0;
        }

        /// <summary>Setzt einen einzelnen Spell-Cooldown zurück.</summary>
        public void ClearCooldown(int spellEntry) => m_Cooldowns.Remove(spellEntry);

        /// <summary>
        /// Liste aller aktiven (entry, remainingMs)-Tupel. Wird beim Login an
        /// den Client gepusht.
        /// </summary>
        public List<(int SpellEntry, int RemainingMs)> GetAllCooldowns()
        {
            List<(int, int)> result = new(m_Cooldowns.Count);
            long now = Now;
            foreach (CooldownEntry entry in m_Cooldowns.Values)
            {
                if (entry.EndTimeMs > now)
                {
                    result.Add((entry.SpellEntry, (int)(entry.EndTimeMs - now)));
                }
            }
            return result;
        }
    }
}
