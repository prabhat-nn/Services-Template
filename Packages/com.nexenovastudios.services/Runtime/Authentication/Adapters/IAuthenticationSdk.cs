#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Services.Authentication;

namespace Nexenova.Services.Authentication
{
    /// <summary>Logic-free adapter over the static UGS Authentication SDK (testability seam).</summary>
    internal interface IAuthenticationSdk
    {
        bool IsSignedIn { get; }
        bool SessionTokenExists { get; }
        string PlayerId { get; }

        UniTask SignInAnonymouslyAsync(CancellationToken ct);
        UniTask SignInWithGooglePlayGamesAsync(string authCode, CancellationToken ct);
        UniTask SignInWithAppleAsync(string idToken, CancellationToken ct);
        UniTask LinkWithGooglePlayGamesAsync(string authCode, CancellationToken ct);
        UniTask LinkWithAppleAsync(string idToken, CancellationToken ct);
        void SignOut(bool clearCredentials);

        event Action SignedIn;
        event Action SignedOut;
        event Action Expired;
    }

    internal sealed class AuthenticationSdk : IAuthenticationSdk, IDisposable
    {
        private IAuthenticationService Sdk => AuthenticationService.Instance;

        public bool IsSignedIn => Sdk.IsSignedIn;
        public bool SessionTokenExists => Sdk.SessionTokenExists;
        public string PlayerId => Sdk.PlayerId ?? string.Empty;

        public event Action? SignedIn;
        public event Action? SignedOut;
        public event Action? Expired;

        private bool _hooked;

        public AuthenticationSdk()
        {
            Sdk.SignedIn += OnSignedIn;
            Sdk.SignedOut += OnSignedOut;
            Sdk.Expired += OnExpired;
            _hooked = true;
        }

        public UniTask SignInAnonymouslyAsync(CancellationToken ct) =>
            Sdk.SignInAnonymouslyAsync().AsUniTask().AttachExternalCancellation(ct);

        public UniTask SignInWithGooglePlayGamesAsync(string authCode, CancellationToken ct) =>
            Sdk.SignInWithGooglePlayGamesAsync(authCode).AsUniTask().AttachExternalCancellation(ct);

        public UniTask SignInWithAppleAsync(string idToken, CancellationToken ct) =>
            Sdk.SignInWithAppleAsync(idToken).AsUniTask().AttachExternalCancellation(ct);

        public UniTask LinkWithGooglePlayGamesAsync(string authCode, CancellationToken ct) =>
            Sdk.LinkWithGooglePlayGamesAsync(authCode).AsUniTask().AttachExternalCancellation(ct);

        public UniTask LinkWithAppleAsync(string idToken, CancellationToken ct) =>
            Sdk.LinkWithAppleAsync(idToken).AsUniTask().AttachExternalCancellation(ct);

        public void SignOut(bool clearCredentials) => Sdk.SignOut(clearCredentials);

        private void OnSignedIn() => SignedIn?.Invoke();
        private void OnSignedOut() => SignedOut?.Invoke();
        private void OnExpired() => Expired?.Invoke();

        public void Dispose()
        {
            if (!_hooked) return;
            _hooked = false;
            Sdk.SignedIn -= OnSignedIn;
            Sdk.SignedOut -= OnSignedOut;
            Sdk.Expired -= OnExpired;
        }
    }
}
