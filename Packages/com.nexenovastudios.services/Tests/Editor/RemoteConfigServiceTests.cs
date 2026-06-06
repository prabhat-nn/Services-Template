#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Nexenova.Services.Core;
using Nexenova.Services.RemoteConfig;

namespace Nexenova.Services.Tests
{
    public sealed class RemoteConfigServiceTests
    {
        private FakeRemoteConfigSdk _sdk = null!;
        private IEventBus _bus = null!;
        private FakeLogger _logger = null!;
        private RemoteConfigServiceModule _service = null!;

        [SetUp]
        public void SetUp()
        {
            _sdk = new FakeRemoteConfigSdk();
            _logger = TestDoubles.Logger();
            _bus = TestDoubles.Bus(_logger);
            var validator = new ConfigValidator(
                new[] { new ConfigRuleSet("test", new[] { new ConfigRule("lives", 1, 10) }) },
                _logger);
            _service = new RemoteConfigServiceModule(
                _sdk, validator, _bus, _logger, TestDoubles.Retry(_logger), new RemoteConfigOptions("1.2.3"));
        }

        [Test]
        public void Getters_BeforeFetch_ReturnDefaults()
        {
            _sdk.Values["lives"] = 5;

            Assert.IsFalse(_service.IsFetched);
            Assert.AreEqual(3, _service.GetInt("lives", 3));
            Assert.AreEqual("d", _service.GetString("anything", "d"));
        }

        [Test]
        public async Task FetchAsync_Success_SetsIsFetchedAndPublishes()
        {
            var fetched = false;
            _bus.Subscribe<RemoteConfigFetchedEvent>(_ => fetched = true);

            var result = await _service.FetchAsync();

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(_service.IsFetched);
            Assert.IsTrue(fetched);
            Assert.AreEqual("1.2.3", _sdk.LastAppVersion, "appVersion must be sent for release targeting");
        }

        [Test]
        public async Task GetInt_OutOfRange_IsClampedAndLogged()
        {
            _sdk.Values["lives"] = 9999;
            await _service.FetchAsync();

            Assert.AreEqual(10, _service.GetInt("lives", 3));
            Assert.IsNotEmpty(_logger.Warnings);
        }

        [Test]
        public async Task GetInt_UnruledKey_PassesThrough()
        {
            _sdk.Values["coins"] = 123456;
            await _service.FetchAsync();

            Assert.AreEqual(123456, _service.GetInt("coins", 0));
        }

        [Test]
        public async Task InitializeAsync_FetchFailure_StillSucceedsDegraded()
        {
            _sdk.ThrowOnFetch = new System.Exception("boom");

            var result = await _service.InitializeAsync(CancellationToken.None);

            Assert.IsTrue(result.IsSuccess, "config fetch failure must not fail the boot");
            Assert.IsFalse(_service.IsFetched);
        }

        [Test]
        public async Task GetJson_ValidPayload_Deserializes()
        {
            _sdk.Values["catalog"] = "[{\"ProductId\":\"p1\"}]";
            await _service.FetchAsync();

            var result = _service.GetJson<List<IapProductDefinition>>("catalog");

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("p1", result.Value[0].ProductId);
        }

        [Test]
        public async Task GetJson_InvalidPayload_FailsValidation()
        {
            _sdk.Values["catalog"] = "{nope";
            await _service.FetchAsync();

            var result = _service.GetJson<List<IapProductDefinition>>("catalog");

            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
        }

        [Test]
        public void GetJson_MissingKey_ReturnsNotFound()
        {
            var result = _service.GetJson<List<IapProductDefinition>>("nope");
            Assert.AreEqual(ServiceErrorCode.NotFound, result.Error.Code);
        }
    }
}
