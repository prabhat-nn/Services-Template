#nullable enable
using System.Collections.Generic;

namespace Nexenova.Services.Core
{
    /// <summary>
    /// Supplies the IAP product catalog. The Purchasing module ships a settings-backed
    /// default; the Unified composition root swaps in a Remote Config–backed source
    /// (with the settings catalog as fallback) when Remote Config is enabled.
    /// </summary>
    internal interface ICatalogSource
    {
        IReadOnlyList<IapProductDefinition> GetCatalog();
    }
}
