using System.Collections.Generic;
using System.Diagnostics;

namespace Riftstorm.Gameplay.Combat.Spells
{
    /// <summary>
    /// Eintrag in der Cooldown-Map.
    /// </summary>
    /// <remarks>
    /// 1:1-Port von <c>CooldownManager.h::CooldownEntry</c>.
    /// </remarks>
    public sealed class CooldownEntry
    {
        /// <summary>Spell-Id (z. B. <c>"fireball"</c>).</summary>
        public string SpellId = string.Empty;
        /// <summary>Ende-Zeitpunkt in Millisekunden (steady-clock-Domäne).</summary>
        public long EndTimeMs;
        /// <summary>Geteilte Cooldown-Kategorie (leer = eigener Slot).</summary>
        public string CategoryId = string.Empty;
    }

    /// <summary>
    /// Per-Unit Cooldown-Tracking für Spells, Items und Kategorien (Shared CDs)
    /// plus globalem Cooldown (GCD).
    /// </summary>
    /// <remarks>
    /// 1:1-Port von <c>source_server/Server/src/Combat/CooldownManager.h/.cpp</c>.
    /// Zeitquelle: monotone <see cref="Stopwatch"/>, prozessweit geteilt — entspricht
    /// <c>std::chrono::steady_clock</c> im Source.
    /// </remarks>
    public sealed class CooldownManager
    {
        /// <summary>Default-GCD-Dauer in Millisekunden (1.5 s).</summary>
        public const int GcdDurationMs = 1500;
        /// <summary>Cooldowns unter diesem Wert nicht an Clients pushen.</summary>
        public const int MinCooldownToSendMs = 100;

        static readonly Stopwatch s_Clock = Stopwatch.StartNew();
        static long Now => s_Clock.ElapsedMilliseconds;

        readonly Dictionary<string, CooldownEntry> m_Cooldowns = new();
        readonly Dictionary<string, long> m_CategoryCooldowns = new();
        long m_GcdEndTime;

        /// <summary>Anzahl aktiver Spell-Cooldowns (für Debug-Anzeige).</summary>
        public int ActiveCount => m_Cooldowns.Count;

        // =====================================================================
        // Start
        // =====================================================================

        /// <summary>Setzt einen Cooldown für <paramref name="spellId"/>.</summary>
        public void StartCooldown(string spellId, int durationMs, string categoryId = "")
        {
            if (durationMs <= 0 || string.IsNullOrEmpty(spellId))
            {
                return;
            }
            long endTime = Now + durationMs;

            if (!m_Cooldowns.TryGetValue(spellId, out CooldownEntry entry))
            {
                entry = new() { SpellId = spellId };
                m_Cooldowns[spellId] = entry;
            }
            entry.EndTimeMs = endTime;
            entry.CategoryId = categoryId ?? string.Empty;

            if (!string.IsNullOrEmpty(categoryId))
            {
                if (!m_CategoryCooldowns.TryGetValue(categoryId, out long existing) || existing < endTime)
                {
                    m_CategoryCooldowns[categoryId] = endTime;
                }
            }
        }

        /// <summary>Triggert den globalen Cooldown (GCD).</summary>
        public void StartGcd() => m_GcdEndTime = Now + GcdDurationMs;

        // =====================================================================
        // Queries
        // =====================================================================

        /// <summary>True, wenn <paramref name="spellId"/> noch nicht bereit ist.</summary>
        public bool IsOnCooldown(string spellId)
        {
            if (!m_Cooldowns.TryGetValue(spellId, out CooldownEntry entry))
            {
                return false;
            }
            return Now < entry.EndTimeMs;
        }

        /// <summary>True, wenn der globale Cooldown läuft.</summary>
        public bool IsOnGcd() => Now < m_GcdEndTime;

