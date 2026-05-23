# 18 — Ranged-Pipeline, Cast-getriggerte Visuals, HUD-Stats, Dead-Target-Guard

> Konsolidierter Stand nach Round 4–6 des Ranged-Slot-Umbaus.
> Ergänzt: [04-animationen-combat.md](04-animationen-combat.md),
> [10-spell-pipeline.md](10-spell-pipeline.md),
> [15-equipment-system-runtime-swap.md](15-equipment-system-runtime-swap.md),
> [16-equipment-stats.md](16-equipment-stats.md).

## 1. Ziel & Zusammenfassung

Spieler sollen MainHand + OffHand (z. B. Longsword + Buckler) **immer** auf
ihrem FLARE-Charakter sehen. Der Bogen aus dem Ranged-Slot wird **nur während
eines aktiven Shoot-Casts** als zusätzliche FLARE-Schicht eingeblendet und am
Cast-Ende (Erfolg oder Abbruch) wieder entfernt. Das ersetzt die in Round 5
kurzzeitig aktive „Stance"-Logik (MainHand/OffHand wurden ausgeblendet, sobald
ein Ranged-Item equippt war). Begründung: die Stance-Variante hat den Bogen
nicht zuverlässig sichtbar gemacht, wenn der FLARE-Layer `Ranged` auf dem
Character-Prefab nicht registriert war oder die PNG-Pfade fehlten — Resultat
war ein „nackter" Charakter.

Parallel sind drei Korrekturen eingeflossen:

- **HUD**: Die Labels `Spell+ / Heal+` waren irreführend (sie wurden als
  Multiplikator gelesen). Jetzt: `Spell / Heal` ohne Plus.
- **Dead-Target-Guard**: Ranged-Casts (z. B. Aimed Shot) auf tote Ziele werden
  hart in `SpellCaster.Validate` abgelehnt, unabhängig vom
  `SpellAttributes.CanTargetDead`-Flag.
- **JSON-Pfad-Verifikation**: `FlareAtlasLoader` liest tatsächlich aus
  `Assets/StreamingAssets/player_male/<atlasName>.json`. Eine fehlende
  Bow-Anzeige war kein Loader-, sondern ein Stance-Bug.

## 2. Daten-Pipeline: Ranged-Slot → Visuals

Der Server hält den equippten Bogen als `NetworkVariable<FixedString64Bytes>`
auf `PlayerCombat`:

```text
PlayerCombat (NetworkBehaviour, server-write)
├── m_CurrentWeaponId   : NetworkVariable<FixedString64Bytes>  // MainHand-Gear-Id
├── m_CurrentOffhandId  : NetworkVariable<FixedString64Bytes>  // OffHand-Gear-Id
└── m_CurrentRangedId   : NetworkVariable<FixedString64Bytes>  // Ranged-Gear-Id (Bow/Crossbow/Gun)
```

Lese-API und Events (für HUD, Range-Indicator, Visuals):

```csharp
public string CurrentWeaponId   { get; }
public string CurrentOffhandId  { get; }
public string CurrentRangedId   { get; }

public event Action<string,string> WeaponChanged;   // (oldId, newId)
public event Action<string,string> OffhandChanged;
public event Action<string,string> RangedChanged;   // wird nicht mehr fuer Visuals abonniert
```

Auflösung der equippten WeaponDefinition für einen konkreten Spell-Cast:

```csharp
WeaponDefinition ResolveWeaponFor(SpellTemplate spell)
{
    return spell.RequiredEquipment switch
    {
        12L => ResolveRangedWeapon(),  // Bow/Crossbow/Gun aus Ranged-Slot
        _   => ResolveMainHandWeapon() // alles andere aus MainHand (Unarmed-Fallback)
    };
}
```

`ResolveRangedWeapon()` liefert **kein** Unarmed-Fallback — gibt `null`
zurück, wenn der Slot leer ist oder das Item nicht `IsRanged` ist. Damit
schlägt der `SpellCaster.CheckEquipment`-Pfad mit `BaseRangedWeaponDamage<=0`
fehl und liefert `CastResult.NoRangedWeapon`.

`UnitStats.BaseRangedWeaponDamage` liest direkt
`m_PlayerCombat.CurrentRangedWeapon` und macht den Wert für HUD und SpellCaster
gleichermaßen lesbar.

## 3. Cast-getriggerte Visuals — Lifecycle

`PlayerEquipmentVisuals` (`MonoBehaviour` neben `PlayerCombat`) bridge die
NetVars auf FLARE-Layer:

| FLARE-Layer | Datenquelle | Sichtbarkeit |
|-------------|-------------|--------------|
| `MainHand`  | `CurrentWeaponId`  | **immer**, sobald equippt |
| `OffHand`   | `CurrentOffhandId` | **immer**, sobald equippt |
| `Ranged`    | `CurrentRangedId`  | **nur während Shoot-Cast** |

Das Show/Hide passiert auf allen Peers synchron, weil es im
`BeginCastClientRpc` / `EndCastClientRpc` von `PlayerCombat` mit aufgerufen
wird — siehe [10-spell-pipeline.md](10-spell-pipeline.md) für den Pipeline-
Überblick. Die beiden ClientRpcs liegen ohnehin schon im Hot Path für
Pose/Particles/Sound; das Ranged-Visual hängt sich exakt dort ein:

