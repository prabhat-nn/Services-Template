#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Nexenova.Services
{
    /// <summary>
    /// VContainer entry point started automatically by <see cref="ServicesLifetimeScope"/>:
    /// boots all registered services, then loads <see cref="BootOptions.NextSceneName"/>.
    /// Place the ServicesLifetimeScope in the starting scene — this runs on Play.
    /// </summary>
    internal sealed class ServicesBootController : IAsyncStartable
    {
        private const string Tag = "Boot";

        private readonly IServicesBootstrap _bootstrap;
        private readonly BootOptions _options;
        private readonly IServiceLogger _logger;

        public ServicesBootController(IServicesBootstrap bootstrap, BootOptions options, IServiceLogger logger)
        {
            _bootstrap = bootstrap;
            _options = options;
            _logger = logger;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            var result = await _bootstrap.InitializeAsync(cancellation);

            if (result.IsFailure)
            {
                _logger.Error(Tag, $"Services boot failed: {result.Error}", result.Error.Cause);
                if (!_options.ProceedToSceneOnFailure)
                    return;
                _logger.Warning(Tag, "Proceeding to next scene despite boot failure (ProceedToSceneOnFailure).");
            }

            await LoadNextSceneAsync(cancellation);
        }

        private async UniTask LoadNextSceneAsync(CancellationToken ct)
        {
            var sceneName = _options.NextSceneName;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                _logger.Info(Tag, "No next scene configured — staying in the boot scene.");
                return;
            }

            if (string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal))
            {
                _logger.Warning(Tag, $"Next scene '{sceneName}' is already active — skipping load.");
                return;
            }

            _logger.Info(Tag, $"Loading next scene '{sceneName}'.");
            await SceneManager.LoadSceneAsync(sceneName)!.ToUniTask(cancellationToken: ct);
        }
    }
}
