#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nexenova.Services.RemoteConfig;

namespace Nexenova.Services.Tests
{
    internal sealed class FakeRemoteConfigSdk : IRemoteConfigSdk
    {
        public readonly Dictionary<string, object> Values = new();
        public Exception? ThrowOnFetch { get; set; }
        public string? LastAppVersion { get; private set; }

        public UniTask FetchAsync(string appVersion, CancellationToken ct)
        {
            LastAppVersion = appVersion;
            if (ThrowOnFetch != null)
                throw ThrowOnFetch;
            return UniTask.CompletedTask;
        }

        public bool HasKey(string key) => Values.ContainsKey(key);

        public int GetInt(string key, int defaultValue) => Values.TryGetValue(key, out var v) ? Convert.ToInt32(v) : defaultValue;

        public long GetLong(string key, long defaultValue) => Values.TryGetValue(key, out var v) ? Convert.ToInt64(v) : defaultValue;

        public float GetFloat(string key, float defaultValue) => Values.TryGetValue(key, out var v) ? Convert.ToSingle(v) : defaultValue;

        public bool GetBool(string key, bool defaultValue) => Values.TryGetValue(key, out var v) ? (bool)v : defaultValue;

        public string GetString(string key, string defaultValue) => Values.TryGetValue(key, out var v) ? (string)v : defaultValue;

        public string GetJson(string key, string defaultValue) => Values.TryGetValue(key, out var v) ? (string)v : defaultValue;
    }
}