        /// <summary>True, wenn die geteilte Kategorie noch nicht bereit ist.</summary>
        public bool IsCategoryOnCooldown(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId))
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
        public int GetRemainingCooldown(string spellId)
        {
            if (!m_Cooldowns.TryGetValue(spellId, out CooldownEntry entry))
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

        /// <summary>Verbleibende Kategorie-Cooldown-Zeit in Millisekunden.</summary>
        public int GetRemainingCategoryCooldown(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)
                || !m_CategoryCooldowns.TryGetValue(categoryId, out long endTime))
            {
                return 0;
            }
            long now = Now;
            return now >= endTime ? 0 : (int)(endTime - now);
        }

        // =====================================================================
        // Mutation
        // =====================================================================

        /// <summary>Setzt alle Cooldowns + GCD zurück.</summary>
        public void ClearAll()
        {
            m_Cooldowns.Clear();
            m_CategoryCooldowns.Clear();
            m_GcdEndTime = 0;
        }

        /// <summary>Setzt einen einzelnen Spell-Cooldown zurück.</summary>
        public void ClearCooldown(string spellId)
        {
            if (!m_Cooldowns.TryGetValue(spellId, out CooldownEntry entry))
            {
                return;
            }
            string category = entry.CategoryId;
            m_Cooldowns.Remove(spellId);
            RecalculateCategoryEnd(category);
        }

        /// <summary>Setzt alle Cooldowns einer Kategorie zurück.</summary>
        public void ClearCategory(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId))
            {
                return;
            }
            List<string> toRemove = null;
            foreach (KeyValuePair<string, CooldownEntry> kvp in m_Cooldowns)
            {
                if (kvp.Value.CategoryId == categoryId)
                {
                    toRemove ??= new();
                    toRemove.Add(kvp.Key);
                }
            }
            if (toRemove != null)
            {
                foreach (string id in toRemove)
                {
                    m_Cooldowns.Remove(id);
                }
            }
            m_CategoryCooldowns.Remove(categoryId);
        }

        /// <summary>Reduziert einen Cooldown um <paramref name="amountMs"/> Millisekunden.</summary>
        public void ReduceCooldown(string spellId, int amountMs)
        {
            if (!m_Cooldowns.TryGetValue(spellId, out CooldownEntry entry))
            {
                return;
            }
            entry.EndTimeMs -= amountMs;
            if (entry.EndTimeMs <= Now)
            {
                string category = entry.CategoryId;
                m_Cooldowns.Remove(spellId);
                RecalculateCategoryEnd(category);
            }
        }

        /// <summary>
        /// Reduziert alle Cooldowns + GCD um <paramref name="percent"/> (0..1).
        /// </summary>
        public void ReduceAllByPercent(float percent)
        {
            if (percent <= 0f || percent > 1f)
            {
                return;
            }
            long now = Now;
            foreach (CooldownEntry entry in m_Cooldowns.Values)
            {
                long remaining = entry.EndTimeMs - now;
                if (remaining > 0)
                {
                    entry.EndTimeMs -= (long)(remaining * percent);
                }
            }
            // Dictionary-Values sind Werttyp (long) → über Schlüssel-Snapshot reduzieren.
            List<string> keys = new(m_CategoryCooldowns.Keys);
            foreach (string key in keys)
            {
                long end = m_CategoryCooldowns[key];
                long remaining = end - now;
                if (remaining > 0)
                {
                    m_CategoryCooldowns[key] = end - (long)(remaining * percent);
                }
            }
            if (m_GcdEndTime > now)
            {
                long remaining = m_GcdEndTime - now;
                m_GcdEndTime -= (long)(remaining * percent);
            }
        }

        /// <summary>
        /// Liste aller aktiven (spellId, remainingMs)-Tupel. Wird beim Login an
        /// den Client gepusht (Client-UI rekonstruiert daraus die CD-Anzeige).
        /// </summary>
        public List<(string SpellId, int RemainingMs)> GetAllCooldowns()
        {
            List<(string, int)> result = new(m_Cooldowns.Count);
            long now = Now;
            foreach (CooldownEntry entry in m_Cooldowns.Values)
            {
                if (entry.EndTimeMs > now)
                {
                    result.Add((entry.SpellId, (int)(entry.EndTimeMs - now)));
                }
            }
            return result;
        }

        /// <summary>Räumt abgelaufene Einträge auf (optional, sonst lazy).</summary>
        public void Cleanup()
        {
            long now = Now;
            List<string> toRemove = null;
            foreach (KeyValuePair<string, CooldownEntry> kvp in m_Cooldowns)
            {
                if (kvp.Value.EndTimeMs <= now)
                {
                    toRemove ??= new();
                    toRemove.Add(kvp.Key);
                }
            }
            if (toRemove != null)
            {
                foreach (string id in toRemove)
                {
                    m_Cooldowns.Remove(id);
                }
            }
            List<string> catKeys = new(m_CategoryCooldowns.Keys);
            foreach (string key in catKeys)
            {
                if (m_CategoryCooldowns[key] <= now)
                {
                    m_CategoryCooldowns.Remove(key);
                }
            }
        }

        void RecalculateCategoryEnd(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId))
            {
                return;
            }
            long max = 0;
            foreach (CooldownEntry entry in m_Cooldowns.Values)
            {
                if (entry.CategoryId == categoryId && entry.EndTimeMs > max)
                {
                    max = entry.EndTimeMs;
                }
            }
            if (max > 0)
            {
                m_CategoryCooldowns[categoryId] = max;
            }
            else
            {
                m_CategoryCooldowns.Remove(categoryId);
            }
        }
    }
}
