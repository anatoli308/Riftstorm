# Hit-Feedback, Range-Indicator & Treffer-Geometrie

Stand: Iteration nach Phase-4-MVP. Behandelt drei zusammenhängende Themen, die in
einer Session gemeinsam gefixt wurden:

1. Owner-lokaler **AttackRangeIndicator** (LoL-Style Boden-Ring).
2. Netzwerk-synchronisierte **Hit-Reaction-Animation** für getroffene Opfer.
3. Konsistente **Treffer-Geometrie** (Server-Hit-Check ↔ Indicator-Ring).

Querverweise:
- `04-animationen-combat.md` — die `PlayerCombatVisuals` Methoden (`PlaySwing`,
  `PlayHit`, `PlayDie` …) sind dort beschrieben.
- `Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs` — Server-Hit-Resolve.
- `Assets/Scripts/Runtime/Game/Combat/UnitStats.cs` — Damage-Fanout + HitRadius.
- `Assets/Scripts/Runtime/Game/Combat/AttackRangeIndicator.cs` — Owner-Visual.

---

## 1. AttackRangeIndicator

**Zweck.** Owner-lokaler Boden-Kreis mit Radius = aktuelle Waffenreichweite,
analog zu LoL/Dota Range-Indicators. Wird per Tastendruck (`-` /
`AttackRangeIndicatorPressed` Input-Action) getoggelt.

**Architektur.**
- `MonoBehaviour`, **keine** Netcode-Synchronisation — rein client-seitig.
- Lebt als Child-`GameObject` (`AttackRangeIndicatorLine`) unter dem Spieler-
  Root, damit der `LineRenderer` keine anderen Komponenten am Root
  beeinflusst (z. B. Hover-Highlight, Hitbox-Visualizer).
- `LineRenderer.useWorldSpace = false` → folgt automatisch dem Spieler-
  Transform, ohne Update-Polling.
- Event-driven: Material/Geometrie werden nur bei `Show/Hide` neu geschrieben.
- Beim Toggle-On wird die Range **frisch** aus `PlayerCombat.CurrentWeaponRange`
  gelesen → Waffenwechsel zur Laufzeit aktualisieren den Ring beim nächsten
  Press automatisch.

**Geometrie-Detail (wichtig).** `LineRenderer.widthMultiplier` extrudiert die
Linie symmetrisch um die Sample-Position, d. h. die *sichtbare Außenkante*
liegt bei `sampleRadius + width/2`. Damit der Ring exakt die Server-Range
abbildet, ziehen wir den Sample-Radius um halbe Linienbreite nach innen:

```csharp
float sampleRadius = Mathf.Max(0.01f, radius - m_LineWidth * 0.5f);
// outerEdge = sampleRadius + width/2 == weapon.Range  ✔
```

**Defaults (nach Tuning).**

| Feld              | Wert     | Bemerkung                                  |
|-------------------|----------|--------------------------------------------|
| `m_LineWidth`     | `0.025`  | dünne Linie, kein "Donut"-Look             |
| `m_Segments`      | `64`     | weiche Kurve ohne Polygon-Treppen          |
| `m_GroundOffset`  | `0.03`   | minimal über dem Boden, kein Z-Fighting    |
| `m_Color`         | Cyan a=0.9 | LoL-typisch                              |
| `m_FallbackRadius`| `1.5`    | wenn Waffe noch nicht geladen ist          |

**Wo gehört der Indicator-Radius nicht hin?** Er ist **kein** Hit-Wert. Der
Server hört nicht auf den Indicator. Wenn die Anzeige zur tatsächlichen
Reichweite passen soll, muss die *Server-Hit-Geometrie* (siehe §3) konsistent
gewählt sein — nicht der Indicator.

---

## 2. Netzwerk-Hit-Reaction

**Problem.** `PlayerCombatVisuals.PlayHit()` existierte bereits, wurde aber
nur lokal aufgerufen. Ein getroffener Spieler hat auf den Maschinen anderer
Clients keine Trefferreaktion gespielt.

**Lösung — eventbasiert, kein Polling, keine zusätzliche RPC nötig.**

`UnitStats.ApplyDamage` (Server) ruft schon immer `BroadcastDamageClientRpc`
auf — sowohl bei tatsächlichem Schaden als auch bei Miss/Dodge/Block, damit
Floating-Text und sonstige FX laufen. Der ClientRpc löst auf **jedem** Peer
das öffentliche Event `ClientDamageReceived(int amount, HitResult result)`
aus.

`PlayerCombat` abonniert dieses Event jetzt auf jedem Peer (nicht nur
Server) und triggert die Hit-Anim, sofern wirklich Schaden geflossen ist:

```csharp
// PlayerCombat.OnNetworkSpawn (jeder Peer)
if (m_Stats != null)
{
    m_Stats.ClientDamageReceived += OnClientDamageReceived;
}

// Handler
private void OnClientDamageReceived(int amount, HitResult result)
{
    if (amount <= 0) return;     // Miss/Dodge/Parry/Resist/Immune/Absorb
    if (m_Visuals != null) m_Visuals.PlayHit();
}
```

Deregistrierung in `OnNetworkDespawn` (auch für jeden Peer).

