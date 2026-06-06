#nullable enable
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services.Authentication
{
    internal enum PlatformProvider
    {
        None,
        GooglePlayGames,
        Apple,
    }

    /// <summary>
    /// Acquires a platform identity token: a server auth code from Google Play Games on
    /// Android, an identity token from Sign in with Apple on iOS. The token is exchanged
    /// with UGS by <see cref="AuthService"/>. Tokens are never logged.
    /// </summary>
    internal interface IPlatformSignInProvider
    {
        bool IsSupported { get; }
        PlatformProvider Provider { get; }
        UniTask<ServiceResult<string>> AcquireTokenAsync(CancellationToken ct);
    }

    /// <summary>Editor / unsupported-platform fallback.</summary>
    internal sealed class NullPlatformSignInProvider : IPlatformSignInProvider
    {
        public bool IsSupported => false;
        public PlatformProvider Provider => PlatformProvider.None;

        public UniTask<ServiceResult<string>> AcquireTokenAsync(CancellationToken ct) =>
            UniTask.FromResult(ServiceResult<string>.Failure(
                ServiceError.Unsupported("Platform sign-in is not available on this platform/build. " +
                                         "Install the Google Play Games (Android) or Apple Sign-In (iOS) plugin and build for the device.")));
    }
}