```csharp
// PlayerCombat.cs
[ClientRpc]
private void BeginCastClientRpc(int spellEntry, float castSeconds, ClientRpcParams _ = default)
{
    TryTriggerCasterPose(spellEntry);
    TryTriggerCasterParticles(spellEntry);
    TryTriggerCasterSound(spellEntry);
    TryShowRangedForCast(spellEntry); // <-- NEU
    ...
}

private void TryShowRangedForCast(int spellEntry)
{
    SpellTemplate spell = SpellCatalogLoader.GetTemplateOrNull(spellEntry);
    if (spell == null || spell.RequiredEquipment != 12L) return;
    PlayerEquipmentVisuals visuals = GetComponent<PlayerEquipmentVisuals>();
    if (visuals == null) return;
    visuals.ShowRangedForCast(CurrentRangedId);
}
```

```csharp
[ClientRpc]
private void EndCastClientRpc(bool completed, ClientRpcParams _ = default)
{
    if (m_ActiveCasterParticles != null) { CasterParticleSpawner.Stop(...); ... }
    TryHideRangedAfterCast();   // <-- NEU, idempotent
    ...
}
```

`PlayerEquipmentVisuals` selbst:

```csharp
public void ShowRangedForCast(string rangedId)
    => ApplyAsync(RangedLayerName, rangedId, AppliedSlot.Ranged);

public void HideRangedAfterCast()
    => ApplyAsync(RangedLayerName, string.Empty, AppliedSlot.Ranged);
```

`ApplyAsync` cached pro Slot die zuletzt applizierte `gearId`, vermeidet
redundante Loader-Calls und schützt mit einer Cancellation-Token-Sequenz
gegen Race-Conditions, wenn der Cast vor Ende eines vorangegangenen
`LoadAsync` getriggert wird.

`RangedChanged` wird **bewusst nicht mehr** abonniert: der Bogen ist
kein dauer-sichtbares Equip-Visual, sondern ein Cast-Effekt.

## 4. FlareAtlasLoader — StreamingAssets-Pfad

`FlareAtlasLoader(string subFolder)` baut seinen Wurzelpfad mit
`Path.Combine(Application.streamingAssetsPath, subFolder)`. Für den Player
wird der Loader mit `"player_male"` instanziert; jede `LoadAsync(atlasName)`
liest dann:

```text
Assets/StreamingAssets/player_male/<atlasName>.json
Assets/StreamingAssets/player_male/<png-aus-json>.png
```

Vorhandene Atlanten (verifiziert):
`default_chest.json`, `longsword.json`, `longbow.json`.

Cache-Key ist `atlasName` (nicht der volle Pfad). `ClearCache()` zerstört
Texturen + Sprites, um Domain-Reloads sauber zu halten. Es gibt keine
`Resources.Load`-Nutzung — Konvention bleibt: nur JSON, nur StreamingAssets,
für Unity-Assets serialisierte `MonoBehaviour`-Felder oder Addressables.

## 5. HUD: Spell / Heal — Formeln & Label-Korrektur

`CharacterHUD` rendert im Stats-Panel den additiven Bonus, den der
Spell-Executor flat auf den `effectValue` eines magischen Spells / Heals
addiert:

```csharp
int spellBonus = m_BoundStats.Intelligence / 20;                                  // ganzzahlig
int healBonus  = (m_BoundStats.Willpower / 15) + (m_BoundStats.Intelligence / 30);
sb.Append("Spell ").Append(spellBonus).Append('\n');
sb.Append("Heal  ").Append(healBonus).Append('\n');
```

Konsequenzen:

- Mit `INT = 5` ist `spellBonus = 0` — **das ist korrekt**. Der erste Punkt
  Spell-Bonus erscheint bei `INT >= 20`.
- Mit `WIL = 5, INT = 5` ist `healBonus = 0` — der erste Punkt erscheint bei
  `WIL >= 15` oder `INT >= 30`.
- Scorch mit `effectValue = 4` macht 4 Schaden bei `Spell = 0`, weil der flat
  Wert ohne Multiplikator angewendet wird. Der HUD-Eintrag `Spell 0` heißt
  „kein additiver Bonus", nicht „Spell schlägt für 0".

Das alte Label `Spell+` / `Heal+` suggerierte fälschlich einen
Total-Damage-Modifikator. Das `+` wurde entfernt; der Auren-Block weiter
unten (`Dmg+ / Heal+ / DmgRcv / HealRc` in Prozent) bleibt unverändert,
weil dort der Plus-Charakter semantisch ein echter Modifier ist.

Quelle der Formel: `SpellExecutor` flat-bonus-Pfad und `UnitStats`-Mapping
für INT/WIL — siehe [16-equipment-stats.md](16-equipment-stats.md).

## 6. Dead-Target-Guard für Ranged-Spells

