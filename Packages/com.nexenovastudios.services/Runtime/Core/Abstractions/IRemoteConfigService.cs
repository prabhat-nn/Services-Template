#nullable enable
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services
{
    /// <summary>
    /// Remote-tunable configuration. Getters are synchronous and never fail: before fetch
    /// (or on fetch failure) they return the provided default. Fetched values pass through
    /// declarative validation — out-of-range values are clamped, wrong-typed values fall
    /// back to the default; both are logged.
    /// </summary>
    public interface IRemoteConfigService
    {
        bool IsFetched { get; }

        UniTask<ServiceResult<Unit>> FetchAsync(CancellationToken ct = default);

        int GetInt(string key, int defaultValue);
        long GetLong(string key, long defaultValue);
        float GetFloat(string key, float defaultValue);
        bool GetBool(string key, bool defaultValue);
        string GetString(string key, string defaultValue);

        /// <summary>Typed object configs, deserialized with hardened JSON settings and validated.</summary>
        ServiceResult<T> GetJson<T>(string key);
    }
}
