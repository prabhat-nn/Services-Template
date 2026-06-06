#nullable enable
using System.Collections.Generic;
using Nexenova.Services.Core;

namespace Nexenova.Services.Purchasing
{
    /// <summary>Fallback catalog source: the products authored on ServicesSettings.</summary>
    internal sealed class SettingsCatalogSource : ICatalogSource
    {
        private readonly PurchasingOptions _options;

        public SettingsCatalogSource(PurchasingOptions options)
        {
            _options = options;
        }

        public IReadOnlyList<IapProductDefinition> GetCatalog() => _options.DefaultCatalog;
    }
}
