#nullable enable
using Nexenova.Services.Core;
using VContainer;
using VContainer.Unity;

namespace Nexenova.Services.Economy
{
    public static class EconomyRegistration
    {
        public static IContainerBuilder RegisterNexenovaEconomy(this IContainerBuilder builder)
        {
            builder.Register<IEconomySdk, EconomySdk>(Lifetime.Singleton);
            builder.Register<IMonotonicClock, SystemMonotonicClock>(Lifetime.Singleton);
            builder.Register<GrantRateLimiter>(Lifetime.Singleton);
            builder.Register<IProcessedTransactionStore, FileProcessedTransactionStore>(Lifetime.Singleton);

            builder.Register<EconomyService>(Lifetime.Singleton)
                .As<IEconomyService>()
                .As<IServiceModule>()
                .As<IDynamicEconomyCaps>()
                .AsSelf();

            builder.RegisterEntryPoint<IapGrantProcessor>();

            return builder;
        }
    }
}
