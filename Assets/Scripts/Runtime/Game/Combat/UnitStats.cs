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
    /// <summary>
    /// Server-autoritative HP- und Combat-Stats-Komponente. Hängt auf jeder
    /// Einheit (Spieler, NPC) und ist die einzige Stelle, an der HP-Werte
    /// verändert werden dürfen.
    ///
    /// <para>
    /// Implementiert <see cref="IDamageable"/> für eingehenden Schaden,
    /// <see cref="IUnitStats"/> als Lese-Schnittstelle für die
    /// <see cref="CombatFormulas"/>, und <see cref="ICombatUnit"/> als
    /// Schreib-/Lese-Fassade für die Spells-Pipeline
    /// (<see cref="SpellExecutor"/>, <see cref="AuraManager"/>,
    /// <see cref="CooldownManager"/>).
    /// </para>
    /// <para>
    /// Aktuelle HP werden via <see cref="NetworkVariable{T}"/> an alle Clients
    /// gesynct (Phase-4-MVP — HUD/Floating-Text-Hookup folgt in einer späteren
    /// Phase, das Server-Event <see cref="OnServerDied"/> ist bereits da).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitStats : NetworkBehaviour, IDamageable, IUnitStats, ICombatUnit
    {
        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [Header("Base Stats")]
        [SerializeField, Min(1)] private int m_MaxHp = 100;
        [Tooltip("Maximale Mana. 0 = Einheit hat keine Mana-Ressource (z. B. reine Melee-Mobs); " +
                 "die Mana-Bar im Target-Frame wird dann ausgeblendet.")]
        [SerializeField, Min(0)] private int m_MaxMana = 100;
        [SerializeField, Min(0)] private int m_Strength = 5;
        [Tooltip("Agility (AGI) — skaliert Ranged-Grundschaden (AGI/14 statt STR/10 im Ranged-Pfad), " +
                 "Ranged-Crit (+1% pro 53 AGI, Classic-Hunter-Faktor) und Dodge (+1% pro 20 AGI, " +
                 "Original-Formel aus CombatFormulas.cpp L149-150).")]
        [SerializeField, Min(0)] private int m_Agility = 0;
        [SerializeField, Min(0)] private int m_Armor = 0;
        [SerializeField, Min(1)] private int m_Level = 1;

        [Header("Primary Attributes (Original-Stat-Set)")]
        [Tooltip("Fortitude (FRT) — skaliert HP-Pool und (perspektivisch) Resistenz gegen " +
                 "DOTs/CC. Aktuell reines Display-Stat; HP-Skalierung erfolgt im ExperienceSystem.")]
        [SerializeField, Min(0)] private int m_Fortitude = 0;
        [Tooltip("Courage (CRG) — fünftes Primary-Attribut aus dem Original. Klassen-spezifische " +
                 "Wirkung (Paladin/Bishop). Aktuell reines Display-Stat.")]
        [SerializeField, Min(0)] private int m_Courage = 0;

        [Header("Magic Stats")]
        [Tooltip("Skaliert magische Spell-Damage und (mit niedrigerer Gewichtung) Heals.")]
        [SerializeField, Min(0)] private int m_Intelligence = 0;
        [Tooltip("Skaliert Heals und (perspektivisch) Mana-Regeneration.")]
        [SerializeField, Min(0)] private int m_Willpower = 0;
        [Tooltip("Grundschaden fuer WeaponDamage-Effekte ohne explizites Weapon-Asset (Melee).")]
        [SerializeField, Min(0)] private int m_WeaponDamage = 0;
        [Tooltip("Grundschaden fuer Ranged-WeaponDamage-Effekte ohne explizites Weapon-Asset. " +
                 "Entspricht 'RangedWeaponValue' aus dem Original-Stat-Enum.")]
        [SerializeField, Min(0)] private int m_RangedWeaponDamage = 0;

        [Header("Regeneration")]
        [Tooltip("HP-Regeneration pro Tick (Server-Tick, siehe Regeneration-System). " +
                 "Entspricht 'Regeneration' aus dem Original-Stat-Enum.")]
        [SerializeField, Min(0)] private int m_HpRegen = 0;
        [Tooltip("Mana-Regeneration pro Tick. Entspricht 'Meditate' aus dem Original-Stat-Enum.")]
        [SerializeField, Min(0)] private int m_ManaRegen = 0;

        [Header("Hit-Modifiers (in Prozent)")]
        [Tooltip("Crit-Chance fuer Melee-Angriffe (Original 'MeleeCritical'). " +
                 "Wird von CombatFormulas.RollMeleeHit konsumiert.")]
        [SerializeField, Range(0, 100)] private int m_MeleeCritChance = 0;
        [Tooltip("Crit-Chance fuer Ranged-Angriffe (Original 'RangedCritical'). Reserviert fuer einen " +
                 "separaten Ranged-Hit-Pfad; aktuell laufen Ranged-Skills ueber RollSpellHit und nutzen SpellCrit.")]
        [SerializeField, Range(0, 100)] private int m_RangedCritChance = 0;
        [Tooltip("Crit-Chance fuer Spells (Original 'SpellCritical'). Wird von " +
                 "CombatFormulas.RollSpellHit und CalculateSpellHeal konsumiert.")]
        [SerializeField, Range(0, 100)] private int m_SpellCritChance = 0;
        [SerializeField, Range(0, 100)] private int m_DodgeChance = 0;
        [Tooltip("Parry-Rating (Source 'ParryRating'). Wird in CombatFormulas.GetParryChance " +
                 "zur BASE_PARRY_CHANCE=5 addiert und mit ParryChanceBonus + CRG/30 zur " +
                 "finalen Parry-Chance kombiniert (Cap 75%). Pflicht: Waffe equipped.")]
        [SerializeField, Range(0, 100)] private int m_ParryChance = 0;
        [Tooltip("Direkter Parry-Chance-Bonus (Source 'ParryChanceBonus'=37). Additiv auf Parry-Rating.")]
        [SerializeField, Range(0, 100)] private int m_ParryChanceBonus = 0;
        [Tooltip("Block-Rating (Source 'BlockRating'=19). Bildet die Basis der Block-Chance — " +
                 "Source hat KEIN Base-Block, Schild ist Pflicht. Wird mit BlockChanceBonus + " +
                 "ShieldSkill/5 + FRT/30 kombiniert (Cap 75%).")]
        [SerializeField, Range(0, 100)] private int m_BlockChance = 0;
        [Tooltip("Direkter Block-Chance-Bonus (Source 'BlockChanceBonus'=38). Additiv auf Block-Rating.")]
        [SerializeField, Range(0, 100)] private int m_BlockChanceBonus = 0;
        [Tooltip("Schild-Skill (Source 'ShieldSkill'=34). Trägt SLD/5 % zur Block-Chance bei. " +
                 "Für NPCs Proxy-Signal 'hat Schild' (>0 ⇒ HasShield=true).")]
        [SerializeField, Min(0)] private int m_ShieldSkill = 0;

        [Header("Resistenzen")]
        [SerializeField, Min(0)] private int m_ResistFire = 0;
        [SerializeField, Min(0)] private int m_ResistFrost = 0;
        [SerializeField, Min(0)] private int m_ResistArcane = 0;
        [SerializeField, Min(0)] private int m_ResistNature = 0;
        [SerializeField, Min(0)] private int m_ResistShadow = 0;
        [SerializeField, Min(0)] private int m_ResistHoly = 0;

        [Header("Hitbox")]
        [Tooltip("Radius der Einheits-Hitbox in Metern. Wird vom Server-seitigen Hit-Resolve " +
                 "als 'Reichweiten-Erweiterung' benutzt: distance(angreifer, opfer) <= " +
                 "weapon.Range + opfer.HitRadius. Damit fühlt sich der LoL-Style Range-Indicator " +
                 "konsistent an — sobald der Ring die Körperhülle des Ziels touchiert, landet der Schlag.")]
        [SerializeField, Min(0f)] private float m_HitRadius = 0.2f;

        [Tooltip("Radius des Maus-Pick-Volumens in Metern (LoL-'selectionRadius'). Sollte etwas " +
                 "größer als HitRadius sein, damit Hovern/Anklicken klickfreundlich ist, ohne dass " +
                 "Skillshots am Modellrand vorbei zischen. <=0 ⇒ Fallback HitRadius * 1.2.")]
        [SerializeField, Min(0f)] private float m_SelectionRadius = 0f;

        [Header("Movement")]
        [Tooltip("Geh-Geschwindigkeit in m/s. Wird vom AI/Movement-Layer als Default benutzt; " +
                 "Status-Effekte (Slow/Haste) wirken multiplikativ darauf.")]
        [SerializeField, Min(0f)] private float m_WalkSpeed = 0f;

        [Tooltip("Lauf-Geschwindigkeit in m/s. 0 = Einheit kann nicht sprinten.")]
        [SerializeField, Min(0f)] private float m_RunSpeed = 0f;

        [Header("Reichweiten")]
        [Tooltip("Melee-Reichweite in Metern (intrinsisch für diese Einheit, z. B. aus MUGEN " +
                 "size.attack_dist). Waffen/Abilities können das pro Cast überschreiben.")]
        [SerializeField, Min(0f)] private float m_AttackRange = 0f;

        [Tooltip("Projektil-Reichweite in Metern (intrinsisch). 0 = die Einheit hat keine " +
                 "Standard-Range-Attacke.")]
        [SerializeField, Min(0f)] private float m_ProjectileRange = 0f;

        [Header("Identität")]
        [Tooltip("Anzeigename für Name-Tags, Target-Frame und Debug-Labels. Wird durch " +
                 "ApplyBaseStats(..., displayName) überschrieben, sobald eine Datenquelle " +
                 "(z. B. MUGEN <Atlas>.stats.json) etwas liefert.")]
        [SerializeField] private string m_DisplayName = "";

        [Tooltip("Faction-Id für friendly/hostile-Auflösung im SpellCaster. Mobs derselben " +
                 "Faction können einander nicht direkt angreifen. 0 = Default-Hostile.")]
        [SerializeField] private int m_FactionId = 0;

        [Tooltip("True ⇒ diese Unit ist ein menschlicher Spieler. Aktiviert Cooldown- und " +
                 "GCD-Tracking im SpellExecutor (Mobs ignorieren beides).")]
        [SerializeField] private bool m_IsPlayer = false;

        [Header("Stat Aggregator (Player only)")]
        [Tooltip("Optional. Wird in Awake auf das eigene GameObject aufgeloest. Wenn gesetzt, " +
                 "routen alle <see cref=\"IUnitStats\"/>-Getter (ausser MaxHp) durch " +
                 "<c>PlayerStats.GetTotal(StatId.X)</c> — damit fliessen Item-Boni " +
                 "(StatType1..4 / StatValue1..4 aus _templates.json) in Combat/Spell-Formeln " +
                 "und das HUD. NPCs lassen das Feld leer und nutzen weiter die Raw-Inspector-Werte.")]
        [SerializeField] private PlayerStats m_PlayerStats;

        // -------------------------------------------------------------------------
        // Netzwerk-State
        // -------------------------------------------------------------------------

        private readonly NetworkVariable<int> m_CurrentHp = new(
            value: 0,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> m_CurrentMana = new(
            value: 0,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// CC-Flags (Bitmaske) &#8212; replizierte Sicht der AuraManager-CC-States.
        /// Bit 0 = Immobilized (Stun ODER Root), Bit 1 = Silenced. Wird vom Server
        /// in <see cref="ServerOnAurasChanged"/> aus dem AuraManager rekomponiert,
        /// damit Owner-Client und Remote-Clients ohne Aura-Snapshot-Auswertung
        /// wissen, ob sie bewegen / casten duerfen (Prediction-Konsistenz).
        /// </summary>
        private readonly NetworkVariable<byte> m_CcFlags = new(
            value: 0,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Aggregierter Move-Speed-Multiplikator &#215; 1000 (FixedPoint, damit als
        /// <c>short</c> ueber NGO geht). 1000 = 1.0x = neutral. 500 = 50% Slow,
        /// 1500 = 50% Haste. Wird vom Server aus <see cref="AuraManager.MoveSpeedMultiplier"/>
        /// gespiegelt, sodass die <see cref="Riftstorm.Game.Movement.PlayerMovement"/>-
        /// Prediction auf dem Owner mit demselben Wert rechnet wie der Server.
        /// </summary>
        private readonly NetworkVariable<short> m_MoveSpeedMultiplierMilli = new(
            value: 1000,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        // CC-Flag-Bitmaske &#8212; intern, damit beide Seiten dieselben Bits verwenden.
        private const byte k_CcFlagImmobilized = 1 << 0;
        private const byte k_CcFlagSilenced = 1 << 1;

        // -------------------------------------------------------------------------
        // Spells-Pipeline (Server-only — Auren/Cooldowns existieren nur auf dem Server,
        // Clients sehen Auswirkungen via separate Sync-Pfade).
        // -------------------------------------------------------------------------

        private readonly AuraManager m_Auras = new();
        private readonly CooldownManager m_Cooldowns = new();

        // Gecachte Sibling-Komponenten fuer Movement-Effekte (Teleport, KnockBack,
        // PullTo, Charge, SlideFrom). Genau eine der beiden Referenzen ist auf einer
        // Unit gesetzt &#8212; Spieler haben <see cref="PlayerMovement"/>, NPCs
        // <see cref="NpcController"/>. Werden in <see cref="OnNetworkSpawn"/>
        // einmalig aufgeloest und vom <see cref="Riftstorm.Game.Spells.Runtime.SpellExecutor"/>
        // ueber die <see cref="ICombatUnit"/>-Schnittstelle konsumiert.
        private PlayerMovement m_PlayerMovement;
        private NpcController m_NpcController;

        // Gecachte Cast-Komponente fuer den Interrupt-Pfad. Nur auf Spieler-
        // Prefabs gesetzt &#8212; NPCs casten ueber NpcController/SpellCaster ohne
        // PlayerCombat-Sibling. Wird in <see cref="OnNetworkSpawn"/> einmalig
        // aufgeloest und vom <see cref="ICombatUnit.ServerInterruptCast"/>-Pfad
        // konsumiert.
        private PlayerCombat m_PlayerCombat;

        // -------------------------------------------------------------------------
        // IUnitStats
        // -------------------------------------------------------------------------

        /// <inheritdoc/>
        public int CurrentHp => m_CurrentHp.Value;

        /// <inheritdoc/>
        /// <remarks>
        /// Aggregiert Item-Boni ueber <see cref="PlayerStats"/> (StatId.Health),
        /// damit Templates mit <c>stat_type=2</c> (z. B. Longsword +10 HP) das
        /// HP-Cap tatsaechlich anheben. Faellt auf den Inspector-Wert zurueck,
        /// solange kein PlayerStats gebunden ist (NPCs, Tests).
        /// </remarks>
        public int MaxHp => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.Health) : m_MaxHp;

        /// <summary>Aktuelle Mana (0 falls die Einheit keine Mana hat).</summary>
        public int CurrentMana => m_CurrentMana.Value;

        /// <summary>Maximale Mana (0 falls die Einheit keine Mana-Ressource besitzt).</summary>
        public int MaxMana => m_MaxMana;

        /// <summary>True, wenn diese Einheit ueberhaupt eine Mana-Ressource hat.</summary>
        public bool HasMana => m_MaxMana > 0;

        /// <inheritdoc/>
        public int Strength => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.Strength) : m_Strength;

        /// <inheritdoc/>
        public int Agility => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.Agility) : m_Agility;

        /// <inheritdoc/>
        public int Armor => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ArmorValue) : m_Armor;

        /// <inheritdoc/>
        public int Level => m_Level;

        /// <inheritdoc/>
        public int Intelligence => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.Intelligence) : m_Intelligence;

        /// <inheritdoc/>
        public int Willpower => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.Willpower) : m_Willpower;

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

        /// <summary>Courage — fuenftes Primary-Attribut, klassenspezifisch. Reines Display.</summary>
        public int Courage => m_Courage;

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
        public int ParryChanceBonus => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.ParryChanceBonus) : m_ParryChanceBonus;

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
        public int BlockChanceBonus => m_PlayerStats != null ? m_PlayerStats.GetTotal(StatId.BlockChanceBonus) : m_BlockChanceBonus;

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
        public int DamageDealtPctMod =>
            m_Auras != null ? m_Auras.GetAuraModifierTotal(AuraType.ModifyDamageDealtPct) : 0;

        /// <inheritdoc/>
        public int DamageReceivedPctMod =>
            m_Auras != null ? m_Auras.GetAuraModifierTotal(AuraType.ModifyDamageReceivedPct) : 0;

        /// <inheritdoc/>
        public int HealingDealtPctMod =>
            m_Auras != null ? m_Auras.GetAuraModifierTotal(AuraType.ModifyHealingDealtPct) : 0;

        /// <inheritdoc/>
        public int HealingReceivedPctMod =>
            m_Auras != null ? m_Auras.GetAuraModifierTotal(AuraType.ModifyHealingRecvPct) : 0;

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
        /// <see cref="Riftstorm.Game.Npc.ThreatManager"/>-Hook im
        /// <see cref="Riftstorm.Game.Npc.NpcController"/> konsumiert, um
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

        /// <inheritdoc/>
        public void ApplyDamage(in DamageInfo info)
        {
            // Convenience-Overload ohne Attacker — fuer Quellen ohne Caster-Ref
            // (Environment-Damage, Self-Damage, alte Aufrufpfade). Threat wird
            // dann nicht aufgebaut.
            ApplyDamage(null, in info);
        }

        /// <summary>
        /// Variante mit Attacker-Ref. Wird vom Caster-Pfad (Spell/Melee)
        /// genutzt, damit der <see cref="Riftstorm.Game.Npc.ThreatManager"/>
        /// die Quelle des Schadens zuordnen und Threat aufbauen kann.
        /// </summary>
        /// <param name="attacker">
        /// Angreifer-Unit oder <c>null</c> (Environment / DoT-ohne-Caster).
        /// </param>
        /// <param name="info">Vom <see cref="CombatFormulas"/> vorbereiteter Schaden.</param>
        public void ApplyDamage(UnitStats attacker, in DamageInfo info)
        {
            if (!IsServer)
            {
                return;
            }
            if (IsDead || info.FinalDamage <= 0)
            {
                // Trotzdem den Client-Fanout schicken, damit Miss/Dodge/Block-FX laufen können.
                BroadcastDamageClientRpc(info.FinalDamage, (byte)info.HitResult);
                return;
            }

            // AbsorbDamage-Auren (Bubble, Power Word Shield) ziehen Schaden ab,
            // bevor er auf die HP geht. Schild wird live im AuraManager
            // gedraint und entfernt sich, sobald komplett verbraucht.
            int incomingDamage = info.FinalDamage;
            int absorbed = 0;
            if (m_Auras != null && incomingDamage > 0)
            {
                absorbed = m_Auras.ConsumeAbsorbShield(incomingDamage);
                if (absorbed > 0)
                {
                    incomingDamage -= absorbed;
                }
            }
            if (incomingDamage <= 0)
            {
                // Komplett absorbiert — kein HP-Verlust, aber FX/Threat trotzdem
                // mit dem absorbierten Betrag fanouten (Standard-MOBA-Verhalten).
                BroadcastDamageClientRpc(0, (byte)info.HitResult);
                return;
            }

            int previousHp = m_CurrentHp.Value;
            int newHp = Mathf.Max(0, previousHp - incomingDamage);
            m_CurrentHp.Value = newHp;

            // "Until struck by damage"-Auren (Bind Spirit, Deep Freeze,
            // Blindside) brechen sobald ueberhaupt Schaden landet — vor
            // dem Death-Pfad, damit auch toedlicher Hit konsistent das
            // CC entfernt (relevant fuer Replays/Statesync).
            if (m_Auras != null)
            {
                m_Auras.NotifyDamageTaken();
            }

            BroadcastDamageClientRpc(incomingDamage, (byte)info.HitResult);

            try
            {
                OnServerDamaged?.Invoke(attacker, incomingDamage, info.HitResult);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }

            if (newHp == 0)
            {
                try
                {
                    OnServerDied?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            }
        }

        // -------------------------------------------------------------------------
        // Konfiguration (vor Netcode-Spawn)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Überschreibt die Base-Stats aus externen Daten (z. B. dem MUGEN-NPC-
        /// Importer). Darf nur VOR <see cref="OnNetworkSpawn"/> aufgerufen
        /// werden — danach wäre <see cref="m_CurrentHp"/> schon initialisiert
        /// und die NetworkVariable würde inkonsistent zu <see cref="MaxHp"/>.
        /// </summary>
        /// <remarks>
        /// Wird der Aufruf zu spät gemacht, wird er verworfen und geloggt.
        /// Radius-, Speed- und Range-Parameter werden nur übernommen, wenn
        /// &gt; 0 — so behalten Inspector-Defaults Vorrang, falls die Quelle
        /// für ein Feld keinen Wert mitliefert. <paramref name="displayName"/>
        /// wird nur gesetzt, wenn nicht-leer.
        /// </remarks>
        public void ApplyBaseStats(
            int maxHp,
            int maxMana,
            int strength,
            int armor,
            float hitRadius,
            float selectionRadius = 0f,
            float walkSpeed = 0f,
            float runSpeed = 0f,
            float attackRange = 0f,
            float projectileRange = 0f,
            string displayName = null,
            int factionId = -1,
            int agility = 0)
        {
            if (IsSpawned)
            {
                Debug.LogWarning(
                    "[UnitStats] ApplyBaseStats nach OnNetworkSpawn ignoriert — " +
                    "Stats müssen vor dem Netcode-Spawn gesetzt werden.", this);
                return;
            }

            // factionId < 0 = Sentinel "Inspector-Default behalten" (z. B. MUGEN-Pfad,
            // der keine Faction-Daten kennt). >= 0 wird übernommen — FLARE-NPCs liefern
            // npc_template.faction (3 = FACTION_HOSTILE) durch.
            if (factionId >= 0)
            {
                m_FactionId = factionId;
            }

            m_MaxHp = Mathf.Max(1, maxHp);
            m_MaxMana = Mathf.Max(0, maxMana);
            m_Strength = Mathf.Max(0, strength);
            m_Agility = Mathf.Max(0, agility);
            m_Armor = Mathf.Max(0, armor);
            if (hitRadius > 0f)
            {
                m_HitRadius = hitRadius;
            }
            if (selectionRadius > 0f)
            {
                m_SelectionRadius = selectionRadius;
            }
            if (walkSpeed > 0f)
            {
                m_WalkSpeed = walkSpeed;
            }
            if (runSpeed > 0f)
            {
                m_RunSpeed = runSpeed;
            }
            if (attackRange > 0f)
            {
                m_AttackRange = attackRange;
            }
            if (projectileRange > 0f)
            {
                m_ProjectileRange = projectileRange;
            }
            if (!string.IsNullOrEmpty(displayName))
            {
                m_DisplayName = displayName;
            }
        }

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        private void Awake()
        {
            // Stat-Aggregator (Spieler-only) automatisch aufloesen. NPCs haben
            // keine PlayerStats-Component → bleibt null → Getter fallen auf
            // Raw-Inspector-Werte zurueck.
            if (m_PlayerStats == null)
            {
                m_PlayerStats = GetComponent<PlayerStats>();
            }
        }

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                // Wichtig: MaxHp (aggregiert) statt m_MaxHp, damit Equipment-HP-Boni
                // (z. B. Longsword +10 HP) das Cap bereits beim Initial-Fill anheben.
                m_CurrentHp.Value = MaxHp;
                m_CurrentMana.Value = m_MaxMana;
                // Owner setzen — AuraManager liest IsStunned/Silenced/Rooted vom Owner
                // selbst nicht, aber Aura-Effekte greifen darüber auf die Unit zu.
                m_Auras.SetOwner(this);
                m_Auras.OnChanged += ServerOnAurasChanged;
                m_LastTickTime = Time.time;
                // PlayerStats kennt das Equipment-Aggregat &#8212; sobald sich Items
                // aendern, faellt evtl. MaxHp anders aus. ServerOnStatsChanged
                // klammert CurrentHp und feuert HpChanged neu, damit HUDs/Bars
                // den neuen Cap sehen. NPCs haben kein PlayerStats &#8594; Subscription
                // wird uebersprungen.
                if (m_PlayerStats != null)
                {
                    m_PlayerStats.StatsChanged += ServerOnStatsChanged;
                }
            }
            // Client: gleicher Refire-Pfad ohne Server-Mutationen. PlayerStats
            // läuft auf jedem Peer (EquipChanged -> RecomputeEquipmentSums)
            // und kennt damit den neuen MaxHp-Cap. Ohne diese Subscription
            // bekommt das PlayerFrameUI nur HP-Value-Changes mit, nicht den
            // geänderten Cap nach Equip/Unequip.
            if (!IsServer && m_PlayerStats != null)
            {
                m_PlayerStats.StatsChanged += ClientOnStatsChanged;
            }
            // Movement-Siblings einmalig aufloesen. Spieler-Prefab: PlayerMovement.
            // NPC-Prefab: NpcController. Beide nie gleichzeitig vorhanden &#8212;
            // <c>ICombatUnit.ServerTeleportTo</c>/<c>ServerApplyImpulse</c> waehlen
            // den passenden Pfad zur Laufzeit.
            m_PlayerMovement = GetComponent<PlayerMovement>();
            m_NpcController = GetComponent<NpcController>();
            m_PlayerCombat = GetComponent<PlayerCombat>();
            m_CurrentHp.OnValueChanged += OnHpValueChangedInternal;
            m_CurrentMana.OnValueChanged += OnManaValueChangedInternal;
            // Initialwerte einmal feuern, damit UI-Schichten korrekt initialisieren.
            HpChanged?.Invoke(m_CurrentHp.Value, MaxHp);
            ManaChanged?.Invoke(m_CurrentMana.Value, m_MaxMana);
        }

        /// <summary>
        /// Server-only: reagiert auf Equip-/Stat-&#196;nderungen. Wenn das
        /// neue MaxHp gewachsen ist, bleibt CurrentHp wie er ist (Heal nur via
        /// expliziten Heal-Pfad); wenn es geschrumpft ist, klammern wir
        /// CurrentHp herunter. In beiden F&#228;llen feuern wir HpChanged neu,
        /// damit HUDs den ge&#228;nderten Cap rendern.
        /// </summary>
        private void ServerOnStatsChanged()
        {
            if (!IsServer)
            {
                return;
            }
            int cap = MaxHp;
            if (m_CurrentHp.Value > cap)
            {
                m_CurrentHp.Value = cap;
                return; // OnHpValueChangedInternal feuert HpChanged bereits.
            }
            HpChanged?.Invoke(m_CurrentHp.Value, cap);
        }

        /// <summary>
        /// Client-only Spiegel zu <see cref="ServerOnStatsChanged"/>: keine
        /// HP-Mutation (das macht der Server via NetworkVariable), aber
        /// HpChanged neu feuern, damit Portrait/Bar den geänderten MaxHp-Cap
        /// nach Equip/Unequip sehen.
        /// </summary>
        private void ClientOnStatsChanged()
        {
            HpChanged?.Invoke(m_CurrentHp.Value, MaxHp);
        }

        // -------------------------------------------------------------------------
        // Server-Tick
        // -------------------------------------------------------------------------

        private float m_LastTickTime;

        /// <summary>
        /// Server-Tick: treibt den <see cref="AuraManager"/> (DoT/HoT/Duration-Decay).
        /// FixedUpdate, damit Aura-Ticks deterministisch zur Sim-Frequenz laufen und
        /// nicht vom Render-FPS abhängen.
        /// </summary>
        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            float now = Time.time;
            int deltaMs = Mathf.Max(0, Mathf.RoundToInt((now - m_LastTickTime) * 1000f));
            m_LastTickTime = now;
            if (deltaMs > 0)
            {
                m_Auras.Update(deltaMs);
            }
        }

        /// <inheritdoc/>
        public override void OnNetworkDespawn()
        {
            m_CurrentHp.OnValueChanged -= OnHpValueChangedInternal;
            m_CurrentMana.OnValueChanged -= OnManaValueChangedInternal;
            if (IsServer)
            {
                m_Auras.OnChanged -= ServerOnAurasChanged;
                if (m_PlayerStats != null)
                {
                    m_PlayerStats.StatsChanged -= ServerOnStatsChanged;
                }
            }
            else if (m_PlayerStats != null)
            {
                m_PlayerStats.StatsChanged -= ClientOnStatsChanged;
            }
            base.OnNetworkDespawn();
        }

        private void OnHpValueChangedInternal(int previous, int current)
        {
            HpChanged?.Invoke(current, MaxHp);
        }

        private void OnManaValueChangedInternal(int previous, int current)
        {
            ManaChanged?.Invoke(current, m_MaxMana);
        }

        // -------------------------------------------------------------------------
        // Server-Helpers
        // -------------------------------------------------------------------------

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
        /// <see cref="Riftstorm.Game.Movement.PlayerMovement"/> sowohl in der Owner-
        /// Prediction als auch im Server-Authority-Pfad konsultiert, sodass beide
        /// Seiten denselben Bewegungs-Block sehen (keine Reconciliation-Rucker).
        /// </summary>
        public bool IsImmobilized =>
            IsServer ? m_Auras.IsImmobilized : (m_CcFlags.Value & k_CcFlagImmobilized) != 0;

        /// <summary>
        /// Server-autoritative Sicht auf den Stun-Zustand. Wird ausschliesslich
        /// fuer server-seitige Action-Gates (Auto-Attack-Gate in
        /// <see cref="Riftstorm.Game.Combat.PlayerCombat"/>, NPC-AI in
        /// <see cref="Riftstorm.Game.Npc.NpcController"/>) genutzt. Auf Clients
        /// stets <c>false</c> &#8212; Clients fragen
        /// <see cref="IsImmobilized"/> ab, das Stun ODER Root abdeckt.
        /// </summary>
        public bool IsStunned => IsServer && m_Auras != null && m_Auras.IsStunned;

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
        void ICombatUnit.ServerInterruptCast()
        {
            if (!IsServer) { return; }
            // Spieler-Casts laufen ueber PlayerCombat &#8212; NPCs casten ohne
            // diese Komponente und werden vom Interrupt-Effekt als No-op
            // behandelt (Cast-Abort fuer NPCs kommt direkt aus deren AI).
            if (m_PlayerCombat != null)
            {
                m_PlayerCombat.ServerInterruptCast();
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

            /// <summary>Vollstaendiger Konstruktor.</summary>
            public AuraSnapshot(int spellEntry, int stacks, bool isPositive, int remainingMs, int maxDurationMs, float receivedAt)
            {
                SpellEntry = spellEntry;
                Stacks = stacks;
                IsPositive = isPositive;
                RemainingMs = remainingMs;
                MaxDurationMs = maxDurationMs;
                ReceivedAt = receivedAt;
            }
        }

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
                w++;
            }

            BroadcastAurasClientRpc(entries, stacks, positive, remainingMs, maxDurationMs);

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
                    receivedAt: now);
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
        }    }
}
