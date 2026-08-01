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
        /// <inheritdoc/>
        public int Strength => ApplyStatAuraModifiers(StatId.Strength, GetUnmodifiedStat(StatId.Strength));

        /// <inheritdoc/>
        public int Agility => ApplyStatAuraModifiers(StatId.Agility, GetUnmodifiedStat(StatId.Agility));

        /// <inheritdoc/>
        public int Armor => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ArmorValue) : m_Armor;

        /// <inheritdoc/>
        public int Level => m_Level;

        /// <summary>
        /// Setzt das Anzeige-/Skalierungs-Level VOR dem Netcode-Spawn. Wird vom
        /// <see cref="FlareNpcSpawner"/> auf jedem Peer aus dem <c>npc_template</c>
        /// gerufen, analog zum <c>displayName</c>-Pfad in <see cref="ApplyBaseStats"/> —
        /// damit Target-Frame und HUD das echte Template-Level statt des
        /// Prefab-Defaults (1) zeigen. Geclamped auf &gt;= 1.
        /// </summary>
        /// <param name="level">Aufgeloestes Template-Level (z. B. aus min_level/max_level).</param>
        public void SetLevel(int level)
        {
            m_Level = Mathf.Max(1, level);
        }

        /// <summary>
        /// Bitmaske der Crowd-Control-Mechaniken, gegen die diese Einheit immun ist.
        /// Bit n entspricht <c>1 &lt;&lt; ((int)<see cref="Mechanic"/> - 1)</c> (Source-Konvention,
        /// SQL <c>mechanic_immune_mask</c>). Wird vom <see cref="AuraManager"/> beim Apply
        /// eines Mechanik-Effekts geprueft.
        /// </summary>
        public int MechanicImmuneMask => m_MechanicImmuneMask;

        /// <summary>
        /// Setzt die Mechanik-Immun-Maske VOR dem Netcode-Spawn. Wird vom
        /// <see cref="FlareNpcSpawner"/> bzw. <see cref="NpcController"/> aus dem
        /// <c>npc_template</c> gerufen, analog zum <see cref="SetLevel"/>-Pfad.
        /// </summary>
        /// <param name="mask">Roh-Bitmaske aus <c>mechanic_immune_mask</c>.</param>
        public void SetMechanicImmuneMask(int mask)
        {
            m_MechanicImmuneMask = Mathf.Max(0, mask);
        }

        /// <summary>
        /// Setzt Melee- und Ranged-Waffenschaden eines NPCs VOR dem Netcode-
        /// Spawn. Notwendig, damit NPC-Auto-Attacks ueber die WeaponDamage-
        /// Spell-Pipeline (Spell 81/82, 100%-Waffenschaden-Pfad) ueberhaupt
        /// Schaden tragen — ohne diesen Wert bleiben <see cref="WeaponDamage"/>
        /// und <see cref="RangedWeaponDamage"/> fuer NPCs 0. Wird vom
        /// <see cref="FlareNpcSpawner"/> aus <c>npc_template.weapon_value</c> /
        /// <c>ranged_weapon_value</c> gerufen, analog zu <see cref="SetLevel"/>.
        /// Fuer Spieler ist der Setter wirkungslos, da deren Werte ueber
        /// <see cref="PlayerStats"/> aggregiert werden.
        /// </summary>
        /// <param name="melee">Melee-Grundschaden aus <c>weapon_value</c>.</param>
        /// <param name="ranged">Ranged-Grundschaden aus <c>ranged_weapon_value</c>.</param>
        public void SetWeaponDamage(int melee, int ranged)
        {
            m_WeaponDamage = Mathf.Max(0, melee);
            m_RangedWeaponDamage = Mathf.Max(0, ranged);
        }

        /// <inheritdoc/>
        public int Intelligence => ApplyStatAuraModifiers(StatId.Intelligence, GetUnmodifiedStat(StatId.Intelligence));

        /// <inheritdoc/>
        public int Willpower => ApplyStatAuraModifiers(StatId.Willpower, GetUnmodifiedStat(StatId.Willpower));

        /// <inheritdoc/>
        public int WeaponDamage => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.WeaponValue) : m_WeaponDamage;

        /// <summary>Ranged-Waffenschaden (Original 'RangedWeaponValue'). Aggregiert Item-Boni
        /// ueber <see cref="PlayerStats"/>, sobald gesetzt — sonst Raw-Inspector-Wert.</summary>
        public int RangedWeaponDamage => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.RangedWeaponValue) : m_RangedWeaponDamage;

        /// <inheritdoc/>
        /// <remarks>
        /// Spieler: liest <c>PlayerCombat.CurrentWeapon.BaseDamage</c>, sofern eine
        /// Melee-Waffe equipped ist (inkl. "unarmed"-Fallback). NPCs: 0, weil sie
        /// ihren Schaden ueber den <see cref="WeaponDamage"/>-Stat modellieren.
        /// </remarks>
        public int BaseWeaponDamage
        {
            get
            {
                if (m_PlayerCombat == null) { return 0; }
                WeaponDefinition w = m_PlayerCombat.CurrentWeapon;
                if (w == null || w.IsRanged) { return 0; }
                return w.BaseDamage;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Spieler: liest <c>PlayerCombat.CurrentRangedWeapon.BaseDamage</c> aus
        /// dem dedizierten Ranged-Slot (Bow/Crossbow/Gun). Liegt im Slot keine
        /// Ranged-Waffe, ist das Ergebnis 0 — damit blockt
        /// <c>SpellCaster.CheckEquipment</c> Ranged-Spells
        /// (<c>required_equipment=12</c>) mit <c>NoRangedWeapon</c> und das HUD
        /// zeigt den Ranged-Schaden korrekt als 0. NPCs: 0 (siehe
        /// <see cref="BaseWeaponDamage"/>).
        /// </remarks>
        public int BaseRangedWeaponDamage
        {
            get
            {
                if (m_PlayerCombat == null) { return 0; }
                WeaponDefinition w = m_PlayerCombat.CurrentRangedWeapon;
                if (w == null) { return 0; }
                return w.BaseDamage;
            }
        }

        /// <summary>Fortitude — fuenftes Primary-Attribut, skaliert HP. Reines Display.</summary>
        public int Fortitude => m_Fortitude;

        /// <summary>
        /// Courage — Primary-Attribut (klassenspezifisch). Wird von Attribut-Auren
        /// (Maske 248 = Strength|Agility|Willpower|Intelligence|Courage, z. B.
        /// Blessing of Champions) mit erfasst und daher aura-modifiziert geliefert.
        /// </summary>
        public int Courage => ApplyStatAuraModifiers(StatId.Courage, GetUnmodifiedStat(StatId.Courage));

        /// <summary>HP-Regeneration pro Tick (Original 'Regeneration'). Reines Display.</summary>
        public int HpRegen => m_HpRegen;

        /// <summary>Mana-Regeneration pro Tick (Original 'Meditate'). Reines Display.</summary>
        public int ManaRegen => m_ManaRegen;

        /// <inheritdoc/>
        /// <remarks>
        /// Setzt sich aus dem reinen Rating (Gear/Talente) + STR-Skill-Bonus
        /// (+1 % pro 20 STR, WoW-Classic-Faktor) zusammen. Damit lohnt sich
        /// STR fuer Melee-Builds doppelt: mehr Grundschaden (STR/2 in
        /// <c>CalculateMeleeDamage</c>) und mehr Crit.
        /// </remarks>
        public int MeleeCritChance
        {
            get
            {
                int rating = m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.MeleeCritical) : m_MeleeCritChance;
                return rating + (Strength / 20);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Setzt sich zusammen aus dem reinen Rating (Gear/Talente) UND einem
        /// AGI-abhaengigen Skill-Bonus (+1 % pro 53 AGI, Classic-Hunter-Faktor).
        /// Dadurch lohnt sich AGI fuer Ranged-Builds doppelt: mehr Grundschaden
        /// (siehe <c>CombatFormulas.CalculateSpellDamage</c> Ranged-Branch) und
        /// mehr Crit. Im HUD wird der finale Wert angezeigt.
        /// </remarks>
        public int RangedCritChance
        {
            get
            {
                int rating = m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.RangedCritical) : m_RangedCritChance;
                return rating + (Agility / 53);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Rohes Rating (Gear/Talente) + INT-Skill-Bonus (+1 % pro 30 INT,
        /// WoW-Classic-Faktor). INT skaliert damit Spell-Damage doppelt:
        /// Grundschaden (INT/20 in <c>CalculateSpellDamage</c>) und Crit.
        /// Heal-Crit nutzt zusätzlich Willpower/40 — direkt in
        /// <see cref="CombatFormulas.CalculateSpellHeal"/> verrechnet,
        /// nicht über diese Property (sonst würde WIL auf Spell-Damage-Crit
        /// "leaken").
        /// </remarks>
        public int SpellCritChance
        {
            get
            {
                int rating = m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.SpellCritical) : m_SpellCritChance;
                return rating + (Intelligence / 30);
            }
        }

        /// <inheritdoc/>
        public int DodgeChance => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.DodgeRating) : m_DodgeChance;

        /// <inheritdoc/>
        /// <remarks>
        /// Aggregierte finale Parry-% — geht durch
        /// <see cref="CombatFormulas.GetParryChance"/>, das aus
        /// <see cref="ParryRating"/> + <see cref="ParryChanceBonus"/> +
        /// <c>CRG/30</c> + <c>BASE_PARRY_CHANCE</c> kombiniert (Cap 75 %).
        /// Liefert 0, wenn <see cref="HasWeapon"/> false ist.
        /// </remarks>
        public int ParryChance => CombatFormulas.GetParryChance(this, HasWeapon);

        /// <inheritdoc/>
        public int ParryRating => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ParryRating) : m_ParryChance;

        /// <inheritdoc/>
        /// <remarks>
        /// Wendet Aura-Stat-Modifikatoren an (z. B. <c>Blessing of Defense</c>
        /// = flat <c>ModifyStat</c> auf die ParryChanceBonus-Maske), damit der
        /// Buff die finale Parry-% in <see cref="CombatFormulas.GetParryChance"/>
        /// und damit die Character-UI tatsaechlich erhoeht.
        /// </remarks>
        public int ParryChanceBonus => ApplyStatAuraModifiers(
            StatId.ParryChanceBonus,
            m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ParryChanceBonus) : m_ParryChanceBonus);

        /// <inheritdoc/>
        /// <remarks>
        /// Aggregierte finale Block-% — geht durch
        /// <see cref="CombatFormulas.GetBlockChance"/>, das aus
        /// <see cref="BlockRating"/> + <see cref="BlockChanceBonus"/> +
        /// <c>ShieldSkill/5</c> + <c>FRT/30</c> kombiniert (Cap 75 %).
        /// Liefert 0, wenn <see cref="HasShield"/> false ist.
        /// </remarks>
        public int BlockChance => CombatFormulas.GetBlockChance(this, HasShield);

        /// <inheritdoc/>
        public int BlockRating => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.BlockRating) : m_BlockChance;

        /// <inheritdoc/>
        /// <remarks>
        /// Wendet Aura-Stat-Modifikatoren an (flat/prozentual auf die
        /// BlockChanceBonus-Maske), analog zu <see cref="ParryChanceBonus"/>,
        /// damit defensive Buffs/Debuffs die finale Block-% beeinflussen.
        /// </remarks>
        public int BlockChanceBonus => ApplyStatAuraModifiers(
            StatId.BlockChanceBonus,
            m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.BlockChanceBonus) : m_BlockChanceBonus);

        /// <inheritdoc/>
        public int ShieldSkill => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ShieldSkill) : m_ShieldSkill;

        /// <inheritdoc/>
        /// <remarks>
        /// Spieler: <c>true</c>, sobald <see cref="PlayerCombat.CurrentWeapon"/>
        /// nicht null ist (Default-Weapon zählt — Faust ist auch eine Waffe).
        /// NPCs (kein PlayerCombat-Sibling): immer <c>true</c>, weil das
        /// C++-Vorbild den Weapon-Check nur für <c>Player*</c> macht.
        /// </remarks>
        public bool HasWeapon => m_PlayerCombat == null || m_PlayerCombat.CurrentWeapon != null;

        /// <inheritdoc/>
        /// <remarks>
        /// Proxy via <see cref="ShieldSkill"/> &gt; 0 — gilt für Spieler
        /// (Schild-Items granten Shield-Skill als Item-Bonus) und NPCs
        /// (Source: <c>getStatValue(victim, ShieldSkill) == 0 ⇒ kein Block</c>,
        /// <c>CombatFormulas.cpp</c> L208-209). Sobald ein dedizierter
        /// Offhand-Shield-Typ existiert, kann das hier verfeinert werden.
        /// </remarks>
        public bool HasShield => ShieldSkill > 0;

        /// <inheritdoc/>
        public int ResistFire => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ResistFire) : m_ResistFire;

        /// <inheritdoc/>
        public int ResistFrost => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ResistFrost) : m_ResistFrost;

        /// <inheritdoc/>
        public int ResistArcane => m_ResistArcane;

        /// <inheritdoc/>
        public int ResistNature => m_ResistNature;

        /// <inheritdoc/>
        public int ResistShadow => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ResistShadow) : m_ResistShadow;

        /// <inheritdoc/>
        public int ResistHoly => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ResistHoly) : m_ResistHoly;

        // -------------------------------------------------------------------------
        // Raw-Accessors fuer den Stat-Aggregator
        // -------------------------------------------------------------------------
        // <see cref="PlayerStats.GetBase"/> liest diese rohen Inspector-Werte,
        // um den Zyklus PlayerStats → UnitStats.Strength → PlayerStats zu
        // vermeiden. NPCs nutzen sie nicht — deren oeffentliche IUnitStats-
        // Getter fallen ohnehin direkt auf <c>m_X</c> zurueck (m_PlayerStats=null).
        internal int RawMaxHp => m_MaxHp;
        internal int RawStrength => m_Strength;
        internal int RawAgility => m_Agility;
        internal int RawArmor => m_Armor;
        internal int RawIntelligence => m_Intelligence;
        internal int RawWillpower => m_Willpower;
        internal int RawWeaponDamage => m_WeaponDamage;
        internal int RawRangedWeaponDamage => m_RangedWeaponDamage;
        internal int RawMeleeCritChance => m_MeleeCritChance;
        internal int RawRangedCritChance => m_RangedCritChance;
        internal int RawSpellCritChance => m_SpellCritChance;
        internal int RawDodgeChance => m_DodgeChance;
        internal int RawParryChance => m_ParryChance;
        internal int RawParryChanceBonus => m_ParryChanceBonus;
        internal int RawBlockChance => m_BlockChance;
        internal int RawBlockChanceBonus => m_BlockChanceBonus;
        internal int RawShieldSkill => m_ShieldSkill;
        internal int RawResistFire => m_ResistFire;
        internal int RawResistFrost => m_ResistFrost;
        internal int RawResistShadow => m_ResistShadow;
        internal int RawResistHoly => m_ResistHoly;

        /// <inheritdoc/>
        public int DamageDealtPctMod => GetAuraPctMod(AuraType.ModifyDamageDealtPct);

        /// <inheritdoc/>
        public int DamageReceivedPctMod => GetAuraPctMod(AuraType.ModifyDamageReceivedPct);

        /// <inheritdoc/>
        public int GetDamageReceivedPctMod(int schoolIndex)
        {
            // Server: schul-gefilterte Summe (z. B. Fortification Aura mit
            // Maske 1 = nur Physical). Client: fallback auf die schul-agnostische
            // Gesamtsumme — Schaden ist ohnehin server-autoritativ, der Client-
            // Wert dient nur der Prediction/Anzeige.
            if (IsServer && m_Auras != null)
            {
                return m_Auras.GetDamageReceivedPctMod((SpellSchool)schoolIndex);
            }
            return DamageReceivedPctMod;
        }

        /// <inheritdoc/>
        public int HealingDealtPctMod => GetAuraPctMod(AuraType.ModifyHealingDealtPct);

        /// <inheritdoc/>
        public int HealingReceivedPctMod => GetAuraPctMod(AuraType.ModifyHealingRecvPct);

    }
}
