# 07 – UI Font System (StreamingAssets + JSON)

> **Status:** Umgesetzt am 2026-05-17.
> **Pattern-Vorbild:** `HudConfigLoader` (`Assets/Scripts/Runtime/Game/UI/HudConfigLoader.cs`).
> **Architektur-Regel:** JSON in `StreamingAssets/` > ScriptableObjects. Kein `Resources/`.

---

## 1. Warum überhaupt ein eigenes Font-System?

Riftstorm baut seine UI mit **UI Toolkit** (UIDocument + UXML + USS). Jedes
`Label`, jeder `TextField` und jeder `Button` setzt seinen Font über
`element.style.unityFontDefinition`. Bisher wurde dort **gar kein Font
gesetzt** → UI Toolkit fiel überall auf den `LegacyRuntime`-Default zurück
und die UI sah generisch aus. Im `Assets/Fonts/`-Ordner lagen aber bereits
11 TTFs:

| Datei                                | Geplante Rolle                                                         |
| ------------------------------------ | ---------------------------------------------------------------------- |
| `Friz Quadrata Bold.ttf`             | **Title** – Login-Screen-Headline, Hauptüberschriften (ARPG-klassisch) |
| `Friz Quadrata Regular.ttf`          | **Heading** – Spieler-/Target-Namen, Section-Header                    |
| `Fontin-Regular.ttf`                 | **Body** (Default) – Eingabefelder, Fließtext, optional Fantasy        |
| `trebuc.ttf`                         | **Small / Keybind** – Statuszeilen, Tastenkürzel, kleine Labels        |
| `Helvetica 400.ttf`                  | **Numeric / Chat** – HP/Mana/XP-Werte, Chat-Bubbles, Konsole, Nameplates |
| `Palatino Linotype Regular.ttf`      | **Dialog** – Confirm-/Dialog-Boxen, Story-Texte                        |
| `Palatino Linotype Bold.ttf`         | Dialog-Variante (Bold-Akzente)                                         |
| `arial.ttf`                          | **Tooltip** – Item-Namen, Skill-Beschreibungen, kompakte Lesbarkeit    |
| `Ringbearer Medium.ttf`              | Fantasy-Option (z. B. Quest-Titel, Lore-Texte)                         |
| `Cambria Regular.ttf`                | Fantasy-Option (Serif-Fließtext)                                       |
| `Constantia Regular.ttf`             | Fantasy-Option (Serif-Akzent)                                          |

**Zwei Constraints, die das Design treiben:**

1. **Unity kann `.ttf` zur Laufzeit nicht aus `StreamingAssets` als `Font`-Asset
   laden.** Es gibt keine Public-API um aus rohen TTF-Bytes ein `UnityEngine.Font`
   zu bauen. Fonts müssen **Projekt-Assets** bleiben.
2. **Resources/ ist projektweit verboten** (siehe `copilot-instructions.md`,
   _No Resources Folder_-Regel). Alle laufzeit-konfigurierbaren Daten gehören
   nach `StreamingAssets/`.

→ **Lösung:** Hybrid. Die Font-Assets bleiben Projekt-Assets, werden aber per
`[SerializeField] Font[]` auf dem `ApplicationEntryPoint` referenziert
(einmalig per Inspector). Die **Rolle → Font-Name-Zuordnung** lebt als JSON
in `StreamingAssets/interface/ui_fonts.json` und ist damit hot-swappable
ohne Recompile.

---

## 2. Verworfene Alternativen

