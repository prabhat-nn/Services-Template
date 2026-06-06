#nullable enable
using System;
using VContainer;

namespace Nexenova.Services.Core
{
    public static class CoreRegistration
    {
        /// <summary>
        /// Registers the event bus, logger, resilience policy, options and the boot
        /// orchestrator. Call before any module registration.
        /// </summary>
        public static IContainerBuilder RegisterNexenovaCore(this IContainerBuilder builder, ServicesSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var core = new CoreOptions(settings);

            builder.RegisterInstance(core);
            builder.RegisterInstance(new AuthOptions(settings));
            builder.RegisterInstance(new EconomyOptions(settings));
            builder.RegisterInstance(new CloudSaveOptions(settings));
            builder.RegisterInstance(new RemoteConfigOptions(settings));
            builder.RegisterInstance(new PurchasingOptions(settings));

            builder.RegisterInstance<IServiceLogger>(new UnityServiceLogger(core.VerboseLogging));
            builder.Register<IEventBus, EventBus>(Lifetime.Singleton);
            builder.Register<IDelayProvider, RealtimeDelayProvider>(Lifetime.Singleton);
            builder.Register(
                resolver => new RetryPolicy(
                    core.MaxRetryAttempts,
                    core.RetryBaseDelay,
                    core.RetryMaxDelay,
                    core.OperationTimeout,
                    resolver.Resolve<IServiceLogger>(),
                    resolver.Resolve<IDelayProvider>()),
                Lifetime.Singleton);

            builder.Register<IUnityServicesSdk, UnityServicesSdk>(Lifetime.Singleton);
            builder.Register<IServicesBootstrap, ServicesInitializer>(Lifetime.Singleton);

            return builder;
        }
    }
}
