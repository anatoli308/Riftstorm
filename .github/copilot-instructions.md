# Copilot Instructions for Riftstorm

## Background Information
Riftstorm is a **Multiplayer Dark-Fantasy PvPvE Topdown Survivor-MOBA/MMO**. The game blends Vampire-Survivors/Megabonk-style horde combat and build evolution with LoL/WoW-style teamfights, objectives and pacing, plus ARPG-style synergy depth. Target session length is 15–25 minutes, ~15 players per match (scalable), 200–400 concurrent enemies, dedicated server and server-authoritative simulation. This file contains the coding standards, architectural principles, and design patterns for the project to ensure consistency, maintainability, and clarity across the codebase. The instructions cover decision-making principles, coding rules, clean coding standards, project architecture, scene architecture, application lifecycle, and patterns & conventions. Always use the latest stable version of Unity 6 and modern C# features where appropriate.

## Decision-Making Principles
- For background tasks or long decision tasks use Python and not PowerShell. PowerShell is only for short scripts and quick fixes, not for complex logic or data processing.
- Always prefer clear, maintainable code over clever one-liners. Readability is more important than brevity.

## Important Developer Coding Rules
- Prefer explicit types when they improve readability.
- Use `var` only when the type is obvious from the right-hand side.
- Always include XML documentation comments (`/// <summary>...</summary>`) for all classes, methods, and public members to ensure clarity of purpose and usage.
- Always use `new(TypeName)` syntax for object instantiation instead of `new TypeName()`. This improves performance by reducing IL code size.
- Always follow the established project architecture and design patterns as outlined below.
- No Polling, No Coroutines: Vermeide Update-Methoden mit Polling-Logik; keine Coroutines für asynchrone Abläufe; stattdessen Events, Callbacks oder Async/Await verwenden.
- No Timer, No Flag Checks: Vermeide Timer- oder Flag-Checks für Ablaufsteuerung; nutze stattdessen State Machines, Event-Driven Logic oder Callback-Mechanismen.
- **JSON over ScriptableObject**: Konfigurations- und Daten-Assets gehören als JSON unter `Assets/StreamingAssets/` (z. B. `interface/`, `combat/`, `npc/`). Keine neuen ScriptableObjects für Daten anlegen — sie umgehen das Loader/Cache/Service-Pattern. Geladen wird synchron per `Newtonsoft.Json.JsonConvert.DeserializeObject<T>` + `File.ReadAllText(Application.streamingAssetsPath + …)` mit Lazy-Static-Cache und Fallback auf Defaults. Referenz-Implementierung: `HudConfigLoader` / `UIFontConfigLoader`.
- **No Resources Folder**: Der `Resources/`-Ordner ist verboten. Alle laufzeit-konfigurierbaren Daten leben unter `Assets/StreamingAssets/`. Unity-Assets, die zwingend Projekt-Assets bleiben müssen (Fonts, Prefabs, Materials, AnimatorController), werden per `[SerializeField]` auf einem MonoBehaviour-Manager (typisch `ApplicationEntryPoint`) referenziert oder via Addressables (`PrefabManager`) geladen — niemals via `Resources.Load`.
- **Input System Only (NEW Input System)**: Verwende AUSSCHLIESSLICH das neue Unity Input System (`UnityEngine.InputSystem`) mit `InputAction` / `InputActionAsset` / `InputActionMap`. Das Legacy `UnityEngine.Input` API (`Input.GetKey*`, `Input.GetMouseButton*`, `Input.GetAxis`, `Input.mousePosition` etc.) ist verboten — auch in Editor-/Dev-Tools. Bindings als `InputAction` (Code-erzeugt: `new InputAction(binding: "<Keyboard>/f1")` + `.Enable()` + `.performed += …`) oder über das Projekt-Asset `Assets/InputSystem_Actions.inputactions` (per `[SerializeField] InputActionAsset` + `FindActionMap`/`FindAction`). Lifecycle: `Enable()` in `OnEnable`, `Disable()` + Callback-Deregistrierung in `OnDisable`/`OnDestroy`. Referenz: `Game/Input/PlayerInputController.cs`.

