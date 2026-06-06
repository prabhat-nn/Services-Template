#nullable enable
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services
{
    public enum CatalogProductType
    {
        Consumable,
        NonConsumable,
        Subscription,
    }

    public readonly struct ProductInfo
    {
        public string ProductId { get; }
        public CatalogProductType ProductType { get; }
        public string LocalizedPrice { get; }
        public string IsoCurrencyCode { get; }
        public string LocalizedTitle { get; }

        public ProductInfo(string productId, CatalogProductType productType, string localizedPrice, string isoCurrencyCode, string localizedTitle)
        {
            ProductId = productId;
            ProductType = productType;
            LocalizedPrice = localizedPrice;
            IsoCurrencyCode = isoCurrencyCode;
            LocalizedTitle = localizedTitle;
        }
    }

    public readonly struct PurchaseReceipt
    {
        public string ProductId { get; }
        public string TransactionId { get; }
        /// <summary>Raw store receipt. Never log this at Info level.</summary>
        public string Receipt { get; }
        public string Store { get; }

        public PurchaseReceipt(string productId, string transactionId, string receipt, string store)
        {
            ProductId = productId;
            TransactionId = transactionId;
            Receipt = receipt;
            Store = store;
        }
    }

    /// <summary>
    /// Optional server-side receipt validation seam. The default implementation is
    /// local-only (Unity IAP obfuscated tangles); games register their backend validator
    /// to override. Validation failure means the purchase is never granted or confirmed.
    /// </summary>
    public interface IReceiptValidator
    {
        UniTask<ServiceResult<Unit>> ValidateAsync(PurchaseReceipt receipt, CancellationToken ct = default);
    }

    /// <summary>
    /// In-app purchases. One purchase in flight at a time; grants flow through the event
    /// bus and the store transaction is confirmed only after the grant pipeline succeeds,
    /// making grants idempotent across crashes.
    /// </summary>
    public interface IPurchaseService
    {
        /// <summary>False when store init failed or the module is compiled out / disabled.</summary>
        bool IsAvailable { get; }

        UniTask<ServiceResult<IReadOnlyList<ProductInfo>>> GetProductsAsync(CancellationToken ct = default);

        UniTask<ServiceResult<PurchaseReceipt>> PurchaseAsync(string productId, CancellationToken ct = default);

        /// <summary>iOS/macOS only; returns Unsupported elsewhere.</summary>
        UniTask<ServiceResult<Unit>> RestorePurchasesAsync(CancellationToken ct = default);
    }
}