| Ansatz                              | Warum verworfen                                                                       |
| ----------------------------------- | ------------------------------------------------------------------------------------- |
| `ScriptableObject` für Font-Mapping | Umgeht das Loader/Cache/Service-Pattern; vom Nutzer explizit abgelehnt.               |
| `Resources.Load<Font>(...)`         | `Resources/`-Ordner ist projektweit verboten.                                         |
| TTFs in `StreamingAssets/` ablegen + zur Laufzeit laden | Technisch nicht möglich – keine Unity-API für Runtime-TTF→Font ohne TMP/AssetBundle. |
| USS `-unity-font: url(...)`         | USS kann das JSON nicht konsumieren → kein zentraler Single-Source-of-Truth.          |
| Addressables (`PrefabManager`)      | Overkill für 11 Fonts, die alle gleichzeitig im Memory bleiben sollen.                |

---

## 3. Architektur-Diagramm

```
┌─────────────────────────────────────────────────────────────────────┐
│  Assets/Fonts/*.ttf  (Projekt-Assets, NICHT Resources/)            │
└───────────────────┬─────────────────────────────────────────────────┘
                    │ [SerializeField] Font[] m_UIFonts
                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  ApplicationEntryPoint  (Boot-Scene, DontDestroyOnLoad)             │
│  Awake() → RegisterPureServices()                                   │
│    └─ new FontRegistry(m_UIFonts) → ServiceLocator.Register(...)    │
└───────────────────┬─────────────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  ServiceLocator                                                     │
│    • FontRegistry   (Pure Service, Dictionary<string, Font>)        │
│    • TextureManager                                                 │
│    • PrefabManager                                                  │
│    • WeaponCatalogLoader / OffhandCatalogLoader                     │
└───────────────────┬─────────────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  UIFonts (static)                                                   │
│    UIFonts.Title    →                                               │
│      UIFontConfigLoader.Load().title    ("Friz Quadrata Bold")      │
│      → ServiceLocator.Get<FontRegistry>().Get("Friz Quadrata Bold") │
│      → UnityEngine.Font (oder null → Unity-Default-Fallback)        │
│                                                                     │
│    UIFonts.Apply(visualElement, font)                               │
│      → element.style.unityFontDefinition = new(font)                │
└─────────────────────────────────────────────────────────────────────┘
                    ▲
                    │ lädt 1× (lazy, static cache)
                    │
┌─────────────────────────────────────────────────────────────────────┐
│  StreamingAssets/interface/ui_fonts.json                            │
│  { "title": "Friz Quadrata Bold", "heading": "...", ... }           │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 4. Datei-Inventar (neu angelegt)

### `Assets/Scripts/Runtime/Game/UI/UIFontConfig.cs`
Reines POCO, deserialisiert per Newtonsoft.Json aus `ui_fonts.json`. Felder
sind mit Default-Strings vorbelegt → fehlt das JSON, greifen die Defaults
automatisch.

```csharp
public sealed class UIFontConfig
{
    public string title    = "Friz Quadrata Bold";
    public string heading  = "Friz Quadrata Regular";
    public string body     = "Fontin-Regular";
    public string small    = "trebuc";
    public string keybind  = "trebuc";
    public string numeric  = "Helvetica 400";
    public string dialog   = "Palatino Linotype Regular";
}
```

### `Assets/Scripts/Runtime/Game/UI/UIFontConfigLoader.cs`
Synchrone, lazy-cached Static-Class. **1:1-Mirror von `HudConfigLoader`** —
gleiche Konstanten-Konvention, gleicher Fallback-Pfad, gleiche Logs.

- `DefaultSubFolder = "interface"`
- `DefaultFileName  = "ui_fonts.json"`
- `Load()` → liest `Application.streamingAssetsPath/interface/ui_fonts.json`
  einmal, cached prozessweit, fällt auf `new UIFontConfig()` zurück.
- `ResetCacheForTesting()` für PlayMode-Tests / Reload-Button.

### `Assets/Scripts/Runtime/Game/UI/FontRegistry.cs`
**Pure Service** (kein MonoBehaviour). Konstruktor nimmt eine `IReadOnlyList<Font>`
entgegen, baut eine case-insensitive `Dictionary<string, Font>` über `font.name`.
`Get(string)` liefert `null` bei Miss → Aufrufer fällt auf Unity-Default zurück.

### `Assets/Scripts/Runtime/Game/UI/UIFonts.cs`
Statischer Accessor, der `UIFontConfigLoader` + `FontRegistry` verbindet:

```csharp
public static Font Title    => Resolve(UIFontConfigLoader.Load().title);
public static Font Heading  => Resolve(UIFontConfigLoader.Load().heading);
public static Font Body     => Resolve(UIFontConfigLoader.Load().body);
public static Font Small    => Resolve(UIFontConfigLoader.Load().small);
public static Font Keybind  => Resolve(UIFontConfigLoader.Load().keybind);
public static Font Numeric  => Resolve(UIFontConfigLoader.Load().numeric);
public static Font Dialog   => Resolve(UIFontConfigLoader.Load().dialog);

