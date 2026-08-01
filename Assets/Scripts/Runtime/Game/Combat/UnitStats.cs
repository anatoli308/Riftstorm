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
[DisallowMultipleComponent]
    /*[RequireComponent(typeof(PlayerStats))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(NpcController))]
    [RequireComponent(typeof(PlayerCombat))]*/
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
    public sealed partial class UnitStats : NetworkBehaviour, IDamageable, IUnitStats, ICombatUnit
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

        [Tooltip("Bitmaske der Crowd-Control-Mechaniken, gegen die diese Einheit immun ist " +
                 "(Source 'mechanic_immune_mask'). Bit n = 1 << ((int)Mechanic - 1). Wird beim " +
                 "Aura-Apply im AuraManager geprueft; gesetzte Bits verwerfen den Mechanik-Effekt.")]
        [SerializeField, Min(0)] private int m_MechanicImmuneMask = 0;

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

        /// <summary>
        /// Server-autoritatives Max-HP-Cap fuer NPCs. Notwendig, weil
        /// <see cref="m_MaxHp"/> im <see cref="FlareNpcSpawner"/> aus einem
        /// <c>Random.Range</c>-Level-Roll berechnet wird, der pro Peer (Server UND
        /// Client) unabhaengig in <c>Awake</c> laeuft. Bei Templates mit Level-Range
        /// (z. B. Lone Howler min_level 6 / max_level 7) wuerfeln Server und Client
        /// unterschiedliche Level → unterschiedliche m_MaxHp. Da <see cref="m_CurrentHp"/>
        /// vom Server gespiegelt wird, das lokale m_MaxHp aber nicht, zeigte das HUD
        /// z. B. 120/110. Der Server schreibt hier den real gerollten Cap, Clients
        /// lesen ihn statt ihres eigenen Roll-Ergebnisses. 0 = noch nicht gesetzt
        /// (Pre-Spawn / Spieler-Pfad) → Fallback auf <see cref="m_MaxHp"/>.
        /// </summary>
        private readonly NetworkVariable<int> m_NetMaxHp = new(
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
        /// gespiegelt, sodass die <see cref="PlayerMovement"/>-
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
        /// HP-Cap tatsaechlich anheben. NPCs (kein PlayerStats) nutzen das
        /// server-autoritative <see cref="m_NetMaxHp"/>, sobald gesetzt — sonst
        /// (Pre-Spawn / Tests) den lokalen Inspector-/Spawner-Wert. Zuletzt werden
        /// aktive <see cref="AuraType.ModifyStat"/>-/<see cref="AuraType.ModifyStatPct"/>-
        /// Auren auf <see cref="StatId.Health"/> angewandt (z. B. Blessing of Health),
        /// analog zu <see cref="Strength"/> &amp; Co. — peer-korrekt auf Server
        /// (autoritativer AuraManager) wie Client (Rekonstruktion aus <see cref="ClientAuras"/>).
        /// </remarks>
        public int MaxHp
        {
            get
            {
                int baseMax = m_PlayerStats != null
                    ? m_PlayerStats.GetTotal(StatId.Health)
                    : (m_NetMaxHp.Value > 0 ? m_NetMaxHp.Value : m_MaxHp);
                return ApplyStatAuraModifiers(StatId.Health, baseMax);
            }
        }

        /// <summary>Aktuelle Mana (0 falls die Einheit keine Mana hat).</summary>
        public int CurrentMana => m_CurrentMana.Value;

        /// <summary>Maximale Mana (0 falls die Einheit keine Mana-Ressource besitzt).</summary>
        public int MaxMana => m_MaxMana;

        /// <summary>True, wenn diese Einheit ueberhaupt eine Mana-Ressource hat.</summary>
        public bool HasMana => m_MaxMana > 0;

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
                // NPC-Cap server-autoritativ festschreiben, BEVOR MaxHp fuer den
                // Initial-Fill gelesen wird. Spiegelt den real auf dem Server
                // gerollten Level-/HP-Wert an die Clients (verhindert 120/110-
                // Desync bei Templates mit Level-Range). Fuer Spieler ist der Wert
                // belanglos — deren MaxHp kommt aus PlayerStats.
                m_NetMaxHp.Value = m_MaxHp;
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

    }
}
