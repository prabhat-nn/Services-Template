#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services.Purchasing
{
    /// <summary>
    /// A store transaction delivered by Unity IAP and held in Pending state.
    /// <see cref="Confirm"/> finalizes it with the store — call only after the
    /// grant pipeline succeeded.
    /// </summary>
    internal sealed class PendingPurchase
    {
        public string ProductId { get; }
        public string TransactionId { get; }
        public string Receipt { get; }
        public string Store { get; }
        public Action Confirm { get; }

        public PendingPurchase(string productId, string transactionId, string receipt, string store, Action confirm)
        {
            ProductId = productId;
            TransactionId = transactionId;
            Receipt = receipt;
            Store = store;
            Confirm = confirm;
        }

        public PurchaseReceipt ToReceipt() => new(ProductId, TransactionId, Receipt, Store);
    }

    /// <summary>
    /// Adapter over Unity IAP's callback model (testability seam). All purchases are
    /// processed as Pending; confirmation is the service's responsibility.
    /// </summary>
    internal interface IStoreSdk
    {
        bool IsInitialized { get; }

        UniTask<ServiceResult<Unit>> InitializeAsync(IReadOnlyList<IapProductDefinition> catalog, CancellationToken ct);

        IReadOnlyList<ProductInfo> GetProducts();

        UniTask<ServiceResult<PendingPurchase>> PurchaseAsync(string productId, CancellationToken ct);

        /// <summary>iOS/macOS only.</summary>
        UniTask<ServiceResult<Unit>> RestoreAsync(CancellationToken ct);

        /// <summary>
        /// Raised for transactions delivered outside an active purchase call:
        /// crash recovery (unconfirmed transactions), restores, deferred purchases.
        /// </summary>
        event Action<PendingPurchase> PendingPurchaseDelivered;
    }
}