public static void Apply(VisualElement element, Font font);  // null-safe
```

### `Assets/StreamingAssets/interface/ui_fonts.json`
Default-Mapping. **Hot-swappable** ohne Recompile (Cache nur ein PlayMode-
Run gültig).

```json
{
  "title":   "Friz Quadrata Bold",
  "heading": "Friz Quadrata Regular",
  "body":    "Fontin-Regular",
  "small":   "trebuc",
  "keybind": "trebuc",
  "numeric": "Helvetica 400",
  "dialog":  "Palatino Linotype Regular"
}
```

---

## 5. Datei-Inventar (modifiziert)

### `Assets/Scripts/Runtime/ApplicationLifecycle/ApplicationEntryPoint.cs`
- `using Riftstorm.Game.UI;` hinzugefügt.
- Neuer Inspector-Header `UI Fonts` mit `[SerializeField] Font[] m_UIFonts`
  (Tooltip erklärt: "Alle Font-Assets, die UI/HUD per Rolle nutzen darf").
- In `RegisterPureServices()` nach `TextureManager` registriert:
  ```csharp
  FontRegistry fontRegistry = new(m_UIFonts);
  ServiceLocator.Register(fontRegistry);
  Debug.Log($"[ApplicationEntryPoint] FontRegistry mit {fontRegistry.Count} Font-Asset(s) registriert.");
  ```
  → `ServiceLocator.ClearAll()` in `OnDestroy()` räumt automatisch mit auf.

### `Assets/Scripts/Runtime/Game/UI/HudStyle.cs` (5 Apply-Sites)
| Methode                  | Element       | Font           |
| ------------------------ | ------------- | -------------- |
| `BuildBarRow`            | `valueLabel`  | `Numeric`      |
| `BuildLevelBadge`        | `levelLabel`  | `Numeric`      |
| `BuildTexturedBar`       | `valueLabel`  | `Numeric`      |
| `BuildActionSlot`        | `bind`        | `Keybind`      |
| `BuildTexturedActionSlot`| `bind`        | `Keybind`      |

### `Assets/Scripts/Runtime/Game/UI/PlayerFrameUI.cs`
- `m_NameLabel` → `UIFonts.Heading` (Spielername in der Unit-Frame).

### `Assets/Scripts/Runtime/Game/UI/TargetFrameUI.cs`
- `m_NameLabel` → `UIFonts.Heading` (Target-Name, rechts ausgerichtet).

### `Assets/Scripts/Runtime/Game/UI/ActionBarHUD.cs`
- `xpValue` → `UIFonts.Numeric` (XP-Prozentwert).

### `Assets/Scripts/Runtime/Metagame/MetagameView.cs`
- `using Riftstorm.Game.UI;` hinzugefügt.
- Neue Methode `ApplyFonts(VisualElement root)`, am Ende von `OnEnable()`
  aufgerufen. Bindet per USS-Klasse:
  | USS-Klasse        | Font      |
  | ----------------- | --------- |
  | `.title`          | `Title`   |
  | `.subtitle`       | `Small`   |
  | `connect-button`  | `Heading` |
  | `status-label`    | `Small`   |
  | `.field-label` *  | `Small`   |
  | `TextField` *     | `Body`    |

  USS bleibt unangetastet — Single-Source-of-Truth ist das JSON.

### `.github/copilot-instructions.md`
Zwei neue Regeln im Abschnitt _Important Developer Coding Rules_:

- **JSON over ScriptableObject**: Konfig & Daten gehören als JSON nach
  `Assets/StreamingAssets/`. Keine neuen ScriptableObjects für Daten.
  Geladen wird synchron per `Newtonsoft.Json` + `File.ReadAllText` +
  Lazy-Static-Cache mit Defaults-Fallback. Referenz: `HudConfigLoader` /
  `UIFontConfigLoader`.
- **No Resources Folder**: Der `Resources/`-Ordner ist verboten. Unity-Assets,
  die zwingend Projekt-Assets bleiben müssen (Fonts, Prefabs, Materials),
  werden per `[SerializeField]` auf einem MonoBehaviour-Manager (typisch
  `ApplicationEntryPoint`) referenziert oder via Addressables geladen —
  niemals via `Resources.Load`.

---

## 6. Rollen-Semantik (Endgültig)

| Rolle    | Font (Default)             | Wofür                                              |
| -------- | -------------------------- | -------------------------------------------------- |
| `title`  | Friz Quadrata Bold         | Login-Screen-Headline, Hauptüberschriften          |
| `heading`| Friz Quadrata Regular      | Spielernamen, Target-Namen, Section-Header, Buttons |
| `body`   | Fontin-Regular             | Eingabefelder, Fließtext, lange Beschreibungen     |
| `small`  | trebuc                     | Status-Labels, Subtitle, kleine UI-Labels          |
| `keybind`| trebuc                     | Tastenkürzel auf Action-Slots (Q/W/E/R/1-9)        |
| `numeric`| Helvetica 400              | HP/Mana/XP-Werte, **Chat-Bubbles, Konsole, Nameplates** |
| `dialog` | Palatino Linotype Regular  | Confirm-Boxen, Dialog-Boxen, Story-Texte           |
| (Tooltip)| _verwendet `small` oder neu_ | Item-Namen, Skill-Beschreibungen (arial geplant)   |
| (Fantasy)| Ringbearer / Cambria / Constantia | Optionale Akzent-Fonts für Quests/Lore       |

> **Hinweis:** `arial` (Tooltip) und die Fantasy-Optionen (Ringbearer/Cambria/
> Constantia) sind aktuell im `UIFontConfig` noch nicht als eigene Rollen
> exponiert. Wenn Tooltips eingebaut werden, einfach `UIFontConfig` um ein
> Feld `public string tooltip = "arial";` erweitern + analog im JSON + im
> `UIFonts`-Accessor — fertig.

---

## 7. Wie das Pattern auf weitere Daten anwendbar ist

Dieses Pattern (`HudConfig` / `UIFontConfig`) ist **die kanonische Form**
für jegliche neue UI/Gameplay-Konfiguration in Riftstorm. Schema:

1. **POCO** mit Default-Werten → `Foo.cs`
2. **Loader** static class mit `Load()` + lazy cache → `FooLoader.cs`
   - `DefaultSubFolder = "<bereich>"` (z. B. `interface`, `combat`, `npc`)
   - `DefaultFileName  = "foo.json"`
3. **JSON-Datei** unter `Assets/StreamingAssets/<bereich>/foo.json`
4. **Optional: Pure Service** wenn Asset-Lookup nötig (wie `FontRegistry`
   für `Font[]` oder `TextureManager` für `Texture2D`).
5. **Optional: Static Accessor** wie `UIFonts`, der Loader + Service zu einer
   typsicheren API zusammenfasst.

**Niemals:**
- `ScriptableObject` für reine Daten
- `Resources/`-Ordner
- Custom Asset-Loader die nicht über `ServiceLocator` registriert sind

---

## 8. Setup-Schritte im Unity Editor (einmalig)

1. **Boot-Scene öffnen**, das GameObject mit `ApplicationEntryPoint`
   selektieren.
2. Unter dem neuen **`UI Fonts`**-Header das Array `m_UIFonts` aufklappen,
   Größe auf `11` setzen (oder beliebige Teilmenge, je nachdem welche Fonts
   wirklich gebraucht werden).
3. Alle `.ttf`-Assets aus `Assets/Fonts/` per Drag & Drop in die Slots
   ziehen. Reihenfolge egal, der Lookup geht über `Font.name`.
4. Scene speichern.
5. **PlayMode starten** → im Console-Log nach
   `[ApplicationEntryPoint] FontRegistry mit X Font-Asset(s) registriert.`
   suchen. `X` muss > 0 sein, sonst greifen überall die Unity-Defaults.
6. **JSON anpassen falls gewünscht:** `Assets/StreamingAssets/interface/ui_fonts.json`
   editieren. Cache wird beim nächsten PlayMode-Run neu geladen.

---

## 9. Fehlerdiagnose

| Symptom                                              | Ursache & Fix                                                                                                  |
| ---------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| Alle Labels nutzen weiterhin Unity-Default-Font      | `m_UIFonts`-Array auf `ApplicationEntryPoint` ist leer → Fonts im Inspector zuweisen.                          |
| Nur einzelne Rollen fallen auf Default zurück        | Font-Name im JSON matcht kein Asset in `m_UIFonts` (Tippfehler / `Font.name` ≠ Dateiname).                     |
| `[UIFontConfigLoader] Fehler beim Laden …`           | JSON-Syntaxfehler. JSON validieren, Datei muss reines JSON sein (kein BOM, keine Kommentare).                  |
| Build hat den Font nicht                             | Font-Asset wurde nicht referenziert → in keiner Scene/Prefab. Inspector-Slot auf `ApplicationEntryPoint` reicht. |
| `FontRegistry mit 0 Font-Asset(s) registriert`       | Inspector-Slots leer **oder** Singleton-Guard hat das GameObject zerstört (zweite Instanz im Scene-Reload).    |

---

## 10. Konversations-Verlauf (Kurzform)

1. **HUD-Bugfixes vorher** (außerhalb dieses Dokuments).
2. **Font-Recherche**: `Assets/Fonts/` enthält 11 TTFs. Source-Dumps von
   steam-main wurden als Hinweis-Quelle durchsucht → bestätigte ARPG-typische
   Rollen-Aufteilung (Friz Quadrata für Title/Heading, Helvetica für
   Numeric/Chat, etc.).
3. **Erster Architektur-Vorschlag**: ScriptableObject + Resources →
   **vom Nutzer abgelehnt** als nicht-idiomatisch für Riftstorm.
4. **Re-Plan**: JSON in `StreamingAssets/` mirror von `HudConfigLoader`,
   `FontRegistry` als Pure Service, `[SerializeField] Font[]` auf
   `ApplicationEntryPoint`.
5. **Implementierung**: 5 neue Files + 5 modifizierte Files + 1 JSON +
   2 neue Regeln in `copilot-instructions.md`.
6. **Validation**: `get_errors` über alle 10 berührten Files → 0 Fehler.
7. **Diese Doku** (`07-ui-fonts-streamingassets.md`).

---

## 11. Verwandte Referenzen

- [01-architektur-kontext.md](01-architektur-kontext.md) — MVC, ServiceLocator, Pure Services
- `Assets/Scripts/Runtime/Game/UI/HudConfigLoader.cs` — Pattern-Vorbild
- `Assets/Scripts/Runtime/Management/TextureManagement/TextureManager.cs` — analoger Pure Service für Texturen
- `.github/copilot-instructions.md` — verbindliche Projektregeln
