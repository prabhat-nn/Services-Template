#nullable enable
#if NEX_GPGS && UNITY_ANDROID
using System.Threading;
using Cysharp.Threading.Tasks;
using GooglePlayGames;
using GooglePlayGames.BasicApi;

namespace Nexenova.Services.Authentication
{
    /// <summary>
    /// Google Play Games Services v2 sign-in (Android). Tries the automatic sign-in first,
    /// falls back to the interactive prompt, then requests a server auth code for UGS.
    /// Requires the web client ID to be configured in the GPGS plugin settings.
    /// </summary>
    internal sealed class GpgsSignInProvider : IPlatformSignInProvider
    {
        private readonly IServiceLogger _logger;

        public bool IsSupported => true;
        public PlatformProvider Provider => PlatformProvider.GooglePlayGames;

        public GpgsSignInProvider(IServiceLogger logger)
        {
            _logger = logger;
            PlayGamesPlatform.Activate();
        }

        public async UniTask<ServiceResult<string>> AcquireTokenAsync(CancellationToken ct)
        {
            await UniTask.SwitchToMainThread(ct);

            var status = await AuthenticateAsync(ct);
            if (status != SignInStatus.Success)
            {
                _logger.Info("Auth", "GPGS automatic sign-in unavailable — showing interactive sign-in.");
                status = await ManuallyAuthenticateAsync(ct);
            }

            if (status != SignInStatus.Success)
            {
                var code = status == SignInStatus.Canceled ? ServiceErrorCode.Cancelled : ServiceErrorCode.ProviderError;
                return ServiceResult<string>.Failure(new ServiceError(code, $"GPGS sign-in failed: {status}."));
            }

            var authCode = await RequestServerSideAccessAsync(ct);
            if (string.IsNullOrEmpty(authCode))
                return ServiceResult<string>.Failure(new ServiceError(
                    ServiceErrorCode.ProviderError,
                    "GPGS returned an empty server auth code. Check the web client ID in the Play Games plugin settings."));

            return ServiceResult<string>.Success(authCode!);
        }

        private static UniTask<SignInStatus> AuthenticateAsync(CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource<SignInStatus>();
            PlayGamesPlatform.Instance.Authenticate(status => tcs.TrySetResult(status));
            return tcs.Task.AttachExternalCancellation(ct);
        }

        private static UniTask<SignInStatus> ManuallyAuthenticateAsync(CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource<SignInStatus>();
            PlayGamesPlatform.Instance.ManuallyAuthenticate(status => tcs.TrySetResult(status));
            return tcs.Task.AttachExternalCancellation(ct);
        }

        private static UniTask<string?> RequestServerSideAccessAsync(CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource<string?>();
            PlayGamesPlatform.Instance.RequestServerSideAccess(false, code => tcs.TrySetResult(code));
            return tcs.Task.AttachExternalCancellation(ct);
        }
    }
}
#endif
