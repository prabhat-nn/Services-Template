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
                .AsSelf(); // resolved concretely by the grant processor (same module)

            builder.RegisterEntryPoint<IapGrantProcessor>();

            return builder;
        }
    }
}
