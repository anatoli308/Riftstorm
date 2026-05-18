# 12 – Nächste Phasen: Melee · Spell · Shoot · Aura

Konkreter, priorisierter Phasenplan, um die vier Combat-Pipelines client-sichtbar fertigzustellen. IST-Stand ist evidenzbasiert in [`10-spell-pipeline.md`](10-spell-pipeline.md) und [`11-spell-visuals-pipeline.md`](11-spell-visuals-pipeline.md) dokumentiert.

> **Ehrliche Antwort auf "Spell-Setup zu 100 % fertig?":** Nein. Funktional fertig sind **Instant + Cast-Time × Self + Single-Target × Direct-Damage / Heal / Aura**. AoE, Skillshot, Ground-Target, Channel, Sound, ~38 von 47 `SpellEffect`-Handlern und alle Bewegungs-Effekte (Charge / KnockBack / PullTo / Teleport) fehlen.

---

## 1. Status-Matrix (Spell-Typen × Pipeline-Stufen)

Legende: ✅ verdrahtet · 🟡 teilweise · ❌ fehlt · n/a nicht relevant

| Spell-Typ | Validation | Cast-Pose | Partikel (Cast) | Frame-Anim (Resolve) | Sound | Effect-Apply | Multi-Target | Travel-Zeit |
|---|---|---|---|---|---|---|---|---|
| Instant Self-Cast | ✅ | ✅ | ✅ | ✅ | ❌ | 🟡¹ | n/a | n/a |
| Cast-Time Self-Cast | ✅ | ✅ | ✅ | ✅ | ❌ | 🟡¹ | n/a | n/a |
| Instant Single-Target | ✅ | ✅ | ✅ | ✅ | ❌ | 🟡¹ | n/a | n/a (Hitscan) |
| Cast-Time Single-Target | ✅ | ✅ | ✅ | ✅ | ❌ | 🟡¹ | n/a | n/a (Hitscan) |
| AoE (Source/Dest-Radius) | ✅ | ✅ | ✅ | ✅ | ❌ | 🟡¹ | ❌² | n/a |
| Ground-Target (Reticle) | ❌³ | ✅ | ✅ | ✅ | ❌ | 🟡¹ | ❌² | n/a |
| Skillshot / Projectile | ✅ | ✅ | ✅ | 🟡⁴ | ❌ | 🟡¹ | ❌² | ❌ |
| Channeled (Tick-Spell) | 🟡⁵ | ✅ | ✅ | ✅ | ❌ | 🟡¹ | ❌² | n/a |

Fußnoten:
1. Effect-Apply ist verdrahtet **nur** für die 9 implementierten Handler in `SpellExecutor.ApplyEffect` (SchoolDamage, WeaponDamage, Heal, HealPct, RestoreMana, RestoreManaPct, ApplyAura, ApplyAreaAura, TriggerSpell). Alle anderen `SpellEffect`-Werte fallen in den Default-Branch und werden stillschweigend übersprungen.
2. `ResolveTarget` fällt für alle `UnitArea*` auf das Primärziel zurück — keine `OverlapSphere`-Iteration, `Radius` wird ignoriert.
3. Welt-Position aus dem Mausklick wird nicht durchgereicht (`TryRequestCastSpell` kennt nur `targetNetId`).
4. Frame-Anim spielt am Cast-Resolve-Punkt auf `targetTransform` — sieht für den Spieler wie Hitscan aus.
5. Cast-Time-Spells warten, dann **ein** Resolve. Periodisches Ticken (Channel-Apply pro Tick) ist nicht implementiert.

---

## 2. Empfohlene Phasen-Reihenfolge

### Phase 2.3 — Sound am Cast *(klein, schließt 2.x ab)*

**Ziel:** Caster + Impact hören sich an wie sie aussehen.

**Aufwand:** ~1 Session.

