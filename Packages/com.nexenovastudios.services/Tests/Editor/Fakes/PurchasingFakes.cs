#nullable enable
#if NEX_SERVICES_IAP
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nexenova.Services.Core;
using Nexenova.Services.Purchasing;

namespace Nexenova.Services.Tests
{
    internal sealed class FakeStoreSdk : IStoreSdk
    {
        public bool IsInitialized { get; private set; }
        public List<IapProductDefinition> InitializedCatalog { get; } = new();
        public ServiceResult<Unit>? InitResultOverride { get; set; }
        public readonly List<string> ConfirmedTransactions = new();

        public event Action<PendingPurchase>? PendingPurchaseDelivered;

        public UniTask<ServiceResult<Unit>> InitializeAsync(IReadOnlyList<IapProductDefinition> catalog, CancellationToken ct)
        {
            if (InitResultOverride is { IsFailure: true })
                return UniTask.FromResult(InitResultOverride.Value);
            InitializedCatalog.AddRange(catalog);
            IsInitialized = true;
            return UniTask.FromResult(ServiceResult.Ok());
        }

        public IReadOnlyList<ProductInfo> GetProducts() =>
            InitializedCatalog.ConvertAll(p => new ProductInfo(p.ProductId, p.ProductType, "$0.99", "USD", p.ProductId));

        public UniTask<ServiceResult<PendingPurchase>> PurchaseAsync(string productId, CancellationToken ct) =>
            UniTask.FromResult(ServiceResult<PendingPurchase>.Success(CreatePending(productId, $"tx_{productId}")));

        public UniTask<ServiceResult<Unit>> RestoreAsync(CancellationToken ct) =>
            UniTask.FromResult(ServiceResult.Ok());

        public PendingPurchase CreatePending(string productId, string transactionId) =>
            new(productId, transactionId, "{\"receipt\":\"fake\"}", "fake",
                confirm: () => ConfirmedTransactions.Add(transactionId));

        public void Redeliver(PendingPurchase pending) => PendingPurchaseDelivered?.Invoke(pending);
    }

    internal sealed class FakeCatalogSource : ICatalogSource
    {
        public List<IapProductDefinition> Catalog { get; } = new();
        public IReadOnlyList<IapProductDefinition> GetCatalog() => Catalog;
    }

    internal sealed class FakeReceiptValidator : IReceiptValidator
    {
        public ServiceResult<Unit> Result { get; set; } = ServiceResult.Ok();
        public int Calls { get; private set; }

        public UniTask<ServiceResult<Unit>> ValidateAsync(PurchaseReceipt receipt, CancellationToken ct = default)
        {
            Calls++;
            return UniTask.FromResult(Result);
        }
    }
}
#endif
