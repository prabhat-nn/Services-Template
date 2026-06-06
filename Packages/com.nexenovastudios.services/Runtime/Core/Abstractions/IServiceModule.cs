#nullable enable
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services
{
    /// <summary>Boot ordering. Stages run sequentially; modules within a stage initialize in parallel.</summary>
    public enum InitializationStage
    {
        /// <summary>UnityServices.InitializeAsync — owned by Core itself, not a module.</summary>
        Platform = 0,
        /// <summary>Authentication.</summary>
        Identity = 1,
        /// <summary>Remote Config, Cloud Save, Economy.</summary>
        Data = 2,
        /// <summary>Purchasing.</summary>
        Monetization = 3,
    }

    /// <summary>
    /// Lifecycle contract implemented by every service module in addition to its functional interface.
    /// </summary>
    public interface IServiceModule
    {
        string ModuleName { get; }
        InitializationStage Stage { get; }

        /// <summary>Required module failure fails the whole boot; optional failure degrades gracefully.</summary>
        bool IsRequired { get; }

        UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct);
    }
}
