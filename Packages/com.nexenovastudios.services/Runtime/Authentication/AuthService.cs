#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nexenova.Services.Core;

namespace Nexenova.Services.Authentication
{
    /// <summary>
    /// Player identity module. Boot flow per AuthOptions.SignInMode: anonymous-only; platform
    /// sign-in (GPGS/Apple) with anonymous fallback (cached session reused when available);
    /// or platform-required (always a fresh platform exchange, never anonymous).
    /// On session expiry it re-signs-in automatically and republishes events.
    /// </summary>
    internal sealed class AuthService : IAuthService, IServiceModule, IDisposable
    {
        private const string Tag = "Auth";

        private readonly IAuthenticationSdk _sdk;
        private readonly IPlatformSignInProvider _platform;
        private readonly IEventBus _events;
        private readonly IServiceLogger _logger;
        private readonly RetryPolicy _retry;
        private readonly AuthOptions _options;

        private UniTask<ServiceResult<Unit>>? _inFlightSignIn;
        private bool _initialized;

        public string ModuleName => "Authentication";
        public InitializationStage Stage => InitializationStage.Identity;
        public bool IsRequired => true;

        public bool IsSignedIn => _sdk.IsSignedIn;
        public string PlayerId => _sdk.IsSignedIn ? _sdk.PlayerId : string.Empty;

        public AuthService(
            IAuthenticationSdk sdk,
            IPlatformSignInProvider platform,
            IEventBus events,
            IServiceLogger logger,
            RetryPolicy retry,
            AuthOptions options)
        {
            _sdk = sdk;
            _platform = platform;
            _events = events;
            _logger = logger;
            _retry = retry;
            _options = options;

            _sdk.SignedOut += OnSdkSignedOut;
            _sdk.Expired += OnSdkExpired;
        }

        public async UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct)
        {
            ServiceResult<Unit> result;

            if (_options.SignInMode == PlatformSignInMode.PlatformRequired && _platform.IsSupported)
            {
                // Always exchange a fresh platform token (silent for returning players) so the
                // UGS account is guaranteed to be the platform account. The cached session is
                // deliberately ignored: it could belong to an anonymous account from a previous
                // install/mode, and the platform exchange resolves to the right account anyway.
                result = await SignInWithPlatformAsync(ct);
                if (result.IsFailure)
                    _logger.Warning(Tag, $"Platform sign-in failed ({result.Error.Code}) and mode is PlatformRequired — staying signed out.");
            }
            else if (_options.SignInMode == PlatformSignInMode.PlatformWithAnonymousFallback &&
                _platform.IsSupported &&
                !_sdk.SessionTokenExists)
            {
                result = await SignInWithPlatformAsync(ct);
                if (result.IsFailure)
                {
                    _logger.Warning(Tag, $"Platform sign-in failed ({result.Error.Code}) — falling back to anonymous.");
                    result = await SignInAnonymouslyAsync(ct);
                }
            }
            else
            {
                if (_options.SignInMode == PlatformSignInMode.PlatformRequired)
                    _logger.Warning(Tag, "PlatformRequired but no platform provider on this platform/build — using anonymous sign-in.");
                result = await SignInAnonymouslyAsync(ct);
            }

            _initialized = result.IsSuccess;
            return result;
        }

        public UniTask<ServiceResult<Unit>> SignInAnonymouslyAsync(CancellationToken ct = default) =>
            SingleFlightSignIn(async token =>
            {
                var hadSession = _sdk.SessionTokenExists;
                var result = await _retry.ExecuteAsync<Unit>("Auth.SignInAnonymously", async attemptCt =>
                {
                    try
                    {
                        if (_sdk.IsSignedIn)
                            return ServiceResult.Ok();
                        await _sdk.SignInAnonymouslyAsync(attemptCt);
                        return ServiceResult.Ok();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        var error = AuthErrorMapper.Map(ex);
                        _logger.Error(Tag, $"Anonymous sign-in failed: {error}", ex);
                        return ServiceResult.Fail(error);
                    }
                }, token);

                if (result.IsSuccess)
                    PublishSignedIn(isNewPlayer: !hadSession);
                return result;
            }, ct);

