#nullable enable
using System;
using Unity.Services.Core;

namespace Nexenova.Services.RemoteConfig
{
    internal static class RemoteConfigErrorMapper
    {
        public static ServiceError Map(Exception ex)
        {
            switch (ex)
            {
                case RequestFailedException rfe:
                    if (rfe.ErrorCode == CommonErrorCodes.TransportError || rfe.ErrorCode == CommonErrorCodes.ServiceUnavailable)
                        return new ServiceError(ServiceErrorCode.Network, $"Remote Config network failure ({rfe.ErrorCode}).", rfe);
                    if (rfe.ErrorCode == CommonErrorCodes.Timeout)
                        return new ServiceError(ServiceErrorCode.Timeout, "Remote Config fetch timed out.", rfe);
                    if (rfe.ErrorCode == CommonErrorCodes.TooManyRequests)
                        return new ServiceError(ServiceErrorCode.RateLimited, "Remote Config rate limited.", rfe);
                    return new ServiceError(ServiceErrorCode.ProviderError, $"Remote Config provider error ({rfe.ErrorCode}).", rfe);

                case System.Net.Http.HttpRequestException:
                    return new ServiceError(ServiceErrorCode.Network, "Remote Config network failure.", ex);

                default:
                    return new ServiceError(ServiceErrorCode.Unknown, $"Unexpected Remote Config error: {ex.Message}", ex);
            }
        }
    }
}
