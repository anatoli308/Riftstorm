using System;
using Riftstorm.Game.Combat;
using Riftstorm.Gameplay.Combat;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Items
{
    /// <summary>
    /// Equip-Slots. Die Zahlen 1..11 sind 1:1 Source-Parity zu
    /// <c>ItemDefines::EquipType</c> (Item-JSON-Feld <c>equip_type</c>) und
    /// duerfen nicht renumeriert werden. Die Namen folgen der UI-Sprache:
    /// <c>Amulet</c> statt <c>Necklace</c>, <c>Boots</c> statt <c>Feet</c>,
    /// <c>MainHand/Offhand</c> statt <c>Weapon/Shield</c>.
    /// <para>
    /// <see cref="Ring2"/> ist ein UI-/Gameplay-Extra ohne Source-Parity:
    /// Source kennt nur eine Ring-EquipType (8); Items mit
    /// <c>equip_type=8</c> werden vom Equip-Pfad zuerst in <see cref="Ring1"/>
    /// und dann in <see cref="Ring2"/> einsortiert.
    /// </para>
    /// <para>
    /// Geplante Erweiterungen (Trinket1/2, Bracers, Shoulders, Back) wuerden
    /// ab Index 13 angehaengt — bestehende Indizes 1..12 bleiben stabil.
    /// </para>
    /// </summary>
    public enum EquipSlot
    {
        /// <summary>Kein Slot — Consumable, Quest-Item, Reagent.</summary>
        None = 0,
        Helm = 1,
        Amulet = 2,
        Chest = 3,
        Belt = 4,
        Legs = 5,
        Boots = 6,
        Hands = 7,
        Ring1 = 8,
        MainHand = 9,
        Offhand = 10,
        Ranged = 11,
        /// <summary>UI-/Gameplay-Extra ohne Source-Parity; gleicher EquipType=8 wie <see cref="Ring1"/>.</summary>
        Ring2 = 12,
    }
[DisallowMultipleComponent]

    /// <summary>
    /// Server-autoritatives Equipment des Spielers — eine
    /// <see cref="NetworkList{T}"/> aus Template-Entries (0 = leer), indiziert
    /// per <see cref="EquipSlot"/>. Index 0 (<c>None</c>) ist reserviert und
    /// bleibt unbenutzt; die echten Slots laufen von 1..11.
    ///
    /// <para>
    /// Move-Semantik: Equip aus Inventar konsumiert den Inventory-Slot
    /// (vorhandenes Equipment-Item wandert in den gleichen Slot zurueck —
    /// 1:1-Swap). Unequip legt das Item ueber
    /// <see cref="PlayerInventory.TryAddItemServer(int,int)"/> zurueck. Keine
    /// Item-Duplikation, alle Schreibwege server-only via
    /// <see cref="ServerRpcAttribute"/>.
    /// </para>
    /// <para>
    /// Bridge zu <see cref="PlayerCombat"/>: Aenderungen an
    /// <see cref="EquipSlot.MainHand"/>, <see cref="EquipSlot.Offhand"/> und
    /// <see cref="EquipSlot.Ranged"/> werden server-seitig in die bestehenden
    /// String-Id-Pfade (<c>Server_ApplyWeaponFromTemplate</c> /
    /// <c>Server_ApplyOffhandFromTemplate</c>) gespiegelt. So bleibt der
    /// WeaponCatalog/OffhandCatalog die Quelle der Combat-Mechanik, waehrend
    /// die ItemCatalog-Entries die kanonische "was steckt im Slot"-Wahrheit
    /// werden.
    /// </para>
    /// <para>
    /// Komponenten-Lifecycle: muss als <c>NetworkBehaviour</c> auf dem
    /// PlayerCharacter-Prefab liegen (Drag&amp;Drop im Inspector), weil
    /// Netcode-Replikation nur fuer Komponenten greift, die vor dem
    /// Network-Spawn auf dem Prefab existieren.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(PlayerInventory))]
    [RequireComponent(typeof(PlayerCombat))]
    public sealed class PlayerEquipment : NetworkBehaviour
    {
        /// <summary>Anzahl echter Equip-Slots (1..12). Index 0 ist <see cref="EquipSlot.None"/> und ungenutzt.</summary>
        public const int SlotCount = 12;

        /// <summary>Interne Listenlaenge inklusive reservierter Index 0.</summary>
        private const int k_ListLength = SlotCount + 1;

        private readonly NetworkList<int> m_EquippedTemplates = new(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Parallel zu <see cref="m_EquippedTemplates"/> indizierte Instanz-Daten
        /// (Rarity + Affix-Slots + Gem-Sockets + Quality-Scores). Identische
        /// Laenge und Index-Mapping; Index 0 ist reserviert
        /// (<see cref="EquipSlot.None"/>) und enthaelt immer
        /// <see cref="ItemInstance.Empty"/>. Jede Schreiboperation auf einen
        /// Slot MUSS beide Listen aktualisieren — kanalisiert ueber
        /// <see cref="SetSlotServer"/> / <see cref="ClearSlotServer"/>.
        /// <para>
        /// Bewusst zweite Liste statt komplettes Schema in einer einzigen
        /// <c>NetworkList&lt;ItemInstance&gt;</c>: <see cref="GetEquipped"/>
        /// und der HUD-/Combat-Pfad sind heute auf Template-Ids gebaut und
        /// bleiben unveraendert lesbar. Phase 19 wird, wenn Inventory ebenfalls
        /// auf Instanzen lebt, die Trennung wieder kollabieren koennen.
        /// </para>
        /// </summary>
        private readonly NetworkList<ItemInstance> m_EquipInstances = new(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        [Header("Default-Loadout")]
        [Tooltip("Item-Template, das der Server jedem neu gespawnten Spieler in MainHand legt " +
                 "(0 = leer). Default: 1018 = 'Vincent's Old Sword' (Longsword).")]
        [SerializeField] private int m_DefaultMainHandTemplate = 1018;

        [Tooltip("Rarity, mit der die Default-MainHand gerollt wird. Steuert Affix-Anzahl " +
                 "(siehe RarityRules). Default: Rare (2 Affixe) — End-to-End-Proof fuer Phase 18.")]
        [SerializeField] private ItemRarity m_DefaultMainHandRarity = ItemRarity.Rare;

        [Tooltip("Item-Template, das der Server jedem neu gespawnten Spieler in Offhand legt " +
                 "(0 = leer). Ignoriert, falls die Default-MainHand ein Zweihaender ist. " +
                 "Default: 17 = 'Barricade' (Buckler).")]
        [SerializeField] private int m_DefaultOffhandTemplate = 17;

        [Tooltip("Rarity fuer die Default-Offhand. Default: Common (0 Affixe).")]
        [SerializeField] private ItemRarity m_DefaultOffhandRarity = ItemRarity.Common;

        private PlayerInventory m_Inventory;
        private PlayerCombat m_Combat;
        private WeaponCatalogLoader m_WeaponCatalogLoader;

        /// <summary>
        /// Feuert auf jedem Peer, sobald sich der Inhalt eines Slots aendert.
        /// Payload: <c>(slot, newTemplateId)</c>; <c>newTemplateId == 0</c>
        /// bedeutet leerer Slot. UI-/Stats-Konsumenten sollten den alten Wert
        /// selbst zwischen-cachen wenn benoetigt — Netcode liefert hier nur
        /// das neue Value-Diff.
        /// </summary>
        public event Action<EquipSlot, int> EquipChanged;

        // -------------------------------------------------------------------------
        // Lese-API
        // -------------------------------------------------------------------------

        /// <summary>Template-Entry im angegebenen Slot, oder 0 wenn leer / out-of-range.</summary>
        public int GetEquipped(EquipSlot slot)
        {
            int idx = (int)slot;
            if (idx <= 0 || idx >= m_EquippedTemplates.Count)
            {
                return 0;
            }
            return m_EquippedTemplates[idx];
        }

        /// <summary>
        /// Liefert die vollstaendige <see cref="ItemInstance"/> im angegebenen
        /// Slot (inkl. Rarity, Affix-Ids und Scores). Bei leerem oder
        /// out-of-range Slot wird <see cref="ItemInstance.Empty"/> zurueckgegeben.
        /// Hauptkonsument: <c>PlayerStats</c> fuer die Affix-Aggregation.
        /// </summary>
        public ItemInstance GetEquippedInstance(EquipSlot slot)
        {
            int idx = (int)slot;
            if (idx <= 0 || idx >= m_EquipInstances.Count)
            {
                return ItemInstance.Empty;
            }
            return m_EquipInstances[idx];
        }

        /// <summary>True wenn der Slot mindestens einen gueltigen Template-Eintrag haelt.</summary>
        public bool IsEquipped(EquipSlot slot) => GetEquipped(slot) > 0;

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        private void Awake()
        {
            // Komponenten am selben GameObject — beide MUESSEN auf dem Prefab
            // liegen, sonst kann Netcode sie nicht synchron spawnen.
            m_Inventory = GetComponent<PlayerInventory>();
            m_Combat = GetComponent<PlayerCombat>();
        }

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer && m_EquippedTemplates.Count == 0)
            {
                // Index 0..12; Index 0 (EquipSlot.None) ist reserviert und bleibt 0 / Empty.
                for (int i = 0; i < k_ListLength; i++)
                {
                    m_EquippedTemplates.Add(0);
                    m_EquipInstances.Add(ItemInstance.Empty);
                }
            }

            m_EquippedTemplates.OnListChanged += HandleListChanged;

            // Default-Loadout NACH dem Subscriben setzen, damit
            // HandleListChanged + ApplyServerSideBridge feuern und
            // PlayerCombat.m_CurrentWeaponId/m_CurrentOffhandId
            // sowie EquipChanged-Konsumenten (CharacterHUD, PlayerStats)
            // automatisch in Sync gehen.
            if (IsServer)
            {
                SeedDefaultLoadoutServer();
            }
            else
            {
                // Client: NGO synchronisiert die NetworkList VOR OnNetworkSpawn,
                // OnListChanged feuert daher fuer die Initial-Sync nicht.
                // Wir holen das hier nach, damit PlayerStats/CharacterHUD den
                // initialen Equip-State sehen (sonst bleiben Equipment-Boni
                // client-seitig auf 0 und das HUD zeigt nur die Base-Stats).
                for (int i = 1; i <= SlotCount; i++)
                {
                    int templateId = m_EquippedTemplates[i];
                    if (templateId != 0)
                    {
                        EquipChanged?.Invoke((EquipSlot)i, templateId);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override void OnNetworkDespawn()
        {
            m_EquippedTemplates.OnListChanged -= HandleListChanged;
            base.OnNetworkDespawn();
        }

        private void HandleListChanged(NetworkListEvent<int> evt)
        {
            if (evt.Type != NetworkListEvent<int>.EventType.Value)
            {
                return;
            }
            if (evt.Index <= 0 || evt.Index > SlotCount)
            {
                return;
            }

            EquipSlot slot = (EquipSlot)evt.Index;
            EquipChanged?.Invoke(slot, evt.Value);

            // Server-Bridge in die bestehenden Combat-Pfade (WeaponCatalog /
            // OffhandCatalog). Reine Visuals/HUD-Konsumenten haengen sich am
            // EquipChanged-Event auf — kein direkter NetVar-Zugriff von aussen.
            if (IsServer)
            {
                ApplyServerSideBridge(slot, evt.Value);
            }
        }

        // -------------------------------------------------------------------------
        // Server-API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg fuer <c>/equip &lt;inventorySlot&gt;</c>. Server
        /// validiert (Spawned, Slot in Range, Template equippable), zieht das
        /// Item aus dem Inventar in den passenden Equip-Slot und stoesst ggf.
        /// zurueck-zu-tauschende Items 1:1 in den freigewordenen Inventory-Slot.
        /// </summary>
        [ServerRpc]
        public void RequestEquipFromInventoryServerRpc(int inventoryIndex, ServerRpcParams rpc = default)
        {
            TryEquipFromInventoryServer(inventoryIndex);
        }

        /// <summary>
        /// Owner-Einstieg fuer <c>/unequip &lt;slot&gt;</c>. Server legt das
        /// aktuell ausgeruestete Item ueber den regulaeren Add-Pfad zurueck
        /// ins Inventar; wenn voll, bleibt das Item ausgeruestet.
        /// </summary>
        [ServerRpc]
        public void RequestUnequipServerRpc(EquipSlot slot, ServerRpcParams rpc = default)
        {
            TryUnequipServer(slot);
        }

        /// <summary>
        /// Direkter Server-Pfad ohne RPC — fuer interne Aufrufer (Tests,
        /// AI-Loadout-Init, Phase-16C-Stat-Aggregator). Move-Semantik
        /// identisch zur <see cref="RequestEquipFromInventoryServerRpc"/>.
        /// </summary>
        public bool TryEquipFromInventoryServer(int inventoryIndex)
        {
            if (!IsServer || m_Inventory == null)
            {
                return false;
            }

            InventoryItem invItem = m_Inventory.GetSlot(inventoryIndex);
            if (invItem.IsEmpty)
            {
                return false;
            }

            if (!ItemCatalogLoader.TryGetTemplate(invItem.TemplateId, out ItemTemplate template) || template == null)
            {
                Debug.LogWarning($"[PlayerEquipment] Equip: unbekanntes Template {invItem.TemplateId} in Slot {inventoryIndex}");
                return false;
            }

            if (!template.IsEquippable)
            {
                Debug.LogWarning($"[PlayerEquipment] Equip: Template {invItem.TemplateId} ist nicht equippable (EquipType=0).");
                return false;
            }

            EquipSlot target = (EquipSlot)template.EquipType;
            if (target == EquipSlot.None || (int)target > SlotCount)
            {
                Debug.LogWarning($"[PlayerEquipment] Equip: Template {invItem.TemplateId} hat ungueltigen EquipType={template.EquipType}.");
                return false;
            }

            // Ring-Routing: Source kennt nur eine Ring-EquipType (8). Wenn
            // Ring1 belegt aber Ring2 frei ist, leiten wir den Equip nach Ring2
            // um. Sind beide belegt, fuellt der normale Swap-Pfad unten Ring1.
            if (target == EquipSlot.Ring1
                && m_EquippedTemplates[(int)EquipSlot.Ring1] > 0
                && m_EquippedTemplates[(int)EquipSlot.Ring2] == 0)
            {
                target = EquipSlot.Ring2;
            }

            int targetIndex = (int)target;
            int previouslyEquipped = m_EquippedTemplates[targetIndex];
            ItemInstance previousInstance = m_EquipInstances[targetIndex];

            // Source-Parity: Zweihaender belegt MainHand + raeumt Offhand/Ranged.
            // Wir tauschen die Offhand zuerst zurueck ins Inventar; passt sie
            // nicht, bricht der ganze Equip-Versuch ab — sonst wuerde der
            // Offhand-Stack verloren gehen.
            bool isTwoHandedWeapon = target == EquipSlot.MainHand && ResolveIsTwoHanded(template);
            if (isTwoHandedWeapon)
            {
                if (!StashEquippedToInventoryIfPresent(EquipSlot.Offhand)
                    || !StashEquippedToInventoryIfPresent(EquipSlot.Ranged))
                {
                    Debug.LogWarning("[PlayerEquipment] Equip Zweihaender abgebrochen: Inventar konnte Offhand/Ranged nicht aufnehmen.");
                    return false;
                }
            }

            // 1) Item aus Inventory-Slot entfernen.
            if (!m_Inventory.TryRemoveAtServer(inventoryIndex, 1))
            {
                return false;
            }

            // 2) Vorher ausgeruestetes Item 1:1 in den jetzt leeren Inventory-Slot.
            //    Phase 19: wir schreiben die vollstaendige ItemInstance zurueck,
            //    damit Affixe beim spaeteren Re-Equip nicht verloren gehen.
            //    Equipment-Items sind nicht stackable -> direkter Slot-Write ist sicher.
            if (previouslyEquipped > 0)
            {
                m_Inventory.TrySetSlotServer(inventoryIndex, new InventoryItem(previousInstance, 1));
            }

            // 3) Neues Item in Equip-Slot schreiben — triggert OnListChanged → Bridge.
            //    Phase 19: Wenn der Inventory-Slot bereits eine echte Instanz
            //    (Rarity + Affixe aus Loot/Unequip) traegt, nehmen wir die
            //    direkt; nur Legacy-Slots ohne Roll-Daten erzeugen einen
            //    Common-Roll als Fallback.
            ItemInstance equipInstance = invItem.Instance;
            if (equipInstance.IsEmpty || equipInstance.TemplateId != invItem.TemplateId)
            {
                ulong seed = ItemRoller.MakeSeed(OwnerClientId, (ulong)invItem.TemplateId, (ulong)targetIndex);
                equipInstance = ItemRoller.Roll(invItem.TemplateId, ItemRarity.Common, seed);
            }
            SetSlotServer(targetIndex, equipInstance);
            return true;
        }

        /// <summary>
        /// Direkter Server-Pfad ohne RPC. Versucht das Item aus dem
        /// angegebenen Equip-Slot ins Inventar zurueckzulegen; gibt
        /// <c>false</c> wenn der Slot leer ist oder das Inventar voll war.
        /// </summary>
        public bool TryUnequipServer(EquipSlot slot)
        {
            if (!IsServer || m_Inventory == null)
            {
                return false;
            }
            int idx = (int)slot;
            if (idx <= 0 || idx > SlotCount)
            {
                return false;
            }

            int currentTemplate = m_EquippedTemplates[idx];
            if (currentTemplate <= 0)
            {
                return false;
            }

            // Phase 19: vollstaendige ItemInstance (Rarity + Affixe) ins
            // Inventar zurueckschieben; legacy TryAddItemServer-Pfad bleibt
            // fuer Stack-Consumables erhalten.
            ItemInstance instance = m_EquipInstances[idx];
            if (!m_Inventory.TryAddInstanceServer(instance))
            {
                Debug.LogWarning($"[PlayerEquipment] Unequip {slot}: Inventar voll — Item bleibt equippt.");
                return false;
            }

            ClearSlotServer(idx);
            return true;
        }

        // -------------------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------------------

        /// <summary>
        /// Seedet beim ersten Spawn auf dem Server die im Inspector
        /// konfigurierten Default-Templates (z. B. Longsword + Buckler) direkt
        /// in MainHand/Offhand. Loopt bewusst durch den NetworkList-Setter,
        /// damit <see cref="HandleListChanged"/> + <see cref="ApplyServerSideBridge"/>
        /// feuern und PlayerCombat sowie alle <see cref="EquipChanged"/>-
        /// Konsumenten (CharacterHUD, PlayerStats) automatisch in Sync gehen.
        /// Ueberspringt Slots, in denen schon ein Item liegt — damit Respawn
        /// oder spaeterer Loadout-Restore das Equipment nicht ueberschreibt.
        /// </summary>
        private void SeedDefaultLoadoutServer()
        {
            if (m_DefaultMainHandTemplate > 0
                && m_EquippedTemplates[(int)EquipSlot.MainHand] == 0
                && IsTemplateEquippableInSlot(m_DefaultMainHandTemplate, EquipSlot.MainHand))
            {
                ulong seed = ItemRoller.MakeSeed(OwnerClientId, (ulong)m_DefaultMainHandTemplate, (ulong)EquipSlot.MainHand);
                ItemInstance rolled = ItemRoller.Roll(m_DefaultMainHandTemplate, m_DefaultMainHandRarity, seed);
                SetSlotServer((int)EquipSlot.MainHand, rolled);
                Debug.Log($"[PlayerEquipment] Default-Loadout: Template {m_DefaultMainHandTemplate} -> MainHand " +
                          $"(Rarity={rolled.Rarity}, Affix1={rolled.Affix1Id}@{rolled.Affix1Score}, " +
                          $"Affix2={rolled.Affix2Id}@{rolled.Affix2Score}).");
            }

            // Offhand nur wenn MainHand nicht Zweihaender ist — Source-Parity.
            bool mainIsTwoHanded = ResolveIsTwoHandedByTemplateId(m_EquippedTemplates[(int)EquipSlot.MainHand]);
            if (!mainIsTwoHanded
                && m_DefaultOffhandTemplate > 0
                && m_EquippedTemplates[(int)EquipSlot.Offhand] == 0
                && IsTemplateEquippableInSlot(m_DefaultOffhandTemplate, EquipSlot.Offhand))
            {
                ulong seed = ItemRoller.MakeSeed(OwnerClientId, (ulong)m_DefaultOffhandTemplate, (ulong)EquipSlot.Offhand);
                ItemInstance rolled = ItemRoller.Roll(m_DefaultOffhandTemplate, m_DefaultOffhandRarity, seed);
                SetSlotServer((int)EquipSlot.Offhand, rolled);
                Debug.Log($"[PlayerEquipment] Default-Loadout: Template {m_DefaultOffhandTemplate} -> Offhand " +
                          $"(Rarity={rolled.Rarity}).");
            }
        }

        /// <summary>
        /// Atomare Schreib-Operation auf einen Equip-Slot: aktualisiert beide
        /// parallelen NetworkLists. <paramref name="instance"/> muss bereits
        /// die finale Roll-Information enthalten; die Template-Id wird aus
        /// <see cref="ItemInstance.TemplateId"/> uebernommen, damit Liste #1
        /// und Liste #2 konsistent bleiben.
        /// </summary>
        private void SetSlotServer(int idx, ItemInstance instance)
        {
            m_EquipInstances[idx] = instance;
            m_EquippedTemplates[idx] = instance.TemplateId; // triggert OnListChanged + Bridge.
        }

        /// <summary>Schreibt beide Listen auf "leer" zurueck.</summary>
        private void ClearSlotServer(int idx)
        {
            m_EquipInstances[idx] = ItemInstance.Empty;
            m_EquippedTemplates[idx] = 0;
        }

        /// <summary>
        /// Validiert, ob ein Template existiert, equippable ist und in den
        /// erwarteten Slot gehoert. Verhindert, dass z. B. ein Ring als
        /// Default-MainHand durchrutscht.
        /// </summary>
        private static bool IsTemplateEquippableInSlot(int templateId, EquipSlot expected)
        {
            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null)
            {
                Debug.LogWarning($"[PlayerEquipment] Default-Loadout: Template {templateId} nicht im Catalog.");
                return false;
            }
            if (!template.IsEquippable)
            {
                Debug.LogWarning($"[PlayerEquipment] Default-Loadout: Template {templateId} ist nicht equippable.");
                return false;
            }
            if ((EquipSlot)template.EquipType != expected)
            {
                Debug.LogWarning($"[PlayerEquipment] Default-Loadout: Template {templateId} EquipType={template.EquipType}, erwartet {expected}.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Wie <see cref="ResolveIsTwoHanded(ItemTemplate)"/>, aber direkt
        /// ueber die Template-Id. Liefert <c>false</c> bei leerem Slot oder
        /// unbekanntem Template.
        /// </summary>
        private bool ResolveIsTwoHandedByTemplateId(int templateId)
        {
            if (templateId <= 0)
            {
                return false;
            }
            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null)
            {
                return false;
            }
            return ResolveIsTwoHanded(template);
        }

        /// <summary>
        /// Verwendet die WeaponCatalog (Model-Bridge) um zu bestimmen, ob ein
        /// Item-Template als Zweihaender gilt. Faellt der Catalog/Model-Lookup
        /// fehl, defaulten wir auf <c>false</c> — lieber falsch einhaendig
        /// gefuehrt als Loot-vernichtende Equip-Abbruche.
        /// </summary>
        private bool ResolveIsTwoHanded(ItemTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.Model) || template.Model == "0")
            {
                return false;
            }
            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null)
            {
                return false;
            }
            if (!catalog.TryGet(template.Model, out WeaponDefinition weaponDef))
            {
                return false;
            }
            return weaponDef.IsTwoHanded;
        }

        /// <summary>
        /// Hilfs-Move fuer Zweihaender-Equip: vorhandenes Offhand/Ranged zuerst
        /// ins Inventar zurueck. Gibt <c>true</c> wenn der Slot leer war oder
        /// das Item untergebracht werden konnte.
        /// </summary>
        private bool StashEquippedToInventoryIfPresent(EquipSlot slot)
        {
            int idx = (int)slot;
            int current = m_EquippedTemplates[idx];
            if (current <= 0)
            {
                return true;
            }
            // Phase 19: Affix-Roll mitnehmen, nicht nur die Template-Id.
            ItemInstance instance = m_EquipInstances[idx];
            if (!m_Inventory.TryAddInstanceServer(instance))
            {
                return false;
            }
            ClearSlotServer(idx);
            return true;
        }

        /// <summary>
        /// Server-side Bridge: spiegelt Weapon/Shield/Ranged-Aenderungen in den
        /// bestehenden PlayerCombat-Pfad. Andere Slots haben aktuell keine
        /// Combat-Auswirkung — werden erst von <c>PlayerStats</c> (Phase 16B)
        /// und Visual-Layern (Phase 17) konsumiert.
        /// </summary>
        private void ApplyServerSideBridge(EquipSlot slot, int newTemplateId)
        {
            if (m_Combat == null)
            {
                return;
            }

            switch (slot)
            {
                case EquipSlot.MainHand:
                    m_Combat.Server_ApplyWeaponFromTemplate(newTemplateId);
                    break;
                case EquipSlot.Offhand:
                    m_Combat.Server_ApplyOffhandFromTemplate(newTemplateId);
                    break;
                case EquipSlot.Ranged:
                    // Source-Parity: Bows liegen im WeaponCatalog, nicht im
                    // OffhandCatalog. Eigener Bridge-Pfad, damit der Ranged-
                    // Slot in m_CurrentRangedId landet und Aimed Shot &Co. die
                    // korrekte Bogen-Definition aus CurrentRangedWeapon lesen.
                    m_Combat.Server_ApplyRangedFromTemplate(newTemplateId);
                    break;
            }
        }
    }
}