**Reihenfolge bei tödlichem Treffer.** `UnitStats.ApplyDamage` fired die
Sequenz auf dem Server:
1. `BroadcastDamageClientRpc(...)` → auf jedem Client `PlayHit()`.
2. Wenn HP=0: `OnServerDied` → `PlayDeathClientRpc()` → auf jedem Client
   `PlayDie()`.

Auf dem Client kommen die RPCs in derselben Reihenfolge an. `PlayDie` setzt
intern den `Dead`-State und `force=true`, überschreibt also die laufende
Hit-Animation sauber. `PlayHit` selbst guarded gegen `Dead`-State und tut
nichts mehr, sobald der Spieler bereits tot ist.

**Welche HitResults triggern die Anim?**

| HitResult        | `FinalDamage > 0`? | Hit-Anim |
|------------------|--------------------|----------|
| Hit, Crit        | ja                 | ✔        |
| Block, Glancing  | ja (reduziert)     | ✔        |
| Miss, Dodge,     |                    |          |
| Parry, Resist,   | nein               | ✗        |
| Immune, Absorb   |                    |          |

(Für Block könnte später eine eigene `PlayBlockHit`-Anim ergänzt werden;
aktuell teilt sich Block dieselbe Reaktion wie ein offener Treffer.)

---

## 3. Treffer-Geometrie — `weapon.Range` + `victim.HitRadius`

**Symptom.** Trotz präziser Indicator-Außenkante hat sich der Ring "zu breit"
angefühlt: Der Spielerkörper des Gegners ragte sichtbar in den Ring, ohne
dass ein Treffer landete.

**Ursache.** Der Server-Hit-Check verglich `centerDistance(angreifer, ziel)`
gegen `weapon.Range`. Trifft nur, wenn der **Mittelpunkt** des Ziels im Ring
liegt — die Körperhülle reicht dafür nicht.

**Fix.** Hit-Radius des Opfers wird in den Check eingerechnet:

```csharp
// PlayerCombat.ServerResolveMeleeHit
float reach = weapon.Range + victimStats.HitRadius;
if (distSqr > reach * reach) return;
```

Damit gilt jetzt: **sobald der Indicator-Ring die Körperhülle des Ziels
berührt, landet der Schlag** — Indicator und Hit-Geometrie sind konsistent.

**Datenmodell.** `UnitStats` hat ein neues Inspector-Feld:

```csharp
[Header("Hitbox")]
[SerializeField, Min(0f)] private float m_HitRadius = 0.5f;
public float HitRadius => m_HitRadius;
```

Default 0.5 m passt zu einer Standard-Charakter-Capsule. Pro Mob/Spieler-
Prefab im Inspector überschreibbar (z. B. großer Boss = 1.5 m).

**Was nicht angefasst wurde.**
- Der **Front-Arc-Gate** (`weapon.FrontArcDeg`) bleibt: ein Schlag gegen ein
  Ziel hinter dem Spieler verfehlt weiterhin (Default 180° = vorderer Halb-
  raum). Falls das überraschende "in Reichweite und trotzdem Miss" verursacht,
  ist die Lösung entweder den Arc zu erhöhen (`360` = Rundumschlag) oder den
  Indicator als 180°-Tortenstück statt Vollring zu zeichnen.
- Y-Achse wird weiterhin ignoriert (2D-Distanz auf XZ, treu zum
  SoF-Vorbild).

---

## Datei-Map

| Datei                                                                                              | Was geändert wurde                                                                                                |
|----------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------|
| `Assets/Scripts/Runtime/Game/Combat/AttackRangeIndicator.cs`                                       | Neue Komponente; Toggle, Material-Build, `sampleRadius`-Inset.                                                    |
| `Assets/Scripts/Runtime/Game/Combat/PlayerCombat.cs`                                               | `CurrentWeaponRange` getter; Abo `ClientDamageReceived → PlayHit`; Server-Check `weapon.Range + victim.HitRadius`. |
| `Assets/Scripts/Runtime/Game/Combat/UnitStats.cs`                                                  | Neues Feld `m_HitRadius` + public getter `HitRadius`.                                                              |
| `Assets/Scripts/Runtime/Game/Combat/PlayerCombatVisuals.cs`                                        | Unverändert (`PlayHit()` existierte bereits).                                                                      |
| `Assets/Scripts/Runtime/Game/Input/PlayerInputController.cs` (+ `InputSystem_Actions.inputactions`)| Action `AttackRangeIndicator` + Event `AttackRangeIndicatorPressed`.                                              |

---

## Architektur-Prinzipien (eingehalten)

- **Server-autoritativ:** Hit-Resolve nur auf dem Server; Clients senden
  keine Schadensentscheidungen.
- **Eventbasiert, kein Polling, keine Coroutines:** Hit-Anim fließt über das
  vorhandene `ClientDamageReceived`-Event aus dem ClientRpc-Fanout.
- **Owner-Only-UI:** Range-Indicator lebt nur auf der Owner-Maschine, kein
  Netzwerk-Traffic.
- **Single Source of Truth:** Reichweite kommt von `WeaponDefinition.Range`;
  Hitbox-Größe von `UnitStats.HitRadius`. Der Indicator liest die Range live
  ab statt sie zu cachen.
- **MonoBehaviour-Klassen kompakt, sealed, `[DisallowMultipleComponent]`,
  XML-Doku.**
