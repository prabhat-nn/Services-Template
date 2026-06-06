#nullable enable
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Services.RemoteConfig;

namespace Nexenova.Services.RemoteConfig
{
    /// <summary>Logic-free adapter over the static UGS Remote Config SDK (testability seam).</summary>
    internal interface IRemoteConfigSdk
    {
        UniTask FetchAsync(string appVersion, CancellationToken ct);

        bool HasKey(string key);
        int GetInt(string key, int defaultValue);
        long GetLong(string key, long defaultValue);
        float GetFloat(string key, float defaultValue);
        bool GetBool(string key, bool defaultValue);
        string GetString(string key, string defaultValue);
        string GetJson(string key, string defaultValue);
    }

    internal sealed class RemoteConfigSdk : IRemoteConfigSdk
    {
        private struct UserAttributes { }

        private struct AppAttributes
        {
            // ReSharper disable once InconsistentNaming — serialized attribute name in the RC payload.
            public string appVersion;
        }

        public UniTask FetchAsync(string appVersion, CancellationToken ct) =>
            RemoteConfigService.Instance
                .FetchConfigsAsync(new UserAttributes(), new AppAttributes { appVersion = appVersion })
                .AsUniTask().AttachExternalCancellation(ct);

        public bool HasKey(string key) => RemoteConfigService.Instance.appConfig.HasKey(key);

        public int GetInt(string key, int defaultValue) => RemoteConfigService.Instance.appConfig.GetInt(key, defaultValue);

        public long GetLong(string key, long defaultValue) => RemoteConfigService.Instance.appConfig.GetLong(key, defaultValue);

        public float GetFloat(string key, float defaultValue) => RemoteConfigService.Instance.appConfig.GetFloat(key, defaultValue);

        public bool GetBool(string key, bool defaultValue) => RemoteConfigService.Instance.appConfig.GetBool(key, defaultValue);

        public string GetString(string key, string defaultValue) => RemoteConfigService.Instance.appConfig.GetString(key, defaultValue);

        public string GetJson(string key, string defaultValue) => RemoteConfigService.Instance.appConfig.GetJson(key, defaultValue);
    }
}
