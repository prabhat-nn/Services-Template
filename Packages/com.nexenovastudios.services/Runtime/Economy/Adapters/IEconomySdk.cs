#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Services.Economy;
using Unity.Services.Economy.Model;
// Aliased: our own EconomyService in this namespace would otherwise shadow the SDK entry point.
using UgsEconomy = Unity.Services.Economy.EconomyService;

namespace Nexenova.Services.Economy
{
    /// <summary>Logic-free adapter over the static UGS Economy SDK (testability seam).</summary>
    internal interface IEconomySdk
    {
        UniTask<IReadOnlyList<CurrencyBalance>> GetBalancesAsync(CancellationToken ct);
        UniTask<CurrencyBalance> IncrementBalanceAsync(string currencyId, int amount, CancellationToken ct);
        UniTask<CurrencyBalance> DecrementBalanceAsync(string currencyId, int amount, CancellationToken ct);
        UniTask<IReadOnlyList<InventoryItem>> GetInventoryAsync(CancellationToken ct);
        UniTask<InventoryItem> AddInventoryItemAsync(string itemId, CancellationToken ct);
    }

    internal sealed class EconomySdk : IEconomySdk
    {
        private const int ItemsPerFetch = 100;

        public async UniTask<IReadOnlyList<CurrencyBalance>> GetBalancesAsync(CancellationToken ct)
        {
            var result = await UgsEconomy.Instance.PlayerBalances
                .GetBalancesAsync(new GetBalancesOptions { ItemsPerFetch = ItemsPerFetch })
                .AsUniTask().AttachExternalCancellation(ct);
            return result.Balances.Select(b => new CurrencyBalance(b.CurrencyId, b.Balance)).ToList();
        }

        public async UniTask<CurrencyBalance> IncrementBalanceAsync(string currencyId, int amount, CancellationToken ct)
        {
            var balance = await UgsEconomy.Instance.PlayerBalances
                .IncrementBalanceAsync(currencyId, amount)
                .AsUniTask().AttachExternalCancellation(ct);
            return new CurrencyBalance(balance.CurrencyId, balance.Balance);
        }

        public async UniTask<CurrencyBalance> DecrementBalanceAsync(string currencyId, int amount, CancellationToken ct)
        {
            var balance = await UgsEconomy.Instance.PlayerBalances
                .DecrementBalanceAsync(currencyId, amount)
                .AsUniTask().AttachExternalCancellation(ct);
            return new CurrencyBalance(balance.CurrencyId, balance.Balance);
        }

        public async UniTask<IReadOnlyList<InventoryItem>> GetInventoryAsync(CancellationToken ct)
        {
            var result = await UgsEconomy.Instance.PlayerInventory
                .GetInventoryAsync(new GetInventoryOptions { ItemsPerFetch = ItemsPerFetch })
                .AsUniTask().AttachExternalCancellation(ct);
            return result.PlayersInventoryItems
                .Select(i => new InventoryItem(i.InventoryItemId, i.PlayersInventoryItemId))
                .ToList();
        }

        public async UniTask<InventoryItem> AddInventoryItemAsync(string itemId, CancellationToken ct)
        {
            var item = await UgsEconomy.Instance.PlayerInventory
                .AddInventoryItemAsync(itemId)
                .AsUniTask().AttachExternalCancellation(ct);
            return new InventoryItem(item.InventoryItemId, item.PlayersInventoryItemId);
        }
    }
}