**Schritte:**
1. `SoundCatalogLoader` (Pure Service, `ServiceLocator`), Newtonsoft-Loader auf `StreamingAssets/sounds/_sounds.json` (Mapping `name → relativePath`), Lazy-Cache, Fallback-Default.
2. `AudioCache` für `name → AudioClip` (`Resources.Load`-Verbot ehren → `UnityWebRequestMultimedia` oder `Art/sounds/`-Refs auf `ApplicationEntryPoint` per `[SerializeField] AudioClip[]` mit Namens-Index — falls Pure-StreamingAssets-Pfad zu sperrig, dann via Addressables).
3. Trigger-Punkte:
   - In `BeginCastClientRpc` nach `TryTriggerCasterParticles`: Caster-Sound (`kit.Sound`).
   - In `PlaySpellCastClientRpc` nach Spawner: Impact-Sound (falls Kit-Feld vorhanden — sonst `Sound` recyceln).
4. 3D vs. 2D: Pooled `AudioSource` am Caster-Transform; bei Distanz > X auto-attenuation via `AudioSource.spatialBlend = 1`.
5. `SpellAttributes.DontStopCastingSound` ehren (Loop-Sounds nicht hart abbrechen bei Cast-Cancel).

---

### Phase 2.4 — AoE Multi-Target *(größter Gameplay-Hebel)*

**Ziel:** Nova/Konsekration/Ground-Pulse treffen tatsächlich alle Einheiten im Radius.

**Aufwand:** ~1–2 Sessions.

**Schritte in `SpellExecutor`:**
1. Neue Signatur: `static void ApplyEffectToTargets(ICombatUnit caster, SpellTemplate spell, SpellTemplateEffect eff, Vector3 epicenter, ICombatUnit primary, …)`.
2. `ResolveTargets` (Plural, Liste) statt `ResolveTarget`:
   - `UnitCaster` / `None` → `[caster]`
   - `UnitTarget*` (single) → `[primary]`
   - `UnitAreaSrc*` → `OverlapSphereNonAlloc(caster.position, eff.Radius/PPU, layerMask)` + Faction-Filter (Friendly/Hostile aus enum-Suffix).
   - `UnitAreaDst*` → gleicher Filter, aber Center = `primary.position`.
   - `UnitAreaDstFromDst` → Center = `primary.position`, Filter relativ zu `primary.factionId`.
3. `SpellTemplate.MaxTargets` ehren (sortiere Distanz aufsteigend, capen).
4. `eff.Radius` ist Source-Pixel → durch `SpellUtils.PixelsPerUnit` (=32) teilen für Unity-Welt-Meter.
5. Loop: `for (int i = 0; i < targets.Count; i++) ApplyEffectSingle(...);` — vorhandener Switch-Branch bleibt unverändert.
6. Pre-allocated `Collider[] s_OverlapBuffer = new Collider[64];` zur GC-Vermeidung (Project-Rule "no per-frame allocations").

**Test-Spells:** `nova_001`, `consecrate_001` aus `_templates.json` mit `radius>0` und `target_type = UnitAreaSrcHostile`.

---

### Phase 2.5 — Skillshot / Projectile

**Ziel:** `spell.Speed > 0` spawnt ein reisendes `NetworkObject`, das beim Impact die Effect-Pipeline auslöst.

**Aufwand:** ~2–3 Sessions.

**Schritte:**
1. Neuer Prefab `SpellProjectile.prefab` mit `NetworkObject` + `SpellProjectile : NetworkBehaviour`.
2. Server-Spawn-Pfad in `SpellExecutor.Execute`:
   ```csharp
   if (spell.Speed > 0f) {
       SpellProjectileSpawner.Spawn(caster, spell, primaryTarget);
       // CD/Mana wurden schon konsumiert. Effect-Loop nicht hier — passiert beim Impact.
       return new(CastResult.Success);
   }
   ```
3. `SpellProjectile`:
   - Server-Tick: bewegt sich linear Richtung `targetPos` mit `spell.Speed / PixelsPerUnit` m/s (oder Richtung Target-Network-Object für homing).
   - `Physics.OverlapSphereNonAlloc(transform.position, hitRadius)` pro FixedUpdate (Project-Rule "manuelle Physik bevorzugt"); first valid hit ⇒ `SpellExecutor.ApplyEffectsOnly(caster, spell, hit)` + `Despawn`.
   - Max-Range (`spell.Range / PPU`) → Despawn ohne Effect.
