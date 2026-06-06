#nullable enable
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services
{
    public enum ServicesState
    {
        NotStarted,
        Initializing,
        /// <summary>All registered modules initialized.</summary>
        Ready,
        /// <summary>Required modules initialized; one or more optional modules failed.</summary>
        Degraded,
        /// <summary>Platform init or a required module failed.</summary>
        Failed,
    }

    /// <summary>
    /// Owns the staged boot sequence. Started explicitly by the game (or the
    /// built-in ServicesBootController) — never from Awake magic inside the package.
    /// </summary>
    public interface IServicesBootstrap
    {
        ServicesState State { get; }
        IReadOnlyList<string> FailedModules { get; }

        /// <summary>Idempotent: concurrent/repeat calls await the same in-flight boot.</summary>
        UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct = default);

        /// <summary>Completes when the state becomes Ready or Degraded. Fails only on Failed.</summary>
        UniTask WaitUntilReadyAsync(CancellationToken ct = default);
    }
}
