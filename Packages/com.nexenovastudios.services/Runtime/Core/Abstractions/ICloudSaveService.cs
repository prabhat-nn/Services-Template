#nullable enable
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services
{
    /// <summary>
    /// Versioned, conflict-aware player data persistence with an offline write-through cache.
    /// Keys must not contain '.' or whitespace; they are namespaced internally.
    /// </summary>
    public interface ICloudSaveService
    {
        UniTask<ServiceResult<T>> LoadAsync<T>(string key, CancellationToken ct = default);

        UniTask<ServiceResult<Unit>> SaveAsync<T>(string key, T value, CancellationToken ct = default);

        UniTask<ServiceResult<Unit>> DeleteAsync(string key, CancellationToken ct = default);

        UniTask<ServiceResult<IReadOnlyList<string>>> ListKeysAsync(CancellationToken ct = default);
    }
}
