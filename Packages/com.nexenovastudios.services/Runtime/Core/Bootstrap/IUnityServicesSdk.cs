#nullable enable
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Core.Environments;

namespace Nexenova.Services.Core
{
    /// <summary>Adapter over the static UnityServices entry point (testability seam, zero logic).</summary>
    internal interface IUnityServicesSdk
    {
        bool IsInitialized { get; }
        UniTask InitializeAsync(string environment, CancellationToken ct);
    }

    internal sealed class UnityServicesSdk : IUnityServicesSdk
    {
        public bool IsInitialized => UnityServices.State == ServicesInitializationState.Initialized;

        public async UniTask InitializeAsync(string environment, CancellationToken ct)
        {
            var options = new InitializationOptions().SetEnvironmentName(environment);
            await UnityServices.InitializeAsync(options).AsUniTask().AttachExternalCancellation(ct);
        }
    }
}
