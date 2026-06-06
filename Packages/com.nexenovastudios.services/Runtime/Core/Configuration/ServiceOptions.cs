#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexenova.Services.Core
{
    /// <summary>
    /// Immutable per-module options built once from <see cref="ServicesSettings"/> at
    /// registration time. Services depend on these, never on the ScriptableObject.
    /// </summary>
    public sealed class CoreOptions
    {
        public string Environment { get; }
        public bool VerboseLogging { get; }
        public int MaxRetryAttempts { get; }
        public TimeSpan RetryBaseDelay { get; }
        public TimeSpan RetryMaxDelay { get; }
        public TimeSpan OperationTimeout { get; }

        public CoreOptions(ServicesSettings s)
        {
            Environment = s.ResolveEnvironment();
            VerboseLogging = s.VerboseLogging;
            MaxRetryAttempts = s.MaxRetryAttempts;
            RetryBaseDelay = TimeSpan.FromSeconds(s.RetryBaseDelaySeconds);
            RetryMaxDelay = TimeSpan.FromSeconds(s.RetryMaxDelaySeconds);
            OperationTimeout = TimeSpan.FromSeconds(s.OperationTimeoutSeconds);
        }

        internal CoreOptions(string environment, bool verboseLogging, int maxRetryAttempts, TimeSpan retryBaseDelay, TimeSpan retryMaxDelay, TimeSpan operationTimeout)
        {
            Environment = environment;
            VerboseLogging = verboseLogging;
            MaxRetryAttempts = maxRetryAttempts;
            RetryBaseDelay = retryBaseDelay;
            RetryMaxDelay = retryMaxDelay;
            OperationTimeout = operationTimeout;
        }
    }

    public sealed class AuthOptions
    {
        public PlatformSignInMode SignInMode { get; }

        public AuthOptions(ServicesSettings s) { SignInMode = s.SignInMode; }
        internal AuthOptions(PlatformSignInMode signInMode) { SignInMode = signInMode; }
    }

    public sealed class EconomyOptions
    {
        public long MaxGrantPerCall { get; }
        public long MaxGrantedPerMinute { get; }

        public EconomyOptions(ServicesSettings s)
        {
            MaxGrantPerCall = s.MaxGrantPerCall;
            MaxGrantedPerMinute = s.MaxGrantedPerMinute;
        }

        internal EconomyOptions(long maxGrantPerCall, long maxGrantedPerMinute)
        {
            MaxGrantPerCall = maxGrantPerCall;
            MaxGrantedPerMinute = maxGrantedPerMinute;
        }
    }

    public sealed class CloudSaveOptions
    {
        public string KeyPrefix { get; }
        public int SchemaVersion { get; }
        public int MaxPayloadBytes { get; }

        public CloudSaveOptions(ServicesSettings s)
        {
            KeyPrefix = s.CloudSaveKeyPrefix;
            SchemaVersion = s.SaveSchemaVersion;
            MaxPayloadBytes = s.MaxSavePayloadKb * 1024;
        }

        internal CloudSaveOptions(string keyPrefix, int schemaVersion, int maxPayloadBytes)
        {
            KeyPrefix = keyPrefix;
            SchemaVersion = schemaVersion;
            MaxPayloadBytes = maxPayloadBytes;
        }
    }

    public sealed class RemoteConfigOptions
    {
        /// <summary>Sent as the appVersion user attribute for release targeting.</summary>
        public string AppVersion { get; }

        public RemoteConfigOptions(ServicesSettings s) { AppVersion = s.AppVersion; }
        internal RemoteConfigOptions(string appVersion) { AppVersion = appVersion; }
    }

    public sealed class PurchasingOptions
    {
        public IReadOnlyList<IapProductDefinition> DefaultCatalog { get; }
        public bool UseLocalReceiptValidation { get; }
        /// <summary>When false (Economy module disabled) transactions confirm immediately instead of awaiting the grant pipeline.</summary>
        public bool AwaitGrantProcessing { get; }

        public PurchasingOptions(ServicesSettings s)
        {
            DefaultCatalog = s.DefaultIapCatalog.ToList();
            UseLocalReceiptValidation = s.UseLocalReceiptValidation;
            AwaitGrantProcessing = s.EnableEconomy;
        }

        internal PurchasingOptions(IReadOnlyList<IapProductDefinition> defaultCatalog, bool useLocalReceiptValidation, bool awaitGrantProcessing)
        {
            DefaultCatalog = defaultCatalog;
            UseLocalReceiptValidation = useLocalReceiptValidation;
            AwaitGrantProcessing = awaitGrantProcessing;
        }
    }
}
