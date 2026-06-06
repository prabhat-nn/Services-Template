#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
using UnityEngine.Purchasing.Security;
#endif

namespace Nexenova.Services.Purchasing
{
    /// <summary>
    /// Default validator when no tangle data or backend validator is registered:
    /// accepts everything and warns once. Register <see cref="TangleReceiptValidator"/>
    /// (local) or a backend-backed <see cref="IReceiptValidator"/> before shipping.
    /// </summary>
    internal sealed class PassThroughReceiptValidator : IReceiptValidator
    {
        private readonly IServiceLogger _logger;
        private bool _warned;

        public PassThroughReceiptValidator(IServiceLogger logger)
        {
            _logger = logger;
        }

        public UniTask<ServiceResult<Unit>> ValidateAsync(PurchaseReceipt receipt, CancellationToken ct = default)
        {
            if (!_warned)
            {
                _warned = true;
                _logger.Warning("Purchasing",
                    "No receipt validator configured — receipts are NOT being validated. " +
                    "Register a TangleReceiptValidator (obfuscated tangles) or a server-side IReceiptValidator before release.");
            }
            return UniTask.FromResult(ServiceResult.Ok());
        }
    }

    /// <summary>
    /// Local receipt validation with Unity IAP obfuscated tangles. The tangle classes are
    /// generated per-game (Services ▸ In-App Purchasing ▸ Receipt Validation Obfuscator),
    /// so the game supplies their bytes at registration:
    /// <code>
    /// builder.RegisterInstance&lt;IReceiptValidator&gt;(new TangleReceiptValidator(
    ///     GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier, logger));
    /// </code>
    /// Editor and non-store platforms pass automatically (the fake store has no real receipts).
    /// </summary>
    public sealed class TangleReceiptValidator : IReceiptValidator
    {
        private readonly byte[] _googlePlayTangle;
        private readonly byte[] _appleTangle;
        private readonly string _bundleId;
        private readonly IServiceLogger _logger;

        public TangleReceiptValidator(byte[] googlePlayTangle, byte[] appleTangle, string bundleId, IServiceLogger logger)
        {
            _googlePlayTangle = googlePlayTangle;
            _appleTangle = appleTangle;
            _bundleId = bundleId;
            _logger = logger;
        }

        public UniTask<ServiceResult<Unit>> ValidateAsync(PurchaseReceipt receipt, CancellationToken ct = default)
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            try
            {
                var validator = new CrossPlatformValidator(_googlePlayTangle, _appleTangle, _bundleId);
                validator.Validate(receipt.Receipt);
                return UniTask.FromResult(ServiceResult.Ok());
            }
            catch (IAPSecurityException ex)
            {
                _logger.Error("Purchasing", $"Receipt validation FAILED for '{receipt.ProductId}' (tx {receipt.TransactionId}).", ex);
                return UniTask.FromResult(ServiceResult.Fail(
                    ServiceErrorCode.Unauthorized, "Receipt failed local validation.", ex));
            }
            catch (Exception ex)
            {
                _logger.Error("Purchasing", "Receipt validator threw unexpectedly — treating as invalid.", ex);
                return UniTask.FromResult(ServiceResult.Fail(
                    ServiceErrorCode.Unauthorized, "Receipt validation error.", ex));
            }
#else
            return UniTask.FromResult(ServiceResult.Ok());
#endif
        }
    }
}
