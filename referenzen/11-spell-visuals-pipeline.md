# 11 – Spell-Visuals-Pipeline (Pose · Partikel · Frame-Anim · Sound)

Beschreibt die drei (geplant: vier) parallelen Visual-Subsysteme, die beim Cast eines Spells gefeuert werden. Stand: aktueller Code inkl. aller Änderungen aus den Polish-Iterationen.

> Trigger-Einbettung in die Spell-Cast-Kette: siehe [`10-spell-pipeline.md`](10-spell-pipeline.md) §5.

---

## 1. Zwei Trigger-Zeitpunkte

| Zeitpunkt | RPC | Subsysteme |
|---|---|---|
| **Cast-START** (sofort beim ServerRpc-Ack) | `PlayerCombat.BeginCastClientRpc(entry, castSeconds)` | Caster-Pose + Caster-Partikel |
| **Cast-RESOLVE** (am Cast-Ende oder sofort bei Instant) | `PlayerCombat.PlaySpellCastClientRpc(entry, sourceNetId, targetNetId)` | Frame-Animation (Travel/Impact/Aura) |
| (geplant) **Cast-START / RESOLVE** | gleich wie oben | Sound — siehe §5 |

Wichtig: **Pose und Partikel laufen während der gesamten Cast-Zeit**, die Frame-Animation feuert erst bei Resolve. Instant-Casts senden `castSeconds=0` durch `BeginCastClientRpc` durch, damit Pose+Partikel auch dort kurz aufblitzen.

---

## 2. Subsystem A — Caster-Pose

Quelle: `spell_visual_kit.unit_cast_animation` (Index pro Spell). Source-Index→State-Mapping ist nicht recoverable, daher gilt:

> **Jeder Nicht-Null-Index ⇒ generische "cast"-Pose** auf dem `FlareCharacter`.

Implementierung: `PlayerCombat.TryTriggerCasterPose(spellEntry)` (inline in `PlayerCombat.cs`).

Pose feuert auch dann, wenn das Visual-Kit kein `spranim` enthält (z. B. Spell 133, Kit 154 → nur psystem + sound), damit der Cast trotzdem als Cast lesbar ist.

---

## 3. Subsystem B — Caster-Partikel (`.psi`)

Quelle: `spell_visual_kit.psystem` → Name → `StreamingAssets/particles/_particles.json`.

Spawn-Layer: [`CasterParticleSpawner.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/Runtime/CasterParticleSpawner.cs).

### 3.1 Format

`.psi` ist ein 128-Byte-Binär-Header, im Projekt als JSON konvertiert. Atlas-Layout fix: **4 Spalten × 8 Zeilen**, Tile-Index per `tile_x`/`tile_y` in 32-Px-Units. Beispiel `casting_fire`:

```json
{ "emission": 73, "life_min": 0.595, "life_max": 2.42,
  "size_start": 0.4, "size_end": 1.087,
  "gravity_min": -28.57, "gravity_max": -28.57,
  "radial_accel_min": -114.28, "radial_accel_max": -114.28,
  "speed_min": 42.85, "spread": 6.28,
  "add_blend": true, "tile_x": 96, "tile_y": 0 }
```

### 3.2 Spawn-Flow

```
1. GO erzeugen, am Caster-Transform parenten
2. GO.SetActive(false)                          ◄── verhindert "Setting duration while playing"
3. ParticleSystem + Renderer als Components
4. ConfigureMain (duration, life, speed, size, color)
5. ConfigureEmission (rate-over-time aus def.Emission)
6. ConfigureShape (Cone, spread)
7. ConfigureForce (gravity → forceOverLifetime, World)
8. ConfigureTextureSheet (Grid 4×8, rowMode=Custom,
                          rowIndex=tile_y/32, startFrame=tile_x/32)
9. ConfigureRenderer (additive/alpha Material, Billboard)
10. GO.SetActive(true); ps.Play();
```

**Lifetime-Cap:** `MaxOpenEndedSeconds = 8f`. Source-Lifetime ≤ 0 = endlos → wird auf 8 s gecappt.

### 3.3 Konstanten ([`CasterParticleSpawner.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/Runtime/CasterParticleSpawner.cs))

