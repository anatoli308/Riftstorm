# 21 - Audit 2026-06-02: Spells, Combat, NPCs & offene Punkte

> Kanonischer Audit-Stand seit 2026-06-02 (loest den vorigen Stand vom
> 2026-06-01 ab; veraltete Vorgaenger-/Pipeline-Docs wurden entfernt).
> Stand nach Code-Reverifikation am 2026-06-02: Die meisten in Doc 20 als
> "offen/fehlerhaft" gefuehrten Punkte sind inzwischen im Code bzw. in den
> JSON-Daten **behoben**. Diese Notiz dokumentiert den aktuellen Ist-Stand und
> grenzt die wenigen echten Restluecken klar ab.
>
> Methodik: gezielte Code-Verifikation (Datei + Zeile angegeben) plus
> Daten-Check in `_templates.json`. Playmode-only-Punkte sind als solche
> markiert und nicht als "behoben" gewertet.

---

## 1. Zweck dieses Dokuments

Dieses Dokument loest Doc 20 als kanonischen Audit-Stand ab. Es beantwortet:

1. Welche der vormals offenen Punkte sind seit 2026-06-01 verifiziert behoben?
2. Welche Punkte sind weiterhin echte Code-/Daten-Luecken?
3. Was bleibt reiner Playmode-Testbedarf?
4. Was hat sich an der NPC-AI (Spell-Casting) geaendert?

Begleitdokumente:

- [19-current-state-und-cleanup.md](19-current-state-und-cleanup.md) — Detailreferenz
  Ranged/NPC/HUD inkl. aktualisiertem NPC-Casting-Abschnitt (Primary/N-Slots).
- [../TODOS_juni_spells.md](../TODOS_juni_spells.md) — Roh-Notizen/Backlog.

---

## 2. Seit 2026-06-01 verifiziert behoben

| Thema | Vormaliger Befund (Doc 20) | Aktueller Stand | Nachweis |
|---|---|---|---|
| `mana_pct`-Berechnung | Rohfaktor auf `MaxMana` addiert | Prozentkosten korrekt aus `MaxMana` berechnet | `SpellUtils.cs:126-128` |
| `Greater Heal` Manaformel | `mana_formula` syntaktisch kaputt → 0 | Daten korrigiert: `"2+((clvl*75)/10)"` (schliessende Klammer vorhanden) | `_templates.json` Entry 66 |
| `Resurrection` | Nur Heal-Budget, kein echtes Revive | Voller Revive-Pfad fuer tote Ziele | `SpellExecutor.cs:711-726` + `UnitStats` ServerRevive |
| `Reincarnation` | Aura-Typ `RepopOntopOfSelf` nicht ausgewertet | Self-Revive-Handler vorhanden | `UnitStats.cs:1033-1075` |
| `Cleanse` / `Remove Curse` | Dispel nur nach Vorzeichen | Dispel nach `DispelType`-Bitmaske (Magic/Disease/Poison/Curse) | `SpellExecutor.cs:662-671` + `AuraManager` RemoveDispellable |
| `Arrow Flurry` | Kein Verbraucher fuer `RangedCooldown` | `RangedCooldown`-Aura wird im Ranged-Cooldown-Pfad konsumiert | `PlayerCombat.cs:891-897` |
| `Multi-Shot` | `TriggerSpell` zeigt evtl. falsch | `TriggerSpell` loest korrekt den Folge-Spell aus | `SpellExecutor.cs:524-531` / `385-416` |
| Heal Floating Text | `FloatingCombatText` nur auf Damage | Subscribed jetzt auch `ClientHealReceived` | `FloatingCombatText.cs:118` |
| Periodic Aura letzter Tick | DoT/HoT verlor Schluss-Tick beim Expire | Delta wird vor Expire geclamped, letzter Tick laeuft | `AuraManager.cs:596-625` |
| Damage-/Healing-Modifier-Auren | Unklar im Math-Pfad | Modifier sauber im Schadens-/Heal-Pfad verdrahtet | `CombatFormulas.cs:340-420` / `459-475` |
| CC-Immunitaet (`Wings of Freedom`, `Deep Freeze`) | Nur teilweise verifiziert | `HasMechanicImmunity` greift in Aura-Apply + Cast-Gate | `AuraManager.cs:399-422` + `SpellExecutor.cs:513-518` |
| Hold-to-move + Cast-Interrupt | Offen in TODOs | Implementiert (Move-Hold + Cast-Abbruch bei Move) | `MobaCommandController` / `PlayerMovement` |
| Threat-Anzeige im Target-UI | Bereits erledigt | Bleibt erledigt | Target-Portrait Threat-Readout |
| NPC `spell_primary` + dynamische N-Slots | Existierte nicht | Implementiert (Notfall-Primary + Fallback, N Slots) | `NpcController.cs` / `NpcTemplate.cs` (s. Abschnitt 5) |

