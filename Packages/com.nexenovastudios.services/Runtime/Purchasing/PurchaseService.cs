#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nexenova.Services.Core;

namespace Nexenova.Services.Purchasing
{
    /// <summary>
    /// In-app purchases over Unity IAP with a crash-safe grant pipeline:
    /// validate receipt → publish PurchaseCompletedEvent → await grant ack →
    /// confirm with the store. Unconfirmed transactions are redelivered by the
    /// store on the next launch and deduped by transaction id downstream.
    /// </summary>
    internal sealed class PurchaseService : IPurchaseService, IServiceModule, IDisposable
    {
        private const string Tag = "Purchasing";
        private static readonly TimeSpan GrantAckTimeout = TimeSpan.FromSeconds(30);

        private readonly IStoreSdk _store;
        private readonly ICatalogSource _catalogSource;
        private readonly IReceiptValidator _validator;
        private readonly IEventBus _events;
        private readonly IServiceLogger _logger;
        private readonly PurchasingOptions _options;

        private readonly Dictionary<string, string> _grantsByProduct = new();
        private bool _purchaseInFlight;
        private bool _initialized;

        public string ModuleName => "Purchasing";
        public InitializationStage Stage => InitializationStage.Monetization;
        public bool IsRequired => false;

        public bool IsAvailable => _initialized && _store.IsInitialized;

        public PurchaseService(
            IStoreSdk store,
            ICatalogSource catalogSource,
            IReceiptValidator validator,
            IEventBus events,
            IServiceLogger logger,
            PurchasingOptions options)
        {
            _store = store;
            _catalogSource = catalogSource;
            _validator = validator;
            _events = events;
            _logger = logger;
            _options = options;

            _store.PendingPurchaseDelivered += OnRedeliveredPurchase;
        }

        public async UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct)
        {
            var catalog = _catalogSource.GetCatalog();
            if (catalog.Count == 0)
            {
                _logger.Warning(Tag, "IAP catalog is empty — purchasing disabled.");
                return ServiceResult.Fail(ServiceError.Validation("IAP catalog is empty."));
            }

            foreach (var product in catalog)
                _grantsByProduct[product.ProductId] = product.GrantsJson;

            var result = await _store.InitializeAsync(catalog, ct);
            _initialized = result.IsSuccess;
            return result;
        }

        public UniTask<ServiceResult<IReadOnlyList<ProductInfo>>> GetProductsAsync(CancellationToken ct = default)
        {
            if (!IsAvailable)
                return UniTask.FromResult(ServiceResult<IReadOnlyList<ProductInfo>>.Failure(ServiceError.NotInitialized(ModuleName)));
            return UniTask.FromResult(ServiceResult<IReadOnlyList<ProductInfo>>.Success(_store.GetProducts()));
        }

        public async UniTask<ServiceResult<PurchaseReceipt>> PurchaseAsync(string productId, CancellationToken ct = default)
        {
            if (!IsAvailable)
                return ServiceResult<PurchaseReceipt>.Failure(ServiceError.NotInitialized(ModuleName));
            if (string.IsNullOrWhiteSpace(productId))
                return ServiceResult<PurchaseReceipt>.Failure(ServiceError.Validation("productId must not be empty."));
            if (_purchaseInFlight)
                return ServiceResult<PurchaseReceipt>.Failure(ServiceError.AlreadyInProgress("Purchase"));

            _purchaseInFlight = true;
            try
            {
                var pendingResult = await _store.PurchaseAsync(productId, ct);
                if (pendingResult.IsFailure)
                {
                    _events.Publish(new PurchaseFailedEvent(productId, pendingResult.Error.Code));
                    return ServiceResult<PurchaseReceipt>.Failure(pendingResult.Error);
                }

                var processed = await ProcessPendingPurchaseAsync(pendingResult.Value, ct);
                return processed.IsSuccess
                    ? ServiceResult<PurchaseReceipt>.Success(pendingResult.Value.ToReceipt())
                    : ServiceResult<PurchaseReceipt>.Failure(processed.Error);
            }
            finally
            {
                _purchaseInFlight = false;
            }
        }

        public UniTask<ServiceResult<Unit>> RestorePurchasesAsync(CancellationToken ct = default)
        {
            if (!IsAvailable)
                return UniTask.FromResult(ServiceResult.Fail(ServiceError.NotInitialized(ModuleName)));
            return _store.RestoreAsync(ct);
        }

        // ── pipeline ───────────────────────────────────────────────────────

        private async UniTask<ServiceResult<Unit>> ProcessPendingPurchaseAsync(PendingPurchase pending, CancellationToken ct)
        {
            // 1. Receipt validation — failure means no grant, no confirmation, ever.
            var validation = await _validator.ValidateAsync(pending.ToReceipt(), ct);
            if (validation.IsFailure)
            {
                _logger.Warning(Tag, $"Receipt for '{pending.ProductId}' (tx {Truncate(pending.TransactionId)}) failed validation — not granting.");
                _events.Publish(new PurchaseFailedEvent(pending.ProductId, ServiceErrorCode.Unauthorized));
                return ServiceResult.Fail(ServiceErrorCode.Unauthorized, "Receipt validation failed.");
            }

            var grantsJson = _grantsByProduct.TryGetValue(pending.ProductId, out var grants) ? grants : "[]";

            // 2. Hand off to the grant pipeline (Economy subscribes via the event bus).
            if (!_options.AwaitGrantProcessing)
            {
                _events.Publish(new PurchaseCompletedEvent(pending.ProductId, pending.TransactionId, grantsJson));
                pending.Confirm();
                return ServiceResult.Ok();
            }

            var ackTcs = new UniTaskCompletionSource<bool>();
            using var subscription = _events.Subscribe<PurchaseGrantProcessedEvent>(evt =>
            {
                if (evt.TransactionId == pending.TransactionId)
                    ackTcs.TrySetResult(evt.Success);
            });

            _events.Publish(new PurchaseCompletedEvent(pending.ProductId, pending.TransactionId, grantsJson));

            bool granted;
            try
            {
                granted = await ackTcs.Task.Timeout(GrantAckTimeout);
            }
            catch (TimeoutException)
            {
                _logger.Warning(Tag, $"Grant ack for tx {Truncate(pending.TransactionId)} timed out — leaving unconfirmed for redelivery.");
                return ServiceResult.Fail(ServiceErrorCode.Timeout, "Purchase grants did not complete; they will retry on next launch.");
            }

            if (!granted)
            {
                _logger.Warning(Tag, $"Grants failed for tx {Truncate(pending.TransactionId)} — leaving unconfirmed for redelivery.");
                return ServiceResult.Fail(ServiceErrorCode.ProviderError, "Purchase grants failed; they will retry on next launch.");
            }

            // 3. Grants applied — finalize with the store.
            pending.Confirm();
            _logger.Info(Tag, $"Purchase '{pending.ProductId}' completed and confirmed.");
            return ServiceResult.Ok();
        }

        private void OnRedeliveredPurchase(PendingPurchase pending)
        {
            UniTask.Void(async () =>
            {
                var result = await ProcessPendingPurchaseAsync(pending, CancellationToken.None);
                if (result.IsFailure)
                    _logger.Warning(Tag, $"Redelivered transaction for '{pending.ProductId}' not completed ({result.Error.Code}).");
            });
        }

        /// <summary>Transaction ids are quasi-receipt data — log a prefix only.</summary>
        private static string Truncate(string value) =>
            value.Length <= 8 ? value : value.Substring(0, 8) + "…";

        public void Dispose() => _store.PendingPurchaseDelivered -= OnRedeliveredPurchase;
    }
}
