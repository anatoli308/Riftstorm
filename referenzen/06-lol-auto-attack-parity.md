- Champion folgt einem **gelockten Ziel dauerhaft**, bis er entweder in
  Range ist (dann Auto-Attack-Loop) oder ein neuer Befehl kommt.
- Auto-Attack läuft als **Loop**: sobald der Cooldown durch ist und das
  Ziel noch in Range + lebt, startet die nächste Schwung-Phase automatisch.
- Windup und Backswing sind **durch einen Move-Befehl jederzeit cancelbar**.
  Der Movement-Input greift dabei sofort; die Anim klingt visuell aus.
- **Spamklick auf dasselbe Ziel resettet den Windup nicht.** Erst ein
  Ziel-Wechsel oder ein Move-Klick bricht den Swing ab.
- Der Char dreht sich **die ganze Zeit zum Ziel**, auch während Windup
  und Recovery — die Sprite-Richtung folgt dem Target, nicht der
  letzten Move-Richtung.

Diese fünf Punkte sind jetzt 1:1 abgebildet.

## Owner-Eingabe-Pipeline

| Klick               | Komponente                                     | Effekt |
|---------------------|------------------------------------------------|--------|
| LMB auf Gegner      | `PlayerTargetingInput.OnAttackPressed`         | `TargetSelection.RequestSelectTargetServerRpc(id)` — **selektiert nur**, greift nicht an. |
| LMB auf Boden       | `PlayerTargetingInput.OnAttackPressed`         | Wenn ein Lock existiert: `RequestSelectTargetServerRpc(NoTarget)` → Lock weg. (LoL-ähnlich: Klick ins Leere clearet die Selektion.) |
| ESC                 | `PlayerTargetingInput` / `MobaCommandController` | Lock clearen + laufenden Move-Intent stoppen. |
| RMB auf Gegner      | `MobaCommandController.OnMoveCommandPressed`   | `m_Intent = FollowTarget`, lockt das Ziel falls nicht schon gelockt. **Cancelt die laufende Attacke nur, wenn sich das Ziel ändert.** |
| RMB auf Boden       | `MobaCommandController.OnMoveCommandPressed`   | `m_Intent = MoveToPoint`, Cancel-Attack feuern, hinlaufen. |
| Folgen + in Range   | `MobaCommandController.Update` → `PlayerCombat.TryRequestAutoAttack` | Idempotent: nur ein RPC pro Cooldown-Fenster (Prediction-Window). |

## Server-State-Flow

`PlayerCombat` ist eine `NetworkStateMachine` mit
`Idle / Attacking / Dead`. Jede Phase:

1. `Idle.OnAttackRequested(weapon)` → `Manager.BeginAttack(weapon)`
   - `FaceCurrentTarget()` (XZ-LookRotation)
   - `PlayAttackClientRpc(anim, cooldown)`
   - `AttackingState.ConfigureFromWeapon(weapon)`
   - `ChangeState(AttackingState)`
2. `AttackingState.RunAttackCycleAsync(token)` (Awaitable, kein Update-Polling):
   - **Phase A — Windup**: `WaitForSecondsAsync(cooldown * HitResolveProgress, token)`.
   - **Resolve**: erneut `FaceCurrentTarget()` direkt vor dem FrontArc-Check,
     dann `ServerResolveMeleeHit(weapon)` (Reach = `weapon.Range + victim.HitRadius`,
     FrontArc-Cosinus, ApplyDamage). Wenn Target ungültig geworden ist →
     `ChangeState(IdleState)`.
   - **Phase B — Backswing**: `WaitForSecondsAsync(remaining, token)`.
   - Ende: `ChangeState(IdleState)`.
3. Cancel (`RequestCancelAttackServerRpc`): wenn aktueller State = Attacking,
   `ChangeState(IdleState)` — das `Exit()` cancelt + dispatched das CTS,
   Awaitable wirft `OperationCanceledException`, Phase wird sofort beendet,
   `NotifyAttackCanceledClientRpc` setzt die Owner-Prediction zurück.

