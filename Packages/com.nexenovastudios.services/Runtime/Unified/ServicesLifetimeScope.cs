#nullable enable
using Nexenova.Services.Authentication;
using Nexenova.Services.CloudSave;
using Nexenova.Services.Core;
using Nexenova.Services.Economy;
using Nexenova.Services.RemoteConfig;
using UnityEngine;
using VContainer;
using VContainer.Unity;
#if NEX_ADDRESSABLES
using UnityEngine.AddressableAssets;
#endif
#if NEX_SERVICES_IAP
using Nexenova.Services.Purchasing;
#endif

namespace Nexenova.Services
{
    /// <summary>
    /// Drop this on a GameObject in the starting scene (with a <see cref="ServicesSettings"/>
    /// asset assigned) and every enabled service module is registered and booted, after which
    /// the configured next scene is loaded.
    /// Games with their own root LifetimeScope can instead call the RegisterNexenova* extensions.
    /// </summary>
    public class ServicesLifetimeScope : LifetimeScope
    {
        /// <summary>Addressables fallback key used when no settings asset is assigned (requires com.unity.addressables).</summary>
        public const string SettingsAddressableKey = "Nexenova/ServicesSettings";

        [SerializeField] private ServicesSettings? settings;

        protected override void Configure(IContainerBuilder builder)
        {
            var resolved = ResolveSettings();
            if (resolved == null)
            {
                Debug.LogError(
                    $"[Nexenova.Boot] No ServicesSettings assigned on {name} and none found at Addressables key " +
                    $"'{SettingsAddressableKey}'. Services will not be registered.");
                return;
            }

            builder.RegisterNexenovaCore(resolved);
            builder.RegisterNexenovaAuthentication();

            if (resolved.EnableEconomy)
                builder.RegisterNexenovaEconomy();
            if (resolved.EnableCloudSave)
                builder.RegisterNexenovaCloudSave();
            if (resolved.EnableRemoteConfig)
                builder.RegisterNexenovaRemoteConfig();
            if (resolved.EnableEconomy && resolved.EnableRemoteConfig)
                builder.RegisterEntryPoint<EconomyConfigBinder>();
#if NEX_SERVICES_IAP
            if (resolved.EnablePurchasing)
            {
                if (resolved.EnableRemoteConfig)
                {
                    builder.RegisterNexenovaPurchasing(registerDefaultCatalogSource: false);
                    builder.Register<Core.ICatalogSource, RemoteConfigCatalogSource>(VContainer.Lifetime.Singleton);
                }
                else
                {
                    builder.RegisterNexenovaPurchasing();
                }
            }
#endif

            builder.RegisterInstance(new BootOptions(resolved));
            builder.RegisterEntryPoint<ServicesBootController>();
        }

        private ServicesSettings? ResolveSettings()
        {
            if (settings != null)
                return settings;

#if NEX_ADDRESSABLES
            try
            {
                var handle = Addressables.LoadAssetAsync<ServicesSettings>(SettingsAddressableKey);
                var asset = handle.WaitForCompletion();
                return asset;
            }
            catch
            {
                return null;
            }
#else
            return null;
#endif
        }
    }
}
