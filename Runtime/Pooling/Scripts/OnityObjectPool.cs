using System;
using System.Threading;
using Onity.Factory;
using UnityEngine.Pool;

namespace Onity.Pooling
{
    /// <summary>
    /// Wrapper around Unity's <see cref="ObjectPool{T0}" />.
    /// </summary>
    /// <typeparam name="T">Pooled item type.</typeparam>
    public sealed class OnityObjectPool<T> : IPool<T>, IDisposable, IOnityPoolDiagnosticsSource
        where T : class
    {
        private readonly ObjectPool<T> m_pool;
        private readonly string m_poolName;
        private long m_getCount;
        private long m_releaseCount;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new pool.
        /// </summary>
        /// <param name="createFunc">Factory callback.</param>
        /// <param name="actionOnGet">Invoked on fetch.</param>
        /// <param name="actionOnRelease">Invoked on return.</param>
        /// <param name="actionOnDestroy">Invoked on destroy.</param>
        /// <param name="collectionCheck">Collection check toggle.</param>
        /// <param name="defaultCapacity">Default pool capacity.</param>
        /// <param name="maxSize">Maximum pool size.</param>
        public OnityObjectPool(
            Func<T> createFunc,
            Action<T> actionOnGet = null,
            Action<T> actionOnRelease = null,
            Action<T> actionOnDestroy = null,
            bool collectionCheck = false,
            int defaultCapacity = 16,
            int maxSize = 1024,
            string diagnosticsName = null)
        {
            m_poolName = string.IsNullOrWhiteSpace(diagnosticsName) ? $"OnityObjectPool<{typeof(T).Name}>" : diagnosticsName;
            m_pool = new ObjectPool<T>(
                createFunc,
                actionOnGet,
                actionOnRelease,
                actionOnDestroy,
                collectionCheck,
                defaultCapacity,
                maxSize);

            OnityPoolDiagnosticsRegistry.Register(this);
        }

        /// <inheritdoc />
        public T Get()
        {
            Interlocked.Increment(ref m_getCount);
            return m_pool.Get();
        }

        /// <inheritdoc />
        public void Release(T item)
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
                nameof(OnityObjectPool<T>),
                typeof(T).FullName,
                countAll,
                countActive,
                countInactive,
                Interlocked.Read(ref m_getCount),
                Interlocked.Read(ref m_releaseCount),
                m_isDisposed);
        }
    }

    /// <summary>
    /// Factory adapter that creates values by taking them from an <see cref="IPool{T}" />.
    /// </summary>
    /// <typeparam name="TValue">Produced value type.</typeparam>
    public sealed class PooledFactory<TValue> : IFactory<TValue>
    {
        private readonly IPool<TValue> m_pool;

        /// <summary>
        /// Initializes the pooled factory.
        /// </summary>
        /// <param name="pool">Pool used for instance retrieval.</param>
        public PooledFactory(IPool<TValue> pool)
        {
            m_pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        /// <inheritdoc />
        public TValue Create()
        {
            return m_pool.Get();
        }
    }
}
