#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Nexenova.Services.Core;

namespace Nexenova.Services.Tests
{
    public sealed class ServicesInitializerTests
    {
        [SetUp]
        public void SetUp() => FakeModule.GlobalInitOrder.Clear();

        private static ServicesInitializer Create(
            IEnumerable<IServiceModule> modules,
            IEventBus? bus = null,
            FakeUnityServicesSdk? sdk = null)
        {
            var logger = TestDoubles.Logger();
            return new ServicesInitializer(
                sdk ?? new FakeUnityServicesSdk(),
                modules,
                bus ?? TestDoubles.Bus(logger),
                logger,
                TestDoubles.Retry(logger),
                TestDoubles.CoreOptions());
        }

        [Test]
        public async Task InitializeAsync_RunsStagesInOrder()
        {
            var identity = new FakeModule("Auth", InitializationStage.Identity, isRequired: true);
            var data = new FakeModule("Economy", InitializationStage.Data, isRequired: false);
            var monetization = new FakeModule("IAP", InitializationStage.Monetization, isRequired: false);
            var initializer = Create(new IServiceModule[] { monetization, data, identity });

            var result = await initializer.InitializeAsync(CancellationToken.None);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(ServicesState.Ready, initializer.State);
            CollectionAssert.AreEqual(new[] { "Auth", "Economy", "IAP" }, FakeModule.GlobalInitOrder);
        }

        [Test]
        public async Task InitializeAsync_RequiredModuleFailure_FailsBoot()
        {
            var identity = new FakeModule("Auth", InitializationStage.Identity, isRequired: true,
                () => ServiceResult.Fail(ServiceErrorCode.Network, "down"));
            var data = new FakeModule("Economy", InitializationStage.Data, isRequired: false);
            var initializer = Create(new IServiceModule[] { identity, data });

            var result = await initializer.InitializeAsync(CancellationToken.None);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(ServicesState.Failed, initializer.State);
            Assert.AreEqual(0, data.InitializeCalls, "later stages must not run after a required failure");
            CollectionAssert.Contains(initializer.FailedModules, "Auth");
        }

        [Test]
        public async Task InitializeAsync_OptionalModuleFailure_DegradesAndContinues()
        {
            var bus = TestDoubles.Bus();
            ServiceDegradedEvent? degraded = null;
            var ready = false;
            bus.Subscribe<ServiceDegradedEvent>(e => degraded = e);
            bus.Subscribe<ServicesReadyEvent>(e => ready = true);

            var identity = new FakeModule("Auth", InitializationStage.Identity, isRequired: true);
            var data = new FakeModule("Economy", InitializationStage.Data, isRequired: false,
                () => ServiceResult.Fail(ServiceErrorCode.Network, "down"));
            var monetization = new FakeModule("IAP", InitializationStage.Monetization, isRequired: false);
            var initializer = Create(new IServiceModule[] { identity, data, monetization }, bus);

            var result = await initializer.InitializeAsync(CancellationToken.None);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(ServicesState.Degraded, initializer.State);
            Assert.IsTrue(ready);
            Assert.AreEqual("Economy", degraded!.Value.ModuleName);
            Assert.AreEqual(1, monetization.InitializeCalls, "optional failure must not stop later stages");
        }

        [Test]
        public async Task InitializeAsync_IsSingleFlight()
        {
            var sdk = new FakeUnityServicesSdk();
            var identity = new FakeModule("Auth", InitializationStage.Identity, isRequired: true);
            var initializer = Create(new IServiceModule[] { identity }, sdk: sdk);

            await initializer.InitializeAsync(CancellationToken.None);
            await initializer.InitializeAsync(CancellationToken.None);

            Assert.AreEqual(1, sdk.InitializeCalls);
            Assert.AreEqual(1, identity.InitializeCalls);
        }

        [Test]
        public async Task InitializeAsync_PlatformFailure_FailsWithoutRunningModules()
        {
            var sdk = new FakeUnityServicesSdk { ThrowOnInitialize = new System.Exception("no network") };
            var identity = new FakeModule("Auth", InitializationStage.Identity, isRequired: true);
            var initializer = Create(new IServiceModule[] { identity }, sdk: sdk);

            var result = await initializer.InitializeAsync(CancellationToken.None);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(ServicesState.Failed, initializer.State);
            Assert.AreEqual(0, identity.InitializeCalls);
        }

        [Test]
        public async Task WaitUntilReadyAsync_CompletesOnceReady()
        {
            var identity = new FakeModule("Auth", InitializationStage.Identity, isRequired: true);
            var initializer = Create(new IServiceModule[] { identity });

            await initializer.InitializeAsync(CancellationToken.None);
            await initializer.WaitUntilReadyAsync(CancellationToken.None);

            Assert.AreEqual(ServicesState.Ready, initializer.State);
        }
    }
}
