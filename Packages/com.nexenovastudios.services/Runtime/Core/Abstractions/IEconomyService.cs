#nullable enable
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services
{
    public readonly struct CurrencyBalance
    {
        public string CurrencyId { get; }
        public long Amount { get; }
        public CurrencyBalance(string currencyId, long amount)
        {
            CurrencyId = currencyId;
            Amount = amount;
        }
    }

    public readonly struct InventoryItem
    {
        public string ItemId { get; }
        /// <summary>UGS players-inventory-item instance id.</summary>
        public string InstanceId { get; }
        public InventoryItem(string itemId, string instanceId)
        {
            ItemId = itemId;
            InstanceId = instanceId;
        }
    }

    /// <summary>
    /// Audit tag carried by every economy mutation, e.g. "level_reward", "ad_reward".
    /// The "iap:" prefix is reserved for the internal IAP grant pipeline and rejected
    /// on the public API.
    /// </summary>
    public readonly struct TransactionReason
    {
        public const string IapPrefix = "iap:";

        public string Source { get; }
        public TransactionReason(string source) { Source = source ?? string.Empty; }
        public override string ToString() => Source;
    }

    /// <summary>
    /// Player currencies and inventory. Balances are cached and refreshed after every
    /// mutation; the server remains the authority. All mutations are validated and capped.
    /// </summary>
    public interface IEconomyService
    {
        UniTask<ServiceResult<IReadOnlyList<CurrencyBalance>>> GetBalancesAsync(CancellationToken ct = default);

        UniTask<ServiceResult<CurrencyBalance>> AddCurrencyAsync(string currencyId, long amount, TransactionReason reason, CancellationToken ct = default);

        UniTask<ServiceResult<CurrencyBalance>> SpendCurrencyAsync(string currencyId, long amount, TransactionReason reason, CancellationToken ct = default);

        UniTask<ServiceResult<IReadOnlyList<InventoryItem>>> GetInventoryAsync(CancellationToken ct = default);

        UniTask<ServiceResult<InventoryItem>> AddInventoryItemAsync(string itemId, CancellationToken ct = default);
    }
}
