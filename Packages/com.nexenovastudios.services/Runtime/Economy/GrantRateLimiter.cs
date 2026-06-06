#nullable enable
using System;
using System.Collections.Generic;

namespace Nexenova.Services.Economy
{
    /// <summary>Thread-safe monotonic clock seam so rate-limit tests run deterministically.</summary>
    internal interface IMonotonicClock
    {
        double NowSeconds { get; }
    }

    internal sealed class SystemMonotonicClock : IMonotonicClock
    {
        private static readonly System.Diagnostics.Stopwatch Stopwatch = System.Diagnostics.Stopwatch.StartNew();

        public double NowSeconds => Stopwatch.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// Rolling one-minute window over total granted currency (abuse prevention).
    /// Applies to non-IAP grants only; IAP grants are gated by receipt validation instead.
    /// </summary>
    internal sealed class GrantRateLimiter
    {
        private const double WindowSeconds = 60.0;

        private readonly IMonotonicClock _clock;
        private readonly Queue<(double time, long amount)> _grants = new();
        private long _windowTotal;

        public GrantRateLimiter(IMonotonicClock clock)
        {
            _clock = clock;
        }

        /// <summary>True if granting <paramref name="amount"/> stays within <paramref name="maxPerMinute"/>.</summary>
        public bool TryReserve(long amount, long maxPerMinute)
        {
            var now = _clock.NowSeconds;
            while (_grants.Count > 0 && now - _grants.Peek().time > WindowSeconds)
                _windowTotal -= _grants.Dequeue().amount;

            if (_windowTotal + amount > maxPerMinute)
                return false;

            _grants.Enqueue((now, amount));
            _windowTotal += amount;
            return true;
        }
    }
}
