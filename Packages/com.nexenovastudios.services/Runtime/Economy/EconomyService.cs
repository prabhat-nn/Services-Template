#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nexenova.Services.Core;

namespace Nexenova.Services.Economy
{
    /// <summary>
    /// Player currencies and inventory with abuse prevention: positive-amount validation,
    /// per-call and per-minute grant caps, audit reasons, and an internal-only pipeline
    /// for receipt-validated IAP grants. The server remains the authority; the local
    /// balance cache is read-through and re-synced on conflicts.
    /// </summary>
    internal sealed class EconomyService : IEconomyService, IServiceModule, IDynamicEconomyCaps
    {
        private const string Tag = "Economy";

        private readonly IEconomySdk _sdk;
        private readonly IEventBus _events;
        private readonly IServiceLogger _logger;
        private readonly RetryPolicy _retry;
        private readonly GrantRateLimiter _rateLimiter;

        private readonly Dictionary<string, long> _balanceCache = new();
        private long _maxGrantPerCall;
        private long _maxGrantedPerMinute;
        private bool _initialized;

        public string ModuleName => "Economy";
        public InitializationStage Stage => InitializationStage.Data;
        public bool IsRequired => false;

        public EconomyService(
            IEconomySdk sdk,
            IEventBus events,
            IServiceLogger logger,
            RetryPolicy retry,
            GrantRateLimiter rateLimiter,
            EconomyOptions options)
        {
            _sdk = sdk;
            _events = events;
            _logger = logger;
            _retry = retry;
            _rateLimiter = rateLimiter;
            _maxGrantPerCall = options.MaxGrantPerCall;
            _maxGrantedPerMinute = options.MaxGrantedPerMinute;
        }

        public async UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct)
        {
            var balances = await FetchBalancesAsync(ct);
            if (balances.IsFailure)
                return ServiceResult.Fail(balances.Error);

            _initialized = true;
            return ServiceResult.Ok();
        }

        void IDynamicEconomyCaps.UpdateCaps(long maxGrantPerCall, long maxGrantedPerMinute)
        {
            _maxGrantPerCall = maxGrantPerCall;
            _maxGrantedPerMinute = maxGrantedPerMinute;
            _logger.Info(Tag, $"Grant caps updated: perCall={maxGrantPerCall}, perMinute={maxGrantedPerMinute}.");
        }

        public async UniTask<ServiceResult<IReadOnlyList<CurrencyBalance>>> GetBalancesAsync(CancellationToken ct = default)
        {
            if (!_initialized)
                return ServiceResult<IReadOnlyList<CurrencyBalance>>.Failure(ServiceError.NotInitialized(ModuleName));
            return await FetchBalancesAsync(ct);
        }

        public UniTask<ServiceResult<CurrencyBalance>> AddCurrencyAsync(
            string currencyId, long amount, TransactionReason reason, CancellationToken ct = default)
        {
            var guard = ValidateMutation(currencyId, amount);
            if (guard != null)
                return UniTask.FromResult(ServiceResult<CurrencyBalance>.Failure(guard));

            if (reason.Source.StartsWith(TransactionReason.IapPrefix, StringComparison.Ordinal))
                return UniTask.FromResult(ServiceResult<CurrencyBalance>.Failure(ServiceError.Validation(
                    $"Reason prefix '{TransactionReason.IapPrefix}' is reserved for the IAP grant pipeline.")));

            if (amount > _maxGrantPerCall)
            {
                _logger.Warning(Tag, $"Grant of {amount} '{currencyId}' rejected: exceeds per-call cap {_maxGrantPerCall} (reason: {reason}).");
                return UniTask.FromResult(ServiceResult<CurrencyBalance>.Failure(ServiceError.Validation(
                    $"Grant amount {amount} exceeds the per-call cap of {_maxGrantPerCall}.")));
            }

            if (!_rateLimiter.TryReserve(amount, _maxGrantedPerMinute))
            {
                _logger.Warning(Tag, $"Grant of {amount} '{currencyId}' rejected: per-minute cap {_maxGrantedPerMinute} reached (reason: {reason}).");
                return UniTask.FromResult(ServiceResult<CurrencyBalance>.Failure(ServiceError.Validation(
                    "Per-minute grant cap reached.")));
            }

            return GrantCoreAsync(currencyId, amount, reason, ct);
        }

        /// <summary>
        /// Receipt-validated IAP grants only (called by the grant processor). Bypasses the
        /// public caps — the store receipt is the gate — but still validates basics.
        /// </summary>
        internal UniTask<ServiceResult<CurrencyBalance>> AddCurrencyFromIapAsync(
            string currencyId, long amount, string transactionId, CancellationToken ct = default) =>
            GrantCoreAsync(currencyId, amount, new TransactionReason($"{TransactionReason.IapPrefix}{transactionId}"), ct);

