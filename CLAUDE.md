# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This repository is a **reusable Unity Gaming Services (UGS) integration package** intended to be imported into any hyper-casual or mobile game. This document is the **authoritative source of truth** for all service-related development. Every implementation decision not explicitly covered here must follow the spirit of the rules below — do not invent parallel patterns.

---

## 1. Technology Stack

| Concern | Technology | Notes |
|---|---|---|
| Engine | Unity 6 LTS (6000.3.x) | Editor at `C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe` |
| Language | C# (latest Unity-supported) | Nullable reference types enabled per-file via `#nullable enable` |
| Backend | Unity Gaming Services | Authentication 3.6.1, Economy 3.5.3, Cloud Save 3.4.0, Remote Config 4.2.5 |
| IAP | Unity Purchasing **4.13.2** | Pinned to the 4.x line deliberately — 5.x replaced the `IDetailedStoreListener`/`CrossPlatformValidator` API this package is built on |
| Async | UniTask 2.5.11 (`com.cysharp.unitask`) | **No** `System.Threading.Tasks.Task` in public APIs, **no** coroutines |
| DI | VContainer 1.18.0 (`jp.hadashikick.vcontainer`) | Constructor injection only |
| Platform sign-in | Google Play Games v2 (`com.google.play.games` 2.1.0, Android) + apple-signin-unity (`com.lupidan.apple-signin-unity` 1.5.0, iOS) | Behind asmdef version defines `NEX_GPGS` / `NEX_APPLE_SIGNIN`; anonymous sign-in works without them |
| Content/config delivery | Addressables 2.11.1 (`com.unity.addressables`) | Settings & catalog assets only (see §10) |
| Serialization | Newtonsoft.Json (`com.unity.nuget.newtonsoft-json`) | Cloud Save payloads; never `BinaryFormatter` |
| Input | New Input System only (`activeInputHandler: 1`) | Legacy `UnityEngine.Input` throws at runtime |

Third-party packages are referenced in `Packages/manifest.json`. UniTask, VContainer, GPGS, EDM4U and apple-signin-unity come from the **OpenUPM scoped registry** (`https://package.openupm.com`). Never vendor third-party source into the package.

---

## 2. Repository & Package Layout

The deliverable is an **embedded package** developed inside this Unity project. All production code lives under `Packages/`, not `Assets/`. `Assets/` is reserved for the demo/dev scene that exercises the package.

```
Packages/com.nexenovastudios.services/
├── package.json                  # name, version (semver), dependencies
├── Runtime/
│   ├── Core/                     # Nexenova.Services.Core.asmdef
│   │   ├── Abstractions/         # All public service interfaces (IAuthService, …)
│   │   ├── Bootstrap/            # ServicesInitializer, IServiceModule, stages
│   │   ├── Events/               # IEventBus, EventBus, event structs
│   │   ├── Results/              # ServiceResult<T>, ServiceError, ServiceErrorCode
│   │   ├── Resilience/           # RetryPolicy, TimeoutPolicy
│   │   ├── Configuration/        # ServicesSettings ScriptableObject
│   │   └── Logging/              # IServiceLogger + default Unity implementation
│   ├── Authentication/           # Nexenova.Services.Authentication.asmdef
│   ├── Economy/                  # Nexenova.Services.Economy.asmdef
│   ├── CloudSave/                # Nexenova.Services.CloudSave.asmdef
│   ├── RemoteConfig/             # Nexenova.Services.RemoteConfig.asmdef
│   ├── Purchasing/               # Nexenova.Services.Purchasing.asmdef
│   └── Unified/                  # Nexenova.Services.asmdef — composition root (see below)
├── Editor/                       # Nexenova.Services.Editor.asmdef (settings inspectors, validators)
├── Tests/
│   ├── Editor/                   # Nexenova.Services.Tests.Editor.asmdef (EditMode unit tests)
│   └── Runtime/                  # Nexenova.Services.Tests.Runtime.asmdef (PlayMode integration tests)
└── Samples~/Demo/                # Importable sample showing a full boot + purchase flow
```

Inside each service module folder:

```
Authentication/
├── AuthService.cs                # Implements IAuthService (interface lives in Core/Abstractions)
├── Adapters/                     # Thin wrappers around static UGS SDK APIs (see §4.4)
├── Models/                       # Module-private DTOs
└── Registration/                 # VContainer registration extension (see §6)
```

### Assembly definition rules

