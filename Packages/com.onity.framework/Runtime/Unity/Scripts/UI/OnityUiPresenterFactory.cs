using System;
using System.Collections.Generic;
using System.Reflection;
using Onity.DI;
using ZLinq;

namespace Onity.Unity.UI
{
    /// <summary>
    /// Creates presenters and auto-injects services marked with <see cref="OnityUiInjectAttribute" />
    /// or <see cref="InjectAttribute" />.
    /// </summary>
    public static class OnityUiPresenterFactory
    {
        private static readonly Dictionary<Type, PropertyInfo[]> s_injectablePropertiesCache =
            new Dictionary<Type, PropertyInfo[]>(32);

        private static readonly object s_cacheGate = new object();

        /// <summary>
        /// Optional custom presenter factory delegate, typically bound to DI container resolve.
        /// </summary>
        public static Func<Type, IOnityUiPresenter> CustomFactory { get; set; }

        /// <summary>
        /// Creates one presenter using custom factory when available, otherwise reflection.
        /// </summary>
        /// <typeparam name="TPresenter">Presenter type.</typeparam>
        /// <returns>Created presenter instance.</returns>
        public static TPresenter Create<TPresenter>()
            where TPresenter : class, IOnityUiPresenter
        {
            return (TPresenter)Create(typeof(TPresenter));
        }

        /// <summary>
        /// Creates one presenter using custom factory when available, otherwise reflection.
        /// </summary>
        /// <param name="presenterType">Presenter runtime type.</param>
        /// <returns>Created presenter instance.</returns>
        public static IOnityUiPresenter Create(Type presenterType)
        {
            if (presenterType == null)
            {
                throw new ArgumentNullException(nameof(presenterType));
            }

            if (typeof(IOnityUiPresenter).IsAssignableFrom(presenterType) == false)
            {
                throw new InvalidCastException(
                    $"Type '{presenterType.FullName}' does not implement '{nameof(IOnityUiPresenter)}'.");
            }

            IOnityUiPresenter presenter = TryCreateFromCustomFactory(presenterType);

            if (presenter == null)
            {
                presenter = (IOnityUiPresenter)Activator.CreateInstance(presenterType);
            }

            AutoInjectServices(presenter);
            return presenter;
        }

        /// <summary>
        /// Clears reflection cache.
        /// </summary>
        public static void ClearCache()
        {
            lock (s_cacheGate)
            {
                s_injectablePropertiesCache.Clear();
            }
        }

        private static IOnityUiPresenter TryCreateFromCustomFactory(Type presenterType)
        {
            Func<Type, IOnityUiPresenter> factory = CustomFactory;

            if (factory == null)
            {
                return null;
            }

            return factory(presenterType);
        }

        private static void AutoInjectServices(object presenter)
        {
            if (presenter == null)
            {
                return;
            }

            Type presenterType = presenter.GetType();
            PropertyInfo[] injectableProperties = GetInjectableProperties(presenterType);

            for (int i = 0; i < injectableProperties.Length; i++)
            {
                PropertyInfo property = injectableProperties[i];
                object service = OnityUiServiceLocator.Get(property.PropertyType);

                if (service == null)
                {
                    continue;
                }

                property.SetValue(presenter, service);
            }
        }

        private static PropertyInfo[] GetInjectableProperties(Type presenterType)
        {
            lock (s_cacheGate)
            {
                if (s_injectablePropertiesCache.TryGetValue(presenterType, out PropertyInfo[] cachedProperties))
                {
                    return cachedProperties;
                }
            }

            PropertyInfo[] allProperties = presenterType.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo[] injectableProperties =
                allProperties
                    .AsValueEnumerable()
                    .Where(static property => property.CanWrite)
                    .Where(
                        static property =>
                            property.IsDefined(typeof(OnityUiInjectAttribute), true)
                            || property.IsDefined(typeof(InjectAttribute), true))
                    .ToArray();

            lock (s_cacheGate)
            {
                s_injectablePropertiesCache[presenterType] = injectableProperties;
            }

            return injectableProperties;
        }
    }
}
