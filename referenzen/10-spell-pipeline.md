# 10 – Spell-Pipeline (Server-autoritativ)

## 1. Datenfluss (1 Cast)

```
Owner-Client                Server                              Alle Clients
────────────                ──────                              ────────────
PlayerSpellInput
  └─ Hotkey (1..n)
     └─ PlayerCombat
         .TryRequestCastSpell(entry)
             │
             └── RequestCastSpellServerRpc(entry, targetNetId)
                                            │
                                            ├─ SpellCatalogLoader.GetTemplateOrNull
                                            ├─ Resolve primaryTarget (NetworkObject)
                                            └─ m_CurrentState.OnCastRequested(...)
                                                    │
                                                    ▼ (Idle akzeptiert)
                                            PlayerCombat.BeginCast
                                                    │
                                  ┌─────────────────┴─────────────────┐
                                  │                                   │
                            CastTime == 0                       CastTime > 0
                                  │                                   │
                  BeginCastClientRpc(entry, 0f) ──────────►   ChangeState(CastingState)
                                  │                                   │
                  ServerCompleteCast(...)                      Awaitable.WaitForSecondsAsync
                                  │                                   │
                                  │                          (Movement → ServerInterruptCast → Exit)
                                  │                                   │
                                  ▼                                   ▼
                          SpellExecutor.Execute(caster, spell, target)
                                  │
                ┌─────────────────┼──────────────────┐
                │                 │                  │
            Validate         Consume Mana/HP    Effect-Loop (1..3)
            (SpellCaster)    Start CD/GCD       (siehe §3)
                                  │
                                  ▼
                          PlaySpellCastClientRpc ──────────►  Spell-Visuals
                          EndCastClientRpc       ──────────►  CastBar zu
```

**Bewegung bricht ab:** `PlayerMovement.SubmitCommandServerRpc` ruft bei nicht-leerem Move-Input während `CastingState` direkt `PlayerCombat.ServerInterruptCast()` auf. Der Awaitable wird über `CancellationTokenSource` aus `Exit()` gecancelt — Ressourcen/Cooldown sind noch nicht abgezogen, also kein Refund nötig.

---

## 2. Beteiligte Typen

| Datei | Rolle |
|---|---|
| [`SpellTemplate.cs`](../Assets/Scripts/Runtime/Game/Spells/SpellTemplate.cs) | DTO 1:1 aus `spell_template` (DB). Cast-Time, Cooldown, Range, Mana-Formel, drei Effect-Slots. |
| [`SpellEnums.cs`](../Assets/Scripts/Runtime/Game/Spells/SpellEnums.cs) | `SpellEffect`, `SpellTargetType`, `AuraType`, `DispelType`, `SpellAttributes [Flags]`, `SpellSchool`. Wertbereich 1:1 aus `Shared/SpellDefines.h`. |
| [`SpellCatalogLoader.cs`](../Assets/Scripts/Runtime/Game/Spells/SpellCatalogLoader.cs) | Pure Service, lädt `StreamingAssets/spells/_templates.json` (Newtonsoft) mit Lazy-Cache. |
| [`SpellCaster.cs`](../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellCaster.cs) | Stateless Validator (Caster-State → Resources → Target → Range). |
| [`SpellExecutor.cs`](../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellExecutor.cs) | Effect-Slot-Loop. Resource-Consume + CD-Start passieren hier, **vor** den Effects. |
| [`SpellUtils.cs`](../Assets/Scripts/Runtime/Game/Spells/Runtime/SpellUtils.cs) | Reine Funktionen: `IsSelfOnly`, `RequiresTarget`, `CanTargetFriendly/Hostile`, `RangeToMeters`, FLARE-Formel-Eval (`CalculateManaCost`). |
| [`AuraManager.cs`](../Assets/Scripts/Runtime/Game/Spells/Runtime/AuraManager.cs) + [`Aura.cs`](../Assets/Scripts/Runtime/Game/Spells/Runtime/Aura.cs) | Aura-Applikation/Tick auf `ICombatUnit`. |
| [`PlayerCombat.cs`](../Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs) | `NetworkStateMachine`. RPC-Endpunkte: `RequestCastSpellServerRpc`, `BeginCastClientRpc`, `EndCastClientRpc`, `PlaySpellCastClientRpc`. |
| [`PlayerCombatCastingState.cs`](../Assets/Scripts/Runtime/Game/Combat/CombatStates/PlayerCombatCastingState.cs) | Awaitable-Cast-Timer; ruft `ServerCompleteCast` am Ende. |
| [`PlayerSpellInput.cs`](../Assets/Scripts/Runtime/Game/Input/PlayerSpellInput.cs) | Hotkey-Mapping → `TryRequestCastSpell`. |

