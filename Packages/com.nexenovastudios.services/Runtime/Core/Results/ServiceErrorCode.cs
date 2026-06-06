#nullable enable

namespace Nexenova.Services
{
    /// <summary>
    /// Canonical error categories surfaced by every service in the package.
    /// SDK-specific exceptions are mapped to these codes at the adapter boundary
    /// and never escape a module.
    /// </summary>
    public enum ServiceErrorCode
    {
        None = 0,
        NotInitialized,
        NotSignedIn,
        Network,
        Timeout,
        RateLimited,
        Validation,
        Unauthorized,
        Conflict,
        NotFound,
        ProviderError,
        Cancelled,
        AlreadyInProgress,
        Unsupported,
        Unknown,
    }
}
