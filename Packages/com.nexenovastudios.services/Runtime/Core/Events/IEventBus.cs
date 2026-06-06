#nullable enable
using System;

namespace Nexenova.Services
{
    /// <summary>Marker interface for all service events. Events are readonly structs carrying data, never behavior.</summary>
    public interface IServiceEvent { }

    /// <summary>
    /// Main-thread, synchronous event bus — the only cross-module communication channel.
    /// Handlers must not throw; the bus catches, logs and continues so one bad
    /// subscriber never breaks another.
    /// </summary>
    public interface IEventBus
    {
        void Publish<TEvent>(TEvent evt) where TEvent : struct, IServiceEvent;
        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IServiceEvent;
    }
}