        public async UniTask<ServiceResult<CurrencyBalance>> SpendCurrencyAsync(
            string currencyId, long amount, TransactionReason reason, CancellationToken ct = default)
        {
            var guard = ValidateMutation(currencyId, amount);
            if (guard != null)
                return ServiceResult<CurrencyBalance>.Failure(guard);

            // Fail fast against the cache; the server still enforces the real balance.
            if (_balanceCache.TryGetValue(currencyId, out var cached) && cached < amount)
                return ServiceResult<CurrencyBalance>.Failure(ServiceError.Validation(
                    $"Insufficient '{currencyId}': have {cached}, need {amount}."));

            var result = await _retry.ExecuteAsync<CurrencyBalance>($"Economy.Spend({currencyId})", async token =>
            {
                try
                {
                    var balance = await _sdk.DecrementBalanceAsync(currencyId, (int)amount, token);
                    return ServiceResult<CurrencyBalance>.Success(balance);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = EconomyErrorMapper.Map(ex);
                    _logger.Error(Tag, $"Spend of {amount} '{currencyId}' failed: {error}", ex);
                    return ServiceResult<CurrencyBalance>.Failure(error);
                }
            }, ct);

            if (result.IsSuccess)
            {
                UpdateCache(result.Value, reason.Source);
            }
            else if (result.Error.Code == ServiceErrorCode.Validation)
            {
                // Server says the cache lied (tampering or stale) — re-sync.
                await FetchBalancesAsync(ct);
            }

            return result;
        }

        public async UniTask<ServiceResult<IReadOnlyList<InventoryItem>>> GetInventoryAsync(CancellationToken ct = default)
        {
            if (!_initialized)
                return ServiceResult<IReadOnlyList<InventoryItem>>.Failure(ServiceError.NotInitialized(ModuleName));

            return await _retry.ExecuteAsync<IReadOnlyList<InventoryItem>>("Economy.GetInventory", async token =>
            {
                try
                {
                    var items = await _sdk.GetInventoryAsync(token);
                    return ServiceResult<IReadOnlyList<InventoryItem>>.Success(items);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = EconomyErrorMapper.Map(ex);
                    _logger.Error(Tag, $"Inventory fetch failed: {error}", ex);
                    return ServiceResult<IReadOnlyList<InventoryItem>>.Failure(error);
                }
            }, ct);
        }

        public async UniTask<ServiceResult<InventoryItem>> AddInventoryItemAsync(string itemId, CancellationToken ct = default)
        {
            if (!_initialized)
                return ServiceResult<InventoryItem>.Failure(ServiceError.NotInitialized(ModuleName));
            if (string.IsNullOrWhiteSpace(itemId))
                return ServiceResult<InventoryItem>.Failure(ServiceError.Validation("itemId must not be empty."));

            return await _retry.ExecuteAsync<InventoryItem>($"Economy.AddItem({itemId})", async token =>
            {
                try
                {
                    var item = await _sdk.AddInventoryItemAsync(itemId, token);
                    return ServiceResult<InventoryItem>.Success(item);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = EconomyErrorMapper.Map(ex);
                    _logger.Error(Tag, $"Add inventory item '{itemId}' failed: {error}", ex);
                    return ServiceResult<InventoryItem>.Failure(error);
                }
            }, ct);
        }

        // ── internals ──────────────────────────────────────────────────────

        private async UniTask<ServiceResult<CurrencyBalance>> GrantCoreAsync(
            string currencyId, long amount, TransactionReason reason, CancellationToken ct)
        {
            var guard = ValidateMutation(currencyId, amount);
            if (guard != null)
                return ServiceResult<CurrencyBalance>.Failure(guard);

            var result = await _retry.ExecuteAsync<CurrencyBalance>($"Economy.Add({currencyId})", async token =>
            {
                try
                {
                    var balance = await _sdk.IncrementBalanceAsync(currencyId, (int)amount, token);
                    return ServiceResult<CurrencyBalance>.Success(balance);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = EconomyErrorMapper.Map(ex);
                    _logger.Error(Tag, $"Grant of {amount} '{currencyId}' failed: {error}", ex);
                    return ServiceResult<CurrencyBalance>.Failure(error);
                }
            }, ct);

            if (result.IsSuccess)
                UpdateCache(result.Value, reason.Source);
            return result;
        }

        private ServiceError? ValidateMutation(string currencyId, long amount)
        {
            if (!_initialized)
                return ServiceError.NotInitialized(ModuleName);
            if (string.IsNullOrWhiteSpace(currencyId))
                return ServiceError.Validation("currencyId must not be empty.");
            if (amount <= 0)
                return ServiceError.Validation($"Amount must be positive (got {amount}).");
            if (amount > int.MaxValue)
                return ServiceError.Validation($"Amount {amount} exceeds the maximum supported per call ({int.MaxValue}).");
            return null;
        }

        private async UniTask<ServiceResult<IReadOnlyList<CurrencyBalance>>> FetchBalancesAsync(CancellationToken ct)
        {
            var result = await _retry.ExecuteAsync<IReadOnlyList<CurrencyBalance>>("Economy.GetBalances", async token =>
            {
                try
                {
                    var balances = await _sdk.GetBalancesAsync(token);
                    return ServiceResult<IReadOnlyList<CurrencyBalance>>.Success(balances);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = EconomyErrorMapper.Map(ex);
                    _logger.Error(Tag, $"Balance fetch failed: {error}", ex);
                    return ServiceResult<IReadOnlyList<CurrencyBalance>>.Failure(error);
                }
            }, ct);

            if (result.IsSuccess)
            {
                foreach (var balance in result.Value)
                    _balanceCache[balance.CurrencyId] = balance.Amount;
            }

            return result;
        }

        private void UpdateCache(CurrencyBalance balance, string reason)
        {
            _balanceCache.TryGetValue(balance.CurrencyId, out var old);
            _balanceCache[balance.CurrencyId] = balance.Amount;
            _events.Publish(new CurrencyBalanceChangedEvent(balance.CurrencyId, old, balance.Amount, reason));
        }
    }
}
