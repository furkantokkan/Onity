using System;
using Onity.DI;
using Onity.Pooling;
using Onity.Unity.UI;
using UnityEngine;

namespace Onity.Unity.Installers
{
    /// <summary>
    /// Base MonoBehaviour installer for Onity contexts.
    /// </summary>
    public abstract class MonoInstaller : MonoBehaviour
    {
        /// <summary>
        /// Registers bindings into the provided container.
        /// </summary>
        /// <param name="container">Current context container.</param>
        public abstract void InstallBindings(OnityContainer container);
    }

    /// <summary>
    /// Convenience extensions for binding ScriptableObject instances with dependency injection.
    /// </summary>
    public static class OnityScriptableObjectBindingExtensions
    {
        /// <summary>
        /// Injects and binds a ScriptableObject instance as its own contract type.
        /// </summary>
        /// <typeparam name="TContract">ScriptableObject contract type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <param name="asset">ScriptableObject instance.</param>
        public static void BindScriptableObject<TContract>(this OnityContainer container, TContract asset)
            where TContract : ScriptableObject
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            if (asset == null)
            {
                throw new OnityBindingException("Cannot bind a null ScriptableObject instance.");
            }

            container.Inject(asset);
            container.BindInstance<TContract>(asset);
        }

        /// <summary>
        /// Injects and binds a ScriptableObject instance as an interface or base contract type.
        /// </summary>
        /// <typeparam name="TContract">Contract type.</typeparam>
        /// <typeparam name="TAsset">Concrete ScriptableObject type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <param name="asset">ScriptableObject instance.</param>
        public static void BindScriptableObject<TContract, TAsset>(this OnityContainer container, TAsset asset)
            where TContract : class
            where TAsset : ScriptableObject, TContract
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            if (asset == null)
            {
                throw new OnityBindingException("Cannot bind a null ScriptableObject instance.");
            }

            container.Inject(asset);
            container.BindInstance<TContract>(asset);
        }
    }

    /// <summary>
    /// Convenience helpers for low-boilerplate factory and pool registrations.
    /// </summary>
    public static class OnityFactoryBindingExtensions
    {
        /// <summary>
        /// Binds a prefab-backed pool and a default pooled factory in one call.
        /// </summary>
        /// <typeparam name="TComponent">Spawned component type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <param name="prefab">Prefab source used for pooled instances.</param>
        /// <param name="parent">Optional parent transform for pooled instances.</param>
        /// <param name="defaultCapacity">Initial pool capacity.</param>
        /// <param name="maxSize">Maximum pool capacity.</param>
        public static void BindPooledFactory<TComponent>(
            this OnityContainer container,
            TComponent prefab,
            Transform parent = null,
            int defaultCapacity = 16,
            int maxSize = 512)
            where TComponent : Component
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            if (prefab == null)
            {
                throw new OnityBindingException("Cannot bind pooled factory with a null prefab.");
            }

            PrefabComponentPool<TComponent> pool = new PrefabComponentPool<TComponent>(
                prefab,
                parent,
                defaultCapacity,
                maxSize);

            BindPooledFactory(container, pool);
        }

        /// <summary>
        /// Binds an existing pool and a default pooled factory in one call.
        /// </summary>
        /// <typeparam name="TValue">Spawned value type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <param name="pool">Existing pool instance.</param>
        public static void BindPooledFactory<TValue>(this OnityContainer container, IPool<TValue> pool)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            if (pool == null)
            {
                throw new OnityBindingException("Cannot bind pooled factory with a null pool.");
            }

            container.BindInstance(pool);
            container.BindFactory<TValue, PooledFactory<TValue>>();
        }
    }

    /// <summary>
    /// Convenience helpers for binding Onity UI resolver bridge.
    /// </summary>
    public static class OnityUiBindingExtensions
    {
        /// <summary>
        /// Binds and initializes <see cref="OnityUiResolverBridge" /> for current container scope.
        /// </summary>
        /// <param name="container">Target container.</param>
        public static void BindUiResolverBridge(this OnityContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            container.BindInterfacesAndSelfTo<OnityUiResolverBridge>().AsSingle();
            container.RegisterBuildCallback(resolver => resolver.Resolve<OnityUiResolverBridge>());
        }
    }
}
