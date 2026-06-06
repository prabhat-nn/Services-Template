#nullable enable
using System;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace Nexenova.Services.Authentication
{
    /// <summary>Explicit mapping table from UGS Authentication exceptions to ServiceErrorCode.</summary>
    internal static class AuthErrorMapper
    {
        public static ServiceError Map(Exception ex)
        {
            switch (ex)
            {
                case AuthenticationException authEx:
                    if (authEx.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked ||
                        authEx.ErrorCode == AuthenticationErrorCodes.AccountLinkLimitExceeded)
                        return new ServiceError(ServiceErrorCode.Conflict, $"Account link conflict ({authEx.ErrorCode}).", authEx);

                    if (authEx.ErrorCode == AuthenticationErrorCodes.InvalidSessionToken ||
                        authEx.ErrorCode == AuthenticationErrorCodes.InvalidParameters)
                        return new ServiceError(ServiceErrorCode.Unauthorized, $"Authentication rejected ({authEx.ErrorCode}).", authEx);

                    if (authEx.ErrorCode == AuthenticationErrorCodes.ClientInvalidUserState)
                        return new ServiceError(ServiceErrorCode.Validation, "Invalid auth state for this operation (already signed in/out?).", authEx);

                    return new ServiceError(ServiceErrorCode.Unauthorized, $"Authentication failed ({authEx.ErrorCode}).", authEx);

                case RequestFailedException rfe:
                    if (rfe.ErrorCode == CommonErrorCodes.TransportError ||
                        rfe.ErrorCode == CommonErrorCodes.ServiceUnavailable ||
                        rfe.ErrorCode == CommonErrorCodes.ApiMissing)
                        return new ServiceError(ServiceErrorCode.Network, $"Network failure ({rfe.ErrorCode}).", rfe);

                    if (rfe.ErrorCode == CommonErrorCodes.Timeout)
                        return new ServiceError(ServiceErrorCode.Timeout, "Authentication request timed out.", rfe);

                    if (rfe.ErrorCode == CommonErrorCodes.TooManyRequests)
                        return new ServiceError(ServiceErrorCode.RateLimited, "Authentication rate limited.", rfe);

                    if (rfe.ErrorCode == CommonErrorCodes.Forbidden ||
                        rfe.ErrorCode == CommonErrorCodes.InvalidToken ||
                        rfe.ErrorCode == CommonErrorCodes.TokenExpired)
                        return new ServiceError(ServiceErrorCode.Unauthorized, $"Authentication unauthorized ({rfe.ErrorCode}).", rfe);

                    return new ServiceError(ServiceErrorCode.ProviderError, $"Authentication provider error ({rfe.ErrorCode}).", rfe);

                default:
                    return new ServiceError(ServiceErrorCode.Unknown, $"Unexpected authentication error: {ex.Message}", ex);
            }
        }
    }
}
