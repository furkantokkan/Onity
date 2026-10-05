using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Onity.Factory;

namespace Onity.Pooling
{
    /// <summary>
    /// Stack-based managed object pool with optional duplicate-return checks in Editor and players.
    /// </summary>
    /// <typeparam name="T">Pooled item type.</typeparam>
    public sealed class OnityObjectPool<T> : IParameterizedPool<T>, IDisposable, IOnityPoolDiagnosticsSource
        where T : class
    {
        private readonly Func<T> m_createFunc;
        private readonly string m_poolName;
        private readonly Action<T> m_actionOnGet;
        private readonly Action<T> m_actionOnRelease;
        private readonly Action<T> m_actionOnDestroy;
        // Checked mode only: every inactive item except the top of the stack, which a
        // reference comparison covers, so repeated one-item rent/return never hashes.
        private readonly HashSet<T> m_inactiveItems;
        private readonly int m_maxSize;
        private readonly bool m_fixedSize;
        private T[] m_inactive;
        private int m_inactiveCount;
        private int m_countAll;
        private long m_getCount;
        private long m_releaseCount;
        private bool m_isDisposed;
        private bool m_suppressReleaseHook;

        /// <summary>
        /// Initializes a new pool.
        /// </summary>
        /// <param name="createFunc">Factory callback.</param>
        /// <param name="actionOnGet">Invoked on fetch.</param>
        /// <param name="actionOnRelease">Invoked on return.</param>
        /// <param name="actionOnDestroy">Invoked on destroy.</param>
        /// <param name="collectionCheck">Rejects returns of already retained items by reference in Editor and players.</param>
        /// <param name="defaultCapacity">Default pool capacity.</param>
        /// <param name="maxSize">Maximum retained size; total capacity when fixedSize is true.</param>
        /// <param name="diagnosticsName">Optional diagnostics label.</param>
        /// <param name="initialSize">Number of distinct items to create before first use.</param>
        /// <param name="fixedSize">Rejects a get when all maxSize items are checked out.</param>
        public OnityObjectPool(
            Func<T> createFunc,
            Action<T> actionOnGet = null,
            Action<T> actionOnRelease = null,
            Action<T> actionOnDestroy = null,
            bool collectionCheck = false,
            int defaultCapacity = 16,
            int maxSize = 1024,
            string diagnosticsName = null,
            int initialSize = 0,
            bool fixedSize = false)
        {
            if (maxSize <= 0 || initialSize < 0 || initialSize > maxSize)
            {
                throw new ArgumentOutOfRangeException(nameof(initialSize),
                    "Pool capacity must be positive and initialSize must be within maxSize.");
            }

            m_createFunc = createFunc ?? throw new ArgumentNullException(nameof(createFunc));
            m_poolName = string.IsNullOrWhiteSpace(diagnosticsName) ? $"OnityObjectPool<{typeof(T).Name}>" : diagnosticsName;
            m_actionOnGet = actionOnGet;
            m_actionOnRelease = actionOnRelease;
            m_actionOnDestroy = actionOnDestroy;
            m_inactiveItems = collectionCheck
                ? new HashSet<T>(PoolReferenceComparer<T>.Instance)
                : null;
            m_maxSize = maxSize;
            m_fixedSize = fixedSize;
            m_inactive = new T[Math.Min(Math.Max(defaultCapacity, initialSize), maxSize)];

            try
            {
                Prewarm(initialSize);
            }
            catch
            {
                DestroyInactive();
                throw;
            }

            OnityPoolDiagnosticsRegistry.Register(this);
        }

        /// <summary>Items this pool has created and not destroyed: checked out plus waiting.</summary>
        public int CountAll => m_countAll;

        /// <summary>Items currently checked out.</summary>
        public int CountActive => m_countAll - m_inactiveCount;

        /// <summary>Items waiting in the pool for the next get.</summary>
        public int CountInactive => m_inactiveCount;

        /// <inheritdoc />
        public T Get()
        {
            CheckCapacity();
            T item = Rent();

            if (m_actionOnGet != null)
            {
                try
                {
                    m_actionOnGet(item);
                }
                catch
                {
                    ReturnWithoutHook(item);
                    throw;
                }
            }

            Interlocked.Increment(ref m_getCount);
            return item;
        }

        /// <inheritdoc />
        public T Get<TParam>(TParam param, Action<T, TParam> initialize)
        {
            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            CheckCapacity();
            T item = Rent();

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
        public T Get<TParam1, TParam2>(
            TParam1 param1, TParam2 param2, Action<T, TParam1, TParam2> initialize)
        {
            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            CheckCapacity();
            T item = Rent();

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

        /// <summary>Gets an item unless a fixed-size pool has every item checked out.</summary>
        /// <param name="item">The item taken, or null when none is available.</param>
        /// <returns>False when a fixed-size pool has no available item; otherwise true.</returns>
        public bool TryGet(out T item)
        {
            if (HasCapacity() == false)
            {
                item = null;
                return false;
            }

            item = Get();
            return true;
        }

        /// <summary>
        /// Gets an item after applying one runtime parameter, unless a fixed-size pool has every item
        /// checked out.
        /// </summary>
        /// <typeparam name="TParam">Parameter type.</typeparam>
        /// <param name="param">Runtime parameter.</param>
        /// <param name="initialize">Sets the item's state before the get hook runs.</param>
        /// <param name="item">The configured item, or null when none is available.</param>
        /// <returns>False when a fixed-size pool has no available item; otherwise true.</returns>
        public bool TryGet<TParam>(TParam param, Action<T, TParam> initialize, out T item)
        {
            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            if (HasCapacity() == false)
            {
                item = null;
                return false;
            }

            item = Get(param, initialize);
            return true;
        }

        /// <summary>
        /// Gets an item after applying two runtime parameters, unless a fixed-size pool has every item
        /// checked out.
        /// </summary>
        /// <typeparam name="TParam1">First parameter type.</typeparam>
        /// <typeparam name="TParam2">Second parameter type.</typeparam>
        /// <param name="param1">First runtime parameter.</param>
        /// <param name="param2">Second runtime parameter.</param>
        /// <param name="initialize">Sets the item's state before the get hook runs.</param>
        /// <param name="item">The configured item, or null when none is available.</param>
        /// <returns>False when a fixed-size pool has no available item; otherwise true.</returns>
        public bool TryGet<TParam1, TParam2>(
            TParam1 param1, TParam2 param2, Action<T, TParam1, TParam2> initialize, out T item)
        {
            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            if (HasCapacity() == false)
            {
                item = null;
                return false;
            }

            item = Get(param1, param2, initialize);
            return true;
        }

        /// <inheritdoc />
        public void Release(T item)
        {
            ThrowIfDisposed();
            Return(item);
            Interlocked.Increment(ref m_releaseCount);
        }

        /// <summary>Creates enough distinct items to reach the requested total size.</summary>
        /// <param name="count">Target total number of created items, up to maxSize.</param>
        public void Prewarm(int count)
        {
            ThrowIfDisposed();

            if (count < 0 || count > m_maxSize)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            int missing = count - m_countAll;
            if (missing <= 0)
            {
                return;
            }

            T[] held = new T[m_inactiveCount + missing];
            int heldCount = 0;
            m_suppressReleaseHook = true;
            try
            {
                for (int i = 0; i < held.Length; i++)
                {
                    T item = Rent();
                    held[heldCount++] = item;
                }
            }
            finally
            {
                for (int i = heldCount - 1; i >= 0; i--)
                {
                    Return(held[i]);
                }
                m_suppressReleaseHook = false;
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            ThrowIfDisposed();
            DestroyInactive();
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
            DestroyInactive();
        }

        /// <inheritdoc />
        public OnityPoolDiagnosticsSnapshot GetDiagnosticsSnapshot()
        {
            int countAll = 0;
            int countActive = 0;
            int countInactive = 0;

            if (m_isDisposed == false)
            {
                countAll = m_countAll;
                countActive = m_countAll - m_inactiveCount;
                countInactive = m_inactiveCount;
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

        private void CheckCapacity()
        {
            if (HasCapacity() == false)
            {
                throw new InvalidOperationException("Fixed-size pool has no available items.");
            }
        }

        // Throws when disposed; false when a fixed-size pool has every item checked out.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasCapacity()
        {
            ThrowIfDisposed();
            return m_fixedSize == false || m_inactiveCount != 0 || m_countAll < m_maxSize;
        }

        private void OnGet(T item)
        {
            m_actionOnGet?.Invoke(item);
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(m_poolName);
            }
        }

        private T Rent()
        {
            if (m_inactiveCount == 0)
            {
                T created = m_createFunc();
                m_countAll++;
                return created;
            }

            int index = --m_inactiveCount;
            T item = m_inactive[index];
            m_inactive[index] = null;
            if (m_inactiveItems != null && index != 0)
            {
                // The item below becomes the top, which the reference check covers.
                m_inactiveItems.Remove(m_inactive[index - 1]);
            }

            return item;
        }

        private void Return(T item)
        {
            if (m_inactiveItems != null && m_inactiveCount != 0 &&
                (ReferenceEquals(m_inactive[m_inactiveCount - 1], item) ||
                 (m_inactiveCount > 1 && m_inactiveItems.Contains(item))))
            {
                throw new InvalidOperationException("Item has already been returned to the pool.");
            }

            // The release hook runs before anything is retained, so a failing hook leaves the
            // pool unchanged and the caller may retry the return.
            if (m_suppressReleaseHook == false)
            {
                m_actionOnRelease?.Invoke(item);
            }

            if (m_inactiveCount < m_maxSize)
            {
                Push(item);
                return;
            }

            m_countAll--;
            m_actionOnDestroy?.Invoke(item);
        }

        private void Push(T item)
        {
            if (m_inactiveCount == m_inactive.Length)
            {
                Array.Resize(ref m_inactive, Math.Min(Math.Max(m_inactive.Length * 2, 4), m_maxSize));
            }

            if (m_inactiveItems != null && m_inactiveCount != 0)
            {
                m_inactiveItems.Add(m_inactive[m_inactiveCount - 1]);
            }

            m_inactive[m_inactiveCount++] = item;
        }

        private void DestroyInactive()
        {
            int count = m_inactiveCount;
            m_inactiveCount = 0;
            m_countAll -= count;
            m_inactiveItems?.Clear();

            for (int i = 0; i < count; i++)
            {
                T item = m_inactive[i];
                m_inactive[i] = null;
                m_actionOnDestroy?.Invoke(item);
            }
        }

        private void ReturnWithoutHook(T item)
        {
            m_suppressReleaseHook = true;
            try
            {
                Return(item);
            }
            finally
            {
                m_suppressReleaseHook = false;
            }
        }
    }

    internal sealed class PoolReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        internal static readonly PoolReferenceComparer<T> Instance = new PoolReferenceComparer<T>();

        public bool Equals(T left, T right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(T item)
        {
            return item == null ? 0 : RuntimeHelpers.GetHashCode(item);
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

    /// <summary>Factory adapter that reconfigures a pooled value before get hooks run.</summary>
    /// <typeparam name="TParam">Runtime parameter type.</typeparam>
    /// <typeparam name="TValue">Pooled value type.</typeparam>
    public sealed class PooledFactory<TParam, TValue> : IFactory<TParam, TValue>
    {
        private readonly IParameterizedPool<TValue> m_pool;
        private readonly Action<TValue, TParam> m_initialize;

        /// <summary>Creates a one-parameter pooled factory.</summary>
        /// <param name="pool">Pool used for instance retrieval.</param>
        /// <param name="initialize">Sets state before get hooks and activation.</param>
        public PooledFactory(IParameterizedPool<TValue> pool, Action<TValue, TParam> initialize)
        {
            m_pool = pool ?? throw new ArgumentNullException(nameof(pool));
            m_initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
        }

        /// <inheritdoc />
        public TValue Create(TParam param)
        {
            return m_pool.Get(param, m_initialize);
        }
    }

    /// <summary>Factory adapter that reconfigures a pooled value with two parameters.</summary>
    /// <typeparam name="TParam1">First runtime parameter type.</typeparam>
    /// <typeparam name="TParam2">Second runtime parameter type.</typeparam>
    /// <typeparam name="TValue">Pooled value type.</typeparam>
    public sealed class PooledFactory<TParam1, TParam2, TValue> : IFactory<TParam1, TParam2, TValue>
    {
        private readonly Func<TParam1, TParam2, Action<TValue, TParam1, TParam2>, TValue> m_get;
        private readonly Action<TValue, TParam1, TParam2> m_initialize;

        /// <summary>Creates a two-parameter pooled factory.</summary>
        /// <param name="pool">Pool used for instance retrieval.</param>
        /// <param name="initialize">Sets state before get hooks and activation.</param>
        public PooledFactory(
            IParameterizedPool<TValue> pool, Action<TValue, TParam1, TParam2> initialize)
        {
            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            m_initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
            m_get = pool.Get<TParam1, TParam2>;
        }

        /// <inheritdoc />
        public TValue Create(TParam1 param1, TParam2 param2)
        {
            return m_get(param1, param2, m_initialize);
        }
    }
}
