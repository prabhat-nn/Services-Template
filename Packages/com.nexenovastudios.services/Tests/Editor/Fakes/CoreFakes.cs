#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nexenova.Services.Core;

namespace Nexenova.Services.Tests
{
    internal sealed class FakeLogger : IServiceLogger
    {
        public readonly List<string> Infos = new();
        public readonly List<string> Warnings = new();
        public readonly List<string> Errors = new();

        public void Info(string tag, string message) => Infos.Add($"{tag}: {message}");
        public void Warning(string tag, string message) => Warnings.Add($"{tag}: {message}");
        public void Error(string tag, string message, Exception? exception = null) => Errors.Add($"{tag}: {message}");
    }

    internal sealed class FakeDelayProvider : IDelayProvider
    {
        public readonly List<TimeSpan> Delays = new();

        public UniTask Delay(TimeSpan duration, CancellationToken ct)
        {
            Delays.Add(duration);
            return UniTask.CompletedTask;
        }
    }

    internal sealed class FakeUnityServicesSdk : IUnityServicesSdk
    {
        public bool IsInitialized { get; private set; }
        public int InitializeCalls { get; private set; }
        public Exception? ThrowOnInitialize { get; set; }

        public UniTask InitializeAsync(string environment, CancellationToken ct)
        {
            InitializeCalls++;
            if (ThrowOnInitialize != null)
                throw ThrowOnInitialize;
            IsInitialized = true;
            return UniTask.CompletedTask;
        }
    }

    internal sealed class FakeModule : IServiceModule
    {
        private readonly Func<ServiceResult<Unit>> _result;

        public string ModuleName { get; }
        public InitializationStage Stage { get; }
        public bool IsRequired { get; }
        public int InitializeCalls { get; private set; }
        public static readonly List<string> GlobalInitOrder = new();

        public FakeModule(string name, InitializationStage stage, bool isRequired, Func<ServiceResult<Unit>>? result = null)
        {
            ModuleName = name;
            Stage = stage;
            IsRequired = isRequired;
            _result = result ?? ServiceResult.Ok;
        }

        public UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct)
        {
            InitializeCalls++;
            GlobalInitOrder.Add(ModuleName);
            return UniTask.FromResult(_result());
        }
    }

    internal static class TestDoubles
    {
        public static FakeLogger Logger() => new();

        public static IEventBus Bus(FakeLogger? logger = null) => new EventBus(logger ?? Logger());

        public static RetryPolicy Retry(FakeLogger? logger = null, FakeDelayProvider? delay = null, int attempts = 3) =>
            new(attempts,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(30),
                logger ?? Logger(),
                delay ?? new FakeDelayProvider());

        public static CoreOptions CoreOptions() =>
            new("test", verboseLogging: true, maxRetryAttempts: 3,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(30));
    }
}