- Root namespace mirrors the asmdef name: `Nexenova.Services.Core`, `Nexenova.Services.Economy`, etc. Public API types live in the `Nexenova.Services` namespace regardless of assembly.
- `autoReferenced: false` on every asmdef. Consuming games reference explicitly (usually just `Nexenova.Services`).
- **Every service module references `Nexenova.Services.Core` and its own UGS SDK assembly — nothing else.** Service modules never reference each other. A reference between two service asmdefs is an architecture violation, not a style issue.
- `Nexenova.Services.Core` references only UniTask, VContainer, and `Unity.Services.Core`.
- **`Nexenova.Services` (Unified) is the composition root** — the only assembly allowed to reference all modules. It holds `ServicesLifetimeScope`, `ServicesBootController`, and cross-module glue (`EconomyConfigBinder`, `RemoteConfigCatalogSource`) that binds modules together via Core seams without the modules referencing each other.
- The Purchasing module uses asmdef **version defines + define constraints** so it compiles only when `com.unity.purchasing` is installed (`NEX_SERVICES_IAP`); Unified wraps its IAP registration in `#if NEX_SERVICES_IAP`. The same pattern gates GPGS (`NEX_GPGS`, Android-only code also guarded by `UNITY_ANDROID`) and Apple sign-in (`NEX_APPLE_SIGNIN` + `UNITY_IOS`).
- Implementation classes are `internal`. Only interfaces, event structs, results, options, and registration extensions are `public`. Core and every module declare `[assembly: InternalsVisibleTo]` for `Nexenova.Services` and `Nexenova.Services.Tests.Editor`.

---

## 3. Architecture Overview

```
                         ┌────────────────────────────┐
 Game code  ───────────► │  Core/Abstractions          │  interfaces, events, results
 (injects interfaces)    │  Core/Bootstrap, Events,…   │
                         └─────────────▲──────────────┘
                                       │ (each module references Core only)
        ┌──────────────┬──────────────┼──────────────┬──────────────┐
        │              │              │              │              │
   Authentication   Economy       CloudSave     RemoteConfig    Purchasing
        │              │              │              │              │
   UGS Auth SDK   UGS Economy    UGS CloudSave  UGS RemoteConfig  Unity IAP
```

**Non-negotiable rules:**

1. **Service modules are independent.** No service references another service's assembly, interface, or implementation. If Economy needs to know a purchase completed, it subscribes to `PurchaseCompletedEvent` on the event bus — it never injects `IPurchaseService`.
2. **All cross-service communication is interfaces (defined in Core) + events (published on `IEventBus`).** Direct calls between modules are forbidden.
3. **Game code depends only on `Core/Abstractions` interfaces.** Games must be able to swap any module for a fake (offline mode, tests) without touching other modules.
4. **No statics, no singletons, no `FindObjectOfType`, no `DontDestroyOnLoad` service MonoBehaviours.** The only MonoBehaviour in the package is the `LifetimeScope` subclass. All services are plain C# classes.
5. **Async-first:** every I/O-bound API returns `UniTask`/`UniTask<ServiceResult<T>>` and accepts a `CancellationToken`.
6. **Platform agnostic:** no `#if UNITY_IOS`/`UNITY_ANDROID` in service logic. Platform differences (e.g., IAP store selection, sign-in providers) are isolated in adapter classes and resolved via options/registration.

---

## 4. Core Module

### 4.1 Service module contract

Every service implements `IServiceModule` for lifecycle orchestration **in addition to** its functional interface:

```csharp
public enum InitializationStage
{
    Platform = 0,        // UnityServices.InitializeAsync (Core itself)
    Identity = 1,        // Authentication
    Data = 2,            // RemoteConfig, CloudSave, Economy — run in parallel
    Monetization = 3,    // Purchasing (needs Identity + Data)
}

public interface IServiceModule
{
    string ModuleName { get; }
    InitializationStage Stage { get; }
    bool IsRequired { get; }   // required module failure => boot fails; optional => degraded mode
    UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct);
}
```

### 4.2 Result types — expected failures never throw

```csharp
public enum ServiceErrorCode
{
    None, NotInitialized, NotSignedIn, Network, Timeout, RateLimited,
    Validation, Unauthorized, Conflict, NotFound, ProviderError,
    Cancelled, AlreadyInProgress, Unsupported, Unknown,
}

public sealed class ServiceError
{
    public ServiceErrorCode Code { get; }
    public string Message { get; }          // developer-facing, never shown to players raw
    public Exception? Cause { get; }        // original SDK exception, for logging only
    public bool IsRetryable => Code is ServiceErrorCode.Network or ServiceErrorCode.Timeout or ServiceErrorCode.RateLimited;
}

public readonly struct ServiceResult<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }                 // valid only when IsSuccess
    public ServiceError Error { get; }      // valid only when !IsSuccess
    // + static Success(T), Failure(ServiceError), Match/Map helpers
}
public readonly struct Unit { public static readonly Unit Value; }
```

