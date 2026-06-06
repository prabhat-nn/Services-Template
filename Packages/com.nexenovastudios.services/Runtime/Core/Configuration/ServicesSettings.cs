#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Nexenova.Services
{
    public enum PlatformSignInMode
    {
        /// <summary>Anonymous sign-in only (default for soft-launch hyper-casual).</summary>
        AnonymousOnly,
        /// <summary>Try GPGS (Android) / Apple (iOS) first; fall back to anonymous on failure or unsupported platform.</summary>
        PlatformWithAnonymousFallback,
    }

    /// <summary>
    /// The single serialized configuration asset for all service modules.
    /// Create via Assets ▸ Create ▸ Nexenova ▸ Services Settings, assign it to the
    /// ServicesLifetimeScope in the boot scene, and fill in per-game values.
    /// Modules never read this directly — they receive immutable options built from it.
    /// </summary>
    [CreateAssetMenu(menuName = "Nexenova/Services Settings", fileName = "ServicesSettings")]
    public sealed class ServicesSettings : ScriptableObject
    {
        [Header("UGS Environment")]
        [SerializeField] private string productionEnvironment = "production";
        [SerializeField] private string editorEnvironment = "development";
        [Tooltip("Use the editor environment when running in the Unity editor.")]
        [SerializeField] private bool useEditorEnvironmentInEditor = true;

        [Header("App Info")]
        [Tooltip("Current app version. Sent as a Remote Config user attribute so configs can be targeted per release.")]
        [SerializeField] private string appVersion = "1.0.0";
        [SerializeField] private string androidBundleId = string.Empty;
        [SerializeField] private string iosBundleId = string.Empty;

        [Header("Boot")]
        [Tooltip("Scene loaded by ServicesBootController once services reach Ready/Degraded. Leave empty to disable scene loading.")]
        [SerializeField] private string nextSceneName = "Main";
        [Tooltip("Hyper-casual default: keep the game playable and load the next scene even if boot fails entirely.")]
        [SerializeField] private bool proceedToSceneOnFailure = true;
        [SerializeField] private bool verboseLogging = true;

        [Header("Modules")]
        [SerializeField] private bool enableEconomy = true;
        [SerializeField] private bool enableCloudSave = true;
        [SerializeField] private bool enableRemoteConfig = true;
        [SerializeField] private bool enablePurchasing = true;

        [Header("Resilience")]
        [SerializeField, Range(1, 6)] private int maxRetryAttempts = 3;
        [SerializeField] private float retryBaseDelaySeconds = 1f;
        [SerializeField] private float retryMaxDelaySeconds = 8f;
        [SerializeField] private float operationTimeoutSeconds = 10f;

        [Header("Authentication")]
        [SerializeField] private PlatformSignInMode signInMode = PlatformSignInMode.PlatformWithAnonymousFallback;
        [Tooltip("Informational: the Google Play Games plugin version this template was built against.")]
        [SerializeField] private string googlePlayGamesPluginVersion = "2.1.0";

        [Header("Ads (data only — read these from your ads integration; no ads module ships here)")]
        [SerializeField] private string admobAppIdAndroid = string.Empty;
        [SerializeField] private string admobAppIdIos = string.Empty;

        [Header("Cloud Save")]
        [Tooltip("Namespace prefix prepended to every cloud save key.")]
        [SerializeField] private string cloudSaveKeyPrefix = "nex";
        [Tooltip("Bump on breaking save-model changes; older saves go through the migration hook.")]
        [SerializeField] private int saveSchemaVersion = 1;
        [SerializeField] private int maxSavePayloadKb = 200;

        [Header("Economy Abuse Prevention")]
        [Tooltip("Maximum currency amount a single non-IAP grant may add. Calls above this fail validation.")]
        [SerializeField] private long maxGrantPerCall = 100_000;
        [Tooltip("Maximum total non-IAP granted currency per rolling minute.")]
        [SerializeField] private long maxGrantedPerMinute = 500_000;

        [Header("In-App Purchases")]
        [Tooltip("Fallback catalog used when the Remote Config catalog is missing or invalid.")]
        [SerializeField] private List<IapProductDefinition> defaultIapCatalog = new();
        [Tooltip("Validate receipts locally with Unity IAP obfuscated tangles (Android/iOS release builds).")]
        [SerializeField] private bool useLocalReceiptValidation = true;

        // ── Read accessors ──────────────────────────────────────────────────

        public string ProductionEnvironment => productionEnvironment;
        public string EditorEnvironment => editorEnvironment;
        public bool UseEditorEnvironmentInEditor => useEditorEnvironmentInEditor;

        public string AppVersion => appVersion;
        public string AndroidBundleId => androidBundleId;
        public string IosBundleId => iosBundleId;

        public string NextSceneName => nextSceneName;
        public bool ProceedToSceneOnFailure => proceedToSceneOnFailure;
        public bool VerboseLogging => verboseLogging;

        public bool EnableEconomy => enableEconomy;
        public bool EnableCloudSave => enableCloudSave;
        public bool EnableRemoteConfig => enableRemoteConfig;
        public bool EnablePurchasing => enablePurchasing;

        public int MaxRetryAttempts => maxRetryAttempts;
        public float RetryBaseDelaySeconds => retryBaseDelaySeconds;
        public float RetryMaxDelaySeconds => retryMaxDelaySeconds;
        public float OperationTimeoutSeconds => operationTimeoutSeconds;

        public PlatformSignInMode SignInMode => signInMode;
        public string GooglePlayGamesPluginVersion => googlePlayGamesPluginVersion;

        public string AdmobAppIdAndroid => admobAppIdAndroid;
        public string AdmobAppIdIos => admobAppIdIos;

        public string CloudSaveKeyPrefix => cloudSaveKeyPrefix;
        public int SaveSchemaVersion => saveSchemaVersion;
        public int MaxSavePayloadKb => maxSavePayloadKb;

        public long MaxGrantPerCall => maxGrantPerCall;
        public long MaxGrantedPerMinute => maxGrantedPerMinute;

        public IReadOnlyList<IapProductDefinition> DefaultIapCatalog => defaultIapCatalog;
        public bool UseLocalReceiptValidation => useLocalReceiptValidation;

        /// <summary>Resolved UGS environment for the current context.</summary>
        public string ResolveEnvironment()
        {
#if UNITY_EDITOR
            if (useEditorEnvironmentInEditor)
                return editorEnvironment;
#endif
            return productionEnvironment;
        }
    }
}