        public UniTask<ServiceResult<Unit>> SignInWithPlatformAsync(CancellationToken ct = default) =>
            SingleFlightSignIn(async token =>
            {
                if (!_platform.IsSupported)
                    return ServiceResult.Fail(ServiceError.Unsupported("No platform sign-in provider available."));

                var hadSession = _sdk.SessionTokenExists;
                var tokenResult = await _platform.AcquireTokenAsync(token);
                if (tokenResult.IsFailure)
                    return ServiceResult.Fail(tokenResult.Error);

                var result = await _retry.ExecuteAsync<Unit>("Auth.SignInWithPlatform", async attemptCt =>
                {
                    try
                    {
                        await ExchangeWithUgsAsync(tokenResult.Value, link: false, attemptCt);
                        return ServiceResult.Ok();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        var error = AuthErrorMapper.Map(ex);
                        _logger.Error(Tag, $"{_platform.Provider} sign-in failed: {error}", ex);
                        return ServiceResult.Fail(error);
                    }
                }, token);

                if (result.IsSuccess)
                    PublishSignedIn(isNewPlayer: !hadSession);
                return result;
            }, ct);

        public async UniTask<ServiceResult<Unit>> LinkWithPlatformAsync(CancellationToken ct = default)
        {
            if (!_sdk.IsSignedIn)
                return ServiceResult.Fail(ServiceError.NotSignedIn(ModuleName));
            if (!_platform.IsSupported)
                return ServiceResult.Fail(ServiceError.Unsupported("No platform sign-in provider available."));

            var tokenResult = await _platform.AcquireTokenAsync(ct);
            if (tokenResult.IsFailure)
                return ServiceResult.Fail(tokenResult.Error);

            return await _retry.ExecuteAsync<Unit>("Auth.LinkWithPlatform", async attemptCt =>
            {
                try
                {
                    await ExchangeWithUgsAsync(tokenResult.Value, link: true, attemptCt);
                    return ServiceResult.Ok();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = AuthErrorMapper.Map(ex);
                    _logger.Error(Tag, $"{_platform.Provider} account link failed: {error}", ex);
                    return ServiceResult.Fail(error);
                }
            }, ct);
        }

        public async UniTask<ServiceResult<Unit>> SignOutAsync(CancellationToken ct = default)
        {
            await UniTask.SwitchToMainThread(ct);
            if (!_sdk.IsSignedIn)
                return ServiceResult.Ok();

            _sdk.SignOut(clearCredentials: true);
            return ServiceResult.Ok();
        }

        private UniTask ExchangeWithUgsAsync(string token, bool link, CancellationToken ct) =>
            (_platform.Provider, link) switch
            {
                (PlatformProvider.GooglePlayGames, false) => _sdk.SignInWithGooglePlayGamesAsync(token, ct),
                (PlatformProvider.GooglePlayGames, true) => _sdk.LinkWithGooglePlayGamesAsync(token, ct),
                (PlatformProvider.Apple, false) => _sdk.SignInWithAppleAsync(token, ct),
                (PlatformProvider.Apple, true) => _sdk.LinkWithAppleAsync(token, ct),
                _ => throw new InvalidOperationException($"No UGS exchange for provider {_platform.Provider}."),
            };

        private async UniTask<ServiceResult<Unit>> SingleFlightSignIn(
            Func<CancellationToken, UniTask<ServiceResult<Unit>>> operation,
            CancellationToken ct)
        {
            if (_inFlightSignIn.HasValue)
                return await _inFlightSignIn.Value;

            var task = operation(ct).Preserve();
            _inFlightSignIn = task;
            try
            {
                return await task;
            }
            finally
            {
                _inFlightSignIn = null;
            }
        }

        private void PublishSignedIn(bool isNewPlayer)
        {
            _logger.Info(Tag, $"Signed in (playerId: {PlayerId}, new: {isNewPlayer}).");
            _events.Publish(new PlayerSignedInEvent(PlayerId, isNewPlayer));
        }

        private void OnSdkSignedOut() => _events.Publish(new PlayerSignedOutEvent());

        private void OnSdkExpired()
        {
            _events.Publish(new SessionExpiredEvent());
            if (!_initialized)
                return;

            UniTask.Void(async () =>
            {
                _logger.Warning(Tag, "Session expired — attempting automatic re-sign-in.");
                // In PlatformRequired mode an expired session must not degrade into a fresh
                // anonymous account — go back through the platform exchange instead.
                if (_options.SignInMode == PlatformSignInMode.PlatformRequired && _platform.IsSupported)
                    await SignInWithPlatformAsync(CancellationToken.None);
                else
                    await SignInAnonymouslyAsync(CancellationToken.None);
            });
        }

        public void Dispose()
        {
            _sdk.SignedOut -= OnSdkSignedOut;
            _sdk.Expired -= OnSdkExpired;
        }
    }
}
