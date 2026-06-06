#nullable enable
using System;
using Unity.Services.CloudSave;
using Unity.Services.Core;

namespace Nexenova.Services.CloudSave
{
    internal static class CloudSaveErrorMapper
    {
        public static ServiceError Map(Exception ex)
        {
            switch (ex)
            {
                case CloudSaveException cse:
                    return cse.Reason.ToString() switch
                    {
                        "Conflict" or "ConflictError"
                            => new ServiceError(ServiceErrorCode.Conflict, "Cloud save write conflict (stale write lock).", cse),
                        "NoInternetConnection" or "ServiceUnavailable"
                            => new ServiceError(ServiceErrorCode.Network, $"Cloud save network failure ({cse.Reason}).", cse),
                        "TooManyRequests" or "RateLimited"
                            => new ServiceError(ServiceErrorCode.RateLimited, "Cloud save rate limited.", cse),
                        "InvalidArgument" or "KeyLimitExceeded" or "ValidationError"
                            => new ServiceError(ServiceErrorCode.Validation, $"Cloud save rejected the request ({cse.Reason}).", cse),
                        "NotFound"
                            => new ServiceError(ServiceErrorCode.NotFound, "Cloud save key not found.", cse),
                        "Unauthorized" or "AccessTokenMissing" or "PlayerIdMissing"
                            => new ServiceError(ServiceErrorCode.Unauthorized, $"Cloud save unauthorized ({cse.Reason}).", cse),
                        _ => new ServiceError(ServiceErrorCode.ProviderError, $"Cloud save error ({cse.Reason}).", cse),
                    };

                case RequestFailedException rfe:
                    if (rfe.ErrorCode == CommonErrorCodes.TransportError || rfe.ErrorCode == CommonErrorCodes.ServiceUnavailable)
                        return new ServiceError(ServiceErrorCode.Network, $"Network failure ({rfe.ErrorCode}).", rfe);
                    if (rfe.ErrorCode == CommonErrorCodes.Timeout)
                        return new ServiceError(ServiceErrorCode.Timeout, "Cloud save request timed out.", rfe);
                    if (rfe.ErrorCode == CommonErrorCodes.TooManyRequests)
                        return new ServiceError(ServiceErrorCode.RateLimited, "Cloud save rate limited.", rfe);
                    return new ServiceError(ServiceErrorCode.ProviderError, $"Cloud save provider error ({rfe.ErrorCode}).", rfe);

                default:
                    return new ServiceError(ServiceErrorCode.Unknown, $"Unexpected cloud save error: {ex.Message}", ex);
            }
        }
    }
}
