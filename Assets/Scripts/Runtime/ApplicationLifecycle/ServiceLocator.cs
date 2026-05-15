using System;
using System.Collections.Generic;
using System.Reflection;

namespace Tolik.Riftstorm.Runtime.ApplicationLifecycle
{
    /// <summary>
    /// Simple service registry for pure services with optional cache clearing support.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> s_Services = new();

        /// <summary>
        /// Registers a service instance under its type key.
        /// </summary>
        public static void Register<T>(T service) where T : class
        {
            s_Services[typeof(T)] = service;
        }

        /// <summary>
        /// Resolves a registered service by type.
        /// </summary>
        public static T Get<T>() where T : class
        {
            if (s_Services.TryGetValue(typeof(T), out object service))
            {
                return service as T;
            }
            return null;
        }

        /// <summary>
        /// Clears all registered services and calls ClearCache when available.
        /// </summary>
        public static void ClearAll()
        {
            foreach (object service in s_Services.Values)
            {
                TryClearCache(service);
            }
            s_Services.Clear();
        }

        private static void TryClearCache(object service)
        {
            if (service == null)
            {
                return;
            }

            Type serviceType = service.GetType();
            MethodInfo clearCacheMethod = serviceType.GetMethod("ClearCache", BindingFlags.Instance | BindingFlags.Public);
            if (clearCacheMethod == null)
            {
                return;
            }

            clearCacheMethod.Invoke(service, null);
        }
    }
}