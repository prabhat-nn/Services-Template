#nullable enable
using VContainer;

namespace Nexenova.Services.RemoteConfig
{
    public static class RemoteConfigRegistration
    {
        public static IContainerBuilder RegisterNexenovaRemoteConfig(this IContainerBuilder builder)
        {
            builder.RegisterInstance(PackageConfigRules.Create());
            builder.Register<ConfigValidator>(Lifetime.Singleton);
            builder.Register<IRemoteConfigSdk, RemoteConfigSdk>(Lifetime.Singleton);

            builder.Register<RemoteConfigServiceModule>(Lifetime.Singleton)
                .As<IRemoteConfigService>()
                .As<IServiceModule>();

            return builder;
        }
    }
}
