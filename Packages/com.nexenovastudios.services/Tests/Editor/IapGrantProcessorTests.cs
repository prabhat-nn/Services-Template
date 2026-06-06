#nullable enable
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Nexenova.Services.Core;
using Nexenova.Services.Economy;

namespace Nexenova.Services.Tests
{
    public sealed class IapGrantProcessorTests
    {
        private FakeEconomySdk _sdk = null!;
        private FakeProcessedTransactionStore _store = null!;
        private IEventBus _bus = null!;
        private IapGrantProcessor _processor = null!;

        [SetUp]
        public async Task SetUp()
        {
            _sdk = new FakeEconomySdk();
            _sdk.Balances["GEMS"] = 0;
            var logger = TestDoubles.Logger();
            _bus = TestDoubles.Bus(logger);
            var economy = new EconomyService(
                _sdk, _bus, logger, TestDoubles.Retry(logger),
                new GrantRateLimiter(new FakeMonotonicClock()),
                new EconomyOptions(maxGrantPerCall: 1000, maxGrantedPerMinute: 2000));
            Assert.IsTrue((await economy.InitializeAsync(CancellationToken.None)).IsSuccess);

            _store = new FakeProcessedTransactionStore();
            _processor = new IapGrantProcessor(economy, _store, _bus, logger);
            _processor.Start();
        }

        [TearDown]
        public void TearDown() => _processor.Dispose();

        [Test]
        public void PurchaseCompleted_AppliesGrants_AndAcksSuccess()
        {
            PurchaseGrantProcessedEvent? ack = null;
            _bus.Subscribe<PurchaseGrantProcessedEvent>(e => ack = e);

            _bus.Publish(new PurchaseCompletedEvent("gem_pack", "tx1", "[{\"CurrencyId\":\"GEMS\",\"Amount\":500}]"));

            Assert.IsTrue(ack!.Value.Success);
            Assert.AreEqual("tx1", ack.Value.TransactionId);
            Assert.AreEqual(500, _sdk.Balances["GEMS"]);
            Assert.IsTrue(_store.Contains("tx1"));
        }

        [Test]
        public void PurchaseCompleted_DuplicateTransaction_AcksWithoutRegranting()
        {
            _store.Add("tx1");
            PurchaseGrantProcessedEvent? ack = null;
            _bus.Subscribe<PurchaseGrantProcessedEvent>(e => ack = e);

            _bus.Publish(new PurchaseCompletedEvent("gem_pack", "tx1", "[{\"CurrencyId\":\"GEMS\",\"Amount\":500}]"));

            Assert.IsTrue(ack!.Value.Success);
            Assert.AreEqual(0, _sdk.Balances["GEMS"], "duplicate must not grant again");
        }

        [Test]
        public void PurchaseCompleted_MalformedGrantsJson_AcksFailure()
        {
            PurchaseGrantProcessedEvent? ack = null;
            _bus.Subscribe<PurchaseGrantProcessedEvent>(e => ack = e);

            _bus.Publish(new PurchaseCompletedEvent("gem_pack", "tx1", "{not json"));

            Assert.IsFalse(ack!.Value.Success);
            Assert.IsFalse(_store.Contains("tx1"), "failed transactions must stay unprocessed for redelivery");
        }

        [Test]
        public void PurchaseCompleted_NonPositiveGrant_AcksFailure()
        {
            PurchaseGrantProcessedEvent? ack = null;
            _bus.Subscribe<PurchaseGrantProcessedEvent>(e => ack = e);

            _bus.Publish(new PurchaseCompletedEvent("gem_pack", "tx1", "[{\"CurrencyId\":\"GEMS\",\"Amount\":-5}]"));

            Assert.IsFalse(ack!.Value.Success);
            Assert.AreEqual(0, _sdk.Balances["GEMS"]);
        }
    }
}