**Rules:**
- Public service APIs return `ServiceResult<T>` for anything that can fail at runtime (network, auth, validation). They throw **only** for programmer errors (`ArgumentNullException`, calling before registration) and `OperationCanceledException` for cancellation.
- Never return `null` to signal failure. Never use `bool` + `out`.
- SDK exceptions are caught at the adapter boundary, mapped to `ServiceErrorCode` (mapping table per module, see module specs), logged once via `IServiceLogger`, and surfaced as `ServiceError`. They never escape a module.

### 4.3 Event bus

Minimal, allocation-conscious, main-thread-only bus defined in Core. Do **not** add MessagePipe or any other library.

```csharp
public interface IEventBus
{
    void Publish<TEvent>(TEvent evt) where TEvent : struct, IServiceEvent;
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IServiceEvent;
}
public interface IServiceEvent { }
```

- All events are **readonly structs** in `Core/Events/`, named in past tense: `PlayerSignedInEvent`, `PlayerSignedOutEvent`, `SessionExpiredEvent`, `CurrencyBalanceChangedEvent`, `RemoteConfigFetchedEvent`, `CloudDataSavedEvent`, `CloudDataConflictDetectedEvent`, `PurchaseCompletedEvent`, `PurchaseFailedEvent`, `ServicesReadyEvent`, `ServiceDegradedEvent`.
- Publishing is synchronous and must happen on the main thread (`await UniTask.SwitchToMainThread()` first if needed). Handlers must not throw; the bus catches, logs, and continues — one bad subscriber never breaks another.
- Events carry **data, not behavior**: IDs, amounts, keys. Never SDK objects, never service references.
- Subscriptions are disposed in the owner's `Dispose()`; every subscribing class implements `IDisposable` and is registered so VContainer disposes it with the scope.

### 4.4 SDK adapters — the testability seam

UGS SDKs are static (`AuthenticationService.Instance`, `CloudSaveService.Instance`, …). Services never call them directly. Each module defines `internal` adapter interfaces that mirror the *minimal* SDK surface used, plus a trivial pass-through implementation:

```csharp
internal interface IAuthenticationSdk
{
    bool IsSignedIn { get; }
    string PlayerId { get; }
    UniTask SignInAnonymouslyAsync(CancellationToken ct);
    event Action SignedIn; event Action SignedOut; event Action Expired;
}
```

All business logic (retry, validation, mapping, event publishing) lives in the service class and is unit-testable against a fake adapter. Adapters contain **zero logic** — one SDK call per method — and are excluded from unit-test coverage expectations.

### 4.5 Resilience

Core provides one shared policy implementation; modules never hand-roll retry loops:

```csharp
public sealed class RetryPolicy
{
    // Exponential backoff with full jitter: delay = random(0, min(cap, base * 2^attempt))
    // Defaults: 3 attempts, base 1s, cap 8s. Retries only ServiceError.IsRetryable.
    public UniTask<ServiceResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, UniTask<ServiceResult<T>>> operation, CancellationToken ct);
}
```

- Every remote call is wrapped in `RetryPolicy` **and** a timeout (default 10s per attempt via `UniTask.Timeout`); both configurable via `ServicesSettings`.
- `Validation`, `Unauthorized`, `Conflict`, `NotFound` are never retried.
- Honor `RateLimited`: respect server `Retry-After` when the SDK exposes it; otherwise back off.

### 4.6 Configuration

A single `ServicesSettings : ScriptableObject` in Core (create via *Assets ▸ Create ▸ Nexenova ▸ Services Settings*) holds **all** per-game service data: UGS environment names, app version (sent as the Remote Config `appVersion` attribute for release targeting), bundle ids, next-scene name, module enable toggles, retry/timeout tunables, sign-in mode, the GPGS plugin version (informational), AdMob app ids for both platforms (**data fields only — no ads module ships here**), Cloud Save key prefix/schema version/size limit, economy grant caps, and the fallback IAP catalog. It is the **only** serialized configuration object; modules read their section via injected, immutable options objects (`AuthOptions`, `EconomyOptions`, …) created from it at registration time. Services never reference the ScriptableObject directly.

---

## 5. Initialization Flow

