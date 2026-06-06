#nullable enable
using System;
using NUnit.Framework;

namespace Nexenova.Services.Tests
{
    public sealed class EventBusTests
    {
        [Test]
        public void Publish_DeliversToAllSubscribers()
        {
            var bus = TestDoubles.Bus();
            var count = 0;
            bus.Subscribe<PlayerSignedInEvent>(_ => count++);
            bus.Subscribe<PlayerSignedInEvent>(_ => count++);

            bus.Publish(new PlayerSignedInEvent("p1", false));

            Assert.AreEqual(2, count);
        }

        [Test]
        public void Publish_ThrowingHandler_DoesNotBreakOtherHandlers()
        {
            var logger = TestDoubles.Logger();
            var bus = TestDoubles.Bus(logger);
            var delivered = false;
            bus.Subscribe<PlayerSignedInEvent>(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe<PlayerSignedInEvent>(_ => delivered = true);

            bus.Publish(new PlayerSignedInEvent("p1", false));

            Assert.IsTrue(delivered);
            Assert.AreEqual(1, logger.Errors.Count);
        }

        [Test]
        public void Dispose_StopsDelivery()
        {
            var bus = TestDoubles.Bus();
            var count = 0;
            var subscription = bus.Subscribe<PlayerSignedOutEvent>(_ => count++);

            bus.Publish(new PlayerSignedOutEvent());
            subscription.Dispose();
            bus.Publish(new PlayerSignedOutEvent());

            Assert.AreEqual(1, count);
        }

        [Test]
        public void Publish_WithNoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => TestDoubles.Bus().Publish(new SessionExpiredEvent()));
        }
    }
}
