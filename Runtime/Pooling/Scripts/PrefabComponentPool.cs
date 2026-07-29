using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Pool;

namespace Onity.Pooling
{
    /// <summary>
    /// Prefab-backed component pool.
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    public sealed class PrefabComponentPool<TComponent> : IPool<TComponent>, IDisposable, IOnityPoolDiagnosticsSource
        where TComponent : Component
    {
        private readonly TComponent m_prefab;
        private readonly Transform m_parent;
        private readonly ObjectPool<TComponent> m_pool;
        private readonly string m_poolName;
        private long m_getCount;
        private long m_releaseCount;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a prefab pool.
        /// </summary>
        /// <param name="prefab">Prefab reference.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="defaultCapacity">Default pool capacity.</param>
        /// <param name="maxSize">Maximum pool size.</param>
        public PrefabComponentPool(
            TComponent prefab,
            Transform parent = null,
            int defaultCapacity = 16,
            int maxSize = 512,
            string diagnosticsName = null)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            m_prefab = prefab;
            m_parent = parent;
            m_poolName = string.IsNullOrWhiteSpace(diagnosticsName)
                ? $"PrefabComponentPool<{typeof(TComponent).Name}>:{prefab.name}"
                : diagnosticsName;
            m_pool = new ObjectPool<TComponent>(
                CreateInstance,
                OnGet,
                OnRelease,
                OnDestroyPooled,
                false,
                defaultCapacity,
                maxSize);

            OnityPoolDiagnosticsRegistry.Register(this);
        }

        /// <inheritdoc />
        public TComponent Get()
        {
            Interlocked.Increment(ref m_getCount);
            return m_pool.Get();
        }

        /// <inheritdoc />
        public void Release(TComponent item)
        {
            Interlocked.Increment(ref m_releaseCount);
            m_pool.Release(item);
        }

        /// <inheritdoc />
        public void Clear()
        {
            m_pool.Clear();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            OnityPoolDiagnosticsRegistry.Unregister(this);
            m_pool.Dispose();
        }

        /// <inheritdoc />
        public OnityPoolDiagnosticsSnapshot GetDiagnosticsSnapshot()
        {
            int countAll = 0;
            int countActive = 0;
            int countInactive = 0;

            if (m_isDisposed == false)
            {
                countAll = m_pool.CountAll;
                countActive = m_pool.CountActive;
                countInactive = m_pool.CountInactive;
            }

            return new OnityPoolDiagnosticsSnapshot(
                m_poolName,
                nameof(PrefabComponentPool<TComponent>),
                typeof(TComponent).FullName,
                countAll,
                countActive,
                countInactive,
                Interlocked.Read(ref m_getCount),
                Interlocked.Read(ref m_releaseCount),
                m_isDisposed);
        }

        private TComponent CreateInstance()
        {
            TComponent instance = UnityEngine.Object.Instantiate(m_prefab, m_parent);
            instance.gameObject.SetActive(false);
            return instance;
        }

        private static void OnGet(TComponent component)
        {
            component.gameObject.SetActive(true);

            if (component is IPoolHooks hooks)
            {
                hooks.OnPoolGet();
            }
        }

        private static void OnRelease(TComponent component)
        {
            if (component is IPoolHooks hooks)
            {
                hooks.OnPoolRelease();
            }

            component.gameObject.SetActive(false);
        }

        private static void OnDestroyPooled(TComponent component)
        {
            if (component != null)
            {
                GameObject instanceRoot = component.gameObject;

                if (instanceRoot == null)
                {
                    return;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(instanceRoot);
                    return;
                }

                UnityEngine.Object.DestroyImmediate(instanceRoot);
            }
        }
    }
}
