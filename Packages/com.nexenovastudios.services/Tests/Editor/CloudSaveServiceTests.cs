#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Nexenova.Services.CloudSave;
using Nexenova.Services.Core;
using Unity.Services.Core;

namespace Nexenova.Services.Tests
{
    public sealed class CloudSaveServiceTests
    {
        private sealed class PlayerData
        {
            public int Level { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        private FakeCloudSaveSdk _sdk = null!;
        private FakeLocalSaveCache _cache = null!;
        private IEventBus _bus = null!;
        private CloudSaveService _service = null!;

        [SetUp]
        public async Task SetUp()
        {
            _sdk = new FakeCloudSaveSdk();
            _cache = new FakeLocalSaveCache();
            var logger = TestDoubles.Logger();
            _bus = TestDoubles.Bus(logger);
            _service = new CloudSaveService(
                _sdk, _cache, new PassThroughSaveMigrator(), _bus, logger, TestDoubles.Retry(logger),
                new CloudSaveOptions(keyPrefix: "nex", schemaVersion: 2, maxPayloadBytes: 1024));
            Assert.IsTrue((await _service.InitializeAsync(CancellationToken.None)).IsSuccess);
        }

        [TestCase("")]
        [TestCase("bad.key")]
        [TestCase("bad key")]
        public async Task SaveAsync_InvalidKey_FailsValidation(string key)
        {
            var result = await _service.SaveAsync(key, new PlayerData());
            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
        }

        [Test]
        public async Task SaveAsync_HappyPath_UploadsEnvelopeAndPublishes()
        {
            CloudDataSavedEvent? saved = null;
            _bus.Subscribe<CloudDataSavedEvent>(e => saved = e);

            var result = await _service.SaveAsync("player", new PlayerData { Level = 3, Name = "Ada" });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("player", saved!.Value.Key);
            Assert.IsTrue(_sdk.Remote.ContainsKey("nex_player"), "keys must be namespaced");
            Assert.IsEmpty(_cache.Pending, "successful upload must clear the pending flag");

            var envelope = JsonConvert.DeserializeObject<SaveEnvelope>(_sdk.Remote["nex_player"].json, SaveJson.Settings);
            Assert.AreEqual(2, envelope!.SchemaVersion);
        }

        [Test]
        public async Task SaveAsync_PayloadTooLarge_FailsValidation()
        {
            var result = await _service.SaveAsync("big", new PlayerData { Name = new string('x', 2000) });
            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
        }

        [Test]
        public async Task SaveAsync_Offline_AcceptsLocallyAndQueues()
        {
            _sdk.ThrowAlways = new RequestFailedException(CommonErrorCodes.TransportError, "offline");

            var result = await _service.SaveAsync("player", new PlayerData { Level = 1 });

            Assert.IsTrue(result.IsSuccess, "offline writes are accepted locally");
            CollectionAssert.Contains(_cache.Pending, "player");
            Assert.IsTrue(_cache.Data.ContainsKey("player"));
        }

        [Test]
        public async Task LoadAsync_RoundTrip_ReturnsSavedValue()
        {
            await _service.SaveAsync("player", new PlayerData { Level = 7, Name = "Ada" });

            var result = await _service.LoadAsync<PlayerData>("player");

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(7, result.Value.Level);
            Assert.AreEqual("Ada", result.Value.Name);
        }

        [Test]
        public async Task LoadAsync_MissingKey_ReturnsNotFound()
        {
            var result = await _service.LoadAsync<PlayerData>("missing");
            Assert.AreEqual(ServiceErrorCode.NotFound, result.Error.Code);
        }

        [Test]
        public async Task LoadAsync_NewerSchema_FailsValidation()
        {
            var envelope = new SaveEnvelope { SchemaVersion = 99, SavedAtUnixMs = 0, PayloadJson = "{}" };
            _sdk.Remote["nex_player"] = (JsonConvert.SerializeObject(envelope, SaveJson.Settings), "1");

            var result = await _service.LoadAsync<PlayerData>("player");

            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
        }

        [Test]
        public async Task LoadAsync_TamperedData_FailsValidationInsteadOfCrashing()
        {
            _sdk.Remote["nex_player"] = ("not-an-envelope", "1");

            var result = await _service.LoadAsync<PlayerData>("player");

            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
        }

        [Test]
        public async Task LoadAsync_Offline_ServesLocalCache()
        {
            await _service.SaveAsync("player", new PlayerData { Level = 5 });
            _sdk.ThrowAlways = new RequestFailedException(CommonErrorCodes.TransportError, "offline");

            var result = await _service.LoadAsync<PlayerData>("player");

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(5, result.Value.Level);
        }

        [Test]
        public async Task DeleteAsync_RemovesRemoteAndLocal()
        {
            await _service.SaveAsync("player", new PlayerData { Level = 5 });

            var result = await _service.DeleteAsync("player");

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(_sdk.Remote.ContainsKey("nex_player"));
            Assert.IsFalse(_cache.Data.ContainsKey("player"));
        }

        [Test]
        public async Task ListKeysAsync_StripsNamespacePrefix()
        {
            await _service.SaveAsync("player", new PlayerData());
            await _service.SaveAsync("settings", new PlayerData());

            var result = await _service.ListKeysAsync();

            Assert.IsTrue(result.IsSuccess);
            CollectionAssert.AreEquivalent(new[] { "player", "settings" }, result.Value);
        }

        [Test]
        public async Task InitializeAsync_ReplaysPendingWrites()
        {
            var sdk = new FakeCloudSaveSdk();
            var cache = new FakeLocalSaveCache();
            var envelope = new SaveEnvelope { SchemaVersion = 2, SavedAtUnixMs = 0, PayloadJson = "{\"Level\":9}" };
            cache.Write("player", JsonConvert.SerializeObject(envelope, SaveJson.Settings));
            cache.MarkPending("player");

            var logger = TestDoubles.Logger();
            var service = new CloudSaveService(
                sdk, cache, new PassThroughSaveMigrator(), TestDoubles.Bus(logger), logger, TestDoubles.Retry(logger),
                new CloudSaveOptions(keyPrefix: "nex", schemaVersion: 2, maxPayloadBytes: 1024));

            await service.InitializeAsync(CancellationToken.None);

            Assert.IsTrue(sdk.Remote.ContainsKey("nex_player"), "queued offline write must replay on boot");
            Assert.IsEmpty(cache.Pending);
        }
    }
}
