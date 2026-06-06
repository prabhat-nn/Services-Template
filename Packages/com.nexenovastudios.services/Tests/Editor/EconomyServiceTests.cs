#nullable enable
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Nexenova.Services.Core;
using Nexenova.Services.Economy;

namespace Nexenova.Services.Tests
{
    public sealed class EconomyServiceTests
    {
        private FakeEconomySdk _sdk = null!;
        private IEventBus _bus = null!;
        private EconomyService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _sdk = new FakeEconomySdk();
            _sdk.Balances["GEMS"] = 100;
            var logger = TestDoubles.Logger();
            _bus = TestDoubles.Bus(logger);
            _service = new EconomyService(
                _sdk,
                _bus,
                logger,
                TestDoubles.Retry(logger),
                new GrantRateLimiter(new FakeMonotonicClock()),
                new EconomyOptions(maxGrantPerCall: 1000, maxGrantedPerMinute: 2000));
        }

        private async Task InitializeAsync() =>
            Assert.IsTrue((await _service.InitializeAsync(CancellationToken.None)).IsSuccess);

        [Test]
        public async Task AddCurrencyAsync_BeforeInitialize_ReturnsNotInitialized()
        {
            var result = await _service.AddCurrencyAsync("GEMS", 10, new TransactionReason("level_reward"));
            Assert.AreEqual(ServiceErrorCode.NotInitialized, result.Error.Code);
        }

        [Test]
        public async Task AddCurrencyAsync_HappyPath_UpdatesBalanceAndPublishesEvent()
        {
            await InitializeAsync();
            CurrencyBalanceChangedEvent? changed = null;
            _bus.Subscribe<CurrencyBalanceChangedEvent>(e => changed = e);

            var result = await _service.AddCurrencyAsync("GEMS", 50, new TransactionReason("level_reward"));

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(150, result.Value.Amount);
            Assert.AreEqual(100, changed!.Value.OldAmount);
            Assert.AreEqual(150, changed.Value.NewAmount);
            Assert.AreEqual("level_reward", changed.Value.Reason);
        }

        [TestCase(0)]
        [TestCase(-5)]
        public async Task AddCurrencyAsync_NonPositiveAmount_FailsValidation(long amount)
        {
            await InitializeAsync();
            var result = await _service.AddCurrencyAsync("GEMS", amount, new TransactionReason("x"));
            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
            Assert.IsEmpty(_sdk.Increments);
        }

        [Test]
        public async Task AddCurrencyAsync_AbovePerCallCap_FailsValidation()
        {
            await InitializeAsync();
            var result = await _service.AddCurrencyAsync("GEMS", 1001, new TransactionReason("x"));
            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
            Assert.IsEmpty(_sdk.Increments);
        }

        [Test]
        public async Task AddCurrencyAsync_PerMinuteCapReached_FailsValidation()
        {
            await InitializeAsync();
            Assert.IsTrue((await _service.AddCurrencyAsync("GEMS", 1000, new TransactionReason("a"))).IsSuccess);
            Assert.IsTrue((await _service.AddCurrencyAsync("GEMS", 1000, new TransactionReason("b"))).IsSuccess);

            var result = await _service.AddCurrencyAsync("GEMS", 1, new TransactionReason("c"));

            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
        }

        [Test]
        public async Task AddCurrencyAsync_IapReasonPrefix_IsRejectedOnPublicApi()
        {
            await InitializeAsync();
            var result = await _service.AddCurrencyAsync("GEMS", 10, new TransactionReason("iap:tx123"));
            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
            Assert.IsEmpty(_sdk.Increments);
        }

        [Test]
        public async Task AddCurrencyFromIapAsync_BypassesCaps()
        {
            await InitializeAsync();
            var result = await _service.AddCurrencyFromIapAsync("GEMS", 50_000, "tx123");
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(50_100, result.Value.Amount);
        }

        [Test]
        public async Task SpendCurrencyAsync_InsufficientCachedBalance_FailsFastWithoutServerCall()
        {
            await InitializeAsync();
            var result = await _service.SpendCurrencyAsync("GEMS", 500, new TransactionReason("shop"));

            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
            Assert.AreEqual(100, _sdk.Balances["GEMS"], "server must not be hit when the cache already disproves the spend");
        }

        [Test]
        public async Task SpendCurrencyAsync_HappyPath_DecrementsAndPublishes()
        {
            await InitializeAsync();
            CurrencyBalanceChangedEvent? changed = null;
            _bus.Subscribe<CurrencyBalanceChangedEvent>(e => changed = e);

            var result = await _service.SpendCurrencyAsync("GEMS", 40, new TransactionReason("shop"));

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(60, result.Value.Amount);
            Assert.AreEqual(60, changed!.Value.NewAmount);
        }

        [Test]
        public async Task AddInventoryItemAsync_EmptyItemId_FailsValidation()
        {
            await InitializeAsync();
            var result = await _service.AddInventoryItemAsync(" ");
            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
        }
    }
}