---

## 3. Weiterhin offen (echte Code-/Daten-Luecke)

| Thema | Typ | Aktueller Befund | Ort |
|---|---|---|---|
| `Illusion Gate` / `SummonObject` | Fehlender Handler | Enum `SummonObject` existiert, aber **kein** Case im `SpellExecutor` | `SpellEnums.cs:50` |
| `Focused Evasion` / `AuraType.Proc` | Fehlender Runtime-Pfad | Enum `Proc` existiert, aber keine Laufzeit-Auswertung der Proc-Aura | `SpellEnums.cs:126` |
| `Charge` Combat-Feel | Design-/Gameplay-Luecke | Nur Impuls/Move-Effekt; kein Swing-Pose-Wechsel (`PlayCast` statt `PlaySwing`), kein direkter Follow-up-Autoattack | `SpellExecutor.cs:597-614` |
| Spell-ID `318` | Datenanomalie | `300`-`317` und `319`-`324` existieren; `318` fehlt weiterhin in `_templates.json` | `_templates.json` (317 @ Z.5992, 319 @ Z.6022) |

Zusaetzlich aus dem Backlog (systemisch noch nicht gebaut, nicht neu):

- Channeling-System (gehaltene Casts mit periodischer Wirkung).
- Freie, richtungsbasierte Skillshots/Projektile (aktuell zielgebunden).
- Boden-Marker-AoE-Spells mit freier Platzierung als Standard-Pattern.
- Cast-Partikel / Projektil-Travel-Visuals als durchgaengiges System.
- `equipment.json`-Pipeline (Daten-getriebenes Equipment-Tuning).
- NPC-Move-Speed-Scaling.
- Buff-Phase (Phase 17) Feinschliff.

---

## 4. Noch im Playmode nachtesten

Systemisch vorhanden, aber pro Template/Spell nur im Playmode final verifizierbar
(Datenmapping, Tooltip-Readout, konkreter Effekt):

