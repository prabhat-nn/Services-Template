#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services.Core
{
    /// <summary>
    /// Shared resilience for every remote call: per-attempt timeout + exponential backoff
    /// with full jitter. Retries only <see cref="ServiceError.IsRetryable"/> failures.
    /// Modules never hand-roll retry loops.
    /// </summary>
    public sealed class RetryPolicy
    {
        private readonly int _maxAttempts;
        private readonly TimeSpan _baseDelay;
        private readonly TimeSpan _maxDelay;
        private readonly TimeSpan _attemptTimeout;
        private readonly IServiceLogger _logger;
        private readonly IDelayProvider _delayProvider;
        private readonly Random _random = new();

        internal RetryPolicy(
            int maxAttempts,
            TimeSpan baseDelay,
            TimeSpan maxDelay,
            TimeSpan attemptTimeout,
            IServiceLogger logger,
            IDelayProvider delayProvider)
        {
            _maxAttempts = Math.Max(1, maxAttempts);
            _baseDelay = baseDelay;
            _maxDelay = maxDelay;
            _attemptTimeout = attemptTimeout;
            _logger = logger;
            _delayProvider = delayProvider;
        }

        public async UniTask<ServiceResult<T>> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, UniTask<ServiceResult<T>>> operation,
            CancellationToken ct)
        {
            ServiceResult<T> result = default;

            for (var attempt = 0; attempt < _maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var timeoutTimer = timeoutCts.CancelAfterSlim(_attemptTimeout);

                try
                {
                    result = await operation(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    result = ServiceResult<T>.Failure(
                        ServiceErrorCode.Timeout,
                        $"{operationName} timed out after {_attemptTimeout.TotalSeconds:0.#}s (attempt {attempt + 1}/{_maxAttempts}).");
                }
                finally
                {
                    timeoutTimer.Dispose();
                }

                if (result.IsSuccess || !result.Error.IsRetryable)
                    return result;

                if (attempt < _maxAttempts - 1)
                {
                    var delay = NextDelay(attempt);
                    _logger.Warning(
                        "Retry",
                        $"{operationName} failed with {result.Error.Code}; retrying in {delay.TotalMilliseconds:0}ms (attempt {attempt + 2}/{_maxAttempts}).");
                    await _delayProvider.Delay(delay, ct);
                }
            }

            return result;
        }

        private TimeSpan NextDelay(int attempt)
        {
            var capMs = Math.Min(_maxDelay.TotalMilliseconds, _baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
            return TimeSpan.FromMilliseconds(_random.NextDouble() * capMs);
        }
    }
}