Boot is owned by `ServicesInitializer` in Core. The standard flow for a game: place the **`ServicesLifetimeScope`** (Unified) on a GameObject in the starting scene with a `ServicesSettings` asset assigned. Its registered `ServicesBootController` entry point (VContainer `IAsyncStartable`) then awaits `IServicesBootstrap.InitializeAsync` and loads `ServicesSettings.NextSceneName` once the state reaches Ready/Degraded (and, by default, even on Failed — `ProceedToSceneOnFailure` keeps hyper-casual titles playable offline; leave `NextSceneName` empty to disable scene loading). Games with their own root scope skip the prefab and call the `RegisterNexenova*` extensions + `InitializeAsync` themselves.

```csharp
public enum ServicesState { NotStarted, Initializing, Ready, Degraded, Failed }

public interface IServicesBootstrap
{
    ServicesState State { get; }
    IReadOnlyList<string> FailedModules { get; }
    UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct);
    UniTask WaitUntilReadyAsync(CancellationToken ct);   // completes on Ready or Degraded
}
```

**Sequence (strict):**

1. **Platform** — `UnityServices.InitializeAsync()` with the environment from `ServicesSettings` (`production` default; `development` in editor unless overridden). Failure here ⇒ `Failed`, nothing else runs.
2. **Identity** — Authentication module signs in (anonymous by default). Failure ⇒ `Failed` (every other service needs a player ID).
3. **Data** — RemoteConfig, CloudSave, Economy initialize **in parallel** (`UniTask.WhenAll`). Remote Config fetch completes before Economy/CloudSave consume any config-driven values within their own init.
4. **Monetization** — Purchasing initializes last (it consumes the Remote Config–driven catalog and needs identity for receipt attribution).

**Failure semantics:** a required module (`IsRequired == true`: Authentication) failing ⇒ `Failed`. Optional module failing ⇒ that module is marked failed, a `ServiceDegradedEvent` is published, state becomes `Degraded`, and the game keeps running — hyper-casual titles must remain playable offline. Defaults for degraded mode: Remote Config serves baked-in defaults, Cloud Save falls back to a local write-through cache, Economy serves last-known balances read-only, IAP reports `Unsupported`.

- `InitializeAsync` is idempotent: concurrent/repeat calls await the same in-flight operation (`AlreadyInProgress` is never surfaced to callers of `InitializeAsync` itself).
- Each module API guards with `NotInitialized` / `NotSignedIn` errors rather than throwing if called early.
- On `SessionExpiredEvent`, the initializer re-runs Identity and republishes `ServicesReadyEvent` on success.

---

## 6. Dependency Injection Standards (VContainer)

- **Constructor injection only.** No `[Inject]` fields/methods, no property injection, no service locator, no `LifetimeScope.Find`.
- The package ships `ServicesLifetimeScope : LifetimeScope` plus **one registration extension per module**:

```csharp
public static class CoreRegistration
{
    public static IContainerBuilder RegisterNexenovaCore(this IContainerBuilder builder, ServicesSettings settings) { … }
}
public static class EconomyRegistration
{
    public static IContainerBuilder RegisterNexenovaEconomy(this IContainerBuilder builder) { … }
}
```

  Games either drop `ServicesLifetimeScope` into their boot scene (it calls all registrations using its serialized `ServicesSettings`) or call the extensions from their own root scope. Both paths must work.
- **Lifetimes:** every service, adapter, policy, and the event bus is `Lifetime.Singleton` within the scope. Nothing in this package is `Transient` or `Scoped`.
- Register implementations `As<TInterface>()` only — concrete types are never resolvable. `AuthService` registers as `IAuthService` **and** `IServiceModule` (`AsImplementedInterfaces` on the interfaces it owns).
- Anything holding subscriptions or SDK callbacks implements `IDisposable` and relies on VContainer scope disposal — no manual teardown calls.
- The package never creates its own scope at runtime; it only provides registrations. No `RuntimeInitializeOnLoadMethod` container bootstrapping.

---

## 7. Async Standards (UniTask)

