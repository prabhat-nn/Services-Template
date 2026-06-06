#nullable enable
using System;
using NUnit.Framework;

namespace Nexenova.Services.Tests
{
    public sealed class ServiceResultTests
    {
        [Test]
        public void Success_ExposesValue_AndThrowsOnError()
        {
            var result = ServiceResult<int>.Success(42);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(42, result.Value);
            Assert.Throws<InvalidOperationException>(() => _ = result.Error);
        }

        [Test]
        public void Failure_ExposesError_AndThrowsOnValue()
        {
            var result = ServiceResult<int>.Failure(ServiceError.Validation("bad"));

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(ServiceErrorCode.Validation, result.Error.Code);
            Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        }

        [Test]
        public void Map_TransformsSuccess_AndPropagatesFailure()
        {
            Assert.AreEqual("7", ServiceResult<int>.Success(7).Map(v => v.ToString()).Value);

            var failed = ServiceResult<int>.Failure(ServiceError.Validation("bad")).Map(v => v.ToString());
            Assert.AreEqual(ServiceErrorCode.Validation, failed.Error.Code);
        }

        [TestCase(ServiceErrorCode.Network, true)]
        [TestCase(ServiceErrorCode.Timeout, true)]
        [TestCase(ServiceErrorCode.RateLimited, true)]
        [TestCase(ServiceErrorCode.Validation, false)]
        [TestCase(ServiceErrorCode.Unauthorized, false)]
        [TestCase(ServiceErrorCode.Conflict, false)]
        public void IsRetryable_MatchesPolicy(ServiceErrorCode code, bool expected)
        {
            Assert.AreEqual(expected, new ServiceError(code, "x").IsRetryable);
        }
    }
}
