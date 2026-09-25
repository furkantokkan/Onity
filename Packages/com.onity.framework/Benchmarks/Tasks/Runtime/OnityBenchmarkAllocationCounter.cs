using System;
using System.Globalization;
using Unity.Profiling;
using UnityEngine.Scripting;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Calibrated allocation counter for the task benchmarks. The candidates are tried in order on
    /// the calling thread and the first one that reads at least 64 KiB for a 64 KiB allocation and
    /// zero for an empty control is used: the per-thread managed counter, the engine's
    /// "GC Allocated In Frame" profiler counter, and a process-wide heap delta. A player may disable
    /// the collector inside each heap-delta slice; the Editor does not support changing the collector
    /// mode, so there a slice interrupted by a collection is discarded instead.
    /// </summary>
    internal sealed class OnityBenchmarkAllocationCounter : IDisposable
    {
        public const string k_kindNone = "None";
        public const string k_kindPerThread = "PerThread";
        public const string k_kindProfilerCounter = "ProfilerCounter";
        public const string k_kindHeapDelta = "HeapDelta";

        private const int k_calibrationSize = 65536;
        private const int k_calibrationAttempts = 4;
        private const string k_profilerCounterName = "GC Allocated In Frame";

        private ProfilerRecorder m_recorder;
        private bool m_switchesCollectorMode;
        private bool m_guardsCollections;
        private int m_collectionsAtBegin;

        private OnityBenchmarkAllocationCounter(string kind, string description, long calibrationBytes, long emptyDeltaBytes)
        {
            Kind = kind;
            Description = description;
            CalibrationBytes = calibrationBytes;
            EmptyDeltaBytes = emptyDeltaBytes;
        }

        /// <summary>Counter kind: None, PerThread, ProfilerCounter, or HeapDelta.</summary>
        public string Kind { get; }

        /// <summary>Human-readable description of the counter and its controls.</summary>
        public string Description { get; }

        /// <summary>Bytes read for the 64 KiB positive control.</summary>
        public long CalibrationBytes { get; }

        /// <summary>Bytes read for the empty control.</summary>
        public long EmptyDeltaBytes { get; }

        /// <summary>True when a counter passed both controls.</summary>
        public bool IsAvailable => Kind != k_kindNone;

        /// <summary>
        /// False for the profiler counter, which the engine resets every frame, so a slice that spans
        /// frames cannot be measured with it.
        /// </summary>
        public bool SupportsCrossFrameSlices => Kind != k_kindProfilerCounter;

        /// <summary>
        /// Selects and calibrates a counter on the calling thread.
        /// </summary>
        /// <param name="allowProfilerCounter">Whether the engine profiler counter may be used; main thread only.</param>
        /// <param name="allowCollectorModeSwitch">
        /// Whether a heap delta may disable the collector inside each slice; false in the Editor,
        /// which does not support it, and on worker threads whose slices the main thread brackets.
        /// </param>
        /// <returns>The selected counter, or an unavailable counter that records why.</returns>
        public static OnityBenchmarkAllocationCounter Create(bool allowProfilerCounter, bool allowCollectorModeSwitch)
        {
            string failures = string.Empty;

            OnityBenchmarkAllocationCounter counter = TryCreatePerThread(ref failures);
            if (counter != null)
            {
                return counter;
            }

            if (allowProfilerCounter)
            {
                counter = TryCreateProfilerCounter(ref failures);
                if (counter != null)
                {
                    return counter;
                }
            }

            counter = TryCreateHeapDelta(allowCollectorModeSwitch, ref failures);
            if (counter != null)
            {
                return counter;
            }

            return new OnityBenchmarkAllocationCounter(k_kindNone, "Unavailable: " + failures.TrimEnd(' ', ';'), 0, 0);
        }

        /// <summary>Opens a slice and returns its starting reading.</summary>
        public long Begin()
        {
            BeginSlice();
            return Read();
        }

        /// <summary>
        /// Closes a slice and returns the bytes allocated since <paramref name="start"/>.
        /// <paramref name="valid"/> is false when a collection ran inside a collection-guarded slice.
        /// </summary>
        public long End(long start, out bool valid)
        {
            long delta = Read() - start;
            valid = IsSliceValid(m_collectionsAtBegin);
            EndSlice();
            return delta;
        }

        /// <summary>Prepares a slice whose readings the caller takes itself.</summary>
        public void BeginSlice()
        {
            if (m_switchesCollectorMode)
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
            }

            m_collectionsAtBegin = ReadCollections();
        }

        /// <summary>Ends a slice opened with <see cref="BeginSlice"/>.</summary>
        public void EndSlice()
        {
            if (m_switchesCollectorMode)
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
            }
        }

        /// <summary>Reads the counter; the difference between two readings is the allocated bytes.</summary>
        public long Read()
        {
            switch (Kind)
            {
                case k_kindPerThread:
#if ENABLE_IL2CPP
                    return 0;
#else
                    return GC.GetAllocatedBytesForCurrentThread();
#endif
                case k_kindProfilerCounter:
                    return m_recorder.CurrentValue;
                case k_kindHeapDelta:
                    return GC.GetTotalMemory(false);
                default:
                    return 0;
            }
        }

        /// <summary>Reads the collection count a guarded slice compares against; zero when not guarded.</summary>
        public int ReadCollections()
        {
            return m_guardsCollections ? GC.CollectionCount(0) : 0;
        }

        /// <summary>True unless a guarded slice saw a collection since <paramref name="collectionsAtStart"/>.</summary>
        public bool IsSliceValid(int collectionsAtStart)
        {
            return !m_guardsCollections || GC.CollectionCount(0) == collectionsAtStart;
        }

        public void Dispose()
        {
            if (Kind == k_kindProfilerCounter)
            {
                m_recorder.Dispose();
            }
        }

        private static OnityBenchmarkAllocationCounter TryCreatePerThread(ref string failures)
        {
#if ENABLE_IL2CPP
            failures += "per-thread counter skipped on IL2CPP after the DI harness observed crashes; ";
            return null;
#else
            try
            {
                GC.GetAllocatedBytesForCurrentThread();
                long before = GC.GetAllocatedBytesForCurrentThread();
                byte[] calibration = new byte[k_calibrationSize];
                long after = GC.GetAllocatedBytesForCurrentThread();
                GC.KeepAlive(calibration);
                long calibrationBytes = after - before;
                before = GC.GetAllocatedBytesForCurrentThread();
                after = GC.GetAllocatedBytesForCurrentThread();
                long emptyDeltaBytes = after - before;
                if (calibrationBytes >= k_calibrationSize && emptyDeltaBytes == 0)
                {
                    return new OnityBenchmarkAllocationCounter(
                        k_kindPerThread,
                        "GC.GetAllocatedBytesForCurrentThread; 64 KiB positive control and empty control passed.",
                        calibrationBytes,
                        emptyDeltaBytes);
                }

                failures += "per-thread counter read " + Format(calibrationBytes) + " / " + Format(emptyDeltaBytes)
                    + " bytes for the 64 KiB and empty controls; ";
            }
            catch (Exception exception)
            {
                failures += "per-thread counter threw " + exception.GetType().Name + "; ";
            }

            return null;
#endif
        }

        private static OnityBenchmarkAllocationCounter TryCreateProfilerCounter(ref string failures)
        {
            ProfilerRecorder recorder = default;
            try
            {
                recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, k_profilerCounterName);
                if (!recorder.Valid)
                {
                    failures += "profiler counter '" + k_profilerCounterName + "' is not valid; ";
                    recorder.Dispose();
                    return null;
                }

                long before = recorder.CurrentValue;
                byte[] calibration = new byte[k_calibrationSize];
                long after = recorder.CurrentValue;
                GC.KeepAlive(calibration);
                long calibrationBytes = after - before;
                before = recorder.CurrentValue;
                after = recorder.CurrentValue;
                long emptyDeltaBytes = after - before;
                if (calibrationBytes >= k_calibrationSize && emptyDeltaBytes == 0)
                {
                    OnityBenchmarkAllocationCounter counter = new OnityBenchmarkAllocationCounter(
                        k_kindProfilerCounter,
                        "Unity.Profiling.ProfilerRecorder '" + k_profilerCounterName + "' (Memory category); 64 KiB "
                        + "positive control and empty control passed. Process-wide and reset every frame, so "
                        + "slices that span frames are not measured with it.",
                        calibrationBytes,
                        emptyDeltaBytes);
                    counter.m_recorder = recorder;
                    return counter;
                }

                failures += "profiler counter read " + Format(calibrationBytes) + " / " + Format(emptyDeltaBytes)
                    + " bytes for the 64 KiB and empty controls; ";
                recorder.Dispose();
            }
            catch (Exception exception)
            {
                failures += "profiler counter threw " + exception.GetType().Name + "; ";
                try
                {
                    recorder.Dispose();
                }
                catch (Exception)
                {
                    // The recorder was never started or is already released.
                }
            }

            return null;
        }

        private static OnityBenchmarkAllocationCounter TryCreateHeapDelta(bool allowCollectorModeSwitch, ref string failures)
        {
            if (allowCollectorModeSwitch)
            {
                try
                {
                    GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                    long calibrationBytes;
                    long emptyDeltaBytes;
                    try
                    {
                        ReadHeapDeltaControls(out calibrationBytes, out emptyDeltaBytes);
                    }
                    finally
                    {
                        GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
                    }

                    if (calibrationBytes >= k_calibrationSize && emptyDeltaBytes == 0)
                    {
                        OnityBenchmarkAllocationCounter counter = new OnityBenchmarkAllocationCounter(
                            k_kindHeapDelta,
                            "GC.GetTotalMemory delta with GarbageCollector.GCMode disabled inside each measured "
                            + "slice; 64 KiB positive control and empty control passed. Process-wide, block "
                            + "granularity: read averages over many operations only.",
                            calibrationBytes,
                            emptyDeltaBytes);
                        counter.m_switchesCollectorMode = true;
                        return counter;
                    }

                    failures += "collector-disabled heap delta read " + Format(calibrationBytes) + " / "
                        + Format(emptyDeltaBytes) + " bytes for the 64 KiB and empty controls; ";
                }
                catch (Exception exception)
                {
                    failures += "collector-disabled heap delta threw " + exception.GetType().Name + "; ";
                }
            }

            try
            {
                for (int attempt = 0; attempt < k_calibrationAttempts; attempt++)
                {
                    int collections = GC.CollectionCount(0);
                    ReadHeapDeltaControls(out long calibrationBytes, out long emptyDeltaBytes);
                    if (GC.CollectionCount(0) != collections)
                    {
                        continue;
                    }

                    if (calibrationBytes >= k_calibrationSize && emptyDeltaBytes == 0)
                    {
                        OnityBenchmarkAllocationCounter counter = new OnityBenchmarkAllocationCounter(
                            k_kindHeapDelta,
                            "GC.GetTotalMemory delta with the collector enabled; a slice in which "
                            + "GC.CollectionCount changed is discarded. 64 KiB positive control and empty "
                            + "control passed. Process-wide, block granularity: read averages over many "
                            + "operations only.",
                            calibrationBytes,
                            emptyDeltaBytes);
                        counter.m_guardsCollections = true;
                        return counter;
                    }

                    failures += "collection-guarded heap delta read " + Format(calibrationBytes) + " / "
                        + Format(emptyDeltaBytes) + " bytes for the 64 KiB and empty controls; ";
                    return null;
                }

                failures += "collection-guarded heap delta was interrupted by a collection on every calibration attempt; ";
            }
            catch (Exception exception)
            {
                failures += "collection-guarded heap delta threw " + exception.GetType().Name + "; ";
            }

            return null;
        }

        private static void ReadHeapDeltaControls(out long calibrationBytes, out long emptyDeltaBytes)
        {
            long before = GC.GetTotalMemory(false);
            byte[] calibration = new byte[k_calibrationSize];
            long after = GC.GetTotalMemory(false);
            GC.KeepAlive(calibration);
            calibrationBytes = after - before;
            before = GC.GetTotalMemory(false);
            after = GC.GetTotalMemory(false);
            emptyDeltaBytes = after - before;
        }

        private static string Format(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