4. Client-Visual: `SpellProjectile` triggert in `OnNetworkSpawn` `SpellVisualSpawner.SpawnTravel(kit, sourceTransform, projectileTransform)` für Travel-Anim, in `OnNetworkDespawn` (mit Replication-Reason "Hit") Impact-Anim.
5. `SpellExecutor` refactoren: extrahiere `ApplyEffectsOnly(caster, spell, target)` (ohne Validation, ohne CD/Mana — wird vom Projektil aufgerufen).

**Test-Spell:** Spell 133 (Fireball) — `Speed > 0`, `Range = 30 yd`.

---

### Phase 2.6 — Ground-Target (Reticle)

**Ziel:** Spells mit `SpellAttributes.TargetsGround` casten auf Welt-Position statt auf Unit.

**Aufwand:** ~1 Session.

**Schritte:**
1. `PlayerSpellInput`: wenn `spell.Attributes & TargetsGround != 0`, statt sofortigem `TryRequestCastSpell` einen **GroundCastReticle-Mode** aktivieren (separater Input-State im `PlayerCombatInput`).
2. Reticle-MB unter dem Cursor (Quad mit Circle-Material, Radius aus `spell.Effect1.Radius / PPU`).
3. Beim nächsten LMB / Bestätigungs-Hotkey: Welt-Raycast auf Ground-Layer → `worldPos`.
4. `PlayerCombat.TryRequestCastSpellGround(int entry, Vector3 worldPos)` → `RequestCastSpellGroundServerRpc(entry, worldPos)`.
5. Serverseitig wird `worldPos` als Pseudo-Target genutzt: AoE-Resolve aus Phase 2.4 mit `epicenter = worldPos`.

**Test-Spell:** Blizzard / Ground-Pulse.

---

### Phase 2.7 — Channeled Spells

**Ziel:** Spells mit `channel_interrupt_flags` / `aura_interrupt_flags` ticken über die Dauer.

**Aufwand:** ~1–2 Sessions.

**Schritte:**
1. Neuer State `PlayerCombatChannelingState : PlayerCombatState` (Variante von `CastingState`).
2. Tick-Loop:
   ```csharp
   for (int tick = 0; tick < tickCount; tick++) {
       await Awaitable.WaitForSecondsAsync(tickInterval, token);
       SpellExecutor.ApplyEffectsOnly(caster, spell, primary);  // pro Tick
       PlayChannelTickClientRpc(...);                            // optional
   }
   ```
3. Bewegungs-Interrupt: `aura_interrupt_flags & InterruptOnMove`. Wenn flag nicht gesetzt, Movement bricht Channel **nicht** ab.
4. Effect-Slot-Daten als Tick-Werte interpretieren statt als Einmal-Werte (Konvention: bei Channel ist `data1` = Damage pro Tick).

---

### Phase 3 — Fehlende SpellEffect-Handler

**Reihenfolge nach Häufigkeit in DB:**

1. **Bewegung:** `KnockBack`, `PullTo`, `Charge`, `Teleport`, `Slide`, `SlideFrom`
   - Alle gehen über `PlayerMovement.ServerApplyImpulse(direction, magnitude)` oder `ServerSetPosition`.
   - Korrekturpunkt: `m_Movement.SubmitImpulse(...)` Server-Pfad ergänzen, Client-Reconcile prüfen.
2. **Threat / Crowd-Control:** `Threat`, `InterruptCast`, `Dispel`, `ManaDrain`, `HealthDrain`, `ManaBurn`
   - Threat erfordert NPC-AI-Threat-Table (existiert noch nicht — eigene Phase 4).
   - `InterruptCast` → `PlayerCombat.ServerInterruptCast()` (gibt es schon, nur aufrufen).
   - `Dispel` → `target.Auras.DispelByType(eff.Data1, eff.Data2)` — `AuraManager`-Method existiert? **prüfen, ggf. ergänzen.**
3. **Summons / Skript:** `SummonNpc`, `SummonObject`, `Resurrect`, `ScriptEffect` — eigene Phase, weil sie Spawn-Infrastructure brauchen.
4. **Items / Crafting:** `LootEffect`, `LearnSpell`, `ApplyOrbEnchant`, `CombineItem`, `ExtractOrb`, … — gameplay-extern, niedrigste Prio.

---