- All async APIs return `UniTask` / `UniTask<T>`; method names end in `Async`. No `Task`, no `IEnumerator` coroutines, no `Awaitable`.
- **Every public async method takes `CancellationToken ct` as its last parameter — no default value in implementations; interfaces may default to `default` for ergonomics.** Pass `ct` through to every awaited call.
- Cancellation propagates as `OperationCanceledException` — do **not** convert it to `ServiceResult` failure inside services; only `ServicesInitializer` and fire-and-forget boundaries catch it.
- `async void` is forbidden everywhere, including Unity event handlers — bridge with `UniTask.Void(async () => …)` or `.Forget()`.
- `.Forget()` is allowed only at true fire-and-forget boundaries and only on methods that contain their own try/catch + logging.
- SDK callbacks may arrive off the main thread: any code touching Unity APIs or publishing events does `await UniTask.SwitchToMainThread(ct)` first.
- No `Task.Run`, no `.Result`/`.Wait()`/`.GetAwaiter().GetResult()`. CPU-bound work (JSON of large saves) may use `UniTask.RunOnThreadPool` and must switch back.
- Single-flight guards: operations that must not run concurrently (sign-in, save-to-cloud, purchase) keep an in-flight `UniTask` and either await it (init) or return `AlreadyInProgress` (purchase, save).

---

## 8. Error Handling Standards

- Adapter boundary: `try { sdk call } catch (specific SDK exception) { map }` — never `catch (Exception)` except as the final fallback mapping to `Unknown` (still logged with full stack).
- Each module owns an `internal static ErrorMapper` with an explicit mapping table (documented per module below). Unmapped SDK error codes map to `ProviderError`, never silently to `Network`.
- **Log once, at the point of mapping**, via `IServiceLogger` (levels: `Info`, `Warning`, `Error`; tag = `ModuleName`). Callers receiving a `ServiceResult` failure do not re-log the same error.
- Player-facing messaging is the game's job. The package never shows UI, never localizes error text.
- No swallowing: every failure path either returns a `ServiceResult` failure or (for background work) logs at `Warning`+.

---

## 9. Service Module Specifications

All interfaces live in `Core/Abstractions`. Signatures below are normative — implement them as written (plus XML docs).

### 9.1 Authentication (`Nexenova.Services.Authentication`)

```csharp
public interface IAuthService
{
    bool IsSignedIn { get; }
    string PlayerId { get; }                 // empty string when signed out, never null
    UniTask<ServiceResult<Unit>> SignInAnonymouslyAsync(CancellationToken ct = default);
    UniTask<ServiceResult<Unit>> SignInWithPlatformAsync(CancellationToken ct = default);  // GPGS on Android, Apple on iOS
    UniTask<ServiceResult<Unit>> LinkWithPlatformAsync(CancellationToken ct = default);
    UniTask<ServiceResult<Unit>> SignOutAsync(CancellationToken ct = default);
}
```

- Platform token acquisition is owned by the module via the internal `IPlatformSignInProvider` seam: `GpgsSignInProvider` (Android — GPGS v2 auto sign-in, then interactive, then `RequestServerSideAccess` for the server auth code; requires the web client ID in the GPGS plugin settings), `AppleSignInProvider` (iOS — apple-signin-unity identity token, message queue pumped without a MonoBehaviour), `NullPlatformSignInProvider` elsewhere (returns `Unsupported`).
- Boot flow per `AuthOptions.SignInMode`: cached session when one exists; otherwise `PlatformWithAnonymousFallback` (default) tries platform sign-in and falls back to anonymous on any failure, `AnonymousOnly` skips platform sign-in.
- On `Expired`, the module publishes `SessionExpiredEvent` and re-signs-in automatically in the background.
- Publishes `PlayerSignedInEvent(string playerId, bool isNewPlayer)` (isNew ≈ no prior session token), `PlayerSignedOutEvent`, `SessionExpiredEvent`.
- Error mapping: `AuthenticationException` invalid-token codes ⇒ `Unauthorized`; account-link conflicts ⇒ `Conflict`; `RequestFailedException` transient codes ⇒ `Network`/`RateLimited`; player-cancelled platform login ⇒ `Cancelled`.
- Security: never log tokens, auth codes, or session secrets. PlayerId is not PII but still only logged at `Info` during boot.

### 9.2 Economy (`Nexenova.Services.Economy`)

```csharp
public interface IEconomyService
{
    UniTask<ServiceResult<IReadOnlyList<CurrencyBalance>>> GetBalancesAsync(CancellationToken ct = default);
    UniTask<ServiceResult<CurrencyBalance>> AddCurrencyAsync(string currencyId, long amount, TransactionReason reason, CancellationToken ct = default);
    UniTask<ServiceResult<CurrencyBalance>> SpendCurrencyAsync(string currencyId, long amount, TransactionReason reason, CancellationToken ct = default);
    UniTask<ServiceResult<IReadOnlyList<InventoryItem>>> GetInventoryAsync(CancellationToken ct = default);
    UniTask<ServiceResult<InventoryItem>> AddInventoryItemAsync(string itemId, CancellationToken ct = default);
}
public readonly struct CurrencyBalance { public string CurrencyId { get; } public long Amount { get; } }
public readonly struct TransactionReason { public string Source { get; } }  // e.g. "level_reward", "iap:gem_pack_1", "ad_reward"
```

