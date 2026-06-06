#nullable enable
using System;
using Unity.Services.Core;
using Unity.Services.Economy;

namespace Nexenova.Services.Economy
{
    internal static class EconomyErrorMapper
    {
        public static ServiceError Map(Exception ex)
        {
            switch (ex)
            {
                case EconomyException ee:
                    // Reason names are matched as strings so the mapping stays stable
                    // across minor SDK enum revisions; unmatched reasons fall through
                    // to ProviderError (never silently to Network).
                    return ee.Reason.ToString() switch
                    {
                        "InsufficientFunds" or "InvalidArgument" or "ValidationError"
                            => new ServiceError(ServiceErrorCode.Validation, $"Economy rejected the operation ({ee.Reason}).", ee),
                        "RateLimited" or "TooManyRequests"
                            => new ServiceError(ServiceErrorCode.RateLimited, "Economy rate limited.", ee),
                        "Unauthorized" or "Forbidden"
                            => new ServiceError(ServiceErrorCode.Unauthorized, $"Economy unauthorized ({ee.Reason}).", ee),
                        "EntityNotFound" or "NotFound"
                            => new ServiceError(ServiceErrorCode.NotFound, $"Economy entity not found ({ee.Reason}).", ee),
                        _ => new ServiceError(ServiceErrorCode.ProviderError, $"Economy error ({ee.Reason}).", ee),
                    };

                case RequestFailedException rfe:
                    if (rfe.ErrorCode == CommonErrorCodes.TransportError || rfe.ErrorCode == CommonErrorCodes.ServiceUnavailable)
                        return new ServiceError(ServiceErrorCode.Network, $"Network failure ({rfe.ErrorCode}).", rfe);
                    if (rfe.ErrorCode == CommonErrorCodes.Timeout)
                        return new ServiceError(ServiceErrorCode.Timeout, "Economy request timed out.", rfe);
                    if (rfe.ErrorCode == CommonErrorCodes.TooManyRequests)
                        return new ServiceError(ServiceErrorCode.RateLimited, "Economy rate limited.", rfe);
                    return new ServiceError(ServiceErrorCode.ProviderError, $"Economy provider error ({rfe.ErrorCode}).", rfe);

                default:
                    return new ServiceError(ServiceErrorCode.Unknown, $"Unexpected economy error: {ex.Message}", ex);
            }
        }
    }
}
