#nullable enable
#if NEX_SERVICES_IAP
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Nexenova.Services.Core;
using Nexenova.Services.Purchasing;

namespace Nexenova.Services.Tests
{
    public sealed class PurchaseServiceTests
    {
        private FakeStoreSdk _store = null!;
        private FakeCatalogSource _catalog = null!;
        private FakeReceiptValidator _validator = null!;
        private IEventBus _bus = null!;
        private PurchaseService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _store = new FakeStoreSdk();
            _catalog = new FakeCatalogSource();
            _catalog.Catalog.Add(new IapProductDefinition
            {
                ProductId = "gem_pack",
                ProductType = CatalogProductType.Consumable,
                GrantsJson = "[{\"CurrencyId\":\"GEMS\",\"Amount\":100}]",
            });
            _validator = new FakeReceiptValidator();
            var logger = TestDoubles.Logger();
            _bus = TestDoubles.Bus(logger);
            _service = new PurchaseService(
                _store, _catalog, _validator, _bus, logger,
                new PurchasingOptions(new List<IapProductDefinition>(), useLocalReceiptValidation: false, awaitGrantProcessing: true));
        }

        [TearDown]
        public void TearDown() => _service.Dispose();

        private async Task InitializeAsync() =>
            Assert.IsTrue((await _service.InitializeAsync(CancellationToken.None)).IsSuccess);

        /// <summary>Simulates the Economy grant processor acknowledging every grant.</summary>
        private void AutoAckGrants(bool success = true) =>
            _bus.Subscribe<PurchaseCompletedEvent>(e =>
                _bus.Publish(new PurchaseGrantProcessedEvent(e.TransactionId, success)));

        [Test]
        public async Task InitializeAsync_EmptyCatalog_Fails()
        {
            _catalog.Catalog.Clear();
            var result = await _service.InitializeAsync(CancellationToken.None);
            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
            Assert.IsFalse(_service.IsAvailable);
        }

        [Test]
        public async Task PurchaseAsync_BeforeInitialize_ReturnsNotInitialized()
        {
            var result = await _service.PurchaseAsync("gem_pack");
            Assert.AreEqual(ServiceErrorCode.NotInitialized, result.Error.Code);
        }

        [Test]
        public async Task PurchaseAsync_HappyPath_ValidatesGrantsAndConfirms()
        {
            await InitializeAsync();
            AutoAckGrants();
            PurchaseCompletedEvent? completed = null;
            _bus.Subscribe<PurchaseCompletedEvent>(e => completed = e);

            var result = await _service.PurchaseAsync("gem_pack");

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, _validator.Calls);
            Assert.AreEqual("gem_pack", completed!.Value.ProductId);
            StringAssert.Contains("GEMS", completed.Value.GrantsJson);
            CollectionAssert.Contains(_store.ConfirmedTransactions, "tx_gem_pack");
        }

        [Test]
        public async Task PurchaseAsync_InvalidReceipt_NeverGrantsOrConfirms()
        {
            await InitializeAsync();
            AutoAckGrants();
            _validator.Result = ServiceResult.Fail(ServiceErrorCode.Unauthorized, "bad receipt");
            PurchaseFailedEvent? failed = null;
            var completedPublished = false;
            _bus.Subscribe<PurchaseFailedEvent>(e => failed = e);
            _bus.Subscribe<PurchaseCompletedEvent>(_ => completedPublished = true);

            var result = await _service.PurchaseAsync("gem_pack");

            Assert.AreEqual(ServiceErrorCode.Unauthorized, result.Error.Code);
            Assert.IsFalse(completedPublished, "invalid receipts must never reach the grant pipeline");
            Assert.IsEmpty(_store.ConfirmedTransactions, "invalid receipts must never be confirmed");
            Assert.AreEqual(ServiceErrorCode.Unauthorized, failed!.Value.ErrorCode);
        }

        [Test]
        public async Task PurchaseAsync_GrantFailure_LeavesTransactionUnconfirmed()
        {
            await InitializeAsync();
            AutoAckGrants(success: false);

            var result = await _service.PurchaseAsync("gem_pack");

            Assert.IsTrue(result.IsFailure);
            Assert.IsEmpty(_store.ConfirmedTransactions, "failed grants must stay unconfirmed for redelivery");
        }

        [Test]
        public async Task RedeliveredTransaction_IsProcessedAndConfirmed()
        {
            await InitializeAsync();
            AutoAckGrants();

            _store.Redeliver(_store.CreatePending("gem_pack", "tx_recovered"));

            CollectionAssert.Contains(_store.ConfirmedTransactions, "tx_recovered");
        }

        [Test]
        public async Task GetProductsAsync_ReturnsCatalogProducts()
        {
            await InitializeAsync();
            var result = await _service.GetProductsAsync();

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, result.Value.Count);
            Assert.AreEqual("gem_pack", result.Value[0].ProductId);
        }
    }
}
#endif
