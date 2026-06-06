#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Nexenova.Services.CloudSave
{
    /// <summary>
    /// Write-through local cache: reads keep working offline and writes queue for
    /// replay on reconnect. Stores the envelope JSON per key plus a pending-upload set.
    /// </summary>
    internal interface ILocalSaveCache
    {
        bool TryRead(string key, out string envelopeJson);
        void Write(string key, string envelopeJson);
        void Delete(string key);
        IReadOnlyList<string> Keys { get; }

        void MarkPending(string key);
        void ClearPending(string key);
        IReadOnlyList<string> PendingKeys { get; }
    }

    internal sealed class FileLocalSaveCache : ILocalSaveCache
    {
        private readonly string _root;
        private readonly string _pendingPath;
        private readonly IServiceLogger _logger;
        private HashSet<string>? _pending;

        public FileLocalSaveCache(IServiceLogger logger)
        {
            _logger = logger;
            _root = Path.Combine(Application.persistentDataPath, "nex_services", "cloudsave");
            _pendingPath = Path.Combine(_root, "_pending.json");
        }

        public IReadOnlyList<string> Keys
        {
            get
            {
                try
                {
                    if (!Directory.Exists(_root))
                        return Array.Empty<string>();
                    return Directory.GetFiles(_root, "*.json")
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(n => n != null && !n.StartsWith("_"))
                        .Select(Decode)
                        .ToList()!;
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }
        }

        public IReadOnlyList<string> PendingKeys
        {
            get
            {
                EnsurePendingLoaded();
                return _pending!.ToList();
            }
        }

        public bool TryRead(string key, out string envelopeJson)
        {
            envelopeJson = string.Empty;
            try
            {
                var path = PathFor(key);
                if (!File.Exists(path))
                    return false;
                envelopeJson = File.ReadAllText(path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning("CloudSave", $"Local cache read failed for '{key}': {ex.Message}");
                return false;
            }
        }

        public void Write(string key, string envelopeJson)
        {
            try
            {
                Directory.CreateDirectory(_root);
                File.WriteAllText(PathFor(key), envelopeJson);
            }
            catch (Exception ex)
            {
                _logger.Warning("CloudSave", $"Local cache write failed for '{key}': {ex.Message}");
            }
        }

        public void Delete(string key)
        {
            try
            {
                var path = PathFor(key);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.Warning("CloudSave", $"Local cache delete failed for '{key}': {ex.Message}");
            }
            ClearPending(key);
        }

        public void MarkPending(string key)
        {
            EnsurePendingLoaded();
            if (_pending!.Add(key))
                PersistPending();
        }

        public void ClearPending(string key)
        {
            EnsurePendingLoaded();
            if (_pending!.Remove(key))
                PersistPending();
        }

        private void EnsurePendingLoaded()
        {
            if (_pending != null)
                return;
            try
            {
                _pending = File.Exists(_pendingPath)
                    ? JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(_pendingPath), SaveJson.Settings) ?? new HashSet<string>()
                    : new HashSet<string>();
            }
            catch
            {
                _pending = new HashSet<string>();
            }
        }

        private void PersistPending()
        {
            try
            {
                Directory.CreateDirectory(_root);
                File.WriteAllText(_pendingPath, JsonConvert.SerializeObject(_pending, SaveJson.Settings));
            }
            catch (Exception ex)
            {
                _logger.Warning("CloudSave", $"Pending-writes persist failed: {ex.Message}");
            }
        }

        // Keys are validated (no dots/whitespace) but base64 keeps filenames safe regardless.
        private string PathFor(string key) => Path.Combine(_root, Encode(key) + ".json");

        private static string Encode(string key) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(key)).Replace('/', '-');

        private static string Decode(string? name) =>
            name == null ? string.Empty : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(name.Replace('-', '/')));
    }
}
