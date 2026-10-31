using System;
using UnityEngine;

namespace SandSort.Core
{
    /// <summary>
    /// Pre-allocated, Zero-GC generic object pool for Unity components and runtime objects.
    /// Uses a contiguous array stack instead of Queue/List to minimize cache misses.
    /// </summary>
    /// <typeparam name="T">Type of pooled object.</typeparam>
    public sealed class ObjectPool<T> where T : class
    {
        private T[] _inactiveStack;
        private int _inactiveCount;

        private readonly Func<T> _factory;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;

        public int InactiveCount => _inactiveCount;
        public int Capacity => _inactiveStack.Length;

        public ObjectPool(
            int initialCapacity,
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            Action<T> onDestroy = null,
            bool prewarm = true)
        {
            if (initialCapacity <= 0) initialCapacity = 16;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;

            _inactiveStack = new T[initialCapacity];
            _inactiveCount = 0;

            if (prewarm)
            {
                Warmup(initialCapacity);
            }
        }

        /// <summary>
        /// Pre-instantiates items up to the target count so no allocations occur during gameplay.
        /// </summary>
        public void Warmup(int count)
        {
            if (count > _inactiveStack.Length)
            {
                Array.Resize(ref _inactiveStack, Mathf.NextPowerOfTwo(count));
            }

            while (_inactiveCount < count)
            {
                T instance = _factory.Invoke();
                _onRelease?.Invoke(instance);
                _inactiveStack[_inactiveCount++] = instance;
            }
        }

        /// <summary>
        /// Retrieves an instance from the pool or creates one if exhausted.
        /// </summary>
        public T Get()
        {
            T item;
            if (_inactiveCount > 0)
            {
                _inactiveCount--;
                item = _inactiveStack[_inactiveCount];
                _inactiveStack[_inactiveCount] = null;
            }
            else
            {
                item = _factory.Invoke();
            }

            _onGet?.Invoke(item);
            return item;
        }

        /// <summary>
        /// Returns an instance back to the pool.
        /// </summary>
        public void Release(T item)
        {
            if (item == null) return;

            _onRelease?.Invoke(item);

            if (_inactiveCount >= _inactiveStack.Length)
            {
                Array.Resize(ref _inactiveStack, _inactiveStack.Length * 2);
            }

            _inactiveStack[_inactiveCount++] = item;
        }

        /// <summary>
        /// Disposes all pooled instances.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _inactiveCount; i++)
            {
                if (_inactiveStack[i] != null)
                {
                    _onDestroy?.Invoke(_inactiveStack[i]);
                    _inactiveStack[i] = null;
                }
            }
            _inactiveCount = 0;
        }
    }
}