- Definitions (currencies, items) are authored in the UGS dashboard; the module fetches and caches them at init. Balances are cached and refreshed after every mutation; `CurrencyBalanceChangedEvent(currencyId, oldAmount, newAmount, reason)` is published on every change.
- **Abuse prevention (mandatory):**
  - All mutations go through this service; the cache is read-only to callers.
  - `amount <= 0` ⇒ `Validation`. Per-call and per-minute grant caps come from `EconomyOptions` (sourced from `ServicesSettings`, overridable by Remote Config). Calls exceeding caps ⇒ `Validation`, logged at `Warning` with the `TransactionReason`.
  - Spends are pre-checked against the cached balance (fail fast with `Validation`) **and** still rely on the server as authority — a server-side insufficient-balance error maps to `Validation`, and the cache is re-synced.
  - Every mutation carries a `TransactionReason`; grants with reason prefix `iap:` are accepted **only** from the IAP grant pipeline (see §9.5) — enforced by an `internal` overload, not the public API.
  - High-value or competitive-economy grants must run through Cloud Code. The module exposes the seam: `internal interface IServerAuthoritativeEconomy` with a default client-direct implementation; games with Cloud Code swap the registration. Do not implement Cloud Code scripts in this repo.
- Error mapping: `EconomyException` insufficient-balance/validation codes ⇒ `Validation`; rate-limit ⇒ `RateLimited`; others ⇒ `ProviderError`.

### 9.3 Cloud Save (`Nexenova.Services.CloudSave`)

```csharp
public interface ICloudSaveService
{
    UniTask<ServiceResult<T>> LoadAsync<T>(string key, CancellationToken ct = default);
    UniTask<ServiceResult<Unit>> SaveAsync<T>(string key, T value, CancellationToken ct = default);
    UniTask<ServiceResult<Unit>> DeleteAsync(string key, CancellationToken ct = default);
    UniTask<ServiceResult<IReadOnlyList<string>>> ListKeysAsync(CancellationToken ct = default);
}
```

- All values are wrapped in an envelope before upload:

```csharp
internal sealed class SaveEnvelope<T>
{
    public int SchemaVersion;        // from ServicesSettings; bump on breaking model changes
    public long SavedAtUnixMs;       // informational only — never trusted for game logic
    public T Payload;
}
```

- Keys are namespaced: `{prefix}.{key}` with prefix from `ServicesSettings` (default `nex`). Public API rejects keys with `.` or whitespace ⇒ `Validation`.
- Serialization: Newtonsoft with explicit `JsonSerializerSettings` (no type-name handling — `TypeNameHandling.None`, hard requirement). Deserialization failure or `SchemaVersion` newer than supported ⇒ `Validation` (never a crash); older versions go through a `ISaveMigrator` hook (default: pass-through).
- **Conflict & integrity:** use UGS write-lock (optimistic concurrency) on save; a lock mismatch ⇒ `Conflict` + `CloudDataConflictDetectedEvent(key)`. Default resolution is **last-write-wins after a forced reload** (the service reloads, re-publishes, and the game decides whether to re-save). Never auto-merge.
- Write-through local cache (`Application.persistentDataPath/nex_services/`) so reads work offline and writes queue for retry on reconnect. Queued writes replay in order, single-flight per key.
- Size guard: serialized payload > 200 KB ⇒ `Validation` (UGS hard limits are far higher; staying small keeps mobile sync fast).
- **Secure practices:** never store secrets, tokens, or PII in save data; never trust client timestamps for reward gating (use Remote Config/Economy for time-gated logic); all values go through the envelope so tampered/unversioned data is rejected, not crashed on.

### 9.4 Remote Config (`Nexenova.Services.RemoteConfig`)

```csharp
public interface IRemoteConfigService
{
    bool IsFetched { get; }
    UniTask<ServiceResult<Unit>> FetchAsync(CancellationToken ct = default);
    int GetInt(string key, int defaultValue);
    long GetLong(string key, long defaultValue);
    float GetFloat(string key, float defaultValue);
    bool GetBool(string key, bool defaultValue);
    string GetString(string key, string defaultValue);
    ServiceResult<T> GetJson<T>(string key);   // typed object configs, validated
}
```

