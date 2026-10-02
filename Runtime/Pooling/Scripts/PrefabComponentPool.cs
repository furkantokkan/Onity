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
    public sealed class PrefabComponentPool<TComponent> : IParameterizedPool<TComponent>, IDisposable, IOnityPoolDiagnosticsSource
        where TComponent : Component
    {
        private readonly TComponent m_prefab;
        private readonly Transform m_parent;
        private readonly ObjectPool<TComponent> m_pool;
        private readonly string m_poolName;
        private readonly int m_maxSize;
        private readonly bool m_fixedSize;
        private long m_getCount;
        private long m_releaseCount;
        private bool m_isDisposed;
        private bool m_suppressReleaseHook;
        private Transform m_inactiveParent;

        /// <summary>
        /// Initializes a prefab pool.
        /// </summary>
        /// <param name="prefab">Prefab reference.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="defaultCapacity">Default pool capacity.</param>
        /// <param name="maxSize">Maximum retained size; total capacity when fixedSize is true.</param>
        /// <param name="diagnosticsName">Optional diagnostics label.</param>
        /// <param name="initialSize">Number of distinct instances to create before first use.</param>
        /// <param name="fixedSize">Rejects a get when all maxSize instances are checked out.</param>
        public PrefabComponentPool(
            TComponent prefab,
            Transform parent = null,
            int defaultCapacity = 16,
            int maxSize = 512,
            string diagnosticsName = null,
            int initialSize = 0,
            bool fixedSize = false)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (maxSize <= 0 || initialSize < 0 || initialSize > maxSize)
            {
                throw new ArgumentOutOfRangeException(nameof(initialSize),
                    "Pool capacity must be positive and initialSize must be within maxSize.");
            }

            m_prefab = prefab;
            m_parent = parent;
            m_maxSize = maxSize;
            m_fixedSize = fixedSize;
            m_poolName = string.IsNullOrWhiteSpace(diagnosticsName)
                ? $"PrefabComponentPool<{typeof(TComponent).Name}>:{prefab.name}"
                : diagnosticsName;
            m_pool = new ObjectPool<TComponent>(
                CreateInstance,
                null,
                OnRelease,
                OnDestroyPooled,
                false,
                defaultCapacity > 0 && initialSize > defaultCapacity ? initialSize : defaultCapacity,
                maxSize);

            try
            {
                Prewarm(initialSize);
            }
            catch
            {
                try
                {
                    m_pool.Dispose();
                }
                finally
                {
                    DestroyInactiveParent();
                }

                throw;
            }

            OnityPoolDiagnosticsRegistry.Register(this);
        }

        /// <inheritdoc />
        public TComponent Get()
        {
            CheckCapacity();
            TComponent item = m_pool.Get();

            try
            {
                OnGet(item);
            }
            catch
            {
                ReturnWithoutHook(item);
                throw;
            }

            Interlocked.Increment(ref m_getCount);
            return item;
        }

        /// <inheritdoc />
        public TComponent Get<TParam>(TParam param, Action<TComponent, TParam> initialize)
        {
            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            CheckCapacity();
            TComponent item = m_pool.Get();

            try
            {
                initialize(item, param);
                OnGet(item);
            }
            catch
            {
                ReturnWithoutHook(item);
                throw;
            }

            Interlocked.Increment(ref m_getCount);
            return item;
        }

        /// <inheritdoc />
        public TComponent Get<TParam1, TParam2>(
            TParam1 param1, TParam2 param2,
            Action<TComponent, TParam1, TParam2> initialize)
        {
            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            CheckCapacity();
            TComponent item = m_pool.Get();

            try
            {
                initialize(item, param1, param2);
                OnGet(item);
            }
            catch
            {
                ReturnWithoutHook(item);
                throw;
            }

            Interlocked.Increment(ref m_getCount);
            return item;
        }

        /// <inheritdoc />
        public void Release(TComponent item)
        {
            ThrowIfDisposed();
            m_pool.Release(item);
            Interlocked.Increment(ref m_releaseCount);
        }

        /// <summary>Creates enough distinct instances to reach the requested total size.</summary>
        /// <param name="count">Target total number of created items, up to maxSize.</param>
        public void Prewarm(int count)
        {
            ThrowIfDisposed();

            if (count < 0 || count > m_maxSize)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            int missing = count - m_pool.CountAll;
            if (missing <= 0)
            {
                return;
            }

            TComponent[] held = new TComponent[m_pool.CountInactive + missing];
            int heldCount = 0;
            m_suppressReleaseHook = true;
            try
            {
                for (int i = 0; i < held.Length; i++)
                {
                    TComponent item = m_pool.Get();
                    held[heldCount++] = item;
                }
            }
            finally
            {
                for (int i = heldCount - 1; i >= 0; i--)
                {
                    m_pool.Release(held[i]);
                }
                m_suppressReleaseHook = false;
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            ThrowIfDisposed();
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
            try
            {
                m_pool.Dispose();
            }
            finally
            {
                DestroyInactiveParent();
            }
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
            if (m_inactiveParent == null)
            {
                GameObject root = new GameObject($"{m_poolName} Inactive Clone Root");
                root.hideFlags = HideFlags.HideAndDontSave;
                root.SetActive(false);
                m_inactiveParent = root.transform;
            }

            TComponent instance = UnityEngine.Object.Instantiate(m_prefab, m_inactiveParent);
            instance.gameObject.SetActive(false);
            instance.transform.SetParent(m_parent, false);
            return instance;
        }

        private void OnGet(TComponent component)
        {
            component.gameObject.SetActive(true);

            if (component is IPoolHooks hooks)
            {
                hooks.OnPoolGet();
            }
        }

        private void OnRelease(TComponent component)
        {
            if (m_suppressReleaseHook == false && component is IPoolHooks hooks)
            {
                hooks.OnPoolRelease();
            }

            component.gameObject.SetActive(false);
        }

        private void CheckCapacity()
        {
            ThrowIfDisposed();

            if (m_fixedSize && m_pool.CountInactive == 0 && m_pool.CountAll >= m_maxSize)
            {
                throw new InvalidOperationException("Fixed-size prefab pool has no available items.");
            }
        }

        private void ReturnWithoutHook(TComponent item)
        {
            m_suppressReleaseHook = true;
            try
            {
                m_pool.Release(item);
            }
            finally
            {
                m_suppressReleaseHook = false;
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(m_poolName);
            }
        }

        private void DestroyInactiveParent()
        {
            if (m_inactiveParent == null)
            {
                return;
            }

            GameObject root = m_inactiveParent.gameObject;
            m_inactiveParent = null;

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }

            UnityEngine.Object.DestroyImmediate(root);
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
