#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace Nexenova.Services.Purchasing
{
    /// <summary>
    /// Bridges Unity IAP's IDetailedStoreListener callbacks into UniTask. Every
    /// ProcessPurchase returns Pending; the PurchaseService confirms after grants.
    /// </summary>
    internal sealed class UnityIapStoreSdk : IStoreSdk, IDetailedStoreListener
    {
        private const string Tag = "Purchasing";

        private readonly IServiceLogger _logger;

        private IStoreController? _controller;
        private IExtensionProvider? _extensions;

        private UniTaskCompletionSource<ServiceResult<Unit>>? _initTcs;
        private UniTaskCompletionSource<ServiceResult<PendingPurchase>>? _purchaseTcs;
        private string? _purchasingProductId;

        public bool IsInitialized => _controller != null;

        public event Action<PendingPurchase>? PendingPurchaseDelivered;

        public UnityIapStoreSdk(IServiceLogger logger)
        {
            _logger = logger;
        }

        public UniTask<ServiceResult<Unit>> InitializeAsync(IReadOnlyList<IapProductDefinition> catalog, CancellationToken ct)
        {
            if (IsInitialized)
                return UniTask.FromResult(ServiceResult.Ok());
            if (_initTcs != null)
                return _initTcs.Task.AttachExternalCancellation(ct);

            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            foreach (var product in catalog)
                builder.AddProduct(product.ProductId, MapType(product.ProductType));

            _initTcs = new UniTaskCompletionSource<ServiceResult<Unit>>();
            UnityPurchasing.Initialize(this, builder);
            return _initTcs.Task.AttachExternalCancellation(ct);
        }

        public IReadOnlyList<ProductInfo> GetProducts()
        {
            if (_controller == null)
                return Array.Empty<ProductInfo>();

            return _controller.products.all
                .Where(p => p.availableToPurchase)
                .Select(p => new ProductInfo(
                    p.definition.id,
                    MapType(p.definition.type),
                    p.metadata.localizedPriceString,
                    p.metadata.isoCurrencyCode,
                    p.metadata.localizedTitle))
                .ToList();
        }

        public UniTask<ServiceResult<PendingPurchase>> PurchaseAsync(string productId, CancellationToken ct)
        {
            if (_controller == null)
                return UniTask.FromResult(ServiceResult<PendingPurchase>.Failure(ServiceError.NotInitialized("Purchasing")));
            if (_purchaseTcs != null)
                return UniTask.FromResult(ServiceResult<PendingPurchase>.Failure(ServiceError.AlreadyInProgress("Purchase")));

            var product = _controller.products.WithID(productId);
            if (product == null || !product.availableToPurchase)
                return UniTask.FromResult(ServiceResult<PendingPurchase>.Failure(new ServiceError(
                    ServiceErrorCode.NotFound, $"Product '{productId}' is unknown or unavailable.")));

            _purchaseTcs = new UniTaskCompletionSource<ServiceResult<PendingPurchase>>();
            _purchasingProductId = productId;
            _controller.InitiatePurchase(product);
            return _purchaseTcs.Task.AttachExternalCancellation(ct);
        }

        public UniTask<ServiceResult<Unit>> RestoreAsync(CancellationToken ct)
        {
#if UNITY_IOS || UNITY_STANDALONE_OSX
            if (_extensions == null)
                return UniTask.FromResult(ServiceResult.Fail(ServiceError.NotInitialized("Purchasing")));

            var tcs = new UniTaskCompletionSource<ServiceResult<Unit>>();
            _extensions.GetExtension<IAppleExtensions>().RestoreTransactions((success, error) =>
                tcs.TrySetResult(success
                    ? ServiceResult.Ok()
                    : ServiceResult.Fail(ServiceErrorCode.ProviderError, $"Restore failed: {error}")));
            return tcs.Task.AttachExternalCancellation(ct);
#else
            return UniTask.FromResult(ServiceResult.Fail(ServiceError.Unsupported(
                "RestorePurchases is only required on Apple platforms; Google Play restores automatically.")));
#endif
        }

        // ── IDetailedStoreListener ─────────────────────────────────────────

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller = controller;
            _extensions = extensions;
            _initTcs?.TrySetResult(ServiceResult.Ok());
        }

        public void OnInitializeFailed(InitializationFailureReason error) => OnInitializeFailed(error, null);

        public void OnInitializeFailed(InitializationFailureReason error, string? message)
        {
            var code = error == InitializationFailureReason.PurchasingUnavailable
                ? ServiceErrorCode.Unsupported
                : ServiceErrorCode.ProviderError;
            _initTcs?.TrySetResult(ServiceResult.Fail(code, $"Store initialization failed: {error} {message}"));
            _initTcs = null;
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            var product = args.purchasedProduct;
            var pending = new PendingPurchase(
                product.definition.id,
                product.transactionID,
                product.receipt ?? string.Empty,
                MapStoreName(product),
                confirm: () => _controller?.ConfirmPendingPurchase(product));

            if (_purchaseTcs != null && product.definition.id == _purchasingProductId)
            {
                var tcs = _purchaseTcs;
                ClearActivePurchase();
                tcs.TrySetResult(ServiceResult<PendingPurchase>.Success(pending));
            }
            else
            {
                // Crash recovery / restore / deferred — delivered outside an active call.
                _logger.Info(Tag, $"Redelivered transaction for '{pending.ProductId}'.");
                PendingPurchaseDelivered?.Invoke(pending);
            }

            // Always Pending: confirmation happens only after the grant pipeline succeeds.
            return PurchaseProcessingResult.Pending;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription) =>
            OnPurchaseFailed(product, failureDescription.reason);

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            if (_purchaseTcs == null || product.definition.id != _purchasingProductId)
            {
                _logger.Warning(Tag, $"Purchase of '{product.definition.id}' failed outside an active call: {failureReason}.");
                return;
            }

            var tcs = _purchaseTcs;
            ClearActivePurchase();
            tcs.TrySetResult(ServiceResult<PendingPurchase>.Failure(MapFailure(failureReason)));
        }

        // ── mapping ────────────────────────────────────────────────────────

        private void ClearActivePurchase()
        {
            _purchaseTcs = null;
            _purchasingProductId = null;
        }

        private static ServiceError MapFailure(PurchaseFailureReason reason) => reason switch
        {
            PurchaseFailureReason.UserCancelled => new ServiceError(ServiceErrorCode.Cancelled, "Purchase cancelled by the player."),
            PurchaseFailureReason.DuplicateTransaction => new ServiceError(ServiceErrorCode.Conflict, "Duplicate transaction."),
            PurchaseFailureReason.SignatureInvalid => new ServiceError(ServiceErrorCode.Unauthorized, "Receipt signature invalid."),
            PurchaseFailureReason.ExistingPurchasePending => new ServiceError(ServiceErrorCode.AlreadyInProgress, "Another purchase is pending."),
            PurchaseFailureReason.PurchasingUnavailable => new ServiceError(ServiceErrorCode.Unsupported, "Purchasing unavailable on this device."),
            PurchaseFailureReason.ProductUnavailable => new ServiceError(ServiceErrorCode.NotFound, "Product unavailable in the store."),
            PurchaseFailureReason.PaymentDeclined => new ServiceError(ServiceErrorCode.ProviderError, "Payment declined."),
            _ => new ServiceError(ServiceErrorCode.Unknown, $"Purchase failed: {reason}."),
        };

        private static ProductType MapType(CatalogProductType type) => type switch
        {
            CatalogProductType.NonConsumable => ProductType.NonConsumable,
            CatalogProductType.Subscription => ProductType.Subscription,
            _ => ProductType.Consumable,
        };

        private static CatalogProductType MapType(ProductType type) => type switch
        {
            ProductType.NonConsumable => CatalogProductType.NonConsumable,
            ProductType.Subscription => CatalogProductType.Subscription,
            _ => CatalogProductType.Consumable,
        };

        private static string MapStoreName(Product product)
        {
#if UNITY_ANDROID
            return "GooglePlay";
#elif UNITY_IOS
            return "AppleAppStore";
#else
            return "fake";
#endif
        }
    }
}
