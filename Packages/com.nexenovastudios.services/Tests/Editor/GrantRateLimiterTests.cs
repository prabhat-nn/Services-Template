#nullable enable
using NUnit.Framework;
using Nexenova.Services.Economy;

namespace Nexenova.Services.Tests
{
    public sealed class GrantRateLimiterTests
    {
        [Test]
        public void TryReserve_WithinCap_Succeeds()
        {
            var limiter = new GrantRateLimiter(new FakeMonotonicClock());

            Assert.IsTrue(limiter.TryReserve(400, maxPerMinute: 1000));
            Assert.IsTrue(limiter.TryReserve(600, maxPerMinute: 1000));
        }

        [Test]
        public void TryReserve_ExceedingCap_Fails()
        {
            var limiter = new GrantRateLimiter(new FakeMonotonicClock());

            Assert.IsTrue(limiter.TryReserve(800, maxPerMinute: 1000));
            Assert.IsFalse(limiter.TryReserve(300, maxPerMinute: 1000));
        }

        [Test]
        public void TryReserve_WindowExpiry_FreesBudget()
        {
            var clock = new FakeMonotonicClock { NowSeconds = 0 };
            var limiter = new GrantRateLimiter(clock);

            Assert.IsTrue(limiter.TryReserve(1000, maxPerMinute: 1000));
            Assert.IsFalse(limiter.TryReserve(1, maxPerMinute: 1000));

            clock.NowSeconds = 61;
            Assert.IsTrue(limiter.TryReserve(1000, maxPerMinute: 1000));
        }
    }
}
