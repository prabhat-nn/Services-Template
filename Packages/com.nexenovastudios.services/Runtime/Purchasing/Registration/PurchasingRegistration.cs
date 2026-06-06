#nullable enable
using Nexenova.Services.Core;
using VContainer;

namespace Nexenova.Services.Purchasing
{
    public static class PurchasingRegistration
    {
        /// <param name="builder">Container builder.</param>
        /// <param name="registerDefaultCatalogSource">
        /// Pass false when the composition root registers its own ICatalogSource
        /// (e.g. the Remote Config–backed source in ServicesLifetimeScope).
        /// </param>
        public static IContainerBuilder RegisterNexenovaPurchasing(this IContainerBuilder builder, bool registerDefaultCatalogSource = true)
        {
            builder.Register<IStoreSdk, UnityIapStoreSdk>(Lifetime.Singleton);
            builder.Register<IReceiptValidator, PassThroughReceiptValidator>(Lifetime.Singleton);

            if (registerDefaultCatalogSource)
                builder.Register<ICatalogSource, SettingsCatalogSource>(Lifetime.Singleton);

            builder.Register<PurchaseService>(Lifetime.Singleton)
                .As<IPurchaseService>()
                .As<IServiceModule>();

            return builder;
        }
    }
}
