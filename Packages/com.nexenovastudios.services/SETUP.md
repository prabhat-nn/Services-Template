# Nexenova Game Services — Setup Guide

Get Authentication (anonymous + GPGS/Apple), Economy, Cloud Save, Remote Config and IAP running in a hyper-casual game in ~15 minutes.

---

## 1. Import the package

Copy these into your project's `Packages/manifest.json` (or copy the whole `Packages/com.nexenovastudios.services` folder into your project's `Packages/`):

```json
"scopedRegistries": [
  {
    "name": "package.openupm.com",
    "url": "https://package.openupm.com",
    "scopes": [
      "com.cysharp.unitask", "jp.hadashikick.vcontainer",
      "com.google.play.games", "com.google.external-dependency-manager",
      "com.lupidan.apple-signin-unity", "com.gitamend.improvedtimers"
    ]
  }
]
```

Required dependencies (resolved automatically if you copy the package; otherwise add to `dependencies`): UniTask 2.5.11, VContainer 1.18.0, `com.unity.services.authentication`, `economy`, `cloudsave`, `remote-config`, `com.unity.purchasing` **4.13.2** (not 5.x), and for device sign-in: `com.google.play.games` 2.1.0 + `com.google.external-dependency-manager` (Android), `com.lupidan.apple-signin-unity` 1.5.0 (iOS).

Studio-standard gameplay utilities (used across our projects, not by the services package itself):

```json
"com.gitamend.improvedtimers": "1.0.2",
"com.gitamend.unityutils": "https://github.com/adammyhre/Unity-Utils.git"
```

## 2. Link the project to UGS

**Edit ▸ Project Settings ▸ Services** → link to your Unity Cloud project (create one per game). In the [Unity Cloud dashboard](https://cloud.unity.com) enable: **Authentication, Economy, Cloud Save, Remote Config**. Create two environments: `production` and `development`.

## 3. Create the settings asset

**Assets ▸ Create ▸ Nexenova ▸ Services Settings**. Fill in:

| Field | What to put |
|---|---|
| Environments | `production` / `development` (editor uses development by default) |
| App Version | Current release version — used for Remote Config targeting |
| Bundle IDs / AdMob App IDs | Per-platform ids (AdMob fields are data-only for your ads integration) |
| Next Scene Name | Your main scene (must be in Build Settings). Empty = no scene load |
| Modules | Toggle off any service the game doesn't use |
| Economy caps | Max grant per call / per minute — tune to your economy scale |
| Default IAP Catalog | Product id, type, and grants JSON, e.g. `[{"currencyId":"GEMS","amount":100}]` |

## 4. Boot scene

1. Create a `Boot` scene, make it **scene 0** in Build Settings.
2. Empty GameObject → add **`ServicesLifetimeScope`** → assign the settings asset.
3. Press Play: services initialize (Platform → Auth → Data → IAP), then your next scene loads. Boot failures don't block the game — it proceeds offline/degraded by default.

## 5. Dashboard configuration per service

- **Economy**: define currencies (e.g. `GEMS`) and inventory items in the dashboard, then **Publish**. IDs in code must match.
- **Remote Config**: add keys + values, publish. Reserved package keys: `nex_iap_catalog` (JSON catalog override), `nex_economy_max_grant_per_call`, `nex_economy_max_granted_per_minute`.
- **Cloud Save / Authentication**: enabled is enough; anonymous sign-in works with zero config.

## 6. Platform sign-in (device builds only)

- **Android (GPGS)**: Play Console → set up Play Games Services, add OAuth credentials. Unity: **Window ▸ Google Play Games ▸ Setup ▸ Android Setup** — paste resources XML + **web client ID** (required for the UGS token exchange). Dashboard: Authentication ▸ add **Google Play Games** provider (client ID + secret).
- **iOS (Apple)**: Dashboard: Authentication ▸ add **Sign in with Apple** provider (bundle ID). The plugin adds the entitlement to the Xcode project.
- Editor and unconfigured platforms automatically fall back to **anonymous** — nothing breaks.

## 7. In-App Purchases

1. Create products in Play Console / App Store Connect with the same IDs as your catalog.
2. Enable IAP: **Project Settings ▸ Services ▸ In-App Purchasing**.
3. **Before release**: generate obfuscated tangles (**Services ▸ In-App Purchasing ▸ Receipt Validation Obfuscator**), then register the validator in your own `LifetimeScope` (child of `ServicesLifetimeScope`):

```csharp
builder.RegisterInstance<IReceiptValidator>(new TangleReceiptValidator(
    GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier, logger));
```

Without this, purchases work but receipts are NOT validated (a loud warning is logged).

## 8. Using the services

Inject interfaces into plain classes via VContainer (your gameplay scope must be a child of `ServicesLifetimeScope`):

```csharp
public sealed class ShopLogic
{
    public ShopLogic(IEconomyService economy, IPurchaseService iap,
                     ICloudSaveService save, IRemoteConfigService config, IEventBus events) { ... }
}

// Spend / grant currency (validated + capped automatically):
await economy.SpendCurrencyAsync("GEMS", 50, new TransactionReason("shop_skin"), ct);

// Save / load (versioned, offline-safe):
await save.SaveAsync("progress", myData, ct);
var data = await save.LoadAsync<ProgressData>("progress", ct);

// Config (always safe, returns default before fetch):
int lives = config.GetInt("starting_lives", 3);

// Buy (validates receipt, grants via Economy, crash-safe):
var result = await iap.PurchaseAsync("gem_pack_1", ct);

// React to anything:
events.Subscribe<CurrencyBalanceChangedEvent>(e => UpdateHud(e.NewAmount));
```

**Every call returns `ServiceResult<T>`** — check `IsSuccess` / `Error.Code`; nothing throws for network/validation failures.

## 9. Verify

- EditMode tests: **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All** — must be green.
- Play the boot scene: console should show `[Nexenova.Bootstrap] Boot finished: Ready` and your scene loads.

## Gotchas

- Unity IAP must stay on **4.x** — do not upgrade to 5.x without porting the Purchasing module.
- Cloud Save keys: no dots or spaces. Payloads ≤ 200 KB.
- Economy grants with reason prefix `iap:` are rejected on the public API — that path is reserved for receipt-validated purchases.
- GPGS only signs in on a real Android device (not the editor).
- Architecture rules for anyone extending the package: see `CLAUDE.md` at the repo root.
