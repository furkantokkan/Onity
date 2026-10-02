using System;
using System.Collections.Generic;
using Onity.Core;

namespace Onity.Unity.UI
{
    /// <summary>
    /// Lightweight resolver stack and service registry for Onity UI flows.
    /// </summary>
    public static class OnityUiServiceLocator
    {
        private static readonly Dictionary<Type, object> s_services = new Dictionary<Type, object>(32);
        private static readonly Dictionary<Type, Func<object>> s_factories = new Dictionary<Type, Func<object>>(16);
        private static readonly List<ResolverEntry> s_resolverStack = new List<ResolverEntry>(4);
        private static readonly object s_gate = new object();

        private struct ResolverEntry
        {
            public Func<Type, object> Resolver;
            public Dictionary<Type, object> Cache;
        }

        /// <summary>
        /// Registers a concrete service instance.
        /// </summary>
        /// <typeparam name="TService">Service contract type.</typeparam>
        /// <param name="service">Concrete instance.</param>
        public static void Register<TService>(TService service)
            where TService : class
        {
            if (service == null)
            {
                return;
            }

            lock (s_gate)
            {
                s_services[typeof(TService)] = service;
            }
        }

        /// <summary>
        /// Registers a lazy service factory.
        /// </summary>
        /// <typeparam name="TService">Service contract type.</typeparam>
        /// <param name="factory">Factory callback.</param>
        public static void RegisterFactory<TService>(Func<TService> factory)
            where TService : class
        {
            if (factory == null)
            {
                return;
            }

            lock (s_gate)
            {
                s_factories[typeof(TService)] = () => factory();
            }
        }

        /// <summary>
        /// Pushes one resolver onto the top of the resolver stack.
        /// </summary>
        /// <param name="resolver">Resolver callback.</param>
        public static void PushResolver(Func<Type, object> resolver)
        {
            if (resolver == null)
            {
                return;
            }

            lock (s_gate)
            {
                s_resolverStack.Add(
                    new ResolverEntry
                    {
                        Resolver = resolver,
                        Cache = new Dictionary<Type, object>(16)
                    });
            }
        }

        /// <summary>
        /// Pushes one resolver and returns a disposable scope that pops it.
        /// </summary>
        /// <param name="resolver">Resolver callback.</param>
        /// <returns>Disposable scope token.</returns>
        public static IDisposable PushResolverScope(Func<Type, object> resolver)
        {
            PushResolver(resolver);
            return new DisposableAction(PopResolver);
        }

        /// <summary>
        /// Pops the current top resolver from stack.
        /// </summary>
        public static void PopResolver()
        {
            lock (s_gate)
            {
                int count = s_resolverStack.Count;

                if (count == 0)
                {
                    return;
                }

                ResolverEntry entry = s_resolverStack[count - 1];
                entry.Cache.Clear();
                s_resolverStack.RemoveAt(count - 1);
            }
        }

        /// <summary>
        /// Resolves a service instance.
        /// </summary>
        /// <typeparam name="TService">Service contract type.</typeparam>
        /// <returns>Resolved instance or null.</returns>
        public static TService Get<TService>()
            where TService : class
        {
            return Get(typeof(TService)) as TService;
        }

        /// <summary>
        /// Resolves a service instance by type.
        /// </summary>
        /// <param name="type">Service contract type.</param>
        /// <returns>Resolved instance or null.</returns>
        public static object Get(Type type)
        {
            if (type == null)
            {
                return null;
            }

            lock (s_gate)
            {
                if (s_services.TryGetValue(type, out object service))
                {
                    return service;
                }

                for (int i = s_resolverStack.Count - 1; i >= 0; i--)
                {
                    ResolverEntry entry = s_resolverStack[i];

                    if (entry.Cache.TryGetValue(type, out object cached))
                    {
                        return cached;
                    }

                    object resolved = null;

                    try
                    {
                        resolved = entry.Resolver(type);
                    }
                    catch
                    {
                    }

                    if (resolved == null)
                    {
                        continue;
                    }

                    entry.Cache[type] = resolved;
                    s_resolverStack[i] = entry;
                    return resolved;
                }

                if (s_factories.TryGetValue(type, out Func<object> factory))
                {
                    object created = factory();
                    s_services[type] = created;
                    return created;
                }
            }

            return null;
        }

        /// <summary>
        /// Clears all registered services, factories, and resolver scopes.
        /// </summary>
        public static void Clear()
        {
            lock (s_gate)
            {
                s_services.Clear();
                s_factories.Clear();

                for (int i = 0; i < s_resolverStack.Count; i++)
                {
                    ResolverEntry entry = s_resolverStack[i];
                    entry.Cache.Clear();
                }

                s_resolverStack.Clear();
            }
        }
    }
}
