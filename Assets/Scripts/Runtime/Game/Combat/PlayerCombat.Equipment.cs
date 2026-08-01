using System.Threading;
using Riftstorm.Game.Combat.CombatStates;
using Riftstorm.Game.Input;
using Riftstorm.Game.Items;
using Riftstorm.Game.Movement;
using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using Riftstorm.Gameplay.Combat.Spells.Visuals;
using Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime;
using Riftstorm.Management.SoundManagement;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Tolik.Riftstorm.Runtime.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    public sealed partial class PlayerCombat
    {
        private WeaponDefinition ResolveCurrentWeapon()
        {
            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null)
            {
                Debug.LogWarning("[PlayerCombat] WeaponCatalog not loaded — attack ignored.");
                return null;
            }
            // Equipped Waffe bevorzugt; sonst Fallback auf "unarmed" (Faust-Kampf).
            // Der "unarmed"-Eintrag liegt in Assets/StreamingAssets/combat/weapons.json
            // und stellt sicher, dass Spieler ohne Item bewaffnung ueberhaupt
            // zuschlagen koennen (Range 1.2, BaseDamage 4, cd 0.6s).
            if (m_CurrentWeaponId.Value.Length > 0)
            {
                WeaponDefinition equipped = catalog.Get(m_CurrentWeaponId.Value.ToString());
                if (equipped != null) { return equipped; }
            }
            return catalog.Get("unarmed");
        }

        /// <summary>
        /// Aktuell ausgeruestete <see cref="WeaponDefinition"/> oder <c>null</c>,
        /// wenn keine Waffe gesetzt ist bzw. der WeaponCatalog noch nicht
        /// geladen wurde. Vom HUD genutzt, um den effektiven Angriffsschaden
        /// (<c>weapon.BaseDamage + WeaponDamage + STR/2</c>) zu rendern.
        /// </summary>
        public WeaponDefinition CurrentWeapon => ResolveCurrentWeapon();

        /// <summary>
        /// Aufloesung der Ranged-Waffe (Bow/Crossbow/Gun) aus dem dedizierten
        /// Ranged-Slot. Anders als <see cref="ResolveCurrentWeapon"/> gibt es
        /// hier KEINEN <c>unarmed</c>-Fallback: ohne ausgeruesteten Bogen ist
        /// das Ergebnis <c>null</c>, damit <c>UnitStats.BaseRangedWeaponDamage</c>
        /// auf 0 faellt und <c>SpellCaster.CheckEquipment</c> Ranged-Spells
        /// (<c>required_equipment=12</c>) blockt. Zusaetzlich wird ein im
        /// Slot liegendes Nicht-Ranged-Modell (defensive Validierung, sollte
        /// vom Equip-Pfad bereits abgefangen sein) auf <c>null</c> gemappt.
        /// </summary>
        private WeaponDefinition ResolveRangedWeapon()
        {
            if (m_CurrentRangedId.Value.Length == 0) { return null; }
            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null) { return null; }
            WeaponDefinition equipped = catalog.Get(m_CurrentRangedId.Value.ToString());
            if (equipped == null || !equipped.IsRanged) { return null; }
            return equipped;
        }

        /// <summary>
        /// Aktuell ausgeruestete Ranged-Waffe (Bow/Crossbow/Gun) oder <c>null</c>.
        /// Wird von <see cref="UnitStats.BaseRangedWeaponDamage"/> gelesen und
        /// von <see cref="ResolveWeaponFor"/>, um bei Ranged-Spells die
        /// passende Waffe zu liefern.
        /// </summary>
        public WeaponDefinition CurrentRangedWeapon => ResolveRangedWeapon();

        /// <summary>
        /// Aufloesung der aktiven Auto-Attack-Waffe je nach Modus
        /// (<see cref="m_RangedModeActive"/>): im Ranged-Modus die Bogen-Definition
        /// aus dem Ranged-Slot, sonst die Main-Hand. Auto-Fallback: ist der
        /// Ranged-Modus aktiv, aber kein Bogen ausgeruestet, wird die Main-Hand
        /// (inkl. <c>unarmed</c>-Fallback) geliefert — so schlaegt der Rechtsklick
        /// nie ins Leere. Wird sowohl von <see cref="RequestAttackServerRpc"/>
        /// (Server, Damage) als auch von <see cref="CurrentWeaponRange"/>
        /// (Owner, Follow-Stop-Radius) gelesen, damit beide Seiten dieselbe
        /// Waffe annehmen.
        /// </summary>
        private WeaponDefinition ResolveAutoAttackWeapon()
        {
            if (m_RangedModeActive.Value)
            {
                WeaponDefinition ranged = ResolveRangedWeapon();
                if (ranged != null) { return ranged; }
            }
            return ResolveCurrentWeapon();
        }

        /// <summary>
        /// Spell-aware Waffen-Aufloesung: Spells mit <c>required_equipment=12</c>
        /// (Ranged) bekommen die Bogen-Definition aus dem Ranged-Slot — falls
        /// keine vorhanden ist, <c>null</c> (kein <c>unarmed</c>-Fallback, weil
        /// <c>SpellCaster.CheckEquipment</c> den Cast vorher bereits ablehnt).
        /// Alle anderen Spells (Melee, Magie, Heal, Aura, ...) ziehen die
        /// Main-Hand-Auflfoesung mit <c>unarmed</c>-Fallback, damit z. B.
        /// Sinister Strike auch ohne Schwert eine Faust-Animation spielt.
        /// </summary>
        public WeaponDefinition ResolveWeaponFor(SpellTemplate spell)
        {
            if (spell != null && spell.RequiredEquipment == 12L)
            {
                return ResolveRangedWeapon();
            }
            return ResolveCurrentWeapon();
        }

        /// <summary>
        /// Effektive Auto-Attack-Reichweite in Unity-Worldunits (Metern). Liefert
        /// die Reichweite des aktiven Basis-Spells — Spell 82 (Ranged Attack) im
        /// Ranged-Modus mit ausgeruestetem Bogen, sonst Spell 81 (Melee Swing) —,
        /// umgerechnet ueber <see cref="SpellUtils.RangeToMeters"/>. Damit zeigen
        /// der lokale <see cref="AttackRangeIndicator"/> (Ground-Kreis) UND der
        /// Follow-Stop-Radius im <see cref="MobaCommandController"/> exakt die
        /// Reichweite, gegen die der Cast serverseitig in
        /// <see cref="Spells.Runtime.SpellCaster"/> validiert wird
        /// (frueher faelschlich die — groessere — <c>WeaponDefinition.Range</c>,
        /// wodurch der Spieler ausserhalb der Spell-Reichweite stoppte und der
        /// Auto-Attack still verworfen wurde). Fallback auf die Waffen-Range nur,
        /// solange der Spell-Katalog noch nicht geladen ist; 0, wenn auch keine
        /// Waffe gesetzt ist.
        /// </summary>
        public float CurrentWeaponRange
        {
            get
            {
                int spellEntry = m_RangedModeActive.Value && ResolveRangedWeapon() != null
                    ? k_RangedAutoAttackSpell
                    : k_MeleeAutoAttackSpell;
                SpellTemplate spell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
                if (spell != null && spell.Range > 0f)
                {
                    return SpellUtils.RangeToMeters(spell.Range);
                }
                WeaponDefinition weapon = ResolveAutoAttackWeapon();
                return weapon != null ? weapon.Range : 0f;
            }
        }

        // -------------------------------------------------------------------------
        // Weapon-Swap (Console-Command /weapon, später Loadout/Inventory)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg fuer einen Waffenwechsel. Wird aktuell vom
        /// <see cref="UI.Console.Commands.WeaponCommand"/> aufgerufen, spaeter vom
        /// Loadout-/Inventory-System. Lokale Sanity-Checks (Spawn + Owner), die
        /// autoritative Katalog-Validierung und das Setzen des
        /// <c>NetworkVariable</c> passieren serverseitig in
        /// <see cref="RequestEquipWeaponServerRpc"/>.
        /// </summary>
        /// <param name="weaponId">Id aus <c>combat/weapons.json</c> (z.B. <c>"shortsword"</c>).</param>
        public void TryRequestEquipWeapon(string weaponId)
        {
            if (!IsSpawned || !IsOwner || string.IsNullOrWhiteSpace(weaponId))
            {
                return;
            }
            RequestEquipWeaponServerRpc(new FixedString64Bytes(weaponId));
        }

        [ServerRpc]
        private void RequestEquipWeaponServerRpc(FixedString64Bytes weaponId, ServerRpcParams _ = default)
        {
            if (weaponId.Length == 0)
            {
                return;
            }

            // Katalog-Validierung — unbekannte Ids werden verworfen, sonst wuerde
            // ResolveCurrentWeapon dauerhaft null liefern und Attacks/Range-Indicator
            // tot bleiben.
            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null)
            {
                Debug.LogWarning("[PlayerCombat] WeaponCatalog not loaded — equip ignored.");
                return;
            }
            if (!catalog.TryGet(weaponId.ToString(), out WeaponDefinition weaponDef))
            {
                Debug.LogWarning($"[PlayerCombat] Unknown weapon id '{weaponId}' — equip ignored.");
                return;
            }

            // Laufenden Swing/Recovery abbrechen, damit der neue Cooldown direkt
            // aus der frisch ausgeruesteten Waffe greift, sobald der naechste
            // Attack-Request eingeht.
            if (m_CurrentState == AttackingState)
            {
                ChangeState(IdleState);
                NotifyAttackCanceledClientRpc();
            }

            m_CurrentWeaponId.Value = weaponId;

            // Source-Parity: TwoHanded-Waffen blockieren den Offhand-Slot. Schild/
            // Buckler/Torch werden beim Equip eines Greatswords/Bows automatisch
            // entfernt; das NetVar-Event fuer Offhand feuert anschliessend und
            // raeumt die OffHand-FLARE-Schicht clientseitig leer.
            if (weaponDef.IsTwoHanded && m_CurrentOffhandId.Value.Length > 0)
            {
                m_CurrentOffhandId.Value = default;
            }
        }

        // -------------------------------------------------------------------------
        // Offhand-Swap (Console-Command /offhand, später Loadout/Inventory)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Owner-Einstieg fuer einen Offhand-Wechsel. Akzeptiert <c>"none"</c>,
        /// <c>"clear"</c> oder Leerstring zum Ausziehen. Lokale Sanity-Checks
        /// (Spawn + Owner), autoritative Katalog-Validierung serverseitig in
        /// <see cref="RequestEquipOffhandServerRpc"/>.
        /// </summary>
        /// <param name="offhandId">Id aus <c>combat/offhand_items.json</c> (z. B. <c>"buckler"</c>) oder <c>"none"</c>.</param>
        public void TryRequestEquipOffhand(string offhandId)
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            string normalized = offhandId == null ? string.Empty : offhandId.Trim();
            if (normalized.Equals("none", System.StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("clear", System.StringComparison.OrdinalIgnoreCase))
            {
                normalized = string.Empty;
            }
            RequestEquipOffhandServerRpc(new FixedString64Bytes(normalized));
        }

        [ServerRpc]
        private void RequestEquipOffhandServerRpc(FixedString64Bytes offhandId, ServerRpcParams _ = default)
        {
            // Leere Id = ausziehen. Direkt setzen, ohne Katalog-Lookup.
            if (offhandId.Length == 0)
            {
                if (m_CurrentOffhandId.Value.Length > 0)
                {
                    m_CurrentOffhandId.Value = default;
                }
                return;
            }

            // TwoHanded-Waffe blockiert Offhand komplett — Source-Parity zum
            // Original (kein Buckler-Equip moeglich, solange Zweihaender gezogen).
            WeaponDefinition currentWeapon = ResolveCurrentWeapon();
            if (currentWeapon != null && currentWeapon.IsTwoHanded)
            {
                Debug.LogWarning($"[PlayerCombat] Cannot equip offhand '{offhandId}' while two-handed weapon '{currentWeapon.Id}' is wielded.");
                return;
            }

            m_OffhandCatalogLoader ??= ServiceLocator.Get<OffhandCatalogLoader>();
            OffhandCatalog catalog = m_OffhandCatalogLoader?.GetCached();
            if (catalog == null)
            {
                Debug.LogWarning("[PlayerCombat] OffhandCatalog not loaded — equip ignored.");
                return;
            }
            if (!catalog.TryGet(offhandId.ToString(), out OffhandDefinition _))
            {
                Debug.LogWarning($"[PlayerCombat] Unknown offhand id '{offhandId}' — equip ignored.");
                return;
            }

            m_CurrentOffhandId.Value = offhandId;
        }

        // -------------------------------------------------------------------------
        // Bridge fuer PlayerEquipment (Phase 16B)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Server-Bridge fuer <see cref="PlayerEquipment"/>: setzt die Waffe
        /// anhand eines <see cref="ItemTemplate"/>-Entries. Aufloesung erfolgt
        /// ueber <see cref="ItemTemplate.Model"/> → WeaponCatalog. Bei
        /// <paramref name="templateId"/> &lt;= 0 wird die Waffe geleert.
        /// </summary>
        /// <remarks>
        /// Wird ausschliesslich von <see cref="PlayerEquipment"/> aufgerufen,
        /// nachdem dort der NetworkList-Slot serverseitig geschrieben wurde.
        /// Logik (Katalog-Validierung, Attack-Cancel, Zweihaender-Offhand-Clear)
        /// ist bewusst eine 1:1-Kopie von <c>RequestEquipWeaponServerRpc</c> —
        /// kein Refactor in dieser Phase, damit der bestehende Console-Pfad
        /// stabil bleibt.
        /// </remarks>
        internal void Server_ApplyWeaponFromTemplate(int templateId)
        {
            if (!IsServer)
            {
                return;
            }

            // Leerer Slot: Waffe ausziehen — kein Default-Fallback hier, damit
            // PlayerEquipment die alleinige Quelle der Wahrheit bleibt sobald
            // ein Item drin liegt; Default greift weiter nur beim Spawn.
            if (templateId <= 0)
            {
                if (m_CurrentWeaponId.Value.Length > 0)
                {
                    m_CurrentWeaponId.Value = default;
                }
                return;
            }

            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null
                || string.IsNullOrEmpty(template.Model) || template.Model == "0")
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyWeaponFromTemplate: Template {templateId} hat kein gueltiges Model.");
                return;
            }

            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null || !catalog.TryGet(template.Model, out WeaponDefinition weaponDef))
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyWeaponFromTemplate: Model '{template.Model}' nicht im WeaponCatalog.");
                return;
            }

            if (m_CurrentState == AttackingState)
            {
                ChangeState(IdleState);
                NotifyAttackCanceledClientRpc();
            }

            m_CurrentWeaponId.Value = new FixedString64Bytes(template.Model);

            if (weaponDef.IsTwoHanded && m_CurrentOffhandId.Value.Length > 0)
            {
                m_CurrentOffhandId.Value = default;
            }
        }

        /// <summary>
        /// Server-Bridge fuer <see cref="PlayerEquipment"/>: setzt die
        /// Ranged-Waffe anhand eines <see cref="ItemTemplate"/>-Entries.
        /// Aufloesung ueber <see cref="ItemTemplate.Model"/> → WeaponCatalog
        /// (Bows liegen in <c>combat/weapons.json</c>, nicht im OffhandCatalog).
        /// Bei <paramref name="templateId"/> &lt;= 0 wird der Ranged-Slot
        /// geleert. Defensive Validierung: nur Modelle mit <c>IsRanged==true</c>
        /// werden akzeptiert, damit ein Longsword nicht durch einen falsch
        /// gemappten EquipType=Ranged-Item-Eintrag in den Ranged-Slot rutscht.
        /// </summary>
        internal void Server_ApplyRangedFromTemplate(int templateId)
        {
            if (!IsServer)
            {
                return;
            }

            if (templateId <= 0)
            {
                if (m_CurrentRangedId.Value.Length > 0)
                {
                    m_CurrentRangedId.Value = default;
                }
                return;
            }

            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null
                || string.IsNullOrEmpty(template.Model) || template.Model == "0")
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyRangedFromTemplate: Template {templateId} hat kein gueltiges Model.");
                return;
            }

            m_WeaponCatalogLoader ??= ServiceLocator.Get<WeaponCatalogLoader>();
            WeaponCatalog catalog = m_WeaponCatalogLoader?.GetCached();
            if (catalog == null || !catalog.TryGet(template.Model, out WeaponDefinition weaponDef))
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyRangedFromTemplate: Model '{template.Model}' nicht im WeaponCatalog.");
                return;
            }

            if (!weaponDef.IsRanged)
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyRangedFromTemplate: Model '{template.Model}' ist keine Ranged-Waffe (Type={weaponDef.Type}).");
                return;
            }

            m_CurrentRangedId.Value = new FixedString64Bytes(template.Model);
        }

        /// <summary>
        /// Server-Bridge fuer <see cref="PlayerEquipment"/>: setzt die Offhand
        /// anhand eines <see cref="ItemTemplate"/>-Entries. Aufloesung ueber
        /// <see cref="ItemTemplate.Model"/> → OffhandCatalog. Bei
        /// <paramref name="templateId"/> &lt;= 0 wird die Offhand geleert.
        /// </summary>
        internal void Server_ApplyOffhandFromTemplate(int templateId)
        {
            if (!IsServer)
            {
                return;
            }

            if (templateId <= 0)
            {
                if (m_CurrentOffhandId.Value.Length > 0)
                {
                    m_CurrentOffhandId.Value = default;
                }
                return;
            }

            // Zweihaender blockt Offhand — Source-Parity. PlayerEquipment
            // raeumt vor einem 2H-Equip die Offhand-Slots leer; hier ist der
            // Check nur die letzte Sicherung gegen Out-of-Order-Bridge-Calls.
            WeaponDefinition currentWeapon = ResolveCurrentWeapon();
            if (currentWeapon != null && currentWeapon.IsTwoHanded)
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyOffhandFromTemplate: Zweihaender '{currentWeapon.Id}' blockiert Offhand.");
                return;
            }

            if (!ItemCatalogLoader.TryGetTemplate(templateId, out ItemTemplate template) || template == null
                || string.IsNullOrEmpty(template.Model) || template.Model == "0")
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyOffhandFromTemplate: Template {templateId} hat kein gueltiges Model.");
                return;
            }

            m_OffhandCatalogLoader ??= ServiceLocator.Get<OffhandCatalogLoader>();
            OffhandCatalog catalog = m_OffhandCatalogLoader?.GetCached();
            if (catalog == null || !catalog.TryGet(template.Model, out OffhandDefinition _))
            {
                Debug.LogWarning($"[PlayerCombat] Server_ApplyOffhandFromTemplate: Model '{template.Model}' nicht im OffhandCatalog.");
                return;
            }

            m_CurrentOffhandId.Value = new FixedString64Bytes(template.Model);
        }

    }
}
