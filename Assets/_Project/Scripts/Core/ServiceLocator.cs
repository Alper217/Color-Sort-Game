using System;
using System.Collections.Generic;

namespace SandSort.Core
{
    /// <summary>
    /// Lightweight, fast ServiceLocator to decouple domain managers without singleton spaghetti.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>(16);

        public static void Register<T>(T service) where T : class
        {
            if (service == null) return;
            Services[typeof(T)] = service;
        }

        public static void Unregister<T>(T service) where T : class
        {
            Type type = typeof(T);
            if (Services.TryGetValue(type, out object existing) && ReferenceEquals(existing, service))
            {
                Services.Remove(type);
            }
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out object obj))
            {
                service = (T)obj;
                return true;
            }

            service = null;
            return false;
        }

        public static T Get<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out object obj))
            {
                return (T)obj;
            }

            throw new InvalidOperationException($"[ServiceLocator] Service of type {typeof(T).Name} is not registered.");
        }

        public static void Clear()
        {
            Services.Clear();
        }
    }
}