Eine zweite `OnAttackRequested` während `AttackingState` wird vom State
**ignoriert** (Kommentar im Source: „kein Combo/Queue in dieser Phase"),
deshalb spamt die Owner-Seite gar nicht — siehe nächster Punkt.

## Owner-Prediction (kein RPC-Spam)

`PlayerCombat.TryRequestAutoAttack`:

- Bricht ab, wenn `IsOwnerPredictingAttack` (Window aktiv).
- Bricht ab, wenn kein Target gelockt.
- Setzt `m_OwnerPredictedAttackUntil = Time.unscaledTime + k_OwnerAttackPredictionWindow`
  und feuert `RequestAttackServerRpc()`.

Damit kann `MobaCommandController` jeden Frame `TryRequestAutoAttack()`
aufrufen — solange Server-Cooldown läuft, geht nur 1 RPC raus, und ist
das Prediction-Window kürzer als der Server-Cooldown, wartet die Owner-
Seite synchron auf den nächsten Slot.

## Move-cancels-Attack (LoL "AA cancel")

`MobaCommandController.OnMoveCommandPressed` ruft `m_Combat.RequestCancelAttack()`
**zielabhängig**:

- RMB auf **neues** Ziel → Cancel + neuer Lock + Follow.
- RMB ins **Leere** → Cancel + Move-to-Point.
- RMB auf das **bereits gelockte** Ziel → **kein** Cancel; der laufende
  Swing wird nicht resettet. Das ist der Fix gegen Spam-Klicks, die den
  Windup permanent zurücksetzen.

`RequestCancelAttack` (Owner):
- Setzt `m_OwnerPredictedAttackUntil = 0f` lokal noch im selben Frame
  (Movement-Prediction sofort frei).
- Feuert `RequestCancelAttackServerRpc`.

Server:
- Nur wenn `m_CurrentState == AttackingState`: `ChangeState(IdleState)`.
- `NotifyAttackCanceledClientRpc` resetted die Owner-Prediction nochmal,
  falls Latenz war.

**Wichtig**: Gameplay-seitig ist der Cancel sofort. Die Attack-Sprite-Anim
darf visuell auslaufen (LoL macht das auch — Champion bewegt sich schon,
Animation klingt aus). Falls das jemals stört: `m_CombatVisuals` in
`NotifyAttackCanceledClientRpc` zwingen, auf Idle/Run zu schneiden.

## Continuous Facing während Attack

Zwei Schichten halten den Char auf das Ziel ausgerichtet:

1. **Server-Yaw (Damage-relevant)**: `PlayerCombat.ServerResolveMeleeHit`
   ruft direkt vor dem FrontArc-Check `FaceCurrentTarget()` auf. Damit ist
   die Hit-Validierung gegen den **aktuellen** Stand des Targets robust,
   auch wenn sich das Ziel während des Windups bewegt hat.
2. **Visual-Direction (FLARE-Sprite)**: `PlayerMovement.UpdateVisuals`
   überschreibt während `combatBusy` (Attacking || Dead) die letzte
   Move-Richtung. Wenn ein Target existiert (`TryGetCurrentTarget`),
   wird `m_LastDirection = ComputeFlareDirection(targetDirXZ)` gesetzt
   und an `m_Character.SetDirection(...)` weitergereicht. So zeigt das
   8-Richtungs-Sprite während Windup + Backswing immer in
   Mauszeiger-/Target-Richtung.

`PlayerMovement` hat dafür ein neues SerializeField `m_TargetSelection`
plus `OnNetworkSpawn`-Fallback per `GetComponent`. Die FLARE-Konvention
bleibt unverändert: `(dir.x, -dir.z)` mit invertierter Z, 8 Sektoren
(0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW).

## Reach & FrontArc — Single Source

Sowohl `ServerResolveMeleeHit` als auch `ServerIsTargetStillValid` nutzen
die **identische** Formel:

```
reach = weapon.Range + victimStats.HitRadius
```

`HitRadius` lebt ausschließlich auf `UnitStats` (Single Source of Truth);
`HitboxIndicator` ist rein visueller Debug-Layer, `SelectionRadius` ist
unabhängiger Radius nur für Klick-/Hover-Selektion. Damit kann der Code
nicht in einen Zustand laufen, in dem "fast in Range" hin und her
flippt — Validierung und Resolve sehen exakt das gleiche Reach-Limit.

## Weapon-Daten (`StreamingAssets/combat/weapons.json`)

Felder, die das Feeling steuern:

| Feld                 | Wirkung |
|----------------------|---------|
| `AttackCooldown`     | Gesamtdauer eines AA-Zyklus (Windup + Backswing). Min-clamp `0.05`. |
| `HitResolveProgress` | Fraktion `[0..1]` von `AttackCooldown` bis Damage landet. LoL-Melee fühlt sich meist um `0.30–0.45` herum richtig an. |
| `Range`              | Waffenreichweite. Wirkliche Reach = `Range + victim.HitRadius`. |
| `FrontArcDeg`        | Voller Öffnungswinkel des Treffer-Cones (Cosinus-Vergleich gegen Forward). |
| `AttackAnim`         | Anim-State-Name, von `PlayAttackClientRpc` an `m_CombatVisuals` weitergereicht. |

Beim Tuning gilt: **erst `HitResolveProgress` justieren**, wenn der Damage
sich „zu früh" oder „zu spät" anfühlt, **dann erst `AttackCooldown`**,
wenn das Tempo des gesamten Loops nicht passt.