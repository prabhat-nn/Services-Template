#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using VContainer.Unity;

namespace Nexenova.Services.Economy
{
    /// <summary>
    /// Subscribes to <see cref="PurchaseCompletedEvent"/> (published by Purchasing after
    /// receipt validation), applies the currency grants through the internal IAP pipeline,
    /// and acknowledges with <see cref="PurchaseGrantProcessedEvent"/> so the store
    /// transaction can be confirmed. Duplicate transactions are acknowledged as success
    /// without re-granting.
    /// </summary>
    internal sealed class IapGrantProcessor : IStartable, IDisposable
    {
        private const string Tag = "Economy";

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
        };

        private sealed class CurrencyGrant
        {
            public string CurrencyId { get; set; } = string.Empty;
            public long Amount { get; set; }
        }

        private readonly EconomyService _economy;
        private readonly IProcessedTransactionStore _processed;
        private readonly IEventBus _events;
        private readonly IServiceLogger _logger;

        private IDisposable? _subscription;

        public IapGrantProcessor(
            EconomyService economy,
            IProcessedTransactionStore processed,
            IEventBus events,
            IServiceLogger logger)
        {
            _economy = economy;
            _processed = processed;
            _events = events;
            _logger = logger;
        }

        public void Start()
        {
            _subscription = _events.Subscribe<PurchaseCompletedEvent>(evt =>
                ProcessAsync(evt, CancellationToken.None).Forget());
        }

        private async UniTask ProcessAsync(PurchaseCompletedEvent evt, CancellationToken ct)
        {
            if (_processed.Contains(evt.TransactionId))
            {
                _logger.Info(Tag, $"Transaction {evt.TransactionId} already granted — acknowledging duplicate.");
                _events.Publish(new PurchaseGrantProcessedEvent(evt.TransactionId, success: true));
                return;
            }

            List<CurrencyGrant>? grants;
            try
            {
                grants = JsonConvert.DeserializeObject<List<CurrencyGrant>>(evt.GrantsJson ?? "[]", JsonSettings);
            }
            catch (Exception ex)
            {
                _logger.Error(Tag, $"Invalid grants JSON for product '{evt.ProductId}' — purchase will not be confirmed.", ex);
                _events.Publish(new PurchaseGrantProcessedEvent(evt.TransactionId, success: false));
                return;
            }

            var allSucceeded = true;
            foreach (var grant in grants ?? new List<CurrencyGrant>())
            {
                if (string.IsNullOrWhiteSpace(grant.CurrencyId) || grant.Amount <= 0)
                {
                    _logger.Warning(Tag, $"Skipping malformed grant for product '{evt.ProductId}' ({grant.CurrencyId}: {grant.Amount}).");
                    allSucceeded = false;
                    continue;
                }

                var result = await _economy.AddCurrencyFromIapAsync(grant.CurrencyId, grant.Amount, evt.TransactionId, ct);
                if (result.IsFailure)
                    allSucceeded = false;
            }

            if (allSucceeded)
                _processed.Add(evt.TransactionId);

            _events.Publish(new PurchaseGrantProcessedEvent(evt.TransactionId, allSucceeded));
        }

        public void Dispose() => _subscription?.Dispose();
    }
}
