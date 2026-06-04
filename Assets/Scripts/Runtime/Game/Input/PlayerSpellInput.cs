using System.Collections.Generic;
using Riftstorm.Game.Combat;
using Riftstorm.Game.Spells;
using UnityEngine;

namespace Riftstorm.Game.Input
{
    /// <summary>
    /// Owner-Bridge zwischen <see cref="PlayerInputController.SpellSlotPressed"/>
    /// und <see cref="PlayerCombat.TryRequestCastSpell"/>. Haelt das aktuelle
    /// Loadout (Slot-Index → Spell-Entry aus <c>spells/_templates.json</c>) und
    /// uebersetzt einen Hotkey-Druck in einen Cast-Request. Die Bridge ist bewusst
    /// duenn — sie kennt keine Cooldowns, kein Ziel-Resolving und keine Resource-
    /// Pruefung; das alles entscheidet der Server in
    /// <see cref="PlayerCombat.BeginCast"/> / <see cref="Spells.SpellExecutor"/>.
    /// <para>
    /// Loadout-Quelle ist aktuell ein Inspector-Array (Test-/Bootstrap-Loadout),
    /// bis der reguliere Loadout-Pipeline-Eintrag aus den character_actionbars
    /// auf das Prefab fliesst.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpellInput : MonoBehaviour
    {
        [Tooltip("PlayerInputController, dessen SpellSlotPressed-Event abonniert wird. " +
                 "Wird in Awake per GetComponentInParent gesucht, falls nicht gesetzt.")]
        [SerializeField] private PlayerInputController m_Input;

        [Tooltip("PlayerCombat dieses Spielers. Zielfunktion ist TryRequestCastSpell — " +
                 "die filtert intern bereits per IsOwner / IsSpawned.")]
        [SerializeField] private PlayerCombat m_Combat;

        [Tooltip("Owner-lokaler Ground-Target-Picker. Wird genutzt, sobald ein Spell " +
                 "mit SpellAttributes.TargetsGround gedrueckt wird. Fehlt der Picker, " +
                 "fallen Ground-Spells leise aus (mit Warn-Log) — der Spielfluss bleibt " +
                 "stabil. Wird in Awake per GetComponentInParent gesucht, falls leer.")]
        [SerializeField] private GroundTargetPicker m_GroundPicker;

        [Tooltip("Loadout-Mapping Slot-Index → Spell-Entry (Eintrag aus " +
                 "spells/_templates.json, z. B. 133 fuer Fireball). Slot 0 entspricht " +
                 "Taste '1', Slot 9 entspricht Taste '0'. Eintrag <= 0 = leerer Slot.")]
        [SerializeField] private int[] m_SlotSpellEntries = new int[PlayerInputController.SpellSlotCount];

        /// <summary>
        /// Read-only Sicht auf das aktuelle Loadout (Slot-Index → Spell-Entry).
        /// Wird von der ActionBar-HUD genutzt, um Icons und Cooldowns je Slot zu
        /// rendern (kein Mutations-API — der Slot-Inhalt aendert sich aktuell
        /// ausschliesslich ueber den Inspector / Loadout-Pipeline-Eintrag).
        /// </summary>
        public IReadOnlyList<int> SlotEntries => m_SlotSpellEntries;

        private void Awake()
        {
            if (m_Input == null)
            {
                m_Input = GetComponentInParent<PlayerInputController>();
            }
            if (m_Combat == null)
            {
                m_Combat = GetComponentInParent<PlayerCombat>();
            }
            if (m_GroundPicker == null)
            {
                m_GroundPicker = GetComponentInParent<GroundTargetPicker>();
            }

            // Loadout-Array auf die kanonische Slot-Anzahl normalisieren — verhindert
            // IndexOutOfRange-Probleme, falls das Array im Inspector aus Versehen
            // kuerzer/laenger angelegt wurde. Inhalte werden uebernommen, fehlende
            // Eintraege bleiben 0 (= leerer Slot).
            if (m_SlotSpellEntries == null || m_SlotSpellEntries.Length != PlayerInputController.SpellSlotCount)
            {
                int[] resized = new int[PlayerInputController.SpellSlotCount];
                if (m_SlotSpellEntries != null)
                {
                    int copyCount = Mathf.Min(m_SlotSpellEntries.Length, resized.Length);
                    for (int i = 0; i < copyCount; i++)
                    {
                        resized[i] = m_SlotSpellEntries[i];
                    }
                }
                m_SlotSpellEntries = resized;
            }
        }

        private void OnEnable()
        {
            if (m_Input != null)
            {
                m_Input.SpellSlotPressed += OnSpellSlotPressed;
            }
        }

        private void OnDisable()
        {
            if (m_Input != null)
            {
                m_Input.SpellSlotPressed -= OnSpellSlotPressed;
            }
        }

        private void OnSpellSlotPressed(int slotIndex)
        {
            if (m_Combat == null || m_SlotSpellEntries == null)
            {
                return;
            }
            if (slotIndex < 0 || slotIndex >= m_SlotSpellEntries.Length)
            {
                return;
            }
            int spellEntry = m_SlotSpellEntries[slotIndex];
            if (spellEntry <= 0)
            {
                // Leerer Slot — kein Beep, kein Warn-Log; das ist der Default-Zustand
                // fuer ein noch unbelegtes Hotkey-Slot und passiert haeufig.
                return;
            }

            SpellTemplate spell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);

            // Skillshots feuern sofort gerichtet in Cursor-Richtung: aktuellen
            // Boden-Zielpunkt unter der Maus greifen und als Destination senden.
            // Faellt kein Aim-Punkt an (kein Picker/Kamera), feuert der Server in
            // Blickrichtung des Casters (Forward-Fallback).
            if (spell != null && spell.IsSkillshot)
            {
                if (m_GroundPicker != null && m_GroundPicker.TryGetAimPoint(out Vector3 aimPoint))
                {
                    m_Combat.TryRequestCastSpellAtGround(spellEntry, aimPoint);
                }
                else
                {
                    m_Combat.TryRequestCastSpell(spellEntry);
                }
                return;
            }

            // Ground-Target-Spells gehen ueber den Picker: erst Reticle anzeigen, dann
            // bei LMB-Confirm die Welt-Position an PlayerCombat.TryRequestCastSpellAtGround
            // schicken. Spells ohne TargetsGround-Flag laufen weiterhin direkt.
            if (spell != null && spell.IsGroundTargeted)
            {
                if (m_GroundPicker == null)
                {
                    Debug.LogWarning($"[PlayerSpellInput] Ground-Spell {spellEntry} gedrueckt, aber kein GroundTargetPicker verdrahtet — Cast wird verworfen.");
                    return;
                }
                float rangeMeters = SpellUtils.RangeToMeters(spell.Range);
                int capturedEntry = spellEntry;
                m_GroundPicker.BeginPick(
                    spellEntry,
                    rangeMeters,
                    onConfirmed: worldDestination =>
                    {
                        if (m_Combat != null)
                        {
                            m_Combat.TryRequestCastSpellAtGround(capturedEntry, worldDestination);
                        }
                    },
                    onCancelled: null);
                return;
            }

            m_Combat.TryRequestCastSpell(spellEntry);
        }

        /// <summary>
        /// Server- bzw. Bootstrap-Einstieg, um das Loadout zur Laufzeit zu setzen.
        /// Spaeter aus der character_actionbars-Pipeline aufgerufen. Idempotent:
        /// ungueltige Laengen werden auf <see cref="PlayerInputController.SpellSlotCount"/>
        /// getrimmt/aufgefuellt.
        /// </summary>
        public void SetLoadout(int[] spellEntriesBySlot)
        {
            int[] target = new int[PlayerInputController.SpellSlotCount];
            if (spellEntriesBySlot != null)
            {
                int copyCount = Mathf.Min(spellEntriesBySlot.Length, target.Length);
                for (int i = 0; i < copyCount; i++)
                {
                    target[i] = spellEntriesBySlot[i];
                }
            }
            m_SlotSpellEntries = target;
        }
    }
}