| Konstante | Wert | Bedeutung |
|---|---|---|
| `AtlasTilePixels` | `32` | Source-Koordinaten-Space. Nutze nur für `tile_x / AtlasTilePixels = col` etc. |
| `AtlasColumns` × `AtlasRows` | `4` × `8` | Atlas-Grid. |
| `ProceduralTileTexPixels` | `128` | Render-Auflösung **pro Tile** für den prozeduralen Fallback-Atlas. |
| `PixelsPerUnit` | `32f` | Source-Px → Unity-Welt-Units. |
| `SizeUnits` | `AtlasTilePixels / PixelsPerUnit = 1f` | Natürliche Partikel-Größe in Unity-Units. |
| `VisualScale` | **`0.15f`** | Zusätzlicher visueller Down-Scale. Begründung in §3.5. |
| `MaxOpenEndedSeconds` | `8f` | Cap für endlose Source-Systeme. |

### 3.4 Prozeduraler Fallback-Atlas

Source-Asset `particles.png` ist im Steam-Main-Dump **nicht enthalten** (nur Code-Referenz `sContentMgr->getTexture("particles.png")`). Daher generiert `BuildProceduralAtlas()` einen synthetischen Atlas (4×8 weiche Discs, weiß) der **alle** Tile-Indizes abdeckt:

- Tile-Größe: `ProceduralTileTexPixels = 128` px
- `mipChain=true`, `FilterMode.Trilinear`, `WrapMode.Clamp`, `anisoLevel=1`
- Effektiver Radius: `tile * 0.42f` (kleiner als Tile-Mitte → Tile-Ecken garantiert alpha=0, kein quadratisches Glow-Boxing beim additiven Stacking)
- Falloff: **Smoothstep** `t² · (3 − 2t)` mit `t = 1 − d/radius` (C¹-stetige weiche Disc ohne harte Kern-Hotspots)
- `Apply(updateMipmaps:true, makeNoLongerReadable:true)`
- Cached in `s_FallbackAtlas` (statisch).

### 3.5 Iterationsprotokoll (gemacht in dieser Session)

| Iter | Symptom | Fix |
|---|---|---|
| 1 | "Setting duration while playing"-Warnings | Deactivated-GO-Pattern (Schritt 2 + 10 oben) |
| 1 | Bloom zu groß | `VisualScale = 0.5f` |
| 2 | weiterhin zu groß + verpixelt | `VisualScale 0.5 → 0.25`; Atlas-Tile **32 → 128** px; kubischer Falloff `a³`; Mipmaps + Trilinear |
| 3 | noch zu groß + "eckig" | `VisualScale 0.25 → 0.15`; Radius `tile/2 - 1` **→ `tile * 0.42`**; Smoothstep statt Kubik |

Begründung "eckig": Mit `radius ≈ tile/2` reichte der Falloff bis fast an die Tile-Grenze. Additives Blending stapelt selbst schwache Alphas zu einem sichtbar **quadratischen** Halo. Mit `radius = 0.42 · tile` bleiben ~16 % Rand komplett schwarz → die Disc bleibt rund, egal wie viele Layer additiv übereinander liegen.

### 3.6 Was noch fehlt

- ❌ **Radial / Tangential Acceleration** — geparst (`def.RadialAccelMin/Max`, `def.TangentialAccelMin/Max`), aber im `ConfigureForce` nicht verdrahtet. Source-Semantik: pro Tick auf `Velocity` aufmultipliziert, `/PixelsPerUnit`. Lösung: Custom-Force-Module mit `ParticleSystem.CustomData` oder ein zweites `ParticleSystemForceField`. Auswirkung bisher: Partikel fallen nur unter Gravity, drift in Casting-Effekten fehlt.
- ❌ **Echtes `particles.png`** — falls je gefunden, würde der prozedurale Atlas durch das Original ersetzt. Code-Pfad: `s_FallbackAtlas` durch geladene `Texture2D` aus `Art/spells/particles.png` ersetzen (ServiceLocator-Loader hinzufügen).

---

## 4. Subsystem C — Frame-Animation (`.sa` → `WorldSpellAnimation`)

Quelle: `spell_visual_kit.spranim` → `.sa`-Name → `StreamingAssets/spells/animations/<name>.json`.

Beteiligte Typen:

