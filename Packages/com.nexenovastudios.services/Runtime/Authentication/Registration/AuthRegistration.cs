#nullable enable
using VContainer;

namespace Nexenova.Services.Authentication
{
    public static class AuthRegistration
    {
        public static IContainerBuilder RegisterNexenovaAuthentication(this IContainerBuilder builder)
        {
            builder.Register<IAuthenticationSdk, AuthenticationSdk>(Lifetime.Singleton);

#if NEX_GPGS && UNITY_ANDROID
            builder.Register<IPlatformSignInProvider, GpgsSignInProvider>(Lifetime.Singleton);
#elif NEX_APPLE_SIGNIN && UNITY_IOS
            builder.Register<IPlatformSignInProvider, AppleSignInProvider>(Lifetime.Singleton);
#else
            builder.Register<IPlatformSignInProvider, NullPlatformSignInProvider>(Lifetime.Singleton);
#endif

            builder.Register<AuthService>(Lifetime.Singleton)
                .As<IAuthService>()
                .As<IServiceModule>();

            return builder;
        }
    }
}
