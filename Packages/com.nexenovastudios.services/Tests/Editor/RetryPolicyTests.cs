#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Nexenova.Services.Tests
{
    public sealed class RetryPolicyTests
    {
        [Test]
        public async Task ExecuteAsync_RetriesRetryableFailures_UntilSuccess()
        {
            var delays = new FakeDelayProvider();
            var policy = TestDoubles.Retry(delay: delays);
            var attempts = 0;

            var result = await policy.ExecuteAsync<int>("op", _ =>
            {
                attempts++;
                return UniTask.FromResult(attempts < 3
                    ? ServiceResult<int>.Failure(ServiceErrorCode.Network, "down")
                    : ServiceResult<int>.Success(99));
            }, CancellationToken.None);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(99, result.Value);
            Assert.AreEqual(3, attempts);
            Assert.AreEqual(2, delays.Delays.Count);
        }

        [Test]
        public async Task ExecuteAsync_DoesNotRetry_NonRetryableFailures()
        {
            var policy = TestDoubles.Retry();
            var attempts = 0;

            var result = await policy.ExecuteAsync<int>("op", _ =>
            {
                attempts++;
                return UniTask.FromResult(ServiceResult<int>.Failure(ServiceErrorCode.Validation, "bad input"));
            }, CancellationToken.None);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(1, attempts);
        }

        [Test]
        public async Task ExecuteAsync_ExhaustsAttempts_ReturnsLastFailure()
        {
            var policy = TestDoubles.Retry(attempts: 3);
            var attempts = 0;

            var result = await policy.ExecuteAsync<int>("op", _ =>
            {
                attempts++;
                return UniTask.FromResult(ServiceResult<int>.Failure(ServiceErrorCode.Network, "down"));
            }, CancellationToken.None);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(ServiceErrorCode.Network, result.Error.Code);
            Assert.AreEqual(3, attempts);
        }

        [Test]
        public void ExecuteAsync_CallerCancellation_Propagates()
        {
            var policy = TestDoubles.Retry();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<System.OperationCanceledException>(async () =>
                await policy.ExecuteAsync<int>("op", _ => UniTask.FromResult(ServiceResult<int>.Success(1)), cts.Token));
        }
    }
}
