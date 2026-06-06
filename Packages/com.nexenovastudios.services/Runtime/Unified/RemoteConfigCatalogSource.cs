#nullable enable
#if NEX_SERVICES_IAP
using System.Collections.Generic;
using System.Linq;
using Nexenova.Services.Core;

namespace Nexenova.Services
{
    /// <summary>
    /// Composition-root glue: serves the IAP catalog from Remote Config
    /// (<see cref="ConfigKeys.IapCatalog"/>) with the ServicesSettings catalog as
    /// fallback. Lives here so Purchasing and RemoteConfig never reference each other.
    /// Catalog shape: [{"ProductId":"gem_pack_1","ProductType":"Consumable","GrantsJson":"[{\"currencyId\":\"GEMS\",\"amount\":100}]"}]
    /// </summary>
    internal sealed class RemoteConfigCatalogSource : ICatalogSource
    {
        private readonly IRemoteConfigService _remoteConfig;
        private readonly PurchasingOptions _options;
        private readonly IServiceLogger _logger;

        public RemoteConfigCatalogSource(IRemoteConfigService remoteConfig, PurchasingOptions options, IServiceLogger logger)
        {
            _remoteConfig = remoteConfig;
            _options = options;
            _logger = logger;
        }

        public IReadOnlyList<IapProductDefinition> GetCatalog()
        {
            var remote = _remoteConfig.GetJson<List<IapProductDefinition>>(ConfigKeys.IapCatalog);
            if (remote.IsSuccess && remote.Value.Count > 0)
            {
                var valid = remote.Value.Where(p => !string.IsNullOrWhiteSpace(p.ProductId)).ToList();
                if (valid.Count > 0)
                {
                    _logger.Info("Purchasing", $"Using Remote Config IAP catalog ({valid.Count} products).");
                    return valid;
                }
            }

            _logger.Info("Purchasing", "Using ServicesSettings IAP catalog (Remote Config catalog missing or invalid).");
            return _options.DefaultCatalog;
        }
    }
}
#endif
