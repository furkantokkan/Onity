using System;
using System.Collections.Generic;

namespace Onity.Reactive
{
    /// <summary>
    /// Tracks active reactive subscriptions for diagnostics.
    /// </summary>
    public static class OnityObservableTracker
    {
        private const int k_maxTrackedSubscriptions = 4096;

        private static readonly object s_gate = new object();
        private static readonly Dictionary<int, TrackedObservableEntry> s_entryById =
            new Dictionary<int, TrackedObservableEntry>(256);
        private static readonly Queue<int> s_order = new Queue<int>(256);

        private static int s_nextTrackingId = 1;

        /// <summary>
        /// Enables or disables tracking.
        /// </summary>
        public static bool EnableTracking { get; set; }

        /// <summary>
        /// Captures stack traces for tracked subscriptions.
        /// </summary>
        public static bool EnableStackTrace { get; set; }

        /// <summary>
        /// Registers a new tracked subscription and returns tracking id.
        /// </summary>
        /// <param name="observable">Observable instance.</param>
        /// <param name="observer">Observer instance.</param>
        /// <returns>Tracking id or 0 when tracking is disabled.</returns>
        public static int RegisterSubscription(object observable, object observer)
        {
            if (EnableTracking == false)
            {
                return 0;
            }

            string observableType = observable?.GetType().FullName ?? "UnknownObservable";
            string observerType = observer?.GetType().FullName ?? "UnknownObserver";
            string stackTrace = EnableStackTrace ? Environment.StackTrace : string.Empty;

            lock (s_gate)
            {
                int trackingId = s_nextTrackingId++;
                TrackedObservableEntry entry =
                    new TrackedObservableEntry(
                        trackingId,
                        observableType,
                        observerType,
                        DateTime.UtcNow,
                        stackTrace);

                s_entryById[trackingId] = entry;
                s_order.Enqueue(trackingId);
                TrimOverflow_NoAlloc();
                return trackingId;
            }
        }

        /// <summary>
        /// Marks a tracked subscription as disposed.
        /// </summary>
        /// <param name="trackingId">Tracking id.</param>
        public static void CompleteSubscription(int trackingId)
        {
            if (trackingId <= 0)
            {
                return;
            }

            lock (s_gate)
            {
                if (s_entryById.TryGetValue(trackingId, out TrackedObservableEntry entry) == false)
                {
                    return;
                }

                entry.IsActive = false;
                entry.DisposedAtUtc = DateTime.UtcNow;
                s_entryById[trackingId] = entry;
            }
        }

        /// <summary>
        /// Copies tracker snapshot rows into the destination list.
        /// </summary>
        /// <param name="results">Destination list.</param>
        /// <param name="includeDisposed">Include disposed entries.</param>
        public static void GetSnapshot(List<OnityTrackedObservableInfo> results, bool includeDisposed = true)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            lock (s_gate)
            {
                DateTime now = DateTime.UtcNow;

                foreach (int trackingId in s_order)
                {
                    if (s_entryById.TryGetValue(trackingId, out TrackedObservableEntry entry) == false)
                    {
                        continue;
                    }

                    if (includeDisposed == false && entry.IsActive == false)
                    {
                        continue;
                    }

                    results.Add(entry.ToInfo(now));
                }
            }
        }

        /// <summary>
        /// Clears all tracked rows.
        /// </summary>
        public static void ClearAll()
        {
            lock (s_gate)
            {
                s_entryById.Clear();
                s_order.Clear();
            }
        }

        /// <summary>
        /// Clears disposed rows and keeps active rows.
        /// </summary>
        public static void ClearDisposed()
        {
            lock (s_gate)
            {
                List<int> activeIds = new List<int>(s_order.Count);

                while (s_order.Count > 0)
                {
                    int trackingId = s_order.Dequeue();

                    if (s_entryById.TryGetValue(trackingId, out TrackedObservableEntry entry) == false)
                    {
                        continue;
                    }

                    if (entry.IsActive == false)
                    {
                        s_entryById.Remove(trackingId);
                        continue;
                    }

                    activeIds.Add(trackingId);
                }

                for (int i = 0; i < activeIds.Count; i++)
                {
                    s_order.Enqueue(activeIds[i]);
                }
            }
        }

        private static void TrimOverflow_NoAlloc()
        {
            while (s_order.Count > k_maxTrackedSubscriptions)
            {
                int oldestId = s_order.Dequeue();
                s_entryById.Remove(oldestId);
            }
        }

        private struct TrackedObservableEntry
        {
            public readonly int TrackingId;
            public readonly string ObservableType;
            public readonly string ObserverType;
            public readonly DateTime AddedAtUtc;
            public readonly string StackTrace;

            public bool IsActive;
            public DateTime DisposedAtUtc;

            public TrackedObservableEntry(
                int trackingId,
                string observableType,
                string observerType,
                DateTime addedAtUtc,
                string stackTrace)
            {
                TrackingId = trackingId;
                ObservableType = observableType;
                ObserverType = observerType;
                AddedAtUtc = addedAtUtc;
                StackTrace = stackTrace ?? string.Empty;
                IsActive = true;
                DisposedAtUtc = default;
            }

            public OnityTrackedObservableInfo ToInfo(DateTime nowUtc)
            {
                DateTime end = IsActive ? nowUtc : DisposedAtUtc;
                double elapsedMilliseconds = (end - AddedAtUtc).TotalMilliseconds;

                return new OnityTrackedObservableInfo(
                    TrackingId,
                    ObservableType,
                    ObserverType,
                    IsActive,
                    AddedAtUtc,
                    elapsedMilliseconds,
                    StackTrace);
            }
        }
    }

    /// <summary>
    /// Snapshot row used by observable tracker diagnostics.
    /// </summary>
    public readonly struct OnityTrackedObservableInfo
    {
        /// <summary>
        /// Tracking identifier.
        /// </summary>
        public int TrackingId { get; }

        /// <summary>
        /// Observable runtime type.
        /// </summary>
        public string ObservableType { get; }

        /// <summary>
        /// Observer runtime type.
        /// </summary>
        public string ObserverType { get; }

        /// <summary>
        /// True when subscription is active.
        /// </summary>
        public bool IsActive { get; }

        /// <summary>
        /// Subscription start time in UTC.
        /// </summary>
        public DateTime AddedAtUtc { get; }

        /// <summary>
        /// Elapsed milliseconds from subscribe to now/disposal.
        /// </summary>
        public double ElapsedMilliseconds { get; }

        /// <summary>
        /// Captured stack trace.
        /// </summary>
        public string StackTrace { get; }

        /// <summary>
        /// Initializes a tracker row.
        /// </summary>
        /// <param name="trackingId">Tracking id.</param>
        /// <param name="observableType">Observable type.</param>
        /// <param name="observerType">Observer type.</param>
        /// <param name="isActive">Active flag.</param>
        /// <param name="addedAtUtc">Added timestamp UTC.</param>
        /// <param name="elapsedMilliseconds">Elapsed milliseconds.</param>
        /// <param name="stackTrace">Stack trace text.</param>
        public OnityTrackedObservableInfo(
            int trackingId,
            string observableType,
            string observerType,
            bool isActive,
            DateTime addedAtUtc,
            double elapsedMilliseconds,
            string stackTrace)
        {
            TrackingId = trackingId;
            ObservableType = observableType ?? "UnknownObservable";
            ObserverType = observerType ?? "UnknownObserver";
            IsActive = isActive;
            AddedAtUtc = addedAtUtc;
            ElapsedMilliseconds = elapsedMilliseconds;
            StackTrace = stackTrace ?? string.Empty;
        }
    }
}
