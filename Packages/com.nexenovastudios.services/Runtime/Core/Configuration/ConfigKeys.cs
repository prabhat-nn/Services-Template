#nullable enable

namespace Nexenova.Services
{
    /// <summary>
    /// Every Remote Config key the package consumes — no magic strings at call sites.
    /// Games declare their own key constants the same way.
    /// </summary>
    public static class ConfigKeys
    {
        /// <summary>JSON array of IAP products (see <see cref="IapProductDefinition"/> shape).</summary>
        public const string IapCatalog = "nex_iap_catalog";

        /// <summary>Overrides ServicesSettings economy cap when present.</summary>
        public const string EconomyMaxGrantPerCall = "nex_economy_max_grant_per_call";

        /// <summary>Overrides ServicesSettings economy cap when present.</summary>
        public const string EconomyMaxGrantedPerMinute = "nex_economy_max_granted_per_minute";
    }
}