| Datei | Rolle |
|---|---|
| [`SpellVisualKitDefinition.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/SpellVisualKitDefinition.cs) | DTO für `spell_visual` (Kit-Definition). Felder: `Spranim`/`Spranim2`, `SpranimX`/`Y` (`Y` kann String wie `"-heightp"` sein), `Sprcolor` (`long`), `SpranimBlend` (`-1`=Alpha), `Psystem`, `Sound`, `GroundGlowColor`, `UnitGlowColor`. |
| [`SpellVisualKitMapping.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/SpellVisualKitMapping.cs) | DTO für `spell_visual_kit` (Spell-Entry → Kit-IDs). |
| [`SpellVisualResolver.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/SpellVisualResolver.cs) | `Resolve(spellEntry, mappings, defs) → SpellVisualDefinition`. |
| [`SpellAnimationCatalog.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/SpellAnimationCatalog.cs) | `name → SpellAnimationDefinition` (Frames, FPS, Loop). |
| [`SpellVisualSpawner.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/Runtime/SpellVisualSpawner.cs) | Spawnt `WorldSpellAnimation`-GO am Caster oder Target. |
| [`WorldSpellAnimation.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/Runtime/WorldSpellAnimation.cs) | MB, spielt Sprite-Frames ab. Phasen: `Caster` (Channel/Wind-up), `Travel`, `Impact`, `Aura`. |
| [`SpellSpriteCache.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/Runtime/SpellSpriteCache.cs) | Lazy `Texture2D → Sprite[]`-Cache aus `Art/spells/*.png`. |

Trigger: `PlayerCombat.PlaySpellCastClientRpc` → `SpellVisualSpawner.Spawn(kit, anims, sourceTransform, targetTransform)`.

**Verdrahtet.** Spielt das vorgerenderte Hauptef­fekt-Visual (cast_001, fire_001, magic_007 etc.).

---

## 5. Subsystem D — Sound *(geplant, noch nicht verdrahtet)*

Quelle: `SpellVisualKitDefinition.Sound` (Name → `Art/sounds/<name>.ogg`).

Plan:

- Pure Service `SoundCatalogLoader` (analog Spell-/Particle-Loader), Cache: `name → AudioClip`.
- Spawn in `BeginCastClientRpc` **nach** Pose+Partikel: `AudioSource.PlayClipAtPoint` für 3D-Punkt-Sound oder pooled `AudioSource` am Caster-Transform (für Caster-Bound Loops).
- Cast-Resolve-Sound (Impact) zusätzlich in `PlaySpellCastClientRpc`, falls Kit `Sound2` o. ä. hat.
- `Sound`-Flag `DontStopCastingSound` (`SpellAttributes`) ehren.

→ Aufgenommen als Phase **2.3** in [`12-naechste-phasen-melee-spell-shoot-aura.md`](12-naechste-phasen-melee-spell-shoot-aura.md).

---

## 6. Schnellzugriff auf die Änderungen aus dieser Session

Alle Edits in [`CasterParticleSpawner.cs`](../Assets/Scripts/Runtime/Gameplay/Combat/Spells/Visuals/Runtime/CasterParticleSpawner.cs):

```csharp
public const int   AtlasTilePixels          = 32;     // Koordinaten-Space
public const int   AtlasColumns             = 4;
public const int   AtlasRows                = 8;
public const int   ProceduralTileTexPixels  = 128;    // (neu)
public const float MaxOpenEndedSeconds      = 8f;
public const float PixelsPerUnit            = 32f;

private const float SizeUnits   = AtlasTilePixels / PixelsPerUnit; // = 1f
private const float VisualScale = 0.15f;                            // 0.5 → 0.25 → 0.15

// BuildProceduralAtlas():
int   tile   = ProceduralTileTexPixels;
float radius = tile * 0.42f;                            // war: tile * 0.5 - 1
// Falloff:
float t = Mathf.Clamp01(1f - d / radius);
float a = t * t * (3f - 2f * t);                        // Smoothstep (war: a*a*a)
// Texture:
new Texture2D(w, h, TextureFormat.RGBA32, mipChain: true)
{ filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 1 };
tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
```

Inline-Cast-Pose-Trigger: `PlayerCombat.TryTriggerCasterPose`/`TryTriggerCasterParticles` (in [`PlayerCombat.cs`](../Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs)) — werden aus `BeginCastClientRpc` aufgerufen, vor dem CastBar-Event.