- Getters are synchronous and never fail: before fetch (or on fetch failure) they return the provided default. `FetchAsync` runs once during boot stage **Data**; games may re-fetch on app foreground.
- Publishes `RemoteConfigFetchedEvent` after a successful fetch and validation pass.
- **Validation (mandatory):** the module owns `internal sealed class ConfigValidator` with a declarative rule set (key → type, min/max range or allowed values). Defaults and rules for every key the package consumes (economy caps, retry tunables, IAP catalog) are declared in one `ConfigKeys` static class — no magic strings at call sites anywhere in the package. Out-of-range values are **clamped**, wrong-typed values fall back to default; both are logged at `Warning`. Games extend validation by registering additional rule sets via `RemoteConfigOptions`.
- **Security:** config values are data, never behavior — no URLs to execute against, no code/eval, no format strings fed to anything. `GetJson<T>` deserializes with the same hardened settings as Cloud Save (`TypeNameHandling.None`).
- Error mapping: fetch failure ⇒ `Network`/`Timeout`; the module still reports init success with `IsFetched == false` (degraded, defaults in force) unless the game marks it required.

### 9.5 In-App Purchases (`Nexenova.Services.Purchasing`)

```csharp
public interface IPurchaseService
{
    bool IsAvailable { get; }   // false when store init failed or module compiled out
    UniTask<ServiceResult<IReadOnlyList<ProductInfo>>> GetProductsAsync(CancellationToken ct = default);
    UniTask<ServiceResult<PurchaseReceipt>> PurchaseAsync(string productId, CancellationToken ct = default);
    UniTask<ServiceResult<Unit>> RestorePurchasesAsync(CancellationToken ct = default);  // Unsupported on Android
}
public readonly struct ProductInfo { /* Id, Type(Consumable/NonConsumable/Subscription), LocalizedPrice, IsoCurrencyCode, LocalizedTitle */ }
public readonly struct PurchaseReceipt { /* ProductId, TransactionId, Receipt (raw), Store */ }
```