> Pure Services (`SpellCatalogLoader`, `VisualKitMappingLoader`, etc.) sind in `ApplicationEntryPoint` via `ServiceLocator.Register<T>` registriert.

---

## 3. Effect-Slot-Modell

Ein Spell hat bis zu **drei Effect-Slots** (`effect1..3`, jeweils mit `targetType`, `data1..3`, `radius`, `positive`, `scale_formula`). Aktiv = `Effect != SpellEffect.None`.

### 3.1 Implementierte Effekte (`SpellExecutor.ApplyEffect`)

| Effekt | Status | Bemerkung |
|---|---|---|
| `SchoolDamage` | ✅ | `data1` = Flatdamage. **Keine** Damage-Formel/Stat-Skalierung. |
| `WeaponDamage` | ✅ | Behandelt wie `SchoolDamage`. |
| `Heal` | ✅ | `data1` = Flatheal. |
| `HealPct` | ✅ | `data1` = Prozent vom Target-MaxHP. |
| `RestoreMana` | ✅ | `data1` = Flat-Mana. |
| `RestoreManaPct` | ✅ | `data1` = Prozent vom Target-MaxMana. |
| `ApplyAura` / `ApplyAreaAura` | ✅ | delegiert an `target.Auras.ApplyAuraFromSpell(caster, spell, slot)`. |
| `TriggerSpell` | ✅ | `data1` = entry des Folge-Spells; rekursiver `Execute`. |
| **Alle anderen** (`Teleport`, `KnockBack`, `Charge`, `PullTo`, `Slide`, `MeleeAtk`, `RangedAtk`, `Resurrect`, `Dispel`, `SummonNpc`, …) | ❌ | Default-Branch im Switch — wird stillschweigend übersprungen. |

### 3.2 Target-Resolution (`SpellExecutor.ResolveTarget`)

**Aktueller Code — Vereinfachung:**

```csharp
return eff.TargetType switch
{
    SpellTargetType.UnitCaster => caster,
    SpellTargetType.None        => caster,
    _                            => primary,
};
```

Bedeutung in Klartext:

- ✅ **Self-Cast** (`UnitCaster`) — trifft den Caster.
- ✅ **Single-Target** (`UnitHostile`, `UnitFriendly`, `UnitAny`) — trifft das HUD-Target.
- ❌ **AoE** (`UnitAreaSrc*`, `UnitAreaDst*`, `UnitAreaDstFromDst`) — **fällt auf `primary` zurück**. Es gibt **keine** `OverlapSphere`-Iteration. `SpellTemplateEffect.Radius` wird geparst, aber **nicht** ausgewertet.
- ❌ **Ground-Target** (`SpellAttributes.TargetsGround`) — Flag exists, wird nirgendwo gelesen. Kein Reticle, keine Welt-Position als Cast-Parameter.

> Konsequenz: Spells mit AoE-Effect treffen aktuell **nur das angeklickte Primärziel**, egal welchen Radius die DB angibt.

---

## 4. Validierung (`SpellCaster.Validate`)

Reihenfolge (entspricht Source-Server):

1. **Caster-State** — `IsDead`, `IsStunned` (außer `IgnoreStun`), `IsSilenced`.
2. **Resources** — Mana (FLARE-Formel), HP-Kosten (flat + %), Cooldown-Entry, GCD, Cooldown-Kategorie.
3. **Target** — Self-only / RequiresTarget / `CantTargetSelf` / `CanTargetDead` / Faction (Friendly/Hostile-Filter).
4. **Range** — `spell.Range > 0`: XZ-Distanz Caster↔Target in Metern (`SpellUtils.RangeToMeters`, PixelsPerUnit=32).

Resultat ist ein [`CastResult`](../Assets/Scripts/Runtime/Game/Spells/CastResult.cs). Bei Fehlschlag in `ServerCompleteCast` → `EndCastClientRpc(false)` und Log.