| Themenblock | Warum Test statt System-Fix? |
|---|---|
| `Blessing of Champions`, `Blessing of Defense` | Formel-/Aura-Skalierung vorhanden; bei fehlender Wirkung eher Datenmapping/Tooltip. |
| `Fortification Aura`, `Divine Protection`, `Vengeance Aura`, `Salvation Aura`, `Righteous Storm`, `Shield Block`, `Aegis of Valor`, `Magical Amplification`, `Magical Dampening`, `Warmth` | Modifier-Auren im Code vorhanden; Template-Mapping playmode-pruefen. |
| `Radiance`, `Holy Wrath`, `Ignite`, `Plague` | Periodic-Ticks vorhanden; reiner Initialeffekt waere Daten-/Anzeige-Bug. |
| `Wings of Freedom`, `Deep Freeze` | Immunitaet im Code; konkrete School/Mechanic-Mappings nachstellen. |
| `Hammer of Might`, `Touch of Salvation`, `Mighty Blow`, `Penance`, `Blessed Shield` | Systempfade vorhanden; offen ist Template-Verhalten/Feinschliff. |
| `Chains of Ice`, `Sleep Arrow`, `Satanic Madness`, `Vanish` | CC-/Mechanic-Pipeline teilweise da; nicht jede Mechanic hat nachgewiesenen Effekt. |
| `Lone Howler` HP/Aggro-Range (TODO allg. #5/#6) | Daten-Frage in `_templates.json`/`npc_templates.json`, noch nicht verifiziert. |

---

## 5. NPC-AI: spell_primary + dynamische N-Slots (neu)

Geaendert gegenueber dem alten 4-Slot-Modell:

- **Dynamische Slot-Anzahl** statt fixer 4 Slots. Aktive Slots werden beim Spawn
  als `m_ActiveSpellSlotCount` gezaehlt; leere Trailing-Slots kosten nichts.
- **`spell_primary` (Notfall-/Fallback-Spell)** mit eigenem Cooldown-Gate
  (`m_PrimaryNextReadyAt`), unabhaengig von den Slot-Timern.

`NpcController.SelectSpellSlotToCast` entscheidet in drei Stufen:

1. **Notfall-Primary (hoechste Prioritaet):** Primary gesetzt, Cooldown frei,
   castbar **und** HP `<= k_EmergencyHealthPct` (30 %) ⇒ sofort Primary.
   Rueckgabe `k_PrimarySlotSentinel` (`-2`).
2. **Regulaere Slots:** Iteration ueber `m_SpellSlots` in Template-Reihenfolge;
   erster Slot, der Interval/Cooldown/Chance/`CanCastSpell` passiert.
3. **Fallback-Primary:** Zog kein Slot, wird der Primary erneut geprueft (ohne
   HP-Bedingung) und gewaehlt, falls bereit.

Konstanten/Felder: `k_EmergencyHealthPct = 30f`, `k_PrimarySlotSentinel = -2`,
`m_PrimarySpellId`, `m_PrimaryNextReadyAt`. Reset (Despawn/Death/Evade) nullt
Slot-Timer **und** `m_PrimaryNextReadyAt` via `ResetSpellRuntimeTimers`.
Unbekannte Primary-IDs werden beim Spawn verworfen (Warnung, `m_PrimarySpellId = 0`).

JSON (`npc_templates.json`):

```jsonc
{
  "entry": 70001,
  "name": "Goblin Shaman",
  "spell_primary": 20030,   // Notfall-Heilung bei <=30% HP, sonst Fallback
  "spell1": 20015, "chance1": 50, "interval1": 3000, "cooldown1": 8000,
  "spell2": 20007, "chance2": 35, "interval2": 4500, "cooldown2": 0
  // weitere Slots optional, dynamisch
}
```

Rueckwaertskompatibel: `spell_primary` fehlt/`0` ⇒ reines Slot-Verhalten wie zuvor.

Details: [19-current-state-und-cleanup.md](19-current-state-und-cleanup.md) Abschnitt 2.

---

## 6. Referenzstatus unter `referenzen/`

| Dokument | Status | Hinweis |
|---|---|---|
| `21-audit-2026-06-02-spells-combat.md` | Aktuell | Dieses Dokument, kanonischer Audit-Stand. |
| `19-current-state-und-cleanup.md` | Aktuell | NPC-Casting-Abschnitt auf Primary/N-Slots aktualisiert. |
| `04-...`, `05-hit-feedback`, `06-...`, `07-...`, `08-...`, `09-...`, `15-...`, `16-...` | Ueberwiegend aktuell | Architektur-/Implementierungsreferenz. |

> Hinweis: Die frueheren Spell-/NPC-/Roadmap-Docs (`05-roadmap`, `10`, `11`,
> `12`, `13`, `14`, `17`, `18`, `20`, `STATUS_AND_ROADMAP`) wurden am 2026-06-02
> als veraltet/abgeloest geloescht. Ihr verbindlicher Inhalt lebt in diesem
> Dokument und in Doc 19 weiter.

---

## 7. Kurzfazit

Gegenueber Doc 20 ist die Liste echter Luecken deutlich geschrumpft. Offen
bleiben im Kern vier Punkte: **SummonObject/Illusion Gate**, **Focused Evasion
(Proc-Aura)**, **Charge Swing-Feel + Follow-up** und die **fehlende Spell-ID
318**. Alles andere ist entweder behoben (Abschnitt 2) oder reiner
Playmode-Testbedarf (Abschnitt 4). Neu hinzugekommen ist die NPC-AI mit
Notfall-Primary und dynamischen N-Slots.