- Wraps Unity IAP (`IStoreListener`/`IDetailedStoreListener` callback model) into the UniTask API via `UniTaskCompletionSource` per transaction; one purchase in flight at a time (`AlreadyInProgress`).
- Product catalog comes from Remote Config (key `ConfigKeys.IapCatalog`, JSON, validated) with a baked-in default catalog in `ServicesSettings` as fallback — never hardcoded product IDs in code.
- **Validation pipeline (mandatory, in order):**
  1. Receipt validation via the `IReceiptValidator` seam. Default registration is `PassThroughReceiptValidator` (accepts + warns loudly — tangle classes are generated per-game and cannot ship in the package). Before release, games register either `TangleReceiptValidator` (local `CrossPlatformValidator`, fed `GooglePlayTangle.Data()`/`AppleTangle.Data()` from the game's obfuscator output) or a backend validator. Validation failure ⇒ purchase result `Unauthorized`, transaction **never** granted or confirmed.
  2. Grant: publish `PurchaseCompletedEvent(productId, transactionId, grantsJson)` — Economy's `IapGrantProcessor` subscribes (via the event, not a reference), applies grants through the internal `iap:`-reasoned pipeline, and acks with `PurchaseGrantProcessedEvent(transactionId, success)`.
  3. **Confirm the pending purchase with the store only after the grant ack reports success** (`ProcessingResult.Pending` until then; 30s ack timeout leaves it unconfirmed). This makes grants idempotent across crashes: on next init, Unity IAP redelivers unconfirmed transactions; the grant processor dedupes by `transactionId` (persisted file set in `persistentDataPath/nex_services/`). When Economy is disabled, `PurchasingOptions.AwaitGrantProcessing` is false and transactions confirm immediately after validation.
- `PurchaseFailureReason` mapping: `UserCancelled` ⇒ `Cancelled`; `PaymentDeclined`/`PurchasingUnavailable` ⇒ `ProviderError`; `DuplicateTransaction` ⇒ `Conflict`; `SignatureInvalid` ⇒ `Unauthorized`.
- Security: never log raw receipts at `Info` (truncate/hash); never grant on `PurchaseFailed`; deferred/Ask-to-Buy purchases surface as `AlreadyInProgress` and complete via the redelivery path.

---

## 10. Addressables Usage

Addressables is used **only** for game-overridable configuration assets — not for service code paths:

- `ServicesSettings` and the default IAP catalog asset are loaded by `ServicesLifetimeScope` via Addressables key `Nexenova/ServicesSettings` when no asset is assigned in the inspector. Inspector assignment wins; Addressables is the override mechanism for games that manage config remotely.
- Handles are released on scope disposal. No other module may call Addressables APIs; gameplay content delivery is the game's concern.

---

## 11. Testing Standards

### Layout & frameworks

- Unity Test Framework 1.6 (`com.unity.test-framework`) with NUnit + UniTask's async test support. No mocking library — **hand-rolled fakes** per adapter interface live in `Tests/Editor/Fakes/` (e.g., `FakeAuthenticationSdk` with scriptable responses and call recording).
- `Tests/Editor` (EditMode) = unit tests, the bulk of coverage. `Tests/Runtime` (PlayMode) = integration tests against real UGS.

### Unit tests (EditMode) — required for every module

- Test the service class against fake adapters + a real `EventBus` + a deterministic `RetryPolicy` (injected zero-delay time source — Core's `RetryPolicy` takes an `internal IDelayProvider` for this).
- Naming: `Method_Condition_ExpectedOutcome` (e.g., `SpendCurrencyAsync_AmountExceedsBalance_ReturnsValidationError`). One behavior per test.
- Mandatory coverage per module: success path, every `ServiceErrorCode` the mapper can produce, cancellation (token honored mid-call), `NotInitialized`/`NotSignedIn` guards, single-flight behavior, every event published with correct payload, and the module's security rules (caps, validation, key rules, idempotent grants).
- Async tests use `[Test] public async Task …` with UniTask conversions; no `UniTask.Delay` with real time — fake the delay provider.

### Integration tests (PlayMode)

- Live in `Tests/Runtime`, run against a dedicated UGS **test environment** (never production). They are gated behind the scripting define `NEX_SERVICES_INTEGRATION` and skipped (`Assert.Ignore`) without it — CI and local runs stay green with no cloud project configured.
- Cover: full boot sequence to `Ready`, anonymous sign-in, config fetch + validation, save/load/conflict round-trip, economy grant/spend round-trip. IAP integration is editor fake-store only.

### Commands

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe"  # editor must be closed

# Unit tests (the default check before any commit)
& $unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults .\TestResults\editmode.xml -logFile .\Logs\test.log

# PlayMode / integration
& $unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults .\TestResults\playmode.xml -logFile .\Logs\test.log

# Single test/class: add -testFilter "Nexenova.Services.Tests.Editor.EconomyServiceTests" (regex, semicolon-separated)

# Compile check only
& $unity -batchmode -quit -projectPath . -logFile .\Logs\compile.log
```

---

## 12. Security Standards (cross-cutting summary)

1. **Client is never the authority** for anything with real or competitive value: IAP grants flow through receipt validation + idempotent processing; high-value economy mutations expose a server-authority seam.
2. **Validate at every trust boundary:** Remote Config values (type/range/clamp), Cloud Save payloads (envelope + schema version + hardened JSON settings), Economy amounts (positive, capped, reasoned), purchase receipts (local validator minimum).
3. **`TypeNameHandling.None` everywhere.** Polymorphic JSON deserialization of remote data is forbidden, no exceptions.
4. **No secrets in the package:** no API keys, no tangle source committed unencrypted beyond what Unity IAP's obfuscator generates, no tokens/receipts/PII in logs or save data.
5. **Fail closed:** validation failure means the operation does not happen (no grant, no save accepted, default config served) — and the game keeps running.

---

## 13. Definition of Done (every module / PR)

- [ ] Interface in `Core/Abstractions`, implementation `internal`, registered via its `RegisterNexenova*` extension as interfaces only
- [ ] No references to other service asmdefs; cross-module effects via `IEventBus` events only
- [ ] All I/O async via UniTask with `CancellationToken`, wrapped in `RetryPolicy` + timeout
- [ ] All expected failures returned as `ServiceResult` with a mapped `ServiceErrorCode`; logged once at the adapter boundary
- [ ] SDK access only through a logic-free adapter with a fake in `Tests/Editor/Fakes/`
- [ ] EditMode tests covering the mandatory matrix in §11; integration tests gated by `NEX_SERVICES_INTEGRATION`
- [ ] Security rules for the module (§9 + §12) implemented and tested
- [ ] `.meta` files paired with every new asset; never hand-edit `.sln`/`.csproj` (generated)

---

## 14. Repository Notes

- This Unity project is the **host/dev environment** for the package; the importable deliverable is `Packages/com.nexenovastudios.services/`. The `Assets/` demo scene may reference the package, never the reverse.
- `Library/`, `Temp/`, `Logs/`, `obj/`, `UserSettings/` are generated and unversioned; never edit `Library/PackageCache` sources.
- URP quality-tier assets (`Assets/Settings/PC_*`/`Mobile_*`) belong to the demo project and are irrelevant to the package.
