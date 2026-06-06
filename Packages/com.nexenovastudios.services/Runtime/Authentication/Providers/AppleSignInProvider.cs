#nullable enable
#if NEX_APPLE_SIGNIN && UNITY_IOS
using System.Text;
using System.Threading;
using AppleAuth;
using AppleAuth.Enums;
using AppleAuth.Extensions;
using AppleAuth.Interfaces;
using AppleAuth.Native;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services.Authentication
{
    /// <summary>
    /// Sign in with Apple (iOS) via apple-signin-unity. The AppleAuthManager message queue
    /// is pumped manually while a login is in flight — no MonoBehaviour required.
    /// </summary>
    internal sealed class AppleSignInProvider : IPlatformSignInProvider
    {
        private AppleAuthManager? _manager;

        public bool IsSupported => AppleAuthManager.IsCurrentPlatformSupported;
        public PlatformProvider Provider => PlatformProvider.Apple;

        public async UniTask<ServiceResult<string>> AcquireTokenAsync(CancellationToken ct)
        {
            await UniTask.SwitchToMainThread(ct);

            if (!AppleAuthManager.IsCurrentPlatformSupported)
                return ServiceResult<string>.Failure(ServiceError.Unsupported("Sign in with Apple is not supported on this device."));

            _manager ??= new AppleAuthManager(new PayloadDeserializer());

            var tcs = new UniTaskCompletionSource<ServiceResult<string>>();
            var loginArgs = new AppleAuthLoginArgs(LoginOptions.None);

            _manager.LoginWithAppleId(
                loginArgs,
                credential =>
                {
                    if (credential is IAppleIDCredential appleId && appleId.IdentityToken != null)
                    {
                        var idToken = Encoding.UTF8.GetString(appleId.IdentityToken);
                        tcs.TrySetResult(ServiceResult<string>.Success(idToken));
                    }
                    else
                    {
                        tcs.TrySetResult(ServiceResult<string>.Failure(new ServiceError(
                            ServiceErrorCode.ProviderError, "Apple sign-in returned no identity token.")));
                    }
                },
                error =>
                {
                    var code = error.GetAuthorizationErrorCode() == AuthorizationErrorCode.Canceled
                        ? ServiceErrorCode.Cancelled
                        : ServiceErrorCode.ProviderError;
                    tcs.TrySetResult(ServiceResult<string>.Failure(new ServiceError(
                        code, $"Apple sign-in failed ({error.GetAuthorizationErrorCode()}).")));
                });

            while (tcs.Task.Status == UniTaskStatus.Pending)
            {
                ct.ThrowIfCancellationRequested();
                _manager.Update();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            return await tcs.Task;
        }
    }
}
#endif
