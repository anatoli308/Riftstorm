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
        /// Liefert den unveraenderten Basiswert eines Primaerattributs OHNE
        /// Aura-Modifikatoren. Fuer Spieler aus dem <see cref="PlayerStats"/>-
        /// Aggregat (Basis + Item-Boni), fuer NPCs aus den Raw-Feldern. Dient
        /// als Vergleichswert fuer die HUD-Faerbung (siehe
        /// <see cref="GetDisplayStat"/>) und als Eingang fuer
        /// <see cref="ApplyStatAuraModifiers"/>.
        /// </summary>
        /// <param name="stat">Abgefragtes Primaerattribut.</param>
        /// <returns>Basiswert vor Aura-Modifikatoren.</returns>
        private int GetUnmodifiedStat(StatId stat)
        {
            // Courage/Fortitude werden nicht ueber PlayerStats aggregiert
            // (kein GetBase-Mapping) — direkt aus den Raw-Feldern lesen, damit
            // Basiswert und Aura-Modifikator auf derselben Quelle aufsetzen.
            if (stat == StatId.Courage) { return m_Courage; }
            if (stat == StatId.Fortitude) { return m_Fortitude; }
            if (m_PlayerStats != null)
            {
                return m_PlayerStats.GetTotal(stat);
            }
            return stat switch
            {
                StatId.Strength => m_Strength,
                StatId.Agility => m_Agility,
                StatId.Intelligence => m_Intelligence,
                StatId.Willpower => m_Willpower,
                _ => 0,
            };
        }

        /// <summary>
        /// Wendet aktive <see cref="AuraType.ModifyStat"/>-/
        /// <see cref="AuraType.ModifyStatPct"/>-Auren auf einen Primaerattribut-
        /// Basiswert an. Reihenfolge nach Source-Konvention:
        /// <c>(Basis + Flat) * (1 + Pct/100)</c>; das Ergebnis wird auf
        /// <c>&gt;= 0</c> geclamped. Greift fuer Spieler (PlayerStats-Aggregat)
        /// wie NPCs (Raw-Felder) gleichermassen, da nur der bereits aufgeloeste
        /// <paramref name="baseValue"/> erweitert wird. Damit wirken z. B. die
        /// Attribut-Reduktionen von <c>Infected Wound</c> (Maske 248 =
        /// Strength|Agility|Willpower|Intelligence) tatsaechlich im Kampf.
        /// </summary>
        /// <param name="stat">Betroffenes Primaerattribut.</param>
        /// <param name="baseValue">Bereits aggregierter Basiswert (Item-Boni
        /// bzw. Raw-Stat), bevor Aura-Modifier angewandt werden.</param>
        /// <returns>Effektiver Attributwert nach Aura-Modifikatoren.</returns>
        private int ApplyStatAuraModifiers(StatId stat, int baseValue)
        {
            GetStatAuraModifiers(stat, out int flat, out int pct);
            int withFlat = baseValue + flat;
            // Prozent-Bonus kaufmaennisch (weg von Null) runden, damit ein
            // +10%-Buff auf kleine Basiswerte (z. B. STR 5 -> +0.5) nicht durch
            // Integer-Division auf +0 verschluckt wird, sondern sichtbar +1
            // ergibt. Debuffs runden symmetrisch (z. B. -10% auf 5 -> -1).
            long scaled = (long)withFlat * pct;
            int pctBonus = (int)((scaled + (scaled >= 0 ? 50L : -50L)) / 100L);
            int effective = withFlat + pctBonus;
            return effective < 0 ? 0 : effective;
        }

        /// <summary>
        /// Ermittelt Flat- und Prozent-Aura-Modifikator fuer ein Attribut
        /// peer-korrekt: Auf dem Server liefert die autoritative
        /// <see cref="AuraManager"/>-Instanz (<see cref="m_Auras"/>) die exakten
        /// Summen. Auf reinen Clients ist <see cref="m_Auras"/> leer (nicht
        /// repliziert) — dort werden die Modifikatoren deterministisch aus den
        /// replizierten <see cref="ClientAuras"/> und den lokal geladenen
        /// Spell-Templates rekonstruiert (gleiche Daten, gleiche Formel).
        /// </summary>
        /// <param name="stat">Betroffenes Primaerattribut.</param>
        /// <param name="flat">Summe der Flat-Modifikatoren (ModifyStat).</param>
        /// <param name="pct">Summe der Prozent-Modifikatoren (ModifyStatPct).</param>
        private void GetStatAuraModifiers(StatId stat, out int flat, out int pct)
        {
            // 1L-Shift: hohe StatIds (z. B. ParryChanceBonus = 37 -> Bit 2^36,
            // Blessing of Defense) wuerden bei einem 32-Bit-int-Shift ueberlaufen
            // und die falsche Maske treffen. long haelt alle 42 Stat-Bits.
            long statMask = 1L << ((int)stat - 1);
            if (IsServer && m_Auras != null)
            {
                flat = m_Auras.GetStatFlatModifierTotal(statMask);
                pct = m_Auras.GetStatPctModifierTotal(statMask);
                return;
            }
            flat = ComputeClientAuraModifier(AuraType.ModifyStat, statMask);
            pct = ComputeClientAuraModifier(AuraType.ModifyStatPct, statMask);
        }

        /// <summary>
        /// Liefert einen prozentualen Aura-Modifikator (Schaden/Heilung)
        /// peer-korrekt: Server aus <see cref="m_Auras"/>, reine Clients aus der
        /// Rekonstruktion ueber <see cref="ComputeClientAuraModifier"/>. Ohne
        /// Stat-Maske, da diese Auren keine Attribut-Maske tragen.
        /// </summary>
        /// <param name="auraType">Gesuchter Modifikator-Aura-Typ.</param>
        /// <returns>Summierter Prozentwert (Vorzeichen-erhaltend).</returns>
        private int GetAuraPctMod(AuraType auraType)
        {
            if (IsServer && m_Auras != null)
            {
                return m_Auras.GetAuraModifierTotal(auraType);
            }
            return ComputeClientAuraModifier(auraType, 0);
        }

        /// <summary>
        /// Rekonstruiert auf einem reinen Client die Summe eines Aura-
        /// Modifikators aus den replizierten <see cref="ClientAuras"/> und den
        /// lokal geladenen Spell-Templates. Spiegelt die Server-Logik aus
        /// <see cref="AuraManager"/>: nur ApplyAura-Effekte des gesuchten
        /// <paramref name="wantType"/>, optional gefiltert ueber die Stat-Maske
        /// <paramref name="statMask"/> (0 = keine Masken-Pruefung). Der Wert je
        /// Effekt stammt aus <see cref="SpellUtils.CalculateEffectValue"/> und
        /// wird mit der Stack-Anzahl skaliert (Naeherung fuer Mehrfach-Stacks:
        /// lineare Skalierung; fuer Einzel-Stack-Auren wie Infected Wound exakt).
        /// </summary>
        /// <param name="wantType">Gesuchter Aura-Typ (ModifyStat, ModifyStatPct, Schadens-/Heil-Modifikatoren).</param>
        /// <param name="statMask">Stat-Bitmaske oder 0 fuer „keine Masken-Pruefung“.</param>
        /// <returns>Summierter Modifikatorwert, vorzeichenerhaltend.</returns>
        private int ComputeClientAuraModifier(AuraType wantType, long statMask)
        {
            // Reentrancy-Guard: caster-abhaengige Effekt-Formeln (z. B. Blessing of
            // Health ueber WIL) fragen beim Rekonstruieren die Caster-Stats ab, die
            // ihrerseits Aura-Modifier aufloesen. Bei verschiedenen Stats (Health
            // braucht WIL) unkritisch; ein hypothetischer self-referenzieller
            // Stat-Buff koennte aber endlos rekursieren. Der Guard bricht das ab und
            // nutzt fuer den verschachtelten Aufruf die unmodifizierten Basis-Stats.
            if (m_ReconstructingAuraModifier)
            {
                return 0;
            }
            m_ReconstructingAuraModifier = true;
            try
            {
                return ComputeClientAuraModifierCore(wantType, statMask);
            }
            finally
            {
                m_ReconstructingAuraModifier = false;
            }
        }

        /// <summary>Reentrancy-Guard fuer <see cref="ComputeClientAuraModifier"/>.</summary>
        private bool m_ReconstructingAuraModifier;

        /// <summary>
        /// Kern-Summierung fuer <see cref="ComputeClientAuraModifier"/>. Loest je
        /// Aura den Caster ueber <see cref="ResolveCasterUnit"/> auf, damit
        /// caster-abhaengige Formeln (z. B. Blessing of Health ueber WIL) mit den
        /// echten Caster-Stats statt Null-Kontext (alle Stats 0) rechnen.
        /// </summary>
        private int ComputeClientAuraModifierCore(AuraType wantType, long statMask)
        {
            if (m_ClientAuras == null || m_ClientAuras.Length == 0)
            {
                return 0;
            }
            long sum = 0;
            for (int i = 0; i < m_ClientAuras.Length; i++)
            {
                AuraSnapshot snap = m_ClientAuras[i];
                SpellTemplate tpl = SpellCatalogLoader.GetTemplateOrNull(snap.SpellEntry);
                if (tpl == null)
                {
                    continue;
                }
                ICombatUnit auraCaster = ResolveCasterUnit(snap.CasterGuid);
                int stacks = snap.Stacks < 1 ? 1 : snap.Stacks;
                int effectCount = tpl.EffectCount;
                for (int e = 1; e <= effectCount; e++)
                {
                    SpellTemplateEffect eff = tpl.GetEffect(e);
                    if (eff.Effect != SpellEffect.ApplyAura && eff.Effect != SpellEffect.ApplyAreaAura)
                    {
                        continue;
                    }
                    if ((AuraType)eff.Data1 != wantType)
                    {
                        continue;
                    }
                    if (statMask != 0 && (SpellUtils.GetAuraMiscValue(eff) & statMask) == 0)
                    {
                        continue;
                    }
                    sum += (long)SpellUtils.CalculateEffectValue(eff, auraCaster) * stacks;
                }
            }
            if (sum > int.MaxValue)
            {
                return int.MaxValue;
            }
            if (sum < int.MinValue)
            {
                return int.MinValue;
            }
            return (int)sum;
        }

        /// <summary>
        /// Loest die Caster-<see cref="ICombatUnit"/> eines Aura-Snapshots ueber
        /// ihre <see cref="ICombatUnit.Guid"/> (= NetworkObjectId) auf. Ermoeglicht
        /// reinen Clients, caster-abhaengige Effekt-Formeln (z. B. Blessing of
        /// Health mit WIL-Skalierung) mit den echten Caster-Stats zu rekonstruieren
        /// statt mit Null-Kontext (der alle Stats als 0 einsetzt). Liefert
        /// <c>null</c>, wenn der Caster nicht (mehr) gespawnt/repliziert ist &#8212;
        /// dann faellt <see cref="SpellUtils.CalculateEffectValue"/> sauber auf den
        /// Null-Kontext zurueck. Fuer Self-Buffs ist der Caster der lokale Spieler
        /// und damit stets verfuegbar.
        /// </summary>
        /// <param name="casterGuid">NetworkObjectId des Casters (0 = keine Quelle).</param>
        /// <returns>Caster-Unit oder <c>null</c>.</returns>
        private static ICombatUnit ResolveCasterUnit(ulong casterGuid)
        {
            if (casterGuid == 0UL)
            {
                return null;
            }
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null)
            {
                return null;
            }
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(casterGuid, out NetworkObject obj) || obj == null)
            {
                return null;
            }
            if (obj.TryGetComponent<UnitStats>(out var stats))
            {
                return stats;
            }
            return obj.GetComponentInChildren<UnitStats>();
        }

        /// <summary>
        /// Liefert fuer die Character-Stats-UI den Basiswert und den effektiven
        /// (Aura-modifizierten) Wert eines Primaerattributs in einem Aufruf —
        /// peer-korrekt auf Server wie Client. Die UI faerbt damit WoW-artig:
        /// rot wenn <paramref name="effectiveValue"/> &lt; <paramref name="baseValue"/>
        /// (Debuff, z. B. Infected Wound), gruen wenn groesser (Selbst-Buff).
        /// </summary>
        /// <param name="stat">Abgefragtes Primaerattribut.</param>
        /// <param name="baseValue">Basiswert ohne Aura-Modifikatoren.</param>
        /// <param name="effectiveValue">Effektivwert nach Aura-Modifikatoren.</param>
        public void GetDisplayStat(StatId stat, out int baseValue, out int effectiveValue)
        {
            baseValue = GetUnmodifiedStat(stat);
            effectiveValue = ApplyStatAuraModifiers(stat, baseValue);
        }

        /// <summary>
        /// Radius der Körper-Hitbox in Metern. Wird vom Server-Hit-Resolve als
        /// Reichweiten-Bonus addiert (siehe <see cref="m_HitRadius"/>).
        /// </summary>
        public float HitRadius => m_HitRadius;

        /// <summary>
        /// Effektiver Maus-Pick-Radius in Metern (LoL-„selectionRadius“). Liegt
        /// per Konvention leicht über <see cref="HitRadius"/>, damit Hovern und
        /// Anklicken klickfreundlich bleiben, ohne dass Skillshots am Modellrand
        /// vorbei zischen. Fällt auf <c>HitRadius * 1.2</c> zurück, wenn der
        /// Inspector-Wert ≤ 0 ist.
        /// </summary>
        public float SelectionRadius => m_SelectionRadius > 0f ? m_SelectionRadius : m_HitRadius * 1.2f;
        /// <summary>Geh-Geschwindigkeit dieser Einheit in m/s. Default 0 ⇒ stationär.</summary>
        public float WalkSpeed => m_WalkSpeed;

        /// <summary>Lauf-Geschwindigkeit dieser Einheit in m/s. Default 0 ⇒ kein Sprint.</summary>
        public float RunSpeed => m_RunSpeed;

        /// <summary>Intrinsische Melee-Reichweite in Metern. Waffen/Abilities können das überschreiben.</summary>
        public float AttackRange => m_AttackRange;

        /// <summary>Intrinsische Projektil-Reichweite in Metern. 0 ⇒ keine Standard-Range-Attacke.</summary>
        public float ProjectileRange => m_ProjectileRange;

        /// <summary>
        /// Anzeigename für Name-Tags und UI. Nie <c>null</c> — leer wenn nicht gesetzt.
        /// </summary>
        public string DisplayName => m_DisplayName ?? "";
        // -------------------------------------------------------------------------
        // IDamageable
        // -------------------------------------------------------------------------

        /// <inheritdoc/>
        public bool IsDead => m_CurrentHp.Value <= 0;

        /// <summary>
        /// Server-Event: wird einmalig ausgelöst, sobald HP auf ≤0 fällt. Wird
        /// vom Spawner / Combat-StateMachine benutzt, um in den Dead-State zu
        /// gehen oder Loot zu droppen.
        /// </summary>
        public event Action OnServerDied;

        /// <summary>
        /// Server-only: feuert nach jedem applizierten Schaden mit
        /// <c>FinalDamage &gt; 0</c>. Liefert den Angreifer (kann <c>null</c>
        /// sein bei Environment-Damage oder DoT ohne Caster-Ref), den
        /// applizierten Schaden und das Hit-Ergebnis. Wird vom
        /// <see cref="ThreatManager"/>-Hook im
        /// <see cref="NpcController"/> konsumiert, um
        /// Threat aufzubauen und Retaliation auszulösen.
        /// </summary>
        public event Action<UnitStats, int, HitResult> OnServerDamaged;

        /// <summary>
        /// Feuert auf jedem Peer, sobald sich die HP aendern (oder beim Spawn
        /// einmal initial). Erster Parameter = aktuelle HP, zweiter = MaxHp.
        /// Listener bauen damit ihre HP-Bar ohne Polling auf.
        /// </summary>
        public event Action<int, int> HpChanged;

        /// <summary>
        /// Feuert auf jedem Peer, sobald sich die Mana aendert (oder beim Spawn
        /// einmal initial). Erster Parameter = aktuelle Mana, zweiter = MaxMana.
        /// Bei <see cref="HasMana"/> == false bleibt der Wert auf 0.
        /// </summary>
        public event Action<int, int> ManaChanged;

    }
}
