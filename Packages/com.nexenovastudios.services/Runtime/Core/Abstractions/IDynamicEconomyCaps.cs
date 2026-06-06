#nullable enable

namespace Nexenova.Services.Core
{
    /// <summary>
    /// Composition-root seam: lets the Unified assembly push Remote Config cap overrides
    /// into the Economy module without the two modules referencing each other.
    /// </summary>
    internal interface IDynamicEconomyCaps
    {
        void UpdateCaps(long maxGrantPerCall, long maxGrantedPerMinute);
    }
}