### Phase 4 — Melee-Hit-Resolution

Aus [`05-roadmap-mmo-port.md`](05-roadmap-mmo-port.md): Phase 4 ist als "nächster Schritt" markiert, ist aber durch den Spell-Visuals-Detour aufgeschoben worden.

**Ziel:** Auto-Attack landet tatsächlich Damage, nicht nur Anim+CD.

**Schritte:**
1. `WeaponDefinition` hat bereits `BaseDamage`, `AttackRange`, `HitResolveProgress`.
2. In `PlayerCombatAttackingState.Enter`: zweites Awaitable auf `AttackCooldown * HitResolveProgress` (zwischen 0..1) → an dem Punkt:
   - Hitscan-Cone `Physics.OverlapBoxNonAlloc` Richtung Forward, mit `AttackRange`.
   - First valid Unit hit → `target.TakeDamage(weapon.BaseDamage, caster)`.
3. Ranged-Auto-Attack: gleicher Pfad, aber spawnt `WeaponProjectile` (analog Phase 2.5).

---

### Phase 5 — Aura-Pipeline End-to-End-Test

Auras werden bereits **appliziert** (`AuraManager.ApplyAuraFromSpell`), aber kein Spell-Test deckt aktuell ab:

- Periodic-Tick (`AuraType.PeriodicDamage`, `PeriodicHeal`).
- Aura-Expiry / Refresh (gleicher Spell zweimal applizieren).
- Stack-Counter (`spell.StackAmount`).
- Dispel (Phase 3).
- Stat-Modifier-Auras (`AuraType.ModStat`, `ModHealth`, `ModMana`).

**Aufgabe:** Definierte Smoke-Spells in `_templates.json` (z. B. "1-Sek-Tick-PeriodicDamage über 5 s") + In-Game-Eval. Wenn etwas fehlt → in `AuraManager` ergänzen.

---

## 3. Vorgeschlagene Bearbeitungsreihenfolge

| Sprint | Phasen | Begründung |
|---|---|---|
| **Sprint A** | 2.3 Sound + 2.4 AoE | Sound schließt die Cast-Sinneswahrnehmung. AoE ist der größte spürbare Gameplay-Hebel und unblockiert "echte" Class-Fantasies. |
| **Sprint B** | 2.5 Projectile + 2.6 Ground-Target | Beide brauchen die neue `ApplyEffectsOnly`-Refactor-API aus Sprint A.4. Ground-Target hebelt visuell auf 2.4 auf. |
| **Sprint C** | 2.7 Channel + Phase 3 (Bewegungs-Effekte) | Erlaubt Boss-Mechaniken (Charge / Knockback) und Channeled-Heals. |
| **Sprint D** | Phase 4 (Melee-Hit) + Phase 5 (Aura-Smoke) | Schließt Auto-Attack-Schleife endgültig, validiert Aura-Pipeline end-to-end. |

---

## 4. Nicht-Ziele (bewusst aus dem Plan raus)

- **Damage-Formeln / Stat-Skalierung** — `data1` wird flach als Damage genommen. Crit / Spellpower / Versatility kommt erst nach Phase 5, weil es eine eigene `CombatFormulas`-Klasse braucht (Source-Vorbild: `source_server/Server/src/Combat/CombatFormulas.cpp`).
- **Item-/Loot-Effekte** — alles in `ApplyOrbEnchant*`, `CombineItem`, `ExtractOrb`. Inventory-System ist eine eigene Säule, nicht Combat.
- **NPC-Threat / Aggro** — eigene Phase, koppelt an `NpcController.cs`.
- **Pet/Summon-Pipeline** — `SummonNpc` ohne Pet-Spawner ist sinnlos, ist Sprint-D++-Material.

---

## 5. Was als Nächstes konkret tun

1. **Bestätige die Sprint-Reihenfolge** (oder priorisiere um — z. B. wenn Skillshot dringender ist als AoE).
2. Erst nach Bestätigung wird Code für **Phase 2.3 Sound** angefangen.
3. Falls du zuerst **eine andere** Phase willst (z. B. direkt Phase 4 Melee-Hit, weil "shoot/melee fertig" wichtiger ist als Spell-AoE) → sag es, und der Plan wird umsortiert.
