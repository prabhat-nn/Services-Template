#nullable enable
using System;

namespace Nexenova.Services
{
    /// <summary>
    /// Immutable description of an expected runtime failure.
    /// <see cref="Message"/> is developer-facing and must never be shown to players raw.
    /// </summary>
    public sealed class ServiceError
    {
        public ServiceErrorCode Code { get; }
        public string Message { get; }

        /// <summary>Original SDK exception, kept for logging only. May be null.</summary>
        public Exception? Cause { get; }

        public bool IsRetryable =>
            Code is ServiceErrorCode.Network or ServiceErrorCode.Timeout or ServiceErrorCode.RateLimited;

        public ServiceError(ServiceErrorCode code, string message, Exception? cause = null)
        {
            Code = code;
            Message = message ?? string.Empty;
            Cause = cause;
        }

        public override string ToString() => $"[{Code}] {Message}";

        public static ServiceError NotInitialized(string moduleName) =>
            new(ServiceErrorCode.NotInitialized, $"{moduleName} is not initialized. Await IServicesBootstrap.InitializeAsync first.");

        public static ServiceError NotSignedIn(string moduleName) =>
            new(ServiceErrorCode.NotSignedIn, $"{moduleName} requires a signed-in player.");

        public static ServiceError Cancelled() =>
            new(ServiceErrorCode.Cancelled, "Operation was cancelled.");

        public static ServiceError Validation(string message) =>
            new(ServiceErrorCode.Validation, message);

        public static ServiceError AlreadyInProgress(string operation) =>
            new(ServiceErrorCode.AlreadyInProgress, $"{operation} is already in progress.");

        public static ServiceError Unsupported(string message) =>
            new(ServiceErrorCode.Unsupported, message);
    }
}
