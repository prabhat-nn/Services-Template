#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nexenova.Services.CloudSave;

namespace Nexenova.Services.Tests
{
    internal sealed class FakeCloudSaveSdk : ICloudSaveSdk
    {
        public readonly Dictionary<string, (string json, string writeLock)> Remote = new();
        public Exception? ThrowOnNextCall { get; set; }
        public Exception? ThrowAlways { get; set; }
        private int _lockCounter;

        public UniTask<(bool found, string json, string? writeLock)> LoadAsync(string fullKey, CancellationToken ct)
        {
            MaybeThrow();
            return UniTask.FromResult(Remote.TryGetValue(fullKey, out var entry)
                ? (true, entry.json, (string?)entry.writeLock)
                : (false, string.Empty, (string?)null));
        }

        public UniTask<string?> SaveAsync(string fullKey, string json, string? expectedWriteLock, CancellationToken ct)
        {
            MaybeThrow();
            var newLock = (++_lockCounter).ToString();
            Remote[fullKey] = (json, newLock);
            return UniTask.FromResult<string?>(newLock);
        }

        public UniTask DeleteAsync(string fullKey, string? expectedWriteLock, CancellationToken ct)
        {
            MaybeThrow();
            Remote.Remove(fullKey);
            return UniTask.CompletedTask;
        }

        public UniTask<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct)
        {
            MaybeThrow();
            return UniTask.FromResult<IReadOnlyList<string>>(Remote.Keys.ToList());
        }

        private void MaybeThrow()
        {
            if (ThrowAlways != null)
                throw ThrowAlways;
            if (ThrowOnNextCall == null)
                return;
            var ex = ThrowOnNextCall;
            ThrowOnNextCall = null;
            throw ex;
        }
    }

    internal sealed class FakeLocalSaveCache : ILocalSaveCache
    {
        public readonly Dictionary<string, string> Data = new();
        public readonly HashSet<string> Pending = new();

        public IReadOnlyList<string> Keys => Data.Keys.ToList();
        public IReadOnlyList<string> PendingKeys => Pending.ToList();

        public bool TryRead(string key, out string envelopeJson)
        {
            if (Data.TryGetValue(key, out var json))
            {
                envelopeJson = json;
                return true;
            }
            envelopeJson = string.Empty;
            return false;
        }

        public void Write(string key, string envelopeJson) => Data[key] = envelopeJson;

        public void Delete(string key)
        {
            Data.Remove(key);
            Pending.Remove(key);
        }

        public void MarkPending(string key) => Pending.Add(key);
        public void ClearPending(string key) => Pending.Remove(key);
    }
}
