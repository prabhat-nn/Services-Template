#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Nexenova.Services.Economy
{
    /// <summary>
    /// Persisted set of processed IAP transaction ids — makes grants idempotent across
    /// crashes (unconfirmed store transactions are redelivered on next launch).
    /// </summary>
    internal interface IProcessedTransactionStore
    {
        bool Contains(string transactionId);
        void Add(string transactionId);
    }

    internal sealed class FileProcessedTransactionStore : IProcessedTransactionStore
    {
        private const int MaxEntries = 500;
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
        };

        private readonly string _path;
        private readonly IServiceLogger _logger;
        private List<string>? _ids;

        public FileProcessedTransactionStore(IServiceLogger logger)
        {
            _logger = logger;
            _path = Path.Combine(Application.persistentDataPath, "nex_services", "processed_transactions.json");
        }

        public bool Contains(string transactionId)
        {
            EnsureLoaded();
            return _ids!.Contains(transactionId);
        }

        public void Add(string transactionId)
        {
            EnsureLoaded();
            if (_ids!.Contains(transactionId))
                return;

            _ids.Add(transactionId);
            while (_ids.Count > MaxEntries)
                _ids.RemoveAt(0);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonConvert.SerializeObject(_ids, JsonSettings));
            }
            catch (Exception ex)
            {
                _logger.Warning("Economy", $"Failed to persist processed transactions: {ex.Message}");
            }
        }

        private void EnsureLoaded()
        {
            if (_ids != null)
                return;

            try
            {
                _ids = File.Exists(_path)
                    ? JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(_path), JsonSettings) ?? new List<string>()
                    : new List<string>();
            }
            catch (Exception ex)
            {
                _logger.Warning("Economy", $"Failed to load processed transactions (starting fresh): {ex.Message}");
                _ids = new List<string>();
            }
        }
    }
}