## Clean Coding Standards
- **KISS (Keep It Simple, Stupid)**: Bevorzuge einfache, klare Lösungen gegenüber komplexen; vermeide Over-Engineering; jede Klasse/Methode sollte eine klare, verständliche Aufgabe haben.
- **DRY (Don't Repeat Yourself)**: Keine Code-Duplikation; extrahiere wiederholte Logik in gemeinsame Methoden/Klassen; nutze Vererbung/Composition sinnvoll.
- **YAGNI (You Aren't Gonna Need It)**: Implementiere nur Features, die aktuell benötigt werden; keine spekulativen Erweiterungen; halte Code fokussiert auf aktuelle Requirements.
- **Single Responsibility Principle (SRP)**: Jede Klasse hat genau eine Verantwortung; Manager orchestrieren, Loader laden Daten, Applier wenden Assets an; keine Mixed Concerns.
- **Separation of Concerns**: Klare Trennung zwischen Datenlogik (Loader), Asset-Anwendung (Applier), Orchestrierung (Manager), UI (View/Controller); siehe Service Decomposition Pattern.
- **Clean Architecture**: Abhängigkeiten zeigen immer nach innen; Pure Services haben keine MonoBehaviour-Dependencies; Applier bekommen nur Daten, keine Loader-Referenzen; Manager orchestrieren, delegieren nicht ihre Verantwortung.
- **Explicit over Implicit**: Keine magischen Strings/Numbers; explizite Typen statt `var`; klare Methodennamen; Konstanten für wiederholte Werte.
- **Fail Fast**: Validierung früh durchführen; klare Error-Messages; Guard Clauses am Anfang von Methoden.
- **Composition over Inheritance**: Bevorzuge Komposition (Service Decomposition) statt tiefe Vererbungshierarchien.
- **Immutability where possible**: Readonly Fields/Properties wo sinnvoll; private Setter für interne State-Änderungen; keine unerwarteten Side Effects.

## Project Architecture
- **Core Movement**: Server-authoritative Topdown-Bewegung mit Client Prediction & Reconciliation; manuelle Physik bevorzugt für deterministisches Verhalten.
- **MVC Architecture (Runtime/Core)**:
  - **BaseApplication**: Root-Klasse für Scene-Scripts; verwaltet EventManager und findet Model/View/Controller-Instanzen per DFS; generisch typisierbar `BaseApplication<M,V,C>`.
  - **Model**: Basisklasse für Datenhaltung; `Model<T>` ermöglicht typsichere App-Referenzen.
  - **View**: Basisklasse für UI-Darstellung (MonoBehaviour); `View<T>` mit `LoadVisualElement()` für UIToolkit-Integration, `Show()`/`Hide()` für Activation.
  - **Controller**: Basisklasse für MVC-Bridge; `Controller<T>` handheld Event-Listeners auf App-EventManager; `AddListener<E>()` / `RemoveListener<E>()` / `RemoveListeners()`.
  - **Element**: Gemeinsame Basis für alle MVC-Klassen; lazy-loaded App-Reference, `Find<T>()` für Component-Suche, `Broadcast(evt)` für Event-Versand.
  - **EventManager**: Typ-sichere Event-Broadcasting; zentrales Kommunikationssystem für MVC-Komponenten.
- **State Machine Architecture (Runtime/Core)**:
  - **StateMachine<TState, TSelf>**: Generische Base-Klasse für Manager; CRTP-Pattern ermöglicht States Rückreferenz zum Manager; `ChangeState()` ruft Exit/Enter auf; `EventManager` für State-Events.
  - **State<TManager>**: Abstrakte Base-Klasse für konkrete States; implementiert `Enter()` und `Exit()`; Manager wird per Property gesetzt.
- **Pure Services (keine MonoBehaviours, via ServiceLocator)**:
  - **PrefabManager**: Cache-first Addressables, delegiert an `PrefabRegistry`, nur über ServiceLocator/DI.
  - **PrefabRegistry**: Interner Cache + Addressables, released Handles on `ClearCache()`, keine Custom-Prefab-Loader.
  - **TextureManager**: Baut Materialien aus Skin-Definitionen, nutzt `TextureRegistry`, zugreifbar via ServiceLocator, `ClearCache()` delegiert.
  - **TextureRegistry**: Cache + Custom Loader (Default Lazy Loader), kein Observer-Pattern, `ClearCache()` zerstört Texturen/Materialien.
- **MonoBehaviour Manager / State Machines**:
  - **ConnectionManager**: StateMachine für NGO; leitet NetworkManager-Callbacks (OnConnectionEvent, OnServerStarted, ApprovalCheck, OnTransportFailure, OnServerStopped) an den aktuellen State weiter; Abos in `Awake`, Deregistrierung in `OnDestroy`.
  - **AuthenticationManager**: StateMachine (Unauthenticated/Authenticating/Authenticated/SessionExpired); Input via `IAuthenticationHandler` je State; Output-Events zentral im Manager (`OnAuthenticationSuccess()`, `OnAuthenticationFailure()`, `OnSessionExpired()` via `EventManager`); verwaltet Authentifizierungsverlauf und Token-Refresh.
  - **ConsoleManager**: StateMachine (ConsoleInactive/ConsoleActive); leitet Kommandos und Aktivierungszustand an den aktuellen State; verwaltet Konsolen-UI und Befehlsausführung.
  - **ApplicationEntryPoint**: Registriert Pure Services im `ServiceLocator` (z. B. TextureManager/PrefabManager) in `Awake`; hält serialisierte MonoBehaviour-Manager (Connection/Authentication/Console); ruft `ServiceLocator.ClearAll()` in `OnDestroy` auf.
- **Sonstiges**: `PrefabDataFactory` bleibt als Helper für PrefabManager.

## Performance First Philosophy

- Gameplay systems must scale to ~15 players and a few hundred enemies (not thousands).
- Avoid hidden allocations in gameplay-critical code paths.
- Avoid LINQ in hot paths.
- Avoid reflection in runtime gameplay systems.
- Prefer struct-based data for high-frequency systems.
- Profile before optimizing, but design systems with scalability in mind.
- Gameplay code must be written with cache locality in mind.
- Prevent per-frame heap allocations in gameplay loops.

## Networking Best Practices

- Server is always authoritative.
- Never trust client-side damage, cooldowns, or hit validation.
- Clients only send input intentions.
- Minimize RPC usage in gameplay-critical systems.
- Avoid network synchronization for purely visual effects.
- Prefer event replication over full object synchronization where possible.
- Use delta compression and snapshot interpolation.
- All gameplay systems must tolerate packet loss and latency.
- Design gameplay systems with deterministic behavior where possible.

## Tick Architecture

- Gameplay simulation must run on a fixed server tick rate.
- Rendering and gameplay simulation must be separated.
- Gameplay logic must not depend on frame rate.
- Avoid Time.deltaTime for authoritative gameplay simulation.
- Prefer fixed-step simulation for networking consistency.

## Visual Readability

- Gameplay readability has priority over visual fidelity.
- Effects must remain readable during large-scale combat.
- Avoid excessive particle spam.
- Enemy silhouettes must remain identifiable during chaotic fights.
- Important gameplay effects require clear telegraphs.
- Use color coding consistently for factions, damage types, and danger zones.
- Avoid screen clutter from overlapping effects.

## Gameplay Scalability

- Gameplay systems must be data-driven whenever possible.
- Avoid hardcoded character, item, or ability logic.
- Skills, buffs, enemies, and items should be configurable through data assets.
- New gameplay content should not require core architecture changes.
- Prefer composition-based ability systems over inheritance-heavy hierarchies.

## Ability System

- Abilities must be modular and composable.
- Avoid monolithic skill implementations.
- Prefer reusable effect components:
  - Damage
  - Knockback
  - DOT
  - Slow
  - Freeze
  - Chain
  - Heal
  - Shield
- Skills should combine effects through composition.
- Support server-side simulation and prediction.

## Class Responsibility Limits

- Avoid God Classes.
- Classes exceeding a single responsibility must be decomposed.
- Managers orchestrate systems but do not implement low-level logic directly.
- Complex systems should be decomposed into focused services.

## Memory & Garbage Collection

- Avoid runtime allocations in gameplay-critical loops.
- Use object pooling for:
  - projectiles
  - enemies
  - effects
  - floating texts
- Avoid frequent string allocations.
- Reuse collections where possible.
- Profile GC spikes regularly.

## Logging

- Avoid excessive runtime logging in gameplay systems.
- Debug logs must not execute in release builds.
- Use structured logging for networking and backend systems.
- Errors must contain actionable information.

## Multiplayer Security

- Clients must never be trusted for gameplay-critical decisions.
- Validate all client requests server-side.
- Prevent speed hacks through server-side movement validation.
- Validate cooldowns and resource usage server-side.
- Disconnect malformed or malicious clients safely.

## Content Pipeline

- Gameplay content should be hot-swappable where possible.
- Designers should be able to create abilities and enemies without code changes.
- Prefer .json data assets for gameplay configuration.
- Avoid hardcoded balance values.

## High-Frequency Gameplay Performance

- Use Object Pooling for projectiles, enemies, VFX, and floating texts.
- Use GPU Instancing for many identical renderers.
- Keep server simulation on a fixed tick (20–30 Hz), separate from render FPS.
- Avoid per-frame allocations in gameplay hot paths.
- Avoid LINQ, reflection, and boxing in hot paths.
- Sync server events / seeds for visual effects, not every detail.
- Never sync each projectile / particle as its own `NetworkObject`.

## Scene Architecture
- **Metagame Scene**: Nutzt MVC-Pattern mit generischem `MetagameApplication<MetagameModel, MetagameView, MetagameController>`. Ist die Hub-Scene für Spieler vor dem Joinen eines Games (Login, Skin-Auswahl, etc.).
- **Game Scene**: Nutzt MVC-Pattern mit generischem `GameApplication<GameModel, GameView, GameController>`. Ist die Main-Gameplay-Scene mit Netcode-Integration, Server/Client-Character-Synchronisation, und networked game state.

## Application Lifecycle & Initialization
- **ServiceLocator**: Typ-sicherer Service-Container für Pure Services; `Register<T>(T service)` registriert, `Get<T>()` ruft ab; `ClearAll()` räumt auf und ruft `ClearCache()` bei Managern auf; initialisiert in `ApplicationEntryPoint.Awake()`.
- **ApplicationEntryPoint**: Singleton, DontDestroyOnLoad; registriert Pure Services (`TextureManager`, `PrefabManager`) in `Awake`; hält MonoBehaviour-Manager (Connection/Authentication/PlayerSkin) als serialisierte Felder; initialisiert Network via `InitializeNetworkLogic()` in `[RuntimeInitializeOnLoadMethod]`.
- **Network Initialization**: Server startet Port-Listening, setzt Framerate/VSync, lädt GameScene nach erfolgreicher Initialisierung; Client lädt MetagameScene, verbindet sich optional auto via `AutoConnectOnStartup`; CommandLineArgumentsParser liest `--port` und `--target-framerate`.

## Patterns & Conventions
- **Single Source of Truth**: Manager halten immer aktuelle State-Daten (z. B. `m_CurrentPlayerPrefab`, `m_CurrentSkinName`); Events sind nur Trigger (minimal Payload); Views/Controller fragen Daten beim Manager an statt aus Events zu lesen; keine State-Duplikation in UI; Manager garantiert State-Konsistenz.
- **Klare Trennungen (Separation of Concerns)**:
  - **Manager**: Hält State, orchestriert, sendet Events, ist Single Source of Truth.
  - **State**: Nur Orchestrierung (Enter/Exit), keine Business-Logik, delegiert an Manager.
  - **Interne Services** (Loader, Applier): Spezifische Operationen (Asset-Laden, Material-Anwendung), keine State-Haltung.
  - **Views**: Nur UI-Rendering und User-Input, fragen State beim Manager ab, senden Events für Actions.
  - **Controller**: UI-Bridge; abonniert Manager-Events, leitet zu Views weiter, triggert Manager-Actions.
  - **Pure Services** (PrefabManager, TextureManager): Global verfügbar, Cache-first, Lifecycle-Management.
- **Dependency Injection**: 
  - Pure Services (global verfügbar) immer über `ServiceLocator.Get<T>()` beziehen (z. B. `PrefabManager`, `TextureManager`).
  - Interne Services (nur von einem Manager genutzt): Manager instanziiert Loader direkt; keine ServiceLocator-Nutzung.
  - Daten-Services (Applier): Bekommen keine Loader-Referenzen, nur konkrete Daten als Parameter; Manager lädt Daten, übergibt sie an Applier.
  - Keine Singleton-Zugriffe über `ApplicationEntryPoint.Singleton` für Services.
- **Cache-First**: Immer über `PrefabManager`/`TextureManager`; Registries nicht umgehen; Cache-Flush via `ClearCache()`/`ServiceLocator.ClearAll()`.
- **State Machines**: Input über State-spezifische Interfaces; Events/Output werden vom Manager (nicht vom State) via `EventManager` gesendet; States steuern nur Transitionen.
- **Service Decomposition**: Komplexe Manager können interne Services (keine MonoBehaviours) nutzen für bessere Separation of Concerns; z. B. `PlayerSkinManager` → 4 Loader (SkinDefinition, SurfaceDefinition, CharacterTemplate, LegacyShader) + `PlayerSkinApplier`. Manager orchestriert, Loader laden Daten, Applier wendet Assets an.
- **Addressables Only**: Prefab-Loading ausschließlich Addressables, keine Custom Prefab Loader; Custom Loader nur in `TextureRegistry` erlaubt.
- **Lifecycle**: Externe Callbacks in `Awake` abonnieren und in `OnDestroy` sauber deregistrieren; `ServiceLocator.ClearAll()` beim Teardown.
- **Manual Physics**: Für Kernbewegung explizite Physik-/Kollisionslogik bevorzugen.
- **Logging**: Kurze Warnungen/Errors; keine Observer-Benachrichtigungen in TextureRegistry.

## Dedicated Server & Networking
- **Multiplayer Roles**: Server/Client-Rollen via `Unity.DedicatedServer.MultiplayerRoles`; konfigurierbar über Command-Line-Arguments (`--port`, `--target-framerate`).
- **CommandLineArgumentsParser**: Parst Server-Startparameter (Default Port: 7777, Default TargetFramerate: 30).
- **NetworkedGameState**: Zentrale Synchronisation des Game-State zwischen Server und Clients.


---
paths:
  - "**/*.cs"
  - "**/*.csx"
---
# C# Coding Style

> This file extends [common/coding-style.md](common/coding-style.md) with C#-specific content.

## Standards

- Follow current .NET conventions and enable nullable reference types
- Prefer explicit access modifiers on public and internal APIs
- Keep files aligned with the primary type they define

## Types and Models

- Prefer `record` or `record struct` for immutable value-like models
- Use `class` for entities or types with identity and lifecycle
- Use `interface` for service boundaries and abstractions
- Avoid `dynamic` in application code; prefer generics or explicit models

```csharp
public sealed record UserDto(Guid Id, string Email);

public interface IUserRepository
{
    Task<UserDto?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
```

## Immutability

- Prefer `init` setters, constructor parameters, and immutable collections for shared state
- Do not mutate input models in-place when producing updated state

```csharp
public sealed record UserProfile(string Name, string Email);

public static UserProfile Rename(UserProfile profile, string name) =>
    profile with { Name = name };
```

## Async and Error Handling

- Prefer `async`/`await` over blocking calls like `.Result` or `.Wait()`
- Pass `CancellationToken` through public async APIs
- Throw specific exceptions and log with structured properties

```csharp
public async Task<Order> LoadOrderAsync(
    Guid orderId,
    CancellationToken cancellationToken)
{
    try
    {
        return await repository.FindAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {orderId} was not found.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to load order {OrderId}", orderId);
        throw;
    }
}
```

## Formatting

- Use `dotnet format` for formatting and analyzer fixes
- Keep `using` directives organized and remove unused imports
- Prefer expression-bodied members only when they stay readable


---
paths:
  - "**/*.cs"
  - "**/*.csx"
  - "**/*.csproj"
  - "**/*.sln"
  - "**/Directory.Build.props"
  - "**/Directory.Build.targets"
---
# C# Hooks

> This file extends [common/hooks.md](common/hooks.md) with C#-specific content.

## PostToolUse Hooks

Configure in `~/.claude/settings.json`:

- **dotnet format**: Auto-format edited C# files and apply analyzer fixes
- **dotnet build**: Verify the solution or project still compiles after edits
- **dotnet test --no-build**: Re-run the nearest relevant test project after behavior changes

## Stop Hooks

- Run a final `dotnet build` before ending a session with broad C# changes
- Warn on modified `appsettings*.json` files so secrets do not get committed


---
paths:
  - "**/*.cs"
  - "**/*.csx"
---
# C# Patterns

> This file extends [common/patterns.md](common/patterns.md) with C#-specific content.

## API Response Pattern

```csharp
public sealed record ApiResponse<T>(
    bool Success,
    T? Data = default,
    string? Error = null,
    object? Meta = null);
```

## Repository Pattern

```csharp
public interface IRepository<T>
{
    Task<IReadOnlyList<T>> FindAllAsync(CancellationToken cancellationToken);
    Task<T?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<T> CreateAsync(T entity, CancellationToken cancellationToken);
    Task<T> UpdateAsync(T entity, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
```

## Options Pattern

Use strongly typed options for config instead of reading raw strings throughout the codebase.

```csharp
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";
    public required string BaseUrl { get; init; }
    public required string ApiKeySecretName { get; init; }
}
```

## Dependency Injection

- Depend on interfaces at service boundaries
- Keep constructors focused; if a service needs too many dependencies, split responsibilities
- Register lifetimes intentionally: singleton for stateless/shared services, scoped for request data, transient for lightweight pure workers


---
paths:
  - "**/*.cs"
  - "**/*.csx"
  - "**/*.csproj"
  - "**/appsettings*.json"
---
# C# Security

> This file extends [common/security.md](common/security.md) with C#-specific content.

## Secret Management

- Never hardcode API keys, tokens, or connection strings in source code
- Use environment variables, user secrets for local development, and a secret manager in production
- Keep `appsettings.*.json` free of real credentials

```csharp
// BAD
const string ApiKey = "sk-live-123";

// GOOD
var apiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
```

## SQL Injection Prevention

- Always use parameterized queries with ADO.NET, Dapper, or EF Core
- Never concatenate user input into SQL strings
- Validate sort fields and filter operators before using dynamic query composition

```csharp
const string sql = "SELECT * FROM Orders WHERE CustomerId = @customerId";
await connection.QueryAsync<Order>(sql, new { customerId });
```

## Input Validation

- Validate DTOs at the application boundary
- Use data annotations, FluentValidation, or explicit guard clauses
- Reject invalid model state before running business logic

## Authentication and Authorization

- Prefer framework auth handlers instead of custom token parsing
- Enforce authorization policies at endpoint or handler boundaries
- Never log raw tokens, passwords, or PII

## Error Handling

- Return safe client-facing messages
- Log detailed exceptions with structured context server-side
- Do not expose stack traces, SQL text, or filesystem paths in API responses

## References

See skill: `security-review` for broader application security review checklists.
