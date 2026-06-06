#nullable enable
using System;
using System.Collections.Generic;

namespace Nexenova.Services.Core
{
    internal sealed class EventBus : IEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly IServiceLogger _logger;

        public EventBus(IServiceLogger logger)
        {
            _logger = logger;
        }

        public void Publish<TEvent>(TEvent evt) where TEvent : struct, IServiceEvent
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list) || list.Count == 0)
                return;

            var snapshot = list.ToArray();
            foreach (var del in snapshot)
            {
                try
                {
                    ((Action<TEvent>)del).Invoke(evt);
                }
                catch (Exception ex)
                {
                    _logger.Error("EventBus", $"Handler for {typeof(TEvent).Name} threw — continuing with remaining handlers.", ex);
                }
            }
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IServiceEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            if (!_handlers.TryGetValue(typeof(TEvent), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(TEvent)] = list;
            }

            list.Add(handler);
            return new Subscription(list, handler);
        }

        private sealed class Subscription : IDisposable
        {
            private List<Delegate>? _list;
            private Delegate? _handler;

            public Subscription(List<Delegate> list, Delegate handler)
            {
                _list = list;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_list != null && _handler != null)
                    _list.Remove(_handler);
                _list = null;
                _handler = null;
            }
        }
    }
}
