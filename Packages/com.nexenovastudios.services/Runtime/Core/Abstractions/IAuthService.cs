#nullable enable
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services
{
    /// <summary>
    /// Player identity. Default boot flow: cached session if available, otherwise per
    /// <c>AuthOptions.SignInMode</c> — platform sign-in (Google Play Games on Android,
    /// Sign in with Apple on iOS) with anonymous fallback, or anonymous only.
    /// </summary>
    public interface IAuthService
    {
        bool IsSignedIn { get; }

        /// <summary>UGS player id; empty string when signed out, never null.</summary>
        string PlayerId { get; }

        UniTask<ServiceResult<Unit>> SignInAnonymouslyAsync(CancellationToken ct = default);

        /// <summary>
        /// Sign in with the current platform's provider: GPGS on Android, Apple on iOS.
        /// In the editor or on unsupported platforms this returns an
        /// <see cref="ServiceErrorCode.Unsupported"/> failure (callers typically fall back to anonymous).
        /// </summary>
        UniTask<ServiceResult<Unit>> SignInWithPlatformAsync(CancellationToken ct = default);

        /// <summary>Link the platform provider to the currently signed-in (e.g. anonymous) account.</summary>
        UniTask<ServiceResult<Unit>> LinkWithPlatformAsync(CancellationToken ct = default);

        UniTask<ServiceResult<Unit>> SignOutAsync(CancellationToken ct = default);
    }
}
