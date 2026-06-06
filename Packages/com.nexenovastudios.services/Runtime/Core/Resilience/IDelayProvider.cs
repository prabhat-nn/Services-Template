#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nexenova.Services.Core
{
    /// <summary>Testability seam: tests inject a zero-delay provider so retry tests run instantly.</summary>
    internal interface IDelayProvider
    {
        UniTask Delay(TimeSpan duration, CancellationToken ct);
    }

    internal sealed class RealtimeDelayProvider : IDelayProvider
    {
        public UniTask Delay(TimeSpan duration, CancellationToken ct) =>
            UniTask.Delay(duration, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
    }
}