Auch wenn `SpellCaster.CheckTarget` bereits einen allgemeinen
`TargetDead`-Pfad besitzt, kann dieser über das Flag
`SpellAttributes.CanTargetDead` umgangen werden (Use-Cases: Resurrect,
Looting). Für Schusswaffen-Spells ist das nie gewollt — ein Bogenschuss
auf eine Leiche ergibt nicht-loot Mechaniken nicht — daher ein
zusätzlicher harter Guard **vor** `CheckTarget`:

```csharp
// SpellCaster.cs
r = CheckEquipment(caster, spell);
if (r != CastResult.Success) return r;

r = CheckRangedAgainstDeadTarget(spell, target);
if (r != CastResult.Success) return r;

r = CheckTarget(caster, spell, target);
if (r != CastResult.Success) return r;

static CastResult CheckRangedAgainstDeadTarget(SpellTemplate spell, ICombatUnit target)
{
    if (spell.RequiredEquipment != 12L) return CastResult.Success;
    if (target == null) return CastResult.Success;
    if (target.IsDead)  return CastResult.TargetDead;
    return CastResult.Success;
}
```

Damit landen sowohl `RequestCastSpellServerRpc` als auch der
Re-Validate-Pfad in `SpellExecutor.Execute` (siehe
[10-spell-pipeline.md §3](10-spell-pipeline.md)) auf
`CastResult.TargetDead`, bevor irgendein Projektil gespawnt wird.

Wichtig: der Guard greift **nur** bei Spells mit
`RequiredEquipment == 12L`. Andere Cast-Pfade behalten ihre bestehende
Semantik (Resurrect darf weiter Tote anvisieren).

## 7. Was reverted wurde (gegenüber Round 5)

| Bereich | Round-5-Variante | Round-6-Zustand |
|---------|------------------|-----------------|
| MainHand/OffHand bei equipptem Bogen | ausgeblendet (Stance-Override) | **immer sichtbar** |
| Ranged-Layer | gefüllt sobald `CurrentRangedId != ""` | nur während Cast |
| `OnRangedChanged`-Subscription | re-applied alle drei Layer | **entfernt** |
| `ApplyMainOrOffhandAsync` | blank-out bei Ranged aktiv | **direkt** mit `gearId` |

Begründung steht in §1. Operativ wichtig: wer den Round-5-Zustand sucht,
findet ihn nur noch in der Git-Historie — die Methoden
`ApplyMainOrOffhandAsync` und `ApplyRangedAsync` existieren nicht mehr.

## 8. Berührte Dateien (Quick-Index)

- `Assets/Scripts/Runtime/Game/Combat/PlayerEquipmentVisuals.cs` — Layer-Bridge,
  `ShowRangedForCast` / `HideRangedAfterCast`.
- `Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs` — `TryShowRangedForCast`
  / `TryHideRangedAfterCast` in `BeginCastClientRpc` / `EndCastClientRpc`.
- `Assets/Scripts/Runtime/Game/UI/Character/CharacterHUD.cs` — `Spell` / `Heal`
  Label-Strings (Platzhalter L644 + Renderpfad L676-679).
- `Assets/Scripts/Runtime/Game/Spells/Runtime/SpellCaster.cs` —
  `CheckRangedAgainstDeadTarget` + Aufruf vor `CheckTarget`.
- `Assets/StreamingAssets/player_male/longbow.json` — FLARE-Atlas, vom
  `FlareAtlasLoader` für Layer `Ranged` geladen.

## 9. Test-Szenarien (manuell)

1. **Equip Longsword + Buckler + Longbow** → MainHand zeigt Schwert, OffHand
   Schild, Ranged-Schicht leer.
2. **Shot-Cast (z. B. Aimed Shot)** → für Dauer des Casts erscheint Bogen
   on top; Pose wechselt auf Shoot. Sword + Shield bleiben sichtbar.
3. **Cast-Cancel (Bewegung)** → Bogen verschwindet sofort, Sword/Shield
   bleiben unverändert.
4. **Shot auf totes Ziel** → `RequestCastSpell` returnt `TargetDead`, kein
   Projektil, keine Mana-/CD-Kosten, keine Bogen-Einblendung.
5. **HUD mit INT = 5, WIL = 5** → `Spell 0`, `Heal 0`. Mit INT = 20:
   `Spell 1`. Mit WIL = 15: `Heal 1`.
6. **Equip-Wechsel `/weapon longbow` ohne anschließenden Cast** → MainHand
   weiter Schwert, Ranged-Schicht weiter leer (kein Auto-Show).

## 10. Offene Punkte

- Tooltip / Glossar für `Spell` und `Heal` im HUD, damit die Semantik
  „additiver flat Bonus, kein Multiplikator" ohne Quellcode klar ist.
- `RequiredEquipment == 11L` (Shield) ist im Code noch durchgewunken — kein
  OffHand-Stat modelliert. Folge-Phase, nicht Teil dieses Rounds.
- Ranged-AutoAttack (siehe [12-naechste-phasen-melee-spell-shoot-aura.md §3](12-naechste-phasen-melee-spell-shoot-aura.md))
  läuft noch nicht über `BeginCastClientRpc`; wenn dort eingeführt, sollte
  der gleiche Show/Hide-Mechanismus angedockt werden.
