#nullable enable
using VContainer;

namespace Nexenova.Services.CloudSave
{
    public static class CloudSaveRegistration
    {
        public static IContainerBuilder RegisterNexenovaCloudSave(this IContainerBuilder builder)
        {
            builder.Register<ICloudSaveSdk, CloudSaveSdk>(Lifetime.Singleton);
            builder.Register<ILocalSaveCache, FileLocalSaveCache>(Lifetime.Singleton);
            builder.Register<ISaveMigrator, PassThroughSaveMigrator>(Lifetime.Singleton);

            builder.Register<CloudSaveService>(Lifetime.Singleton)
                .As<ICloudSaveService>()
                .As<IServiceModule>();

            return builder;
        }
    }
}
