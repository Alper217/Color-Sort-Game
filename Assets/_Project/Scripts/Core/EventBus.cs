using System;
using System.Collections.Generic;

namespace SandSort.Core
{
    /// <summary>
    /// Central registry to clear all typed EventBus channels upon scene reload or domain reset.
    /// </summary>
    public static class EventBus
    {
        private static readonly List<Action> ResetActions = new List<Action>(32);

        internal static void RegisterReset(Action resetAction)
        {
            if (!ResetActions.Contains(resetAction))
            {
                ResetActions.Add(resetAction);
            }
        }

        /// <summary>
        /// Clears all registered listeners across all <see cref="EventBus{T}"/> channels.
        /// </summary>
        public static void ClearAll()
        {
            for (int i = 0; i < ResetActions.Count; i++)
            {
                ResetActions[i]?.Invoke();
            }
        }
    }

    /// <summary>
    /// Zero-GC, struct-constrained, strongly-typed EventBus channel.
    /// Pre-allocates listener arrays to avoid allocations or boxing during gameplay loops.
    /// </summary>
    /// <typeparam name="T">Struct implementing <see cref="IGameEvent"/>.</typeparam>
    public static class EventBus<T> where T : struct, IGameEvent
    {
        public delegate void EventCallback(in T eventData);

        private static EventCallback[] _listeners = new EventCallback[16];
        private static int _count;
        private static bool _isRegisteredForReset;

        public static void Subscribe(EventCallback listener)
        {
            if (listener == null) return;

            if (!_isRegisteredForReset)
            {
                _isRegisteredForReset = true;
                EventBus.RegisterReset(Clear);
            }

            // Prevent duplicate subscription
            for (int i = 0; i < _count; i++)
            {
                if (_listeners[i] == listener) return;
            }

            if (_count >= _listeners.Length)
            {
                Array.Resize(ref _listeners, _listeners.Length * 2);
            }

            _listeners[_count++] = listener;
        }

        public static void Unsubscribe(EventCallback listener)
        {
            if (listener == null) return;

            for (int i = 0; i < _count; i++)
            {
                if (_listeners[i] == listener)
                {
                    _count--;
                    // Shift remaining elements to preserve deterministic order
                    for (int j = i; j < _count; j++)
                    {
                        _listeners[j] = _listeners[j + 1];
                    }
                    _listeners[_count] = null;
                    return;
                }
            }
        }

        public static void Publish(in T eventData)
        {
            // Iterate backwards or snapshot count safely without allocating
            for (int i = 0; i < _count; i++)
            {
                _listeners[i]?.Invoke(in eventData);
            }
        }

        public static void Clear()
        {
            for (int i = 0; i < _count; i++)
            {
                _listeners[i] = null;
            }
            _count = 0;
        }
    }
}

