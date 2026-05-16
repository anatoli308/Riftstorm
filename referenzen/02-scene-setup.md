# Scene-Setup — Boot → Metagame → Game (NGO)

> Code-Seite ist fertig (`ApplicationEntryPoint`, `ConnectionManager`, States, MVC-Apps). Dieser
> Guide beschreibt, was du **einmalig im Unity Editor** verdrahten musst, damit das Setup läuft.

---

## 1. Build Settings — Szenen-Reihenfolge

`File → Build Profiles → Scene List`:

| Index | Scene |
|---|---|
| 0 | `Assets/Scenes/Boot.unity` |
| 1 | `Assets/Scenes/Metagame.unity` |
| 2 | `Assets/Scenes/Game.unity` |

> `Boot` **muss** Index 0 sein — sonst wird `ApplicationEntryPoint` nicht initialisiert.

---

## 2. Boot-Scene — NetworkManager

In `Boot.unity`:

1. Leeres GameObject `NetworkManager` erstellen.
2. Komponenten hinzufügen:
   - `NetworkManager` (Unity.Netcode)
   - `Unity Transport` (Unity.Netcode.Transports.UTP)
3. Im `NetworkManager` Inspector:
   - **Network Transport** → das `Unity Transport` Component am selben GameObject zuweisen.
   - **Connection Approval** → **enabled** (wichtig — sonst greift der ApprovalCheck der `ServerListeningState` nicht).
   - **Player Prefab** → bleibt zunächst leer (wir spawnen Player später über GameApplication / Spawnpunkte).
   - **Network Prefabs List** → leere Liste ok, bis erste Netcode-Prefabs existieren.
4. Im `Unity Transport` Inspector:
   - **Protocol Type** → Unity Transport
   - **Address / Port** sind Defaults; werden zur Laufzeit von `ConnectionManager` via `SetConnectionData(...)` überschrieben.

---

## 3. Boot-Scene — ApplicationEntryPoint + ConnectionManager

In `Boot.unity`:

1. GameObject `ApplicationEntryPoint` erstellen.
2. Komponenten hinzufügen:
   - `ApplicationEntryPoint` (`Tolik.Riftstorm.Runtime.ApplicationLifecycle`)
   - `ConnectionManager` (`Tolik.Riftstorm.Runtime.ConnectionManagement`)
3. Inspector-Verdrahtung:
   - `ConnectionManager.Network Manager` → das `NetworkManager` GameObject aus Schritt 2.
   - `ConnectionManager.Max Players` → 15 (oder dein Limit).
   - `ApplicationEntryPoint.Connection Manager` → die `ConnectionManager`-Component am selben GameObject.

> Beide Components rufen `DontDestroyOnLoad` in `Awake`, bleiben also über Scene-Wechsel persistent.

---

## 4. Metagame-Scene

In `Metagame.unity`:

1. GameObject `MetagameApplication` mit Komponenten:
   - `MetagameApplication`
   - `MetagameModel`
   - `MetagameView`
   - `MetagameController`

   (Alle vier können erstmal auf demselben GameObject liegen — `BaseApplication.Find<T>` löst sie über `GetComponentInChildren` auf.)

2. Sobald du eine Connect-UI baust: deren Button-OnClick ruft
   `MetagameController.RequestConnect()` auf. Der Controller liest `ServerAddress` / `ServerPort` /
   `PlayerName` aus `MetagameModel` und delegiert an `ApplicationEntryPoint.Singleton.ConnectionManager.StartClient(...)`.

---

## 5. Game-Scene

In `Game.unity`:

1. GameObject `GameApplication` mit Komponenten:
   - `GameApplication`
   - `GameModel`
   - `GameView`
   - `GameController`

2. Bestehende `GamePlayerBootstrap`, Kamera-Rig usw. bleiben unverändert. Sobald
   Server-authoritative Spawns hinzukommen, werden NetworkBehaviours über NGO auf dem Server
   gespawnt und via `NetworkObject` an Clients gespiegelt.

---

## 6. Server vs Client Build

`File → Build Profiles`:

- **Windows Server** Profil → hat `UNITY_SERVER` Define + setzt die Multiplayer-Role auf
  `Server`. Damit nimmt `ApplicationEntryPoint.InitializeNetworkLogic()` den Server-Zweig.
- **Windows / Mac Client** Profil → Multiplayer-Role `Client`, lädt `Metagame`.

> Server-Start akzeptiert CLI-Args:
> ```
> Riftstorm.exe -batchmode -nographics --port 7777 --target-framerate 30 --listen-address 0.0.0.0
> ```

---

## 7. Editor-Workflow — MPPM (Virtual Players)

Über `Window → Multiplayer → Multiplayer Play Mode`:

- **Player 1** als Server (Role: Server)
- **Player 2 / 3 / ...** als Client (Role: Client)
- Beim Play-Press startet Player 1 in Boot → Server-Pfad → lädt Game. Player 2+ starten in Boot
  → Client-Pfad → laden Metagame. Connect-Button löst NGO-Connect aus, NGO-SceneSync zieht den
  Client in Game.

---

## 8. Smoke-Test

1. `Boot` als Startscene öffnen, Play drücken (Editor ohne MPPM = Role meist Client).
2. Log sollte zeigen: `[ApplicationEntryPoint] Client mode — loading Metagame scene.`
3. Mit zweitem MPPM-Player als Server starten: Log
   `[ApplicationEntryPoint] Server starting on 0.0.0.0:7777 @ 30 Hz.` →
   `[ConnectionManager] Changed state from OfflineState to StartingServerState.` →
   `[ApplicationEntryPoint] Server up. Loading Game scene via NGO SceneManager.`
4. Client triggert Connect → States gehen `Offline → ClientConnecting → ClientConnected` →
   Client landet automatisch in `Game`.

Wenn einer dieser Schritte hängt: in `ConnectionState`-Logs schauen, dann gegen die NGO Docs
(`NetworkManager.StartClient`, `ApprovalCheck`, Scene Management) gegenchecken.
