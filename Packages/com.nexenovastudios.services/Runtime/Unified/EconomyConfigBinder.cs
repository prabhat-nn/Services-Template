#nullable enable
using System;
using Nexenova.Services.Core;
using VContainer.Unity;

namespace Nexenova.Services
{
    /// <summary>
    /// Composition-root glue: when Remote Config is fetched, pushes the economy cap
    /// overrides (<see cref="ConfigKeys.EconomyMaxGrantPerCall"/>, …) into the Economy
    /// module. Lives here so Economy and RemoteConfig never reference each other.
    /// </summary>
    internal sealed class EconomyConfigBinder : IStartable, IDisposable
    {
        private readonly IRemoteConfigService _remoteConfig;
        private readonly IDynamicEconomyCaps _economyCaps;
        private readonly EconomyOptions _defaults;
        private readonly IEventBus _events;

        private IDisposable? _subscription;

        public EconomyConfigBinder(
            IRemoteConfigService remoteConfig,
            IDynamicEconomyCaps economyCaps,
            EconomyOptions defaults,
            IEventBus events)
        {
            _remoteConfig = remoteConfig;
            _economyCaps = economyCaps;
            _defaults = defaults;
            _events = events;
        }

        public void Start()
        {
            _subscription = _events.Subscribe<RemoteConfigFetchedEvent>(_ => ApplyCaps());
        }

        private void ApplyCaps()
        {
            var perCall = _remoteConfig.GetLong(ConfigKeys.EconomyMaxGrantPerCall, _defaults.MaxGrantPerCall);
            var perMinute = _remoteConfig.GetLong(ConfigKeys.EconomyMaxGrantedPerMinute, _defaults.MaxGrantedPerMinute);
            _economyCaps.UpdateCaps(perCall, perMinute);
        }

        public void Dispose() => _subscription?.Dispose();
    }
}
