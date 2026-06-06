#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nexenova.Services.Economy;

namespace Nexenova.Services.Tests
{
    internal sealed class FakeEconomySdk : IEconomySdk
    {
        public readonly Dictionary<string, long> Balances = new();
        public readonly List<InventoryItem> Inventory = new();
        public readonly List<(string currencyId, int amount)> Increments = new();
        public Exception? ThrowOnNextCall { get; set; }

        public UniTask<IReadOnlyList<CurrencyBalance>> GetBalancesAsync(CancellationToken ct)
        {
            MaybeThrow();
            return UniTask.FromResult<IReadOnlyList<CurrencyBalance>>(
                Balances.Select(kv => new CurrencyBalance(kv.Key, kv.Value)).ToList());
        }

        public UniTask<CurrencyBalance> IncrementBalanceAsync(string currencyId, int amount, CancellationToken ct)
        {
            MaybeThrow();
            Increments.Add((currencyId, amount));
            Balances.TryGetValue(currencyId, out var current);
            Balances[currencyId] = current + amount;
            return UniTask.FromResult(new CurrencyBalance(currencyId, Balances[currencyId]));
        }

        public UniTask<CurrencyBalance> DecrementBalanceAsync(string currencyId, int amount, CancellationToken ct)
        {
            MaybeThrow();
            Balances.TryGetValue(currencyId, out var current);
            Balances[currencyId] = current - amount;
            return UniTask.FromResult(new CurrencyBalance(currencyId, Balances[currencyId]));
        }

        public UniTask<IReadOnlyList<InventoryItem>> GetInventoryAsync(CancellationToken ct)
        {
            MaybeThrow();
            return UniTask.FromResult<IReadOnlyList<InventoryItem>>(Inventory.ToList());
        }

        public UniTask<InventoryItem> AddInventoryItemAsync(string itemId, CancellationToken ct)
        {
            MaybeThrow();
            var item = new InventoryItem(itemId, Guid.NewGuid().ToString("N"));
            Inventory.Add(item);
            return UniTask.FromResult(item);
        }

        private void MaybeThrow()
        {
            if (ThrowOnNextCall == null)
                return;
            var ex = ThrowOnNextCall;
            ThrowOnNextCall = null;
            throw ex;
        }
    }

    internal sealed class FakeMonotonicClock : IMonotonicClock
    {
        public double NowSeconds { get; set; }
    }

    internal sealed class FakeProcessedTransactionStore : IProcessedTransactionStore
    {
        public readonly HashSet<string> Ids = new();
        public bool Contains(string transactionId) => Ids.Contains(transactionId);
        public void Add(string transactionId) => Ids.Add(transactionId);
    }
}