---

## 5. Visuals (Trigger-Punkte)

Zwei Trigger-Punkte:

| Trigger | RPC | Was passiert |
|---|---|---|
| **Cast-Start** (auch bei Instant mit `castSeconds=0`) | `BeginCastClientRpc` | Cast-Pose (`TryTriggerCasterPose`) + Caster-Partikel (`TryTriggerCasterParticles`). HUD-CastBar nur wenn `castSeconds>0`. |
| **Cast-Resolve** (am Ende der Cast-Zeit / Instant-Sofort) | `PlaySpellCastClientRpc` | Frame-Animation per `SpellVisualResolver` → `SpellVisualSpawner.Spawn(kit, anims, sourceTransform, targetTransform)`. |

Details: siehe [`11-spell-visuals-pipeline.md`](11-spell-visuals-pipeline.md).

---

## 6. Was funktioniert end-to-end — und was nicht

| Spell-Typ | Status | Was zu tun |
|---|---|---|
| **Instant Self-Cast** (z. B. Buff auf sich selbst, Heal-on-Self) | ✅ funktioniert | – |
| **Cast-Time Self-Cast** (Buff mit Wind-up) | ✅ funktioniert | – |
| **Instant Single-Target** (Direct-Damage Snap, Heal-Other) | ✅ funktioniert | – |
| **Cast-Time Single-Target** (Fireball-Stil mit Wind-up, Hitscan-Resolve) | ✅ funktioniert | – |
| **AoE (Source/Dest)** (Nova, Konsekration, Ground-Pulse) | ❌ trifft nur Primärziel | Phase 2.4: `Physics.OverlapSphereNonAlloc` in `ResolveTarget`, `MaxTargets`/`Radius` ehren, multi-Apply pro Effect-Slot. |
| **Skillshot / Projectile** (Fireball mit Reisezeit, Lineshot) | ❌ kein Projektil-System | Phase 2.5: `Projectile : NetworkBehaviour` mit `spell.Speed`-Travel, On-Hit-Trigger → `SpellExecutor.Execute(caster, spell, hit)`. |
| **Ground-Target** (Reticle, Welt-Klick als Cast-Parameter) | ❌ Flag nicht ausgewertet | Phase 2.6: Reticle-State im `PlayerSpellInput`, Welt-Position über `RequestCastGroundServerRpc(entry, worldPos)` mitschicken. |
| **Channeled** (Tick-Spells über mehrere Sekunden) | ❌ kein Channel-State | Phase 2.7: Variante von `CastingState` mit Tick-Loop, `aura_interrupt_flags` ehren. |
| **Charge / KnockBack / PullTo / Slide / Teleport** | ❌ default-Branch | Phase 3.x: Bewegungs-Effekte über `PlayerMovement.ServerApplyImpulse` o. ä. |
| **Sound** | ❌ nicht verdrahtet | Phase 2.3: `SpellVisualKitDefinition.Sound` → `AudioSource.PlayClipAtPoint` oder pooled AudioSource am Caster. |

---

## 7. Daten-Quellen (StreamingAssets)

| Datei | Inhalt |
|---|---|
| `StreamingAssets/spells/_templates.json` | Alle `spell_template`-Zeilen aus der DB. |
| `StreamingAssets/spells/_visuals.json` | `spell_visual_kit`-Mapping (Spell-Entry → Visual-Kit-IDs). |
| `StreamingAssets/spells/_visual_kits.json` | `spell_visual`-Definitionen (Kit-IDs → Animations-/Particle-/Sound-Namen + Y-Offset). |
| `StreamingAssets/spells/animations/*.json` | `.sa`-Konvertierungen (Frame-Animationen für `WorldSpellAnimation`). |
| `StreamingAssets/particles/_particles.json` | `.psi`-Konvertierungen (Partikel-Definitionen für `CasterParticleSpawner`). |

Pure-Service-Loader (alle Newtonsoft + Lazy-Cache + Default-Fallback):
`SpellCatalogLoader`, `SpellVisualKitMappingCatalogLoader`, `SpellVisualKitDefinitionCatalogLoader`, `SpellAnimationCatalogLoader`, `ParticleSystemCatalogLoader`.
