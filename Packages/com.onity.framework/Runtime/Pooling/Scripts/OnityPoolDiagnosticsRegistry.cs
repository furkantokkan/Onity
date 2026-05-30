using System;
using System.Collections.Generic;

namespace Onity.Pooling
{
    /// <summary>
    /// Registry for runtime pool diagnostics used by editor monitoring tools.
    /// </summary>
    public static class OnityPoolDiagnosticsRegistry
    {
        private static readonly object s_gate = new object();
        private static readonly List<IOnityPoolDiagnosticsSource> s_sources = new List<IOnityPoolDiagnosticsSource>(64);

        /// <summary>
        /// Registers a diagnostics source.
        /// </summary>
        /// <param name="source">Pool diagnostics source.</param>
        public static void Register(IOnityPoolDiagnosticsSource source)
        {
            if (source == null)
            {
                return;
            }

            lock (s_gate)
            {
                if (s_sources.Contains(source))
                {
                    return;
                }

                s_sources.Add(source);
            }
        }

        /// <summary>
        /// Unregisters a diagnostics source.
        /// </summary>
        /// <param name="source">Pool diagnostics source.</param>
        public static void Unregister(IOnityPoolDiagnosticsSource source)
        {
            if (source == null)
            {
                return;
            }

            lock (s_gate)
            {
                s_sources.Remove(source);
            }
        }

        /// <summary>
        /// Copies pool diagnostics snapshots into destination list.
        /// </summary>
        /// <param name="results">Destination list.</param>
        public static void GetSnapshots(List<OnityPoolDiagnosticsSnapshot> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            lock (s_gate)
            {
                for (int i = 0; i < s_sources.Count; i++)
                {
                    IOnityPoolDiagnosticsSource source = s_sources[i];

                    if (source == null)
                    {
                        continue;
                    }

                    results.Add(source.GetDiagnosticsSnapshot());
                }
            }
        }
    }

    /// <summary>
    /// Pool diagnostics source contract.
    /// </summary>
    public interface IOnityPoolDiagnosticsSource
    {
        /// <summary>
        /// Returns current pool snapshot.
        /// </summary>
        /// <returns>Pool diagnostics snapshot.</returns>
        OnityPoolDiagnosticsSnapshot GetDiagnosticsSnapshot();
    }

    /// <summary>
    /// Runtime pool diagnostics snapshot.
    /// </summary>
    public readonly struct OnityPoolDiagnosticsSnapshot
    {
        /// <summary>
        /// Pool label.
        /// </summary>
        public string PoolName { get; }

        /// <summary>
        /// Pool implementation type.
        /// </summary>
        public string PoolType { get; }

        /// <summary>
        /// Pooled item type.
        /// </summary>
        public string ItemType { get; }

        /// <summary>
        /// Total instances known by pool.
        /// </summary>
        public int CountAll { get; }

        /// <summary>
        /// Active (checked-out) instance count.
        /// </summary>
        public int CountActive { get; }

        /// <summary>
        /// Inactive (available) instance count.
        /// </summary>
        public int CountInactive { get; }

        /// <summary>
        /// Number of Get calls.
        /// </summary>
        public long GetCount { get; }

        /// <summary>
        /// Number of Release calls.
        /// </summary>
        public long ReleaseCount { get; }

        /// <summary>
        /// True when pool has been disposed.
        /// </summary>
        public bool IsDisposed { get; }

        /// <summary>
        /// Initializes pool diagnostics snapshot.
        /// </summary>
        /// <param name="poolName">Pool label.</param>
        /// <param name="poolType">Pool implementation type.</param>
        /// <param name="itemType">Pooled item type.</param>
        /// <param name="countAll">Total instance count.</param>
        /// <param name="countActive">Active instance count.</param>
        /// <param name="countInactive">Inactive instance count.</param>
        /// <param name="getCount">Get call count.</param>
        /// <param name="releaseCount">Release call count.</param>
        /// <param name="isDisposed">Disposed flag.</param>
        public OnityPoolDiagnosticsSnapshot(
            string poolName,
            string poolType,
            string itemType,
            int countAll,
            int countActive,
            int countInactive,
            long getCount,
            long releaseCount,
            bool isDisposed)
        {
            PoolName = poolName ?? string.Empty;
            PoolType = poolType ?? string.Empty;
            ItemType = itemType ?? string.Empty;
            CountAll = countAll;
            CountActive = countActive;
            CountInactive = countInactive;
            GetCount = getCount;
            ReleaseCount = releaseCount;
            IsDisposed = isDisposed;
        }
    }
}
