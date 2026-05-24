using System;
using System.Collections.Generic;
using System.Text;
using Riftstorm.Game.Items;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Quellseitig nummerierte Stat-Ids — Subset aus
    /// <c>Combat::Stat</c> der Source, der in Phase 16B von ItemTemplate
    /// <c>StatType1..4</c>/<c>StatValue1..4</c> wirklich genutzt wird. Werte
    /// matchen 1:1 die C++-Enum-Werte aus
    /// <c>source_server/Shared/CombatStats.h</c>, damit JSON-Templates ohne
    /// Mapping konsumiert werden koennen.
    /// </summary>
    public enum StatId
    {
        None = 0,
        Health = 2,
        ArmorValue = 3,
        Strength = 4,
        Agility = 5,
        Willpower = 6,
        Intelligence = 7,
        WeaponValue = 11,
        MeleeCooldown = 12,
        RangedWeaponValue = 13,
        RangedCooldown = 14,
        MeleeCritical = 15,
        RangedCritical = 16,
        SpellCritical = 17,
        DodgeRating = 18,
        BlockRating = 19,
        ResistFrost = 20,
        ResistFire = 21,
        ResistShadow = 22,
        ResistHoly = 23,
        ShieldSkill = 34,
        ParryChanceBonus = 37,
        BlockChanceBonus = 38,
        ParryRating = 40,
    }

    /// <summary>
    /// Read-only Aggregator ueber <see cref="UnitStats"/> (Base) +
    /// <see cref="PlayerEquipment"/> (Item-Boni). Buffs/Auras kommen in
    /// Phase 17. Liegt als MonoBehaviour (nicht NetworkBehaviour) auf dem
    /// PlayerCharacter-Prefab; die replizierten Quellen sind UnitStats und
    /// PlayerEquipment — diese Klasse rechnet rein lokal pro Peer.
    ///
    /// <para>
    /// Lifecycle: rechnet die Equipment-Summe nur dann neu, wenn
    /// <see cref="PlayerEquipment.EquipChanged"/> feuert (event-driven, kein
    /// Polling). HUD/CombatFormulas haengen sich an <see cref="StatsChanged"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStats : MonoBehaviour
    {
        [Tooltip("Quelle der Basis-Werte. Wird in Awake auf das eigene GameObject aufgeloest, wenn leer.")]
        [SerializeField] private UnitStats m_BaseStats;

        [Tooltip("Quelle der Equipment-Boni. Wird in Awake auf das eigene GameObject aufgeloest, wenn leer.")]
        [SerializeField] private PlayerEquipment m_Equipment;

        /// <summary>
        /// Aggregierte Item-Boni je <see cref="StatId"/>. Wird komplett neu
        /// gebaut bei jedem <see cref="PlayerEquipment.EquipChanged"/>. Buffs
        /// sind in v1 nicht enthalten — TODO Phase 17.
        /// </summary>
        private readonly Dictionary<StatId, int> m_EquipmentSums = new();

        /// <summary>
        /// Feuert auf dem lokalen Peer, sobald die aggregierten Stats sich
        /// geaendert haben (Equip/Unequip oder spaeter Buff-Up/Down). HUDs
        /// koennen damit gezielt Bars/Tooltips refreshen.
        /// </summary>
        public event Action StatsChanged;

        private void Awake()
        {
            if (m_BaseStats == null)
            {
                m_BaseStats = GetComponent<UnitStats>();
            }
            if (m_Equipment == null)
            {
                m_Equipment = GetComponent<PlayerEquipment>();
            }
        }

        private void OnEnable()
        {
            if (m_Equipment != null)
            {
                m_Equipment.EquipChanged += OnEquipChanged;
            }
            RecomputeEquipmentSums();
        }

        private void OnDisable()
        {
            if (m_Equipment != null)
            {
                m_Equipment.EquipChanged -= OnEquipChanged;
            }
        }

        private void OnEquipChanged(EquipSlot _, int __)
        {
            RecomputeEquipmentSums();
        }

        // -------------------------------------------------------------------------
        // Lese-API
        // -------------------------------------------------------------------------

        /// <summary>Equipment-Bonus auf einen Stat (nur Item-Boni, keine Base, keine Buffs).</summary>
        public int GetEquipmentBonus(StatId stat) => m_EquipmentSums.TryGetValue(stat, out int v) ? v : 0;

        /// <summary>
        /// Basis-Wert aus <see cref="UnitStats"/> (Inspector-konfiguriert, ggf.
        /// per <c>UnitStats.ApplyBaseStats</c> ueberschrieben). Liest gezielt
        /// die <c>Raw*</c>-Accessoren, damit kein Zyklus entsteht: die
        /// oeffentlichen <c>UnitStats</c>-Getter routen ihrerseits durch
        /// <see cref="GetTotal"/>, das wiederum <c>GetBase</c> aufruft.
        /// Stats ohne Mapping in UnitStats geben 0 zurueck — Aggregator faellt
        /// damit implizit auf den Equipment-Bonus zurueck.
        /// </summary>
        public int GetBase(StatId stat)
        {
            if (m_BaseStats == null)
            {
                return 0;
            }
            switch (stat)
            {
                case StatId.Health: return m_BaseStats.RawMaxHp;
                case StatId.ArmorValue: return m_BaseStats.RawArmor;
                case StatId.Strength: return m_BaseStats.RawStrength;
                case StatId.Agility: return m_BaseStats.RawAgility;
                case StatId.Willpower: return m_BaseStats.RawWillpower;
                case StatId.Intelligence: return m_BaseStats.RawIntelligence;
                case StatId.WeaponValue: return m_BaseStats.RawWeaponDamage;
                case StatId.RangedWeaponValue: return m_BaseStats.RawRangedWeaponDamage;
                case StatId.MeleeCritical: return m_BaseStats.RawMeleeCritChance;
                case StatId.RangedCritical: return m_BaseStats.RawRangedCritChance;
                case StatId.SpellCritical: return m_BaseStats.RawSpellCritChance;
                case StatId.DodgeRating: return m_BaseStats.RawDodgeChance;
                case StatId.BlockRating: return m_BaseStats.RawBlockChance;
                case StatId.ParryRating: return m_BaseStats.RawParryChance;
                case StatId.ParryChanceBonus: return m_BaseStats.RawParryChanceBonus;
                case StatId.BlockChanceBonus: return m_BaseStats.RawBlockChanceBonus;
                case StatId.ShieldSkill: return m_BaseStats.RawShieldSkill;
                case StatId.ResistFire: return m_BaseStats.RawResistFire;
                case StatId.ResistFrost: return m_BaseStats.RawResistFrost;
                case StatId.ResistShadow: return m_BaseStats.RawResistShadow;
                case StatId.ResistHoly: return m_BaseStats.RawResistHoly;
                default: return 0;
            }
        }

        /// <summary>Gesamtwert = Base + Equipment + Buffs (Buffs=0 in v1).</summary>
        public int GetTotal(StatId stat) => GetBase(stat) + GetEquipmentBonus(stat);

        // -------------------------------------------------------------------------
        // Recompute
        // -------------------------------------------------------------------------

        /// <summary>
        /// Baut <see cref="m_EquipmentSums"/> neu auf, indem alle 11 Equip-Slots
        /// gelesen, das jeweilige <see cref="ItemTemplate"/> ueber den
        /// <see cref="ItemCatalogLoader"/> aufgeloest und die vier StatType/Value-
        /// Paare pro Template summiert werden. Source-Parity: Templates
        /// referenzieren bis zu 4 Statboni.
        /// </summary>
        public void RecomputeEquipmentSums()
        {
            m_EquipmentSums.Clear();

            if (m_Equipment == null)
            {
                StatsChanged?.Invoke();
                return;
            }

            int slotsRead = 0;
            int slotsWithItem = 0;
            int slotsResolved = 0;
            for (int slotIdx = 1; slotIdx <= PlayerEquipment.SlotCount; slotIdx++)
            {
                EquipSlot slot = (EquipSlot)slotIdx;
                int templateId = m_Equipment.GetEquipped(slot);
                slotsRead++;
                if (templateId <= 0)
                {
                    continue;
                }
                slotsWithItem++;
                if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null)
                {
                    Debug.LogWarning($"[PlayerStats] Recompute: Slot {slot} hat Template {templateId}, aber ItemCatalogLoader liefert null.");
                    continue;
                }
                slotsResolved++;
                AccumulateStat(template.StatType1, template.StatValue1);
                AccumulateStat(template.StatType2, template.StatValue2);
                AccumulateStat(template.StatType3, template.StatValue3);
                AccumulateStat(template.StatType4, template.StatValue4);
                Debug.Log(
                    $"[PlayerStats] Recompute: Slot {slot} Template {templateId} -> "
                    + $"({template.StatType1}:{template.StatValue1}, {template.StatType2}:{template.StatValue2}, "
                    + $"{template.StatType3}:{template.StatValue3}, {template.StatType4}:{template.StatValue4})");

                // Affixe (Phase 18): zusaetzliche Stat-Boni aus den Rolls. Gems
                // bleiben fuer Phase 19 ausgespart (separates Schema in _gems.json).
                ItemInstance instance = m_Equipment.GetEquippedInstance(slot);
                AccumulateAffixStats(instance, slot);
            }

            // Aggregierte Summen als kompakte Zeile loggen, damit auf einen Blick
            // sichtbar ist, ob die Equipment-Boni wirklich beim Aggregator
            // ankommen. Wird in Phase 17 / nach dem Debugging wieder entfernt.
            StringBuilder sums = new(64);
            foreach (KeyValuePair<StatId, int> kv in m_EquipmentSums)
            {
                if (sums.Length > 0) sums.Append(", ");
                sums.Append(kv.Key).Append('=').Append(kv.Value);
            }
            Debug.Log($"[PlayerStats] Recompute done (slots={slotsRead}, items={slotsWithItem}, resolved={slotsResolved}). Sums: [{sums}]");

            StatsChanged?.Invoke();
        }

        private void AccumulateStat(int statTypeId, int statValue)
        {
            if (statTypeId <= 0 || statValue == 0)
            {
                return;
            }
            StatId id = (StatId)statTypeId;
            if (m_EquipmentSums.TryGetValue(id, out int current))
            {
                m_EquipmentSums[id] = current + statValue;
            }
            else
            {
                m_EquipmentSums[id] = statValue;
            }
        }

        /// <summary>
        /// Loest die beiden Affix-Slots einer <see cref="ItemInstance"/> ueber
        /// den <see cref="AffixCatalogLoader"/> auf und summiert ihre vier
        /// Stat-Paare in <see cref="m_EquipmentSums"/>. Score-Multiplikator:
        /// <c>final = round(statValue * (0.5 + score/200))</c> \u2014 d. h.
        /// Score 0 = 50 %, Score 100 = 100 %. Pure Daten, kein Round-Tripping
        /// ueber NetCode; PlayerStats sieht denselben Snapshot wie das HUD.
        /// </summary>
        private void AccumulateAffixStats(ItemInstance instance, EquipSlot slot)
        {
            if (instance.IsEmpty)
            {
                return;
            }
            for (int affixSlot = 1; affixSlot <= 2; affixSlot++)
            {
                (ushort affixId, byte score) = instance.GetAffix(affixSlot);
                if (affixId == 0)
                {
                    continue;
                }
                if (!AffixCatalogLoader.TryGetAffix(affixId, out ItemAffix affix) || affix == null)
                {
                    Debug.LogWarning($"[PlayerStats] Recompute: Slot {slot} Affix-Slot {affixSlot} verweist auf unbekannten Affix-Entry {affixId}.");
                    continue;
                }
                float multiplier = 0.5f + (score / 200f);
                for (int statSlot = 0; statSlot < 4; statSlot++)
                {
                    (int statType, float statValue) = affix.GetStat(statSlot);
                    if (statType <= 0 || statValue == 0f)
                    {
                        continue;
                    }
                    int scaled = Mathf.RoundToInt(statValue * multiplier);
                    if (scaled == 0)
                    {
                        continue;
                    }
                    AccumulateStat(statType, scaled);
                }
                Debug.Log($"[PlayerStats] Recompute: Slot {slot} Affix '{affix.Name}' (#{affixId}, score={score}, mult={multiplier:0.00}).");
            }
        }
    }
}
