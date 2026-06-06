#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Services.CloudSave.Models;
using PlayerDeleteOptions = Unity.Services.CloudSave.Models.Data.Player.DeleteOptions;
using UgsCloudSave = Unity.Services.CloudSave.CloudSaveService;

namespace Nexenova.Services.CloudSave
{
    /// <summary>Logic-free adapter over the static UGS Cloud Save SDK (testability seam).</summary>
    internal interface ICloudSaveSdk
    {
        UniTask<(bool found, string json, string? writeLock)> LoadAsync(string fullKey, CancellationToken ct);

        /// <summary>Saves with optimistic concurrency; returns the new write lock.</summary>
        UniTask<string?> SaveAsync(string fullKey, string json, string? expectedWriteLock, CancellationToken ct);

        UniTask DeleteAsync(string fullKey, string? expectedWriteLock, CancellationToken ct);

        UniTask<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct);
    }

    internal sealed class CloudSaveSdk : ICloudSaveSdk
    {
        public async UniTask<(bool found, string json, string? writeLock)> LoadAsync(string fullKey, CancellationToken ct)
        {
            var items = await UgsCloudSave.Instance.Data.Player
                .LoadAsync(new HashSet<string> { fullKey })
                .AsUniTask().AttachExternalCancellation(ct);

            if (!items.TryGetValue(fullKey, out var item))
                return (false, string.Empty, null);

            return (true, item.Value.GetAs<string>(), item.WriteLock);
        }

        public async UniTask<string?> SaveAsync(string fullKey, string json, string? expectedWriteLock, CancellationToken ct)
        {
            var data = new Dictionary<string, SaveItem> { { fullKey, new SaveItem(json, expectedWriteLock) } };
            var newLocks = await UgsCloudSave.Instance.Data.Player
                .SaveAsync(data)
                .AsUniTask().AttachExternalCancellation(ct);

            return newLocks.TryGetValue(fullKey, out var writeLock) ? writeLock : null;
        }

        public UniTask DeleteAsync(string fullKey, string? expectedWriteLock, CancellationToken ct) =>
            UgsCloudSave.Instance.Data.Player
                .DeleteAsync(fullKey, new PlayerDeleteOptions { WriteLock = expectedWriteLock })
                .AsUniTask().AttachExternalCancellation(ct);

        public async UniTask<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct)
        {
            var keys = await UgsCloudSave.Instance.Data.Player
                .ListAllKeysAsync()
                .AsUniTask().AttachExternalCancellation(ct);
            return keys.Select(k => k.Key).ToList();
        }
    }
}
