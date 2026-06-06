#nullable enable

namespace Nexenova.Services
{
    // ── Bootstrap ───────────────────────────────────────────────────────────

    /// <summary>All required services initialized; optional failures listed in IServicesBootstrap.FailedModules.</summary>
    public readonly struct ServicesReadyEvent : IServiceEvent
    {
        public bool IsDegraded { get; }
        public ServicesReadyEvent(bool isDegraded) { IsDegraded = isDegraded; }
    }

    /// <summary>An optional module failed to initialize; the game keeps running without it.</summary>
    public readonly struct ServiceDegradedEvent : IServiceEvent
    {
        public string ModuleName { get; }
        public ServiceErrorCode ErrorCode { get; }
        public ServiceDegradedEvent(string moduleName, ServiceErrorCode errorCode)
        {
            ModuleName = moduleName;
            ErrorCode = errorCode;
        }
    }

    // ── Authentication ──────────────────────────────────────────────────────

    public readonly struct PlayerSignedInEvent : IServiceEvent
    {
        public string PlayerId { get; }
        public bool IsNewPlayer { get; }
        public PlayerSignedInEvent(string playerId, bool isNewPlayer)
        {
            PlayerId = playerId;
            IsNewPlayer = isNewPlayer;
        }
    }

    public readonly struct PlayerSignedOutEvent : IServiceEvent { }

    public readonly struct SessionExpiredEvent : IServiceEvent { }

    // ── Economy ─────────────────────────────────────────────────────────────

    public readonly struct CurrencyBalanceChangedEvent : IServiceEvent
    {
        public string CurrencyId { get; }
        public long OldAmount { get; }
        public long NewAmount { get; }
        public string Reason { get; }
        public CurrencyBalanceChangedEvent(string currencyId, long oldAmount, long newAmount, string reason)
        {
            CurrencyId = currencyId;
            OldAmount = oldAmount;
            NewAmount = newAmount;
            Reason = reason;
        }
    }

    // ── Cloud Save ──────────────────────────────────────────────────────────

    public readonly struct CloudDataSavedEvent : IServiceEvent
    {
        public string Key { get; }
        public CloudDataSavedEvent(string key) { Key = key; }
    }

    public readonly struct CloudDataConflictDetectedEvent : IServiceEvent
    {
        public string Key { get; }
        public CloudDataConflictDetectedEvent(string key) { Key = key; }
    }

    // ── Remote Config ───────────────────────────────────────────────────────

    public readonly struct RemoteConfigFetchedEvent : IServiceEvent { }

    // ── Purchasing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Receipt validated; grant content should now be applied.
    /// Economy (or the game) subscribes, applies the grants, then publishes
    /// <see cref="PurchaseGrantProcessedEvent"/> so the store transaction can be confirmed.
    /// </summary>
    public readonly struct PurchaseCompletedEvent : IServiceEvent
    {
        public string ProductId { get; }
        public string TransactionId { get; }
        /// <summary>JSON array of grants, e.g. [{"currencyId":"GEMS","amount":100}].</summary>
        public string GrantsJson { get; }
        public PurchaseCompletedEvent(string productId, string transactionId, string grantsJson)
        {
            ProductId = productId;
            TransactionId = transactionId;
            GrantsJson = grantsJson;
        }
    }

    /// <summary>Published by the grant pipeline once grants for a transaction were applied (or skipped as duplicates).</summary>
    public readonly struct PurchaseGrantProcessedEvent : IServiceEvent
    {
        public string TransactionId { get; }
        public bool Success { get; }
        public PurchaseGrantProcessedEvent(string transactionId, bool success)
        {
            TransactionId = transactionId;
            Success = success;
        }
    }

    public readonly struct PurchaseFailedEvent : IServiceEvent
    {
        public string ProductId { get; }
        public ServiceErrorCode ErrorCode { get; }
        public PurchaseFailedEvent(string productId, ServiceErrorCode errorCode)
        {
            ProductId = productId;
            ErrorCode = errorCode;
        }
    }
}
