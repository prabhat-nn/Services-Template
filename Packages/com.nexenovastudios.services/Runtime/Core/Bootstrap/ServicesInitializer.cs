#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services.Core
{
    /// <summary>
    /// Owns the staged boot sequence:
    /// Platform (UnityServices) → Identity → Data (parallel) → Monetization.
    /// Required module failure ⇒ Failed; optional failure ⇒ Degraded, game keeps running.
    /// </summary>
    internal sealed class ServicesInitializer : IServicesBootstrap
    {
        private const string Tag = "Bootstrap";

        private readonly IUnityServicesSdk _unityServices;
        private readonly IReadOnlyList<IServiceModule> _modules;
        private readonly IEventBus _events;
        private readonly IServiceLogger _logger;
        private readonly RetryPolicy _retry;
        private readonly CoreOptions _options;
        private readonly List<string> _failedModules = new();
        private readonly UniTaskCompletionSource _readySource = new();

        private UniTask<ServiceResult<Unit>>? _inFlight;
        private ServiceError? _bootError;

        public ServicesState State { get; private set; } = ServicesState.NotStarted;
        public IReadOnlyList<string> FailedModules => _failedModules;

        public ServicesInitializer(
            IUnityServicesSdk unityServices,
            IEnumerable<IServiceModule> modules,
            IEventBus events,
            IServiceLogger logger,
            RetryPolicy retry,
            CoreOptions options)
        {
            _unityServices = unityServices;
            _modules = modules.OrderBy(m => m.Stage).ToList();
            _events = events;
            _logger = logger;
            _retry = retry;
            _options = options;
        }

        public UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct = default)
        {
            // Single-flight: concurrent/repeat calls share the same boot.
            _inFlight ??= RunAsync(ct).Preserve();
            return _inFlight.Value;
        }

        public async UniTask WaitUntilReadyAsync(CancellationToken ct = default)
        {
            if (State is ServicesState.Ready or ServicesState.Degraded)
                return;
            if (State != ServicesState.Failed)
                await _readySource.Task.AttachExternalCancellation(ct);
            if (State == ServicesState.Failed)
                throw new InvalidOperationException($"Services boot failed: {_bootError}");
        }

        private async UniTask<ServiceResult<Unit>> RunAsync(CancellationToken ct)
        {
            State = ServicesState.Initializing;
            _logger.Info(Tag, $"Boot started (environment: {_options.Environment}).");

            // Stage 0 — Platform.
            var platform = await _retry.ExecuteAsync<Unit>(
                "UnityServices.Initialize",
                async token =>
                {
                    try
                    {
                        await _unityServices.InitializeAsync(_options.Environment, token);
                        return ServiceResult.Ok();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        return ServiceResult.Fail(ServiceErrorCode.Network, $"UnityServices initialization failed: {ex.Message}", ex);
                    }
                },
                ct);

            if (platform.IsFailure)
                return Fail("UnityServices", platform.Error);

            // Stages 1..3 — modules, sequential by stage, parallel within a stage.
            foreach (var stage in _modules.GroupBy(m => m.Stage).OrderBy(g => g.Key))
            {
                var results = await UniTask.WhenAll(stage.Select(m => InitializeModuleAsync(m, ct)));

                foreach (var (module, result) in stage.Zip(results, (m, r) => (m, r)))
                {
                    if (result.IsSuccess)
                        continue;

                    if (module.IsRequired)
                        return Fail(module.ModuleName, result.Error);

                    _failedModules.Add(module.ModuleName);
                    _logger.Warning(Tag, $"Optional module '{module.ModuleName}' failed ({result.Error.Code}) — continuing degraded.");
                    _events.Publish(new ServiceDegradedEvent(module.ModuleName, result.Error.Code));
                }
            }

            State = _failedModules.Count > 0 ? ServicesState.Degraded : ServicesState.Ready;
            _logger.Info(Tag, $"Boot finished: {State}.");
            _events.Publish(new ServicesReadyEvent(State == ServicesState.Degraded));
            _readySource.TrySetResult();
            return ServiceResult.Ok();
        }

        private async UniTask<ServiceResult<Unit>> InitializeModuleAsync(IServiceModule module, CancellationToken ct)
        {
            try
            {
                return await module.InitializeAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Modules should return failures, never throw — treat an escape as a module bug but stay resilient.
                _logger.Error(Tag, $"Module '{module.ModuleName}' threw during initialization (modules must return ServiceResult failures).", ex);
                return ServiceResult.Fail(ServiceErrorCode.Unknown, ex.Message, ex);
            }
        }

        private ServiceResult<Unit> Fail(string moduleName, ServiceError error)
        {
            State = ServicesState.Failed;
            _failedModules.Add(moduleName);
            _bootError = error;
            _logger.Error(Tag, $"Boot failed at '{moduleName}': {error}", error.Cause);
            // Complete (not fault) the ready task: an unawaited faulted UniTask would surface
            // as an unobserved exception at GC. WaitUntilReadyAsync throws based on State.
            _readySource.TrySetResult();
            return ServiceResult.Fail(error);
        }
    }
}
