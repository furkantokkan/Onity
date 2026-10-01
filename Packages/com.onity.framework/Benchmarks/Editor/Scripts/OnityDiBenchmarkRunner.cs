using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Onity.DI;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Profiling;
using VContainer;
using Zenject;

namespace Onity.Editor.Benchmarks
{
    /// <summary>
    /// Runs DI benchmark scenarios for Onity, VContainer, and Zenject.
    /// </summary>
    public static class OnityDiBenchmarkRunner
    {
        private const int k_warmupIterations = 512;
        private const int k_samplesPerCase = 8;
        private const int k_hotPathWarmupIterations = 10000;
        private const int k_hotPathSamplesPerCase = 16;
        private const int k_allocationProbeCount = 128;
        private const int k_allocationProbeBytes = 8192;
        private const int k_allocationEventIterations = 64;
        private const int k_allocationEventSamplesPerCase = 3;
        private const int k_allocationEventCapacity = 131072;
        private const int k_allocationEventProbeCapacity = 1024;
        private const string k_resultsDirectory = "Packages/com.onity.framework/Benchmarks/Results";
        private const string k_outputDirectoryArgument = "-onityBenchmarkOutputDirectory";
        private const string k_eventsOnlyArgument = "-onityBenchmarkEventsOnly";
        private const string k_hotPathsOnlyArgument = "-onityBenchmarkHotPathsOnly";
        private const string k_bytesProbeArgument = "-onityBenchmarkBytesProbe";
        private const string k_bytesOnlyArgument = "-onityBenchmarkBytesOnly";
        private const string k_latestJsonFileName = "di-benchmark-latest.json";
        private const string k_latestCsvFileName = "di-benchmark-latest.csv";
        private const string k_latestMarkdownFileName = "di-benchmark-latest.md";
        private const string k_latestEventJsonFileName = "di-allocation-events-latest.json";
        private const string k_latestByteJsonFileName = "di-allocation-bytes-latest.json";
        private const string k_emptyProbeMarkerName = "Onity.DI.BytesProbe.Empty";
        private const string k_positiveProbeMarkerName = "Onity.DI.BytesProbe.Positive";
        private const string k_doubleProbeMarkerName = "Onity.DI.BytesProbe.Double";
        private const string k_byteSampleMarkerName = "Onity.DI.Bytes.Sample";
        private const string k_keyedServiceId = "benchmark-service";

        private static readonly ProfilerMarker s_emptyProbeMarker = new ProfilerMarker(k_emptyProbeMarkerName);
        private static readonly ProfilerMarker s_positiveProbeMarker = new ProfilerMarker(k_positiveProbeMarkerName);
        private static readonly ProfilerMarker s_doubleProbeMarker = new ProfilerMarker(k_doubleProbeMarkerName);
        private static readonly ProfilerMarker s_byteSampleMarker = new ProfilerMarker(k_byteSampleMarkerName);
        private static bool s_probeMarkersWritten;
        private static bool s_probeCompleted;
        private static bool s_runByteBenchmarks;
        private static bool s_originalProfilerEnabled;
        private static bool s_originalProfileEditor;
        private static bool s_originalCpuAreaEnabled;
        private static int s_probeLastScannedFrame;
        private static DateTime s_probeStartedAtUtc;
        private static int s_controlPositiveEvents;
        private static long s_controlPositiveBytes;
        private static int s_controlDoubleEvents;
        private static long s_controlDoubleBytes;
        private static List<AllocationByteCase> s_byteCases;
        private static int s_byteCaseIndex;
        private static int s_byteSampleIndex;
        private static int s_byteLastScannedFrame;
        private static bool s_byteSamplePending;
        private static DateTime s_byteSampleStartedAtUtc;

        private static readonly BenchmarkContainerKind[] k_containers =
        {
            BenchmarkContainerKind.Onity,
            BenchmarkContainerKind.VContainer,
            BenchmarkContainerKind.Zenject
        };

        // Onity is measured on both resolve paths so the baked fast path can be
        // compared side by side against the proven reflection path. The other
        // containers expose no such toggle and are measured once.
        private static readonly OnityResolveMode[] k_onityResolveModes =
        {
            OnityResolveMode.Reflection,
            OnityResolveMode.Baked
        };

        // OnityContainer.UseBakedResolve is internal; it is reflected exactly as
        // the parity test suite does so the benchmark can flip the path without a
        // public API. Captured per pass and restored in a finally.
        private static readonly PropertyInfo s_useBakedResolveProperty =
            typeof(OnityContainer).GetProperty(
                "UseBakedResolve",
                BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly ScenarioConfig[] k_scenarios =
        {
            new ScenarioConfig(BenchmarkScenario.ResolveSingleton, "Resolve (Singleton)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveTransient, "Resolve (Transient)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveCombined, "Resolve (Combined)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveComplex, "Resolve (Complex)", 10000),
            new ScenarioConfig(BenchmarkScenario.PrepareAndRegisterComplex, "Prepare & Register (Complex)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveKeyedSingleton, "Resolve (Keyed Singleton)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveScopedSingleton, "Resolve (Scoped Singleton)", 10000)
        };

        [MenuItem("Onity/Benchmarks/Run DI Benchmarks (Editor)")]
        private static void RunBenchmarksFromMenu()
        {
            BenchmarkReport report = RunBenchmarks();
            string jsonPath = SaveReport(report);
            AssetDatabase.Refresh();

            UnityEngine.Debug.Log($"Onity DI benchmark completed. Latest report: {jsonPath}");
        }

        /// <summary>
        /// Command-line entry point for Unity batchmode benchmark generation.
        /// </summary>
        public static void RunBenchmarksFromCommandLine()
        {
            bool hotPathsOnly = HasArgument(k_hotPathsOnlyArgument);
            if (hotPathsOnly &&
                (HasArgument(k_bytesOnlyArgument) || HasArgument(k_bytesProbeArgument) ||
                 HasArgument(k_eventsOnlyArgument)))
            {
                throw new ArgumentException("The hot-path timing option cannot be combined with allocation options.");
            }

            if (HasArgument(k_bytesOnlyArgument))
            {
                StartAllocationByteProbe(true);
                return;
            }

            if (HasArgument(k_bytesProbeArgument))
            {
                StartAllocationByteProbe(false);
                return;
            }

            if (HasArgument(k_eventsOnlyArgument))
            {
                AllocationEventReport eventReport = RunAllocationEventBenchmarks();
                string eventPath = SaveAllocationEventReport(eventReport);
                UnityEngine.Debug.Log($"Onity DI allocation-event benchmark completed: {eventPath}");
                return;
            }

            BenchmarkReport report = RunBenchmarks(hotPathsOnly);
            string jsonPath = SaveReport(report, hotPathsOnly);
            AssetDatabase.Refresh();

            UnityEngine.Debug.Log($"Onity DI benchmark completed in batchmode. Latest report: {jsonPath}");
        }

        private static void StartAllocationByteProbe(bool runBenchmarks)
        {
            if (runBenchmarks && s_useBakedResolveProperty == null)
            {
                throw new InvalidOperationException("Internal OnityContainer.UseBakedResolve flag was not found.");
            }

            s_probeStartedAtUtc = DateTime.UtcNow;
            s_probeLastScannedFrame = ProfilerDriver.lastFrameIndex;
            s_probeMarkersWritten = false;
            s_probeCompleted = false;
            s_runByteBenchmarks = runBenchmarks;
            s_originalProfilerEnabled = ProfilerDriver.enabled;
            s_originalProfileEditor = ProfilerDriver.profileEditor;
            s_originalCpuAreaEnabled = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
            ProfilerDriver.profileEditor = true;
            ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
            ProfilerDriver.enabled = true;
            EditorApplication.update += UpdateAllocationByteProbe;
            UnityEngine.Debug.Log("Onity DI byte probe started.");
        }

        private static void UpdateAllocationByteProbe()
        {
            if (s_probeCompleted)
            {
                UpdateAllocationByteBenchmarks();
                return;
            }

            if ((DateTime.UtcNow - s_probeStartedAtUtc).TotalSeconds > 20)
            {
                FinishAllocationByteProbe(false, "Profiler frame or probe markers were unavailable.");
                return;
            }

            if (s_probeMarkersWritten == false)
            {
                s_probeLastScannedFrame = ProfilerDriver.lastFrameIndex;

                using (s_emptyProbeMarker.Auto())
                {
                }

                using (s_positiveProbeMarker.Auto())
                {
                    byte[][] probes = new byte[k_allocationProbeCount][];
                    for (int i = 0; i < probes.Length; i++)
                    {
                        probes[i] = new byte[k_allocationProbeBytes];
                    }

                    GC.KeepAlive(probes);
                }

                using (s_doubleProbeMarker.Auto())
                {
                    byte[][] probes = new byte[k_allocationProbeCount][];
                    for (int i = 0; i < probes.Length; i++)
                    {
                        probes[i] = new byte[k_allocationProbeBytes * 2];
                    }

                    GC.KeepAlive(probes);
                }

                s_probeMarkersWritten = true;
                return;
            }

            int lastFrame = ProfilerDriver.lastFrameIndex;
            for (int frameIndex = s_probeLastScannedFrame + 1; frameIndex <= lastFrame; frameIndex++)
            {
                if (TryReadAllocationByteProbe(
                    frameIndex,
                    out int emptyEvents,
                    out long emptyBytes,
                    out int positiveEvents,
                    out long positiveBytes,
                    out int doubleEvents,
                    out long doubleBytes) == false)
                {
                    continue;
                }

                bool valid = emptyEvents == 0
                    && emptyBytes == 0
                    && positiveEvents >= k_allocationProbeCount
                    && positiveBytes >= k_allocationProbeCount * k_allocationProbeBytes
                    && doubleEvents >= k_allocationProbeCount
                    && doubleBytes >= 2L * k_allocationProbeCount * k_allocationProbeBytes
                    && doubleBytes - positiveBytes >= k_allocationProbeCount * k_allocationProbeBytes;

                string detail = $"frame={frameIndex}, empty={emptyEvents}/{emptyBytes} B, "
                    + $"positive={positiveEvents}/{positiveBytes} B, "
                    + $"double={doubleEvents}/{doubleBytes} B";
                if (valid == false || s_runByteBenchmarks == false)
                {
                    FinishAllocationByteProbe(valid, detail);
                    return;
                }

                s_controlPositiveEvents = positiveEvents;
                s_controlPositiveBytes = positiveBytes;
                s_controlDoubleEvents = doubleEvents;
                s_controlDoubleBytes = doubleBytes;
                s_probeCompleted = true;
                s_byteCases = CreateAllocationByteCases();
                s_byteCaseIndex = 0;
                s_byteSampleIndex = 0;
                s_byteSamplePending = false;
                UnityEngine.Debug.Log($"Onity DI allocation-byte controls passed: {detail}");
                return;
            }

            s_probeLastScannedFrame = lastFrame;
        }

        private static bool TryReadAllocationByteProbe(
            int frameIndex,
            out int emptyEvents,
            out long emptyBytes,
            out int positiveEvents,
            out long positiveBytes,
            out int doubleEvents,
            out long doubleBytes)
        {
            emptyEvents = 0;
            emptyBytes = 0;
            positiveEvents = 0;
            positiveBytes = 0;
            doubleEvents = 0;
            doubleBytes = 0;
            bool foundEmpty = false;
            bool foundPositive = false;
            bool foundDouble = false;

            for (int threadIndex = 0; ; threadIndex++)
            {
                using RawFrameDataView frame = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex);
                if (frame.valid == false)
                {
                    break;
                }

                int allocId = frame.GetMarkerId("GC.Alloc");
                int emptyId = frame.GetMarkerId(k_emptyProbeMarkerName);
                int positiveId = frame.GetMarkerId(k_positiveProbeMarkerName);
                int doubleId = frame.GetMarkerId(k_doubleProbeMarkerName);

                for (int sampleIndex = 0; sampleIndex < frame.sampleCount; sampleIndex++)
                {
                    int markerId = frame.GetSampleMarkerId(sampleIndex);
                    if (markerId != emptyId && markerId != positiveId && markerId != doubleId)
                    {
                        continue;
                    }

                    int events = 0;
                    long bytes = 0;
                    int lastChild = sampleIndex + frame.GetSampleChildrenCountRecursive(sampleIndex);
                    for (int childIndex = sampleIndex + 1; childIndex <= lastChild; childIndex++)
                    {
                        if (frame.GetSampleMarkerId(childIndex) == allocId)
                        {
                            events++;
                            bytes += frame.GetSampleMetadataAsLong(childIndex, 0);
                        }
                    }

                    if (markerId == emptyId)
                    {
                        foundEmpty = true;
                        emptyEvents = events;
                        emptyBytes = bytes;
                    }
                    else if (markerId == positiveId)
                    {
                        foundPositive = true;
                        positiveEvents = events;
                        positiveBytes = bytes;
                    }
                    else
                    {
                        foundDouble = true;
                        doubleEvents = events;
                        doubleBytes = bytes;
                    }
                }
            }

            return foundEmpty && foundPositive && foundDouble;
        }

        private static List<AllocationByteCase> CreateAllocationByteCases()
        {
            List<AllocationByteCase> cases =
                new List<AllocationByteCase>(k_scenarios.Length * (k_containers.Length + 1));

            for (int scenarioIndex = 0; scenarioIndex < k_scenarios.Length; scenarioIndex++)
            {
                ScenarioConfig config = k_scenarios[scenarioIndex];

                for (int containerIndex = 0; containerIndex < k_containers.Length; containerIndex++)
                {
                    BenchmarkContainerKind containerKind = k_containers[containerIndex];
                    if (containerKind == BenchmarkContainerKind.Onity)
                    {
                        for (int modeIndex = 0; modeIndex < k_onityResolveModes.Length; modeIndex++)
                        {
                            cases.Add(new AllocationByteCase(config, containerKind, k_onityResolveModes[modeIndex]));
                        }
                    }
                    else
                    {
                        cases.Add(new AllocationByteCase(config, containerKind, OnityResolveMode.Reflection));
                    }
                }
            }

            return cases;
        }

        private static void UpdateAllocationByteBenchmarks()
        {
            if (s_byteCaseIndex >= s_byteCases.Count)
            {
                try
                {
                    string path = SaveAllocationByteReport();
                    FinishAllocationByteProbe(true, $"allocation-byte report: {path}");
                }
                catch (Exception error)
                {
                    UnityEngine.Debug.LogException(error);
                    FinishAllocationByteProbe(false, "Could not save allocation-byte report.");
                }

                return;
            }

            if (s_byteSamplePending == false)
            {
                try
                {
                    AllocationByteCase currentCase = s_byteCases[s_byteCaseIndex];
                    bool isOnity = currentCase.ContainerKind == BenchmarkContainerKind.Onity;
                    object originalFlag = isOnity ? s_useBakedResolveProperty.GetValue(null) : null;

                    try
                    {
                        if (isOnity)
                        {
                            SetUseBakedResolve(currentCase.ResolveMode == OnityResolveMode.Baked);
                        }

                        using BenchmarkOperation operation = CreateOperation(
                            currentCase.ContainerKind,
                            currentCase.Config.scenario);
                        for (int i = 0; i < k_warmupIterations; i++)
                        {
                            operation.Invoke();
                        }

                        ForceFullGc();
                        s_byteLastScannedFrame = ProfilerDriver.lastFrameIndex;
                        using (s_byteSampleMarker.Auto())
                        {
                            for (int i = 0; i < k_allocationEventIterations; i++)
                            {
                                operation.Invoke();
                            }
                        }
                    }
                    finally
                    {
                        if (isOnity)
                        {
                            s_useBakedResolveProperty.SetValue(null, originalFlag);
                        }
                    }

                    s_byteSampleStartedAtUtc = DateTime.UtcNow;
                    s_byteSamplePending = true;
                }
                catch (Exception error)
                {
                    UnityEngine.Debug.LogException(error);
                    FinishAllocationByteProbe(false, "Could not run an allocation-byte sample.");
                }

                return;
            }

            if ((DateTime.UtcNow - s_byteSampleStartedAtUtc).TotalSeconds > 20)
            {
                FinishAllocationByteProbe(false, "A measured profiler frame was unavailable.");
                return;
            }

            try
            {
                int lastFrame = ProfilerDriver.lastFrameIndex;
                for (int frameIndex = s_byteLastScannedFrame + 1; frameIndex <= lastFrame; frameIndex++)
                {
                    if (TryReadMarkedAllocation(
                        frameIndex,
                        k_byteSampleMarkerName,
                        out int eventCount,
                        out long allocationBytes) == false)
                    {
                        continue;
                    }

                    AllocationByteCase currentCase = s_byteCases[s_byteCaseIndex];
                    currentCase.BytesPerSample[s_byteSampleIndex] = allocationBytes;
                    currentCase.EventsPerSample[s_byteSampleIndex] = eventCount;
                    s_byteSampleIndex++;

                    if (s_byteSampleIndex == k_allocationEventSamplesPerCase)
                    {
                        s_byteSampleIndex = 0;
                        s_byteCaseIndex++;
                    }

                    s_byteSamplePending = false;
                    return;
                }

                s_byteLastScannedFrame = lastFrame;
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogException(error);
                FinishAllocationByteProbe(false, "Could not read an allocation-byte sample.");
            }
        }

        private static bool TryReadMarkedAllocation(
            int frameIndex,
            string markerName,
            out int eventCount,
            out long allocationBytes)
        {
            eventCount = 0;
            allocationBytes = 0;
            bool found = false;

            for (int threadIndex = 0; ; threadIndex++)
            {
                using RawFrameDataView frame = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex);
                if (frame.valid == false)
                {
                    break;
                }

                int markerId = frame.GetMarkerId(markerName);
                int allocId = frame.GetMarkerId("GC.Alloc");

                for (int sampleIndex = 0; sampleIndex < frame.sampleCount; sampleIndex++)
                {
                    if (frame.GetSampleMarkerId(sampleIndex) != markerId)
                    {
                        continue;
                    }

                    if (found)
                    {
                        throw new InvalidOperationException($"Multiple {markerName} samples were found in frame {frameIndex}.");
                    }

                    found = true;
                    int lastChild = sampleIndex + frame.GetSampleChildrenCountRecursive(sampleIndex);
                    for (int childIndex = sampleIndex + 1; childIndex <= lastChild; childIndex++)
                    {
                        if (frame.GetSampleMarkerId(childIndex) != allocId)
                        {
                            continue;
                        }

                        if (frame.GetSampleMetadataCount(childIndex) == 0)
                        {
                            throw new InvalidOperationException("GC.Alloc sample lacks allocation-size metadata.");
                        }

                        eventCount++;
                        allocationBytes += frame.GetSampleMetadataAsLong(childIndex, 0);
                    }
                }
            }

            return found;
        }

        internal static void ImportIl2CppAllocationProfile(string rawProfilePath, string latestJson)
        {
            if (!File.Exists(rawProfilePath) || new FileInfo(rawProfilePath).Length == 0)
            {
                throw new FileNotFoundException("The IL2CPP player profiler capture is missing or empty.", rawProfilePath);
            }

            bool originalProfilerEnabled = ProfilerDriver.enabled;
            bool originalProfileEditor = ProfilerDriver.profileEditor;

            try
            {
                ProfilerDriver.enabled = false;
                ProfilerDriver.profileEditor = false;
                if (!ProfilerDriver.LoadProfile(rawProfilePath, false))
                {
                    throw new InvalidOperationException($"Unity could not load the player profiler capture: {rawProfilePath}");
                }

                List<AllocationByteCase> cases = CreateAllocationByteCases();
                string[] markerNames = new string[cases.Count];
                int[] caseCounts = new int[cases.Count];
                for (int i = 0; i < cases.Count; i++)
                {
                    AllocationByteCase currentCase = cases[i];
                    markerNames[i] = BuildProfilerMarkerName(
                        currentCase.Config,
                        currentCase.ContainerKind,
                        currentCase.ResolveMode);
                }

                int emptyCount = 0;
                int positiveCount = 0;
                int doubleCount = 0;
                int emptyEvents = 0;
                long emptyBytes = 0;
                int positiveEvents = 0;
                long positiveBytes = 0;
                int doubleEvents = 0;
                long doubleBytes = 0;
                int firstFrame = ProfilerDriver.firstFrameIndex;
                int lastFrame = ProfilerDriver.lastFrameIndex;
                if (firstFrame < 0 || lastFrame < firstFrame)
                {
                    throw new InvalidOperationException("The player profiler capture has no readable frames.");
                }

                for (int frameIndex = firstFrame; frameIndex <= lastFrame; frameIndex++)
                {
                    for (int threadIndex = 0; ; threadIndex++)
                    {
                        using RawFrameDataView frame = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex);
                        if (!frame.valid)
                        {
                            break;
                        }

                        int allocId = frame.GetMarkerId("GC.Alloc");
                        int emptyId = frame.GetMarkerId(k_emptyProbeMarkerName);
                        int positiveId = frame.GetMarkerId(k_positiveProbeMarkerName);
                        int doubleId = frame.GetMarkerId(k_doubleProbeMarkerName);
                        Dictionary<int, int> caseIds = new Dictionary<int, int>(cases.Count);
                        for (int i = 0; i < markerNames.Length; i++)
                        {
                            int markerId = frame.GetMarkerId(markerNames[i]);
                            if (markerId != FrameDataView.invalidMarkerId)
                            {
                                caseIds.Add(markerId, i);
                            }
                        }

                        for (int sampleIndex = 0; sampleIndex < frame.sampleCount; sampleIndex++)
                        {
                            int markerId = frame.GetSampleMarkerId(sampleIndex);
                            bool isEmpty = markerId == emptyId && emptyId != FrameDataView.invalidMarkerId;
                            bool isPositive = markerId == positiveId && positiveId != FrameDataView.invalidMarkerId;
                            bool isDouble = markerId == doubleId && doubleId != FrameDataView.invalidMarkerId;
                            bool isCase = caseIds.TryGetValue(markerId, out int caseIndex);
                            if (!isEmpty && !isPositive && !isDouble && !isCase)
                            {
                                continue;
                            }

                            ReadAllocationSample(frame, sampleIndex, allocId, out int events, out long bytes);
                            if (isEmpty)
                            {
                                emptyCount++;
                                emptyEvents = events;
                                emptyBytes = bytes;
                            }
                            else if (isPositive)
                            {
                                positiveCount++;
                                positiveEvents = events;
                                positiveBytes = bytes;
                            }
                            else if (isDouble)
                            {
                                doubleCount++;
                                doubleEvents = events;
                                doubleBytes = bytes;
                            }
                            else
                            {
                                int nextSample = caseCounts[caseIndex];
                                if (nextSample >= k_allocationEventSamplesPerCase)
                                {
                                    throw new InvalidOperationException(
                                        $"Too many player profiler samples for {markerNames[caseIndex]}.");
                                }

                                cases[caseIndex].EventsPerSample[nextSample] = events;
                                cases[caseIndex].BytesPerSample[nextSample] = bytes;
                                caseCounts[caseIndex] = nextSample + 1;
                            }
                        }
                    }
                }

                if (emptyCount != 1 || positiveCount != 1 || doubleCount != 1)
                {
                    throw new InvalidOperationException(
                        $"Player profiler control markers are incomplete or duplicated: empty={emptyCount}, positive={positiveCount}, double={doubleCount}.");
                }

                bool controlsValid = emptyEvents == 0
                    && emptyBytes == 0
                    && positiveEvents >= k_allocationProbeCount
                    && positiveBytes >= (long)k_allocationProbeCount * k_allocationProbeBytes
                    && doubleEvents >= k_allocationProbeCount
                    && doubleBytes >= 2L * k_allocationProbeCount * k_allocationProbeBytes
                    && doubleBytes - positiveBytes >= (long)k_allocationProbeCount * k_allocationProbeBytes;
                if (!controlsValid)
                {
                    throw new InvalidOperationException(
                        $"Player profiler allocation controls failed: empty={emptyEvents}/{emptyBytes} B, "
                        + $"positive={positiveEvents}/{positiveBytes} B, double={doubleEvents}/{doubleBytes} B.");
                }

                for (int i = 0; i < caseCounts.Length; i++)
                {
                    if (caseCounts[i] != k_allocationEventSamplesPerCase)
                    {
                        throw new InvalidOperationException(
                            $"Player profiler captured {caseCounts[i]} of {k_allocationEventSamplesPerCase} expected samples for {markerNames[i]}.");
                    }
                }

                SaveIl2CppAllocationByteReport(
                    latestJson,
                    rawProfilePath,
                    cases,
                    positiveEvents,
                    positiveBytes,
                    doubleEvents,
                    doubleBytes);
            }
            finally
            {
                ProfilerDriver.enabled = false;
                ProfilerDriver.profileEditor = originalProfileEditor;
                ProfilerDriver.enabled = originalProfilerEnabled;
            }
        }

        private static void ReadAllocationSample(
            RawFrameDataView frame,
            int sampleIndex,
            int allocId,
            out int eventCount,
            out long allocationBytes)
        {
            eventCount = 0;
            allocationBytes = 0;
            int lastChild = sampleIndex + frame.GetSampleChildrenCountRecursive(sampleIndex);
            for (int childIndex = sampleIndex + 1; childIndex <= lastChild; childIndex++)
            {
                if (frame.GetSampleMarkerId(childIndex) != allocId ||
                    allocId == FrameDataView.invalidMarkerId)
                {
                    continue;
                }

                if (frame.GetSampleMetadataCount(childIndex) == 0)
                {
                    throw new InvalidOperationException("A player GC.Alloc sample lacks allocation-size metadata.");
                }

                long bytes = frame.GetSampleMetadataAsLong(childIndex, 0);
                if (bytes <= 0)
                {
                    throw new InvalidOperationException("A player GC.Alloc sample has an invalid allocation size.");
                }

                eventCount++;
                allocationBytes += bytes;
            }
        }

        private static void FinishAllocationByteProbe(bool valid, string detail)
        {
            EditorApplication.update -= UpdateAllocationByteProbe;
            ProfilerDriver.enabled = false;
            ProfilerDriver.profileEditor = s_originalProfileEditor;
            ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, s_originalCpuAreaEnabled);
            ProfilerDriver.enabled = s_originalProfilerEnabled;
            if (valid)
            {
                UnityEngine.Debug.Log($"Onity DI byte probe passed: {detail}");
            }
            else
            {
                UnityEngine.Debug.LogError($"Onity DI byte probe failed: {detail}");
            }

            EditorApplication.Exit(valid ? 0 : 2);
        }

        /// <summary>
        /// Runs all scenarios or the selected baked Onity timing subset.
        /// </summary>
        /// <returns>Benchmark report.</returns>
        private static BenchmarkReport RunBenchmarks(bool hotPathsOnly = false)
        {
            if (s_useBakedResolveProperty == null)
            {
                throw new InvalidOperationException(
                    "Internal OnityContainer.UseBakedResolve flag was not found; the baked-vs-reflection benchmark cannot toggle the resolve path.");
            }

            if (hotPathsOnly)
            {
                return RunHotPathBenchmarks();
            }

            AllocationCounterKind allocationCounter = SelectAllocationCounter();

            BenchmarkReport report = new BenchmarkReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                scope = "Full comparison",
                samplesPerCase = k_samplesPerCase,
                warmupIterations = k_warmupIterations,
                allocationMeasured = allocationCounter != AllocationCounterKind.Unavailable,
                allocationCounter = allocationCounter.ToString(),
                scenarios = new ScenarioReport[k_scenarios.Length]
            };

            List<MetricReport> metrics = new List<MetricReport>(k_containers.Length + 1);

            for (int scenarioIndex = 0; scenarioIndex < k_scenarios.Length; scenarioIndex++)
            {
                ScenarioConfig config = k_scenarios[scenarioIndex];
                metrics.Clear();

                for (int containerIndex = 0; containerIndex < k_containers.Length; containerIndex++)
                {
                    BenchmarkContainerKind containerKind = k_containers[containerIndex];

                    if (containerKind == BenchmarkContainerKind.Onity)
                    {
                        // Onity runs once per resolve mode so the reflection and
                        // baked paths appear as separate, clearly labeled rows.
                        for (int modeIndex = 0; modeIndex < k_onityResolveModes.Length; modeIndex++)
                        {
                            OnityResolveMode resolveMode = k_onityResolveModes[modeIndex];
                            metrics.Add(MeasureScenario(containerKind, config, resolveMode, allocationCounter));
                        }
                    }
                    else
                    {
                        metrics.Add(MeasureScenario(containerKind, config, OnityResolveMode.Reflection, allocationCounter));
                    }
                }

                report.scenarios[scenarioIndex] = new ScenarioReport
                {
                    scenario = config.scenario.ToString(),
                    displayName = config.displayName,
                    iterationsPerSample = config.iterationsPerSample,
                    results = metrics.ToArray()
                };
            }

            return report;
        }

        private static BenchmarkReport RunHotPathBenchmarks()
        {
            ScenarioConfig[] configs =
            {
                new ScenarioConfig(BenchmarkScenario.ResolveSingleton, "Resolve (Singleton)", 1000000),
                new ScenarioConfig(BenchmarkScenario.ResolveTransient, "Resolve (Transient)", 100000),
                new ScenarioConfig(BenchmarkScenario.ResolveCombined, "Resolve (Combined)", 100000),
                new ScenarioConfig(BenchmarkScenario.ResolveComplex, "Resolve (Complex)", 10000)
            };
            BenchmarkReport report = new BenchmarkReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                scope = "Onity Baked hot paths, interleaved",
                samplesPerCase = k_hotPathSamplesPerCase,
                warmupIterations = k_hotPathWarmupIterations,
                allocationMeasured = false,
                allocationCounter = AllocationCounterKind.Unavailable.ToString(),
                scenarios = new ScenarioReport[configs.Length]
            };
            BenchmarkOperation[] operations = new BenchmarkOperation[configs.Length];
            double[][] samples = new double[configs.Length][];
            int[][] gen0Collections = new int[configs.Length][];
            int[][] gen1Collections = new int[configs.Length][];
            int[][] gen2Collections = new int[configs.Length][];
            ProfilerMarker[] markers = new ProfilerMarker[configs.Length];
            bool originalBakedResolve = (bool)s_useBakedResolveProperty.GetValue(null);
            bool originalDiagnostics = OnityContainer.DiagnosticsCollectionEnabled;

            try
            {
                SetUseBakedResolve(true);
                OnityContainer.DiagnosticsCollectionEnabled = false;

                for (int caseIndex = 0; caseIndex < configs.Length; caseIndex++)
                {
                    operations[caseIndex] = CreateOperation(BenchmarkContainerKind.Onity, configs[caseIndex].scenario);
                    samples[caseIndex] = new double[k_hotPathSamplesPerCase];
                    gen0Collections[caseIndex] = new int[k_hotPathSamplesPerCase];
                    gen1Collections[caseIndex] = new int[k_hotPathSamplesPerCase];
                    gen2Collections[caseIndex] = new int[k_hotPathSamplesPerCase];
                    markers[caseIndex] = new ProfilerMarker(
                        BuildProfilerMarkerName(configs[caseIndex], BenchmarkContainerKind.Onity, OnityResolveMode.Baked));

                    Action invoke = operations[caseIndex].Invoke;
                    for (int i = 0; i < k_hotPathWarmupIterations; i++)
                    {
                        invoke();
                    }
                }

                ForceFullGc();

                for (int sample = 0; sample < k_hotPathSamplesPerCase; sample++)
                {
                    for (int slot = 0; slot < configs.Length; slot++)
                    {
                        int caseIndex = (slot + sample) % configs.Length;
                        Action invoke = operations[caseIndex].Invoke;
                        int iterations = configs[caseIndex].iterationsPerSample;
                        int gen0Before = GC.CollectionCount(0);
                        int gen1Before = GC.CollectionCount(1);
                        int gen2Before = GC.CollectionCount(2);
                        long elapsedTicks;

                        using (markers[caseIndex].Auto())
                        {
                            long start = Stopwatch.GetTimestamp();
                            for (int i = 0; i < iterations; i++)
                            {
                                invoke();
                            }
                            elapsedTicks = Stopwatch.GetTimestamp() - start;
                        }

                        samples[caseIndex][sample] = elapsedTicks * 1000000000d /
                            (Stopwatch.Frequency * iterations);
                        gen0Collections[caseIndex][sample] = GC.CollectionCount(0) - gen0Before;
                        gen1Collections[caseIndex][sample] = GC.CollectionCount(1) - gen1Before;
                        gen2Collections[caseIndex][sample] = GC.CollectionCount(2) - gen2Before;
                    }
                }

                for (int caseIndex = 0; caseIndex < configs.Length; caseIndex++)
                {
                    double[] sorted = (double[])samples[caseIndex].Clone();
                    Array.Sort(sorted);
                    Stats stats = CalculateStats(samples[caseIndex]);
                    int iterations = configs[caseIndex].iterationsPerSample;
                    MetricReport metric = new MetricReport
                    {
                        container = BuildContainerLabel(BenchmarkContainerKind.Onity, OnityResolveMode.Baked),
                        meanMilliseconds = stats.mean * iterations / 1000000d,
                        minMilliseconds = stats.min * iterations / 1000000d,
                        maxMilliseconds = stats.max * iterations / 1000000d,
                        standardDeviationMilliseconds = stats.standardDeviation * iterations / 1000000d,
                        nanosecondsPerOperation = stats.mean,
                        allocBytesPerSampleMean = -1d,
                        allocBytesPerOperationMean = -1d,
                        medianNanosecondsPerOperation = MedianOfSortedRange(sorted, 0, sorted.Length),
                        q1NanosecondsPerOperation = MedianOfSortedRange(sorted, 0, sorted.Length / 2),
                        q3NanosecondsPerOperation = MedianOfSortedRange(sorted, sorted.Length / 2, sorted.Length / 2),
                        sampleNanosecondsPerOperation = samples[caseIndex],
                        gen0CollectionsPerSample = gen0Collections[caseIndex],
                        gen1CollectionsPerSample = gen1Collections[caseIndex],
                        gen2CollectionsPerSample = gen2Collections[caseIndex]
                    };
                    report.scenarios[caseIndex] = new ScenarioReport
                    {
                        scenario = configs[caseIndex].scenario.ToString(),
                        displayName = configs[caseIndex].displayName,
                        iterationsPerSample = iterations,
                        results = new[] { metric }
                    };
                }

                return report;
            }
            finally
            {
                try
                {
                    for (int i = 0; i < operations.Length; i++)
                    {
                        operations[i]?.Dispose();
                    }
                }
                finally
                {
                    OnityContainer.DiagnosticsCollectionEnabled = originalDiagnostics;
                    SetUseBakedResolve(originalBakedResolve);
                }
            }
        }

        private static double MedianOfSortedRange(double[] sorted, int start, int length)
        {
            int middle = start + length / 2;
            return (sorted[middle - 1] + sorted[middle]) / 2d;
        }

        private static AllocationEventReport RunAllocationEventBenchmarks()
        {
            if (s_useBakedResolveProperty == null)
            {
                throw new InvalidOperationException("Internal OnityContainer.UseBakedResolve flag was not found.");
            }

            int positiveControlEvents = ValidateAllocationEventRecorder();
            AllocationEventReport report = new AllocationEventReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                eventSource = "Unity.Profiling GC.Alloc, current thread",
                emptyControlEvents = 0,
                positiveControlBytes = k_allocationProbeCount * k_allocationProbeBytes,
                positiveControlEvents = positiveControlEvents,
                iterationsPerSample = k_allocationEventIterations,
                samplesPerCase = k_allocationEventSamplesPerCase,
                warmupIterations = k_warmupIterations,
                scenarios = new AllocationEventScenarioReport[k_scenarios.Length]
            };

            List<AllocationEventMetricReport> metrics =
                new List<AllocationEventMetricReport>(k_containers.Length + 1);

            for (int scenarioIndex = 0; scenarioIndex < k_scenarios.Length; scenarioIndex++)
            {
                ScenarioConfig config = k_scenarios[scenarioIndex];
                metrics.Clear();

                for (int containerIndex = 0; containerIndex < k_containers.Length; containerIndex++)
                {
                    BenchmarkContainerKind containerKind = k_containers[containerIndex];

                    if (containerKind == BenchmarkContainerKind.Onity)
                    {
                        for (int modeIndex = 0; modeIndex < k_onityResolveModes.Length; modeIndex++)
                        {
                            metrics.Add(MeasureAllocationEvents(
                                containerKind,
                                config,
                                k_onityResolveModes[modeIndex]));
                        }
                    }
                    else
                    {
                        metrics.Add(MeasureAllocationEvents(
                            containerKind,
                            config,
                            OnityResolveMode.Reflection));
                    }
                }

                report.scenarios[scenarioIndex] = new AllocationEventScenarioReport
                {
                    scenario = config.scenario.ToString(),
                    displayName = config.displayName,
                    results = metrics.ToArray()
                };
            }

            return report;
        }

        private static AllocationEventMetricReport MeasureAllocationEvents(
            BenchmarkContainerKind containerKind,
            ScenarioConfig config,
            OnityResolveMode resolveMode)
        {
            bool isOnity = containerKind == BenchmarkContainerKind.Onity;
            object originalFlag = isOnity ? s_useBakedResolveProperty.GetValue(null) : null;

            try
            {
                if (isOnity)
                {
                    SetUseBakedResolve(resolveMode == OnityResolveMode.Baked);
                }

                long[] eventCounts = new long[k_allocationEventSamplesPerCase];

                for (int sampleIndex = 0; sampleIndex < eventCounts.Length; sampleIndex++)
                {
                    using BenchmarkOperation operation = CreateOperation(containerKind, config.scenario);

                    for (int i = 0; i < k_warmupIterations; i++)
                    {
                        operation.Invoke();
                    }

                    ForceFullGc();

                    using ProfilerRecorder recorder = new ProfilerRecorder(
                        ProfilerCategory.Internal,
                        "GC.Alloc",
                        k_allocationEventCapacity,
                        ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

                    if (recorder.Valid == false)
                    {
                        throw new InvalidOperationException("GC.Alloc profiler marker became unavailable.");
                    }

                    recorder.Start();

                    for (int i = 0; i < k_allocationEventIterations; i++)
                    {
                        operation.Invoke();
                    }

                    recorder.Stop();

                    if (recorder.WrappedAround || recorder.Count >= k_allocationEventCapacity)
                    {
                        throw new InvalidOperationException(
                            $"GC.Alloc recorder capacity was exceeded for {config.displayName} / {containerKind}.");
                    }

                    eventCounts[sampleIndex] = recorder.Count;
                }

                Stats stats = CalculateStats(eventCounts);
                return new AllocationEventMetricReport
                {
                    container = BuildContainerLabel(containerKind, resolveMode),
                    meanEventsPerSample = stats.mean,
                    minEventsPerSample = stats.min,
                    maxEventsPerSample = stats.max,
                    meanEventsPerOperation = stats.mean / k_allocationEventIterations,
                    eventCountsPerSample = eventCounts
                };
            }
            finally
            {
                if (isOnity)
                {
                    s_useBakedResolveProperty.SetValue(null, originalFlag);
                }
            }
        }

        private static MetricReport MeasureScenario(
            BenchmarkContainerKind containerKind,
            ScenarioConfig config,
            OnityResolveMode resolveMode,
            AllocationCounterKind allocationCounter)
        {
            bool isOnity = containerKind == BenchmarkContainerKind.Onity;

            // Only Onity flips the internal baked-resolve flag. The flag wraps the
            // entire measurement because CreateOperation builds the container (and
            // its baked graph) inside the sample loop, so Build() must observe it.
            // The original value is restored in the finally below.
            object originalFlag = isOnity ? s_useBakedResolveProperty.GetValue(null) : null;

            try
            {
                if (isOnity)
                {
                    SetUseBakedResolve(resolveMode == OnityResolveMode.Baked);
                }

                double[] elapsedMsSamples = new double[k_samplesPerCase];
                bool measureAllocations = allocationCounter != AllocationCounterKind.Unavailable;
                long[] allocSamples = measureAllocations ? new long[k_samplesPerCase] : null;
                ProfilerMarker measuredLoopMarker = new ProfilerMarker(
                    BuildProfilerMarkerName(config, containerKind, resolveMode));

                for (int sampleIndex = 0; sampleIndex < k_samplesPerCase; sampleIndex++)
                {
                    using BenchmarkOperation operation = CreateOperation(containerKind, config.scenario);

                    int warmup = Math.Min(k_warmupIterations, config.iterationsPerSample);

                    for (int i = 0; i < warmup; i++)
                    {
                        operation.Invoke();
                    }

                    ForceFullGc();

                    Stopwatch stopwatch = new Stopwatch();
                    long allocBefore = measureAllocations ? ReadAllocatedBytes(allocationCounter) : 0;

                    using (measuredLoopMarker.Auto())
                    {
                        stopwatch.Restart();

                        for (int i = 0; i < config.iterationsPerSample; i++)
                        {
                            operation.Invoke();
                        }

                        stopwatch.Stop();
                    }

                    long allocAfter = measureAllocations ? ReadAllocatedBytes(allocationCounter) : 0;

                    elapsedMsSamples[sampleIndex] = stopwatch.Elapsed.TotalMilliseconds;

                    // A cumulative counter must never move backwards. Do not hide
                    // counter failure by clamping a negative delta to zero.
                    if (measureAllocations && allocAfter < allocBefore)
                    {
                        throw new InvalidOperationException("The gross allocation counter moved backwards during a benchmark sample.");
                    }

                    if (measureAllocations)
                    {
                        allocSamples[sampleIndex] = allocAfter - allocBefore;
                    }
                }

                Stats stats = CalculateStats(elapsedMsSamples);
                Stats allocStats = measureAllocations ? CalculateStats(allocSamples) : default;

                return new MetricReport
                {
                    container = BuildContainerLabel(containerKind, resolveMode),
                    meanMilliseconds = stats.mean,
                    minMilliseconds = stats.min,
                    maxMilliseconds = stats.max,
                    standardDeviationMilliseconds = stats.standardDeviation,
                    nanosecondsPerOperation = stats.mean * 1000000d / config.iterationsPerSample,
                    allocBytesPerSampleMean = measureAllocations ? allocStats.mean : -1d,
                    allocBytesPerOperationMean = measureAllocations
                        ? allocStats.mean / config.iterationsPerSample
                        : -1d
                };
            }
            finally
            {
                if (isOnity)
                {
                    s_useBakedResolveProperty.SetValue(null, originalFlag);
                }
            }
        }

        private static void SetUseBakedResolve(bool value)
        {
            s_useBakedResolveProperty.SetValue(null, value);
        }

        private static string BuildContainerLabel(BenchmarkContainerKind containerKind, OnityResolveMode resolveMode)
        {
            if (containerKind != BenchmarkContainerKind.Onity)
            {
                return containerKind.ToString();
            }

            return resolveMode == OnityResolveMode.Baked ? "Onity (Baked)" : "Onity (Reflection)";
        }

        private static string BuildProfilerMarkerName(
            ScenarioConfig config,
            BenchmarkContainerKind containerKind,
            OnityResolveMode resolveMode)
        {
            string containerLabel = BuildContainerLabel(containerKind, resolveMode)
                .Replace(" ", string.Empty)
                .Replace("(", string.Empty)
                .Replace(")", string.Empty);

            return $"Onity.DI.Benchmark/{config.scenario}/{containerLabel}";
        }

        private static BenchmarkOperation CreateOperation(BenchmarkContainerKind containerKind, BenchmarkScenario scenario)
        {
            switch (containerKind)
            {
                case BenchmarkContainerKind.Onity:
                    return CreateOnityOperation(scenario);

                case BenchmarkContainerKind.VContainer:
                    return CreateVContainerOperation(scenario);

                case BenchmarkContainerKind.Zenject:
                    return CreateZenjectOperation(scenario);

                default:
                    throw new ArgumentOutOfRangeException(nameof(containerKind), containerKind, "Unknown benchmark container.");
            }
        }

        private static BenchmarkOperation CreateOnityOperation(BenchmarkScenario scenario)
        {
            switch (scenario)
            {
                case BenchmarkScenario.ResolveSingleton:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterSimpleOnity(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService service = container.Resolve<IBenchmarkSingletonService>();
                            BenchmarkBlackhole.Consume(service);
                        },
                        container.Dispose);
                }

                case BenchmarkScenario.ResolveKeyedSingleton:
                {
                    OnityContainer container = new OnityContainer();
                    container.Bind<IBenchmarkSingletonService>()
                        .To<BenchmarkSingletonService>().WithId(k_keyedServiceId).AsSingle();
                    container.Build();
                    ValidateSingleton(
                        container.Resolve<IBenchmarkSingletonService>(k_keyedServiceId),
                        container.Resolve<IBenchmarkSingletonService>(k_keyedServiceId));
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(
                            container.Resolve<IBenchmarkSingletonService>(k_keyedServiceId)),
                        container.Dispose);
                }

                case BenchmarkScenario.ResolveScopedSingleton:
                {
                    OnityContainer root = new OnityContainer();
                    root.Bind<IBenchmarkSingletonService>()
                        .To<BenchmarkSingletonService>().AsScoped();
                    root.Build();
                    OnityContainer scope = new OnityContainer(root);
                    scope.Build();
                    ValidateSingleton(scope.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    ValidateSeparateScopes(root.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(scope.Resolve<IBenchmarkSingletonService>()),
                        () => { scope.Dispose(); root.Dispose(); });
                }

                case BenchmarkScenario.ResolveTransient:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterSimpleOnity(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkTransientService service = container.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(service);
                        },
                        container.Dispose);
                }

                case BenchmarkScenario.ResolveCombined:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterSimpleOnity(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService singleton = container.Resolve<IBenchmarkSingletonService>();
                            IBenchmarkTransientService transient = container.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(singleton);
                            BenchmarkBlackhole.Consume(transient);
                        },
                        container.Dispose);
                }

                case BenchmarkScenario.ResolveComplex:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterComplexOnity(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IComplexRoot root = container.Resolve<IComplexRoot>();
                            BenchmarkBlackhole.Consume(root);
                        },
                        container.Dispose);
                }

                case BenchmarkScenario.PrepareAndRegisterComplex:
                {
                    return new BenchmarkOperation(
                        () =>
                        {
                            using OnityContainer container = new OnityContainer();
                            RegisterComplexOnity(container);
                        });
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown benchmark scenario.");
            }
        }

        private static BenchmarkOperation CreateVContainerOperation(BenchmarkScenario scenario)
        {
            switch (scenario)
            {
                case BenchmarkScenario.ResolveSingleton:
                {
                    IObjectResolver resolver = BuildSimpleVContainer();
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService service = resolver.Resolve<IBenchmarkSingletonService>();
                            BenchmarkBlackhole.Consume(service);
                        },
                        resolver.Dispose);
                }

                case BenchmarkScenario.ResolveKeyedSingleton:
                {
                    ContainerBuilder builder = new ContainerBuilder();
                    builder.Register<IBenchmarkSingletonService, BenchmarkSingletonService>(
                        VContainer.Lifetime.Singleton).Keyed(k_keyedServiceId);
                    IObjectResolver resolver = builder.Build();
                    ValidateSingleton(resolver.Resolve<IBenchmarkSingletonService>(k_keyedServiceId),
                        resolver.Resolve<IBenchmarkSingletonService>(k_keyedServiceId));
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(
                            resolver.Resolve<IBenchmarkSingletonService>(k_keyedServiceId)),
                        resolver.Dispose);
                }

                case BenchmarkScenario.ResolveScopedSingleton:
                {
                    ContainerBuilder builder = new ContainerBuilder();
                    builder.Register<IBenchmarkSingletonService, BenchmarkSingletonService>(
                        VContainer.Lifetime.Scoped);
                    IObjectResolver root = builder.Build();
                    IObjectResolver scope = root.CreateScope();
                    ValidateSingleton(scope.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    ValidateSeparateScopes(root.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(scope.Resolve<IBenchmarkSingletonService>()),
                        () => { scope.Dispose(); root.Dispose(); });
                }

                case BenchmarkScenario.ResolveTransient:
                {
                    IObjectResolver resolver = BuildSimpleVContainer();
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkTransientService service = resolver.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(service);
                        },
                        resolver.Dispose);
                }

                case BenchmarkScenario.ResolveCombined:
                {
                    IObjectResolver resolver = BuildSimpleVContainer();
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService singleton = resolver.Resolve<IBenchmarkSingletonService>();
                            IBenchmarkTransientService transient = resolver.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(singleton);
                            BenchmarkBlackhole.Consume(transient);
                        },
                        resolver.Dispose);
                }

                case BenchmarkScenario.ResolveComplex:
                {
                    IObjectResolver resolver = BuildComplexVContainer();
                    return new BenchmarkOperation(
                        () =>
                        {
                            IComplexRoot root = resolver.Resolve<IComplexRoot>();
                            BenchmarkBlackhole.Consume(root);
                        },
                        resolver.Dispose);
                }

                case BenchmarkScenario.PrepareAndRegisterComplex:
                {
                    return new BenchmarkOperation(
                        () =>
                        {
                            ContainerBuilder builder = new ContainerBuilder();
                            RegisterComplexVContainer(builder);
                            IObjectResolver resolver = builder.Build();
                            resolver.Dispose();
                        });
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown benchmark scenario.");
            }
        }

        private static BenchmarkOperation CreateZenjectOperation(BenchmarkScenario scenario)
        {
            switch (scenario)
            {
                case BenchmarkScenario.ResolveSingleton:
                {
                    DiContainer container = new DiContainer();
                    RegisterSimpleZenject(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService service = container.Resolve<IBenchmarkSingletonService>();
                            BenchmarkBlackhole.Consume(service);
                        });
                }

                case BenchmarkScenario.ResolveKeyedSingleton:
                {
                    DiContainer container = new DiContainer();
                    container.Bind<IBenchmarkSingletonService>().WithId(k_keyedServiceId)
                        .To<BenchmarkSingletonService>().AsSingle();
                    ValidateSingleton(container.ResolveId<IBenchmarkSingletonService>(k_keyedServiceId),
                        container.ResolveId<IBenchmarkSingletonService>(k_keyedServiceId));
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(
                            container.ResolveId<IBenchmarkSingletonService>(k_keyedServiceId)));
                }

                case BenchmarkScenario.ResolveScopedSingleton:
                {
                    DiContainer root = new DiContainer();
                    root.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
                    DiContainer scope = root.CreateSubContainer();
                    scope.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
                    ValidateSingleton(scope.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    ValidateSeparateScopes(root.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(scope.Resolve<IBenchmarkSingletonService>()));
                }

                case BenchmarkScenario.ResolveTransient:
                {
                    DiContainer container = new DiContainer();
                    RegisterSimpleZenject(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkTransientService service = container.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(service);
                        });
                }

                case BenchmarkScenario.ResolveCombined:
                {
                    DiContainer container = new DiContainer();
                    RegisterSimpleZenject(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService singleton = container.Resolve<IBenchmarkSingletonService>();
                            IBenchmarkTransientService transient = container.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(singleton);
                            BenchmarkBlackhole.Consume(transient);
                        });
                }

                case BenchmarkScenario.ResolveComplex:
                {
                    DiContainer container = new DiContainer();
                    RegisterComplexZenject(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IComplexRoot root = container.Resolve<IComplexRoot>();
                            BenchmarkBlackhole.Consume(root);
                        });
                }

                case BenchmarkScenario.PrepareAndRegisterComplex:
                {
                    return new BenchmarkOperation(
                        () =>
                        {
                            DiContainer container = new DiContainer();
                            RegisterComplexZenject(container);
                            BenchmarkBlackhole.Consume(container);
                        });
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown benchmark scenario.");
            }
        }

        private static void ValidateSingleton(object first, object second)
        {
            if (!ReferenceEquals(first, second))
            {
                throw new InvalidOperationException("Benchmark registration did not reuse the singleton.");
            }
        }

        private static void ValidateSeparateScopes(object first, object second)
        {
            if (ReferenceEquals(first, second))
            {
                throw new InvalidOperationException("Benchmark scopes shared an instance.");
            }
        }

        private static void RegisterSimpleOnity(OnityContainer container)
        {
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();
            // Build commits the registrations and, when UseBakedResolve is true,
            // compiles the baked graph the resolve hot path reads. Required so the
            // baked pass actually exercises the fast path instead of staying on
            // the reflection fallback.
            container.Build();
        }

        private static IObjectResolver BuildSimpleVContainer()
        {
            ContainerBuilder builder = new ContainerBuilder();
            builder.Register<IBenchmarkSingletonService, BenchmarkSingletonService>(VContainer.Lifetime.Singleton);
            builder.Register<IBenchmarkTransientService, BenchmarkTransientService>(VContainer.Lifetime.Transient);
            return builder.Build();
        }

        private static void RegisterSimpleZenject(DiContainer container)
        {
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();
        }

        private static void RegisterComplexOnity(OnityContainer container)
        {
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();

            container.Bind<ISharedSettings>().To<SharedSettings>().AsSingle();
            container.Bind<ISharedClock>().To<SharedClock>().AsSingle();
            container.Bind<ISharedRandom>().To<SharedRandom>().AsSingle();

            container.Bind<ILeafA>().To<LeafA>().AsTransient();
            container.Bind<ILeafB>().To<LeafB>().AsTransient();
            container.Bind<ILeafC>().To<LeafC>().AsTransient();
            container.Bind<ILeafD>().To<LeafD>().AsTransient();
            container.Bind<ILeafE>().To<LeafE>().AsTransient();
            container.Bind<ILeafF>().To<LeafF>().AsTransient();
            container.Bind<ILeafG>().To<LeafG>().AsTransient();
            container.Bind<ILeafH>().To<LeafH>().AsTransient();

            container.Bind<IComplexServiceA>().To<ComplexServiceA>().AsTransient();
            container.Bind<IComplexServiceB>().To<ComplexServiceB>().AsTransient();
            container.Bind<IComplexServiceC>().To<ComplexServiceC>().AsTransient();
            container.Bind<IComplexServiceD>().To<ComplexServiceD>().AsTransient();
            container.Bind<IComplexServiceE>().To<ComplexServiceE>().AsTransient();
            container.Bind<IComplexRoot>().To<ComplexRoot>().AsTransient();
            // Build commits the registrations and, when UseBakedResolve is true,
            // compiles the baked graph the resolve hot path reads. For the
            // prepare/register scenario this also makes baked-graph construction
            // part of the measured build cost, matching VContainer's builder.Build.
            container.Build();
        }

        private static void RegisterComplexVContainer(IContainerBuilder builder)
        {
            builder.Register<IBenchmarkSingletonService, BenchmarkSingletonService>(VContainer.Lifetime.Singleton);
            builder.Register<IBenchmarkTransientService, BenchmarkTransientService>(VContainer.Lifetime.Transient);

            builder.Register<ISharedSettings, SharedSettings>(VContainer.Lifetime.Singleton);
            builder.Register<ISharedClock, SharedClock>(VContainer.Lifetime.Singleton);
            builder.Register<ISharedRandom, SharedRandom>(VContainer.Lifetime.Singleton);

            builder.Register<ILeafA, LeafA>(VContainer.Lifetime.Transient);
            builder.Register<ILeafB, LeafB>(VContainer.Lifetime.Transient);
            builder.Register<ILeafC, LeafC>(VContainer.Lifetime.Transient);
            builder.Register<ILeafD, LeafD>(VContainer.Lifetime.Transient);
            builder.Register<ILeafE, LeafE>(VContainer.Lifetime.Transient);
            builder.Register<ILeafF, LeafF>(VContainer.Lifetime.Transient);
            builder.Register<ILeafG, LeafG>(VContainer.Lifetime.Transient);
            builder.Register<ILeafH, LeafH>(VContainer.Lifetime.Transient);

            builder.Register<IComplexServiceA, ComplexServiceA>(VContainer.Lifetime.Transient);
            builder.Register<IComplexServiceB, ComplexServiceB>(VContainer.Lifetime.Transient);
            builder.Register<IComplexServiceC, ComplexServiceC>(VContainer.Lifetime.Transient);
            builder.Register<IComplexServiceD, ComplexServiceD>(VContainer.Lifetime.Transient);
            builder.Register<IComplexServiceE, ComplexServiceE>(VContainer.Lifetime.Transient);
            builder.Register<IComplexRoot, ComplexRoot>(VContainer.Lifetime.Transient);
        }

        private static IObjectResolver BuildComplexVContainer()
        {
            ContainerBuilder builder = new ContainerBuilder();
            RegisterComplexVContainer(builder);
            return builder.Build();
        }

        private static void RegisterComplexZenject(DiContainer container)
        {
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();

            container.Bind<ISharedSettings>().To<SharedSettings>().AsSingle();
            container.Bind<ISharedClock>().To<SharedClock>().AsSingle();
            container.Bind<ISharedRandom>().To<SharedRandom>().AsSingle();

            container.Bind<ILeafA>().To<LeafA>().AsTransient();
            container.Bind<ILeafB>().To<LeafB>().AsTransient();
            container.Bind<ILeafC>().To<LeafC>().AsTransient();
            container.Bind<ILeafD>().To<LeafD>().AsTransient();
            container.Bind<ILeafE>().To<LeafE>().AsTransient();
            container.Bind<ILeafF>().To<LeafF>().AsTransient();
            container.Bind<ILeafG>().To<LeafG>().AsTransient();
            container.Bind<ILeafH>().To<LeafH>().AsTransient();

            container.Bind<IComplexServiceA>().To<ComplexServiceA>().AsTransient();
            container.Bind<IComplexServiceB>().To<ComplexServiceB>().AsTransient();
            container.Bind<IComplexServiceC>().To<ComplexServiceC>().AsTransient();
            container.Bind<IComplexServiceD>().To<ComplexServiceD>().AsTransient();
            container.Bind<IComplexServiceE>().To<ComplexServiceE>().AsTransient();
            container.Bind<IComplexRoot>().To<ComplexRoot>().AsTransient();
        }

        private static string SaveReport(BenchmarkReport report, bool hotPathsOnly = false)
        {
            string absoluteResultsDirectory = GetResultsDirectory();
            Directory.CreateDirectory(absoluteResultsDirectory);

            string fileStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string versionedJson = Path.Combine(absoluteResultsDirectory,
                hotPathsOnly ? $"di-benchmark-hot-paths-{fileStamp}.json" : $"di-benchmark-{fileStamp}.json");
            string latestJson = Path.Combine(absoluteResultsDirectory,
                hotPathsOnly ? "di-benchmark-hot-paths-latest.json" : k_latestJsonFileName);
            string latestCsv = Path.Combine(absoluteResultsDirectory,
                hotPathsOnly ? "di-benchmark-hot-paths-latest.csv" : k_latestCsvFileName);
            string latestMarkdown = Path.Combine(absoluteResultsDirectory,
                hotPathsOnly ? "di-benchmark-hot-paths-latest.md" : k_latestMarkdownFileName);

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(versionedJson, json, Encoding.UTF8);
            File.WriteAllText(latestJson, json, Encoding.UTF8);
            File.WriteAllText(latestCsv, BuildCsv(report), Encoding.UTF8);
            File.WriteAllText(latestMarkdown, BuildMarkdown(report), Encoding.UTF8);
            return latestJson;
        }

        private static string SaveAllocationEventReport(AllocationEventReport report)
        {
            string absoluteResultsDirectory = GetResultsDirectory();
            Directory.CreateDirectory(absoluteResultsDirectory);

            string fileStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string versionedJson = Path.Combine(
                absoluteResultsDirectory,
                $"di-allocation-events-{fileStamp}.json");
            string latestJson = Path.Combine(
                absoluteResultsDirectory,
                k_latestEventJsonFileName);
            string json = JsonUtility.ToJson(report, true);

            File.WriteAllText(versionedJson, json, Encoding.UTF8);
            File.WriteAllText(latestJson, json, Encoding.UTF8);
            return latestJson;
        }

        private static string SaveAllocationByteReport()
        {
            AllocationByteReport report = new AllocationByteReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                byteSource = "UnityEditor.Profiling RawFrameDataView GC.Alloc sample metadata",
                emptyControlBytes = 0,
                positiveControlExpectedBytes = k_allocationProbeCount * k_allocationProbeBytes,
                positiveControlEvents = s_controlPositiveEvents,
                positiveControlBytes = s_controlPositiveBytes,
                doubleControlEvents = s_controlDoubleEvents,
                doubleControlBytes = s_controlDoubleBytes,
                iterationsPerSample = k_allocationEventIterations,
                samplesPerCase = k_allocationEventSamplesPerCase,
                warmupIterations = k_warmupIterations,
                cases = new AllocationByteCaseReport[s_byteCases.Count]
            };

            for (int i = 0; i < s_byteCases.Count; i++)
            {
                AllocationByteCase currentCase = s_byteCases[i];
                Stats byteStats = CalculateStats(currentCase.BytesPerSample);
                Stats eventStats = CalculateStats(currentCase.EventsPerSample);
                report.cases[i] = new AllocationByteCaseReport
                {
                    scenario = currentCase.Config.scenario.ToString(),
                    displayName = currentCase.Config.displayName,
                    container = BuildContainerLabel(currentCase.ContainerKind, currentCase.ResolveMode),
                    meanBytesPerOperation = byteStats.mean / k_allocationEventIterations,
                    meanEventsPerOperation = eventStats.mean / k_allocationEventIterations,
                    bytesPerSample = currentCase.BytesPerSample,
                    eventsPerSample = currentCase.EventsPerSample
                };
            }

            string absoluteResultsDirectory = GetResultsDirectory();
            Directory.CreateDirectory(absoluteResultsDirectory);
            string fileStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string versionedJson = Path.Combine(
                absoluteResultsDirectory,
                $"di-allocation-bytes-{fileStamp}.json");
            string latestJson = Path.Combine(absoluteResultsDirectory, k_latestByteJsonFileName);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(versionedJson, json, Encoding.UTF8);
            File.WriteAllText(latestJson, json, Encoding.UTF8);
            return latestJson;
        }

        private static void SaveIl2CppAllocationByteReport(
            string latestJson,
            string rawProfilePath,
            List<AllocationByteCase> cases,
            int positiveEvents,
            long positiveBytes,
            int doubleEvents,
            long doubleBytes)
        {
            PlayerAllocationByteReport report = new PlayerAllocationByteReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = "WindowsPlayer",
                scriptingBackend = "IL2CPP",
                buildConfiguration = "Development",
                byteSource = "UnityEditor.Profiling RawFrameDataView GC.Alloc metadata from player .raw profile",
                rawProfilePath = rawProfilePath,
                emptyControlEvents = 0,
                emptyControlBytes = 0,
                positiveControlExpectedBytes = (long)k_allocationProbeCount * k_allocationProbeBytes,
                positiveControlEvents = positiveEvents,
                positiveControlBytes = positiveBytes,
                doubleControlEvents = doubleEvents,
                doubleControlBytes = doubleBytes,
                iterationsPerSample = k_allocationEventIterations,
                samplesPerCase = k_allocationEventSamplesPerCase,
                warmupIterations = k_warmupIterations,
                cases = new AllocationByteCaseReport[cases.Count]
            };

            for (int i = 0; i < cases.Count; i++)
            {
                AllocationByteCase currentCase = cases[i];
                Stats byteStats = CalculateStats(currentCase.BytesPerSample);
                Stats eventStats = CalculateStats(currentCase.EventsPerSample);
                report.cases[i] = new AllocationByteCaseReport
                {
                    scenario = currentCase.Config.scenario.ToString(),
                    displayName = currentCase.Config.displayName,
                    container = BuildContainerLabel(currentCase.ContainerKind, currentCase.ResolveMode),
                    meanBytesPerOperation = byteStats.mean / k_allocationEventIterations,
                    meanEventsPerOperation = eventStats.mean / k_allocationEventIterations,
                    bytesPerSample = currentCase.BytesPerSample,
                    eventsPerSample = currentCase.EventsPerSample
                };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(latestJson));
            File.WriteAllText(latestJson, JsonUtility.ToJson(report, true), Encoding.UTF8);
        }

        private static bool HasArgument(string option)
        {
            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], option, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetResultsDirectory()
        {
            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], k_outputDirectoryArgument, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                if (i + 1 >= arguments.Length || Path.IsPathRooted(arguments[i + 1]) == false)
                {
                    throw new ArgumentException(
                        $"A rooted directory must follow {k_outputDirectoryArgument}.");
                }

                return arguments[i + 1];
            }

            return Path.Combine(Directory.GetCurrentDirectory(), k_resultsDirectory);
        }

        private static string BuildCsv(BenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder(2048);
            builder.AppendLine(
                "scenario,container,iterations,mean_ms,min_ms,max_ms,stddev_ms,ns_per_op,alloc_bytes_per_sample_mean,alloc_bytes_per_op_mean,allocation_counter");

            for (int scenarioIndex = 0; scenarioIndex < report.scenarios.Length; scenarioIndex++)
            {
                ScenarioReport scenario = report.scenarios[scenarioIndex];

                for (int metricIndex = 0; metricIndex < scenario.results.Length; metricIndex++)
                {
                    MetricReport metric = scenario.results[metricIndex];
                    builder.Append(EscapeCsv(scenario.displayName)).Append(',');
                    builder.Append(EscapeCsv(metric.container)).Append(',');
                    builder.Append(scenario.iterationsPerSample).Append(',');
                    builder.Append(ToInvariant(metric.meanMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.minMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.maxMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.standardDeviationMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.nanosecondsPerOperation)).Append(',');
                    if (report.allocationMeasured)
                    {
                        builder.Append(ToInvariant(metric.allocBytesPerSampleMean)).Append(',');
                        builder.Append(ToInvariant(metric.allocBytesPerOperationMean));
                    }
                    else
                    {
                        builder.Append(',');
                    }

                    builder.Append(',').Append(report.allocationCounter).AppendLine();
                }
            }

            return builder.ToString();
        }

        private static string BuildMarkdown(BenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder(4096);
            builder.AppendLine("# Onity DI Benchmark");
            builder.AppendLine();
            builder.AppendLine($"- Generated (UTC): `{report.generatedAtUtc}`");
            builder.AppendLine($"- Unity: `{report.unityVersion}`");
            builder.AppendLine($"- Platform: `{report.platform}`");
            builder.AppendLine($"- Scope: `{report.scope}`");
            builder.AppendLine($"- Samples per case: `{report.samplesPerCase}`");
            builder.AppendLine($"- Warmup iterations: `{report.warmupIterations}`");
            builder.AppendLine($"- Allocation counter: `{report.allocationCounter}`");
            if (report.allocationMeasured == false)
            {
                builder.AppendLine("- Allocation values: unavailable on this runtime; timing results only.");
            }
            builder.AppendLine();
            builder.AppendLine(
                "| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");

            for (int scenarioIndex = 0; scenarioIndex < report.scenarios.Length; scenarioIndex++)
            {
                ScenarioReport scenario = report.scenarios[scenarioIndex];

                for (int metricIndex = 0; metricIndex < scenario.results.Length; metricIndex++)
                {
                    MetricReport metric = scenario.results[metricIndex];
                    builder.Append("| ").Append(scenario.displayName).Append(" | ");
                    builder.Append(metric.container).Append(" | ");
                    builder.Append(metric.meanMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ");
                    builder.Append(metric.nanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture)).Append(" | ");
                    if (report.allocationMeasured)
                    {
                        builder.Append(metric.allocBytesPerSampleMean.ToString("F2", CultureInfo.InvariantCulture)).Append(" | ");
                        builder.Append(metric.allocBytesPerOperationMean.ToString("F6", CultureInfo.InvariantCulture)).AppendLine(" |");
                    }
                    else
                    {
                        builder.AppendLine("N/A | N/A |");
                    }
                }
            }

            if (report.scope == "Onity Baked hot paths, interleaved")
            {
                builder.AppendLine();
                builder.AppendLine("| Scenario | Median (ns/op) | Q1–Q3 (ns/op) | Range (ns/op) | Samples with GC |");
                builder.AppendLine("|---|---:|---:|---:|---:|");

                for (int i = 0; i < report.scenarios.Length; i++)
                {
                    ScenarioReport scenario = report.scenarios[i];
                    MetricReport metric = scenario.results[0];
                    int gcSamples = 0;
                    for (int sample = 0; sample < metric.sampleNanosecondsPerOperation.Length; sample++)
                    {
                        if (metric.gen0CollectionsPerSample[sample] > 0 ||
                            metric.gen1CollectionsPerSample[sample] > 0 ||
                            metric.gen2CollectionsPerSample[sample] > 0)
                        {
                            gcSamples++;
                        }
                    }

                    builder.Append("| ").Append(scenario.displayName).Append(" | ");
                    builder.Append(metric.medianNanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture))
                        .Append(" | ");
                    builder.Append(metric.q1NanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture))
                        .Append("–")
                        .Append(metric.q3NanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture))
                        .Append(" | ");
                    builder.Append((metric.minMilliseconds * 1000000d / scenario.iterationsPerSample)
                        .ToString("F2", CultureInfo.InvariantCulture)).Append("–");
                    builder.Append((metric.maxMilliseconds * 1000000d / scenario.iterationsPerSample)
                        .ToString("F2", CultureInfo.InvariantCulture)).Append(" | ");
                    builder.Append(gcSamples).Append("/").Append(report.samplesPerCase).AppendLine(" |");
                }
            }

            return builder.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private static string ToInvariant(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------------
        // Allocation measurement source
        //
        // Per-op allocation is derived from a GROSS, cumulative managed-allocation
        // counter sampled tightly around the timed loop, divided by the iteration
        // count. The gross counter only ever grows; a full GC before the loop does
        // not reset it, so the delta across the loop is the bytes allocated by the
        // measured work (including objects later collected), which is exactly what
        // a per-op allocation figure should report.
        //
        // Prefer System.GC.GetTotalAllocatedBytes(precise: true), a process-wide
        // cumulative counter. If unavailable, a validated current-thread counter
        // can be used. Without a passing positive control, only timing is reported.
        //
        // Why the previous source was wrong: the runner used
        // GC.GetAllocatedBytesForCurrentThread(). On Unity 2022.3 Editor-Mono that
        // API does not return a reliable per-thread allocation total — the
        // committed run read 0 B for EVERY container, including the known
        // allocation-heavy Zenject and the transient-resolve paths that must
        // allocate the instance they return. A flat 0 B across the board means the
        // counter was not tracking, not that the paths allocate nothing, so those
        // figures were withdrawn.
        //
        // The preferred method is invoked through a guarded reflected MethodInfo.
        // No counter can publish allocation values without the positive control.
        // ---------------------------------------------------------------------

        // Cached reflected accessor for GC.GetTotalAllocatedBytes(bool). Resolved
        // once; null when the running Mono build does not expose the method.
        private static readonly MethodInfo s_getTotalAllocatedBytesMethod =
            typeof(GC).GetMethod(
                "GetTotalAllocatedBytes",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);

        /// <summary>
        /// Reads the selected cumulative managed-allocation counter.
        /// </summary>
        private static long ReadAllocatedBytes(AllocationCounterKind counter)
        {
            if (counter == AllocationCounterKind.ProcessGross)
            {
                return (long)s_getTotalAllocatedBytesMethod.Invoke(null, s_preciseArgs);
            }

            if (counter == AllocationCounterKind.CurrentThread)
            {
                return GC.GetAllocatedBytesForCurrentThread();
            }

            throw new InvalidOperationException("No allocation counter was selected.");
        }

        private static AllocationCounterKind SelectAllocationCounter()
        {
            if (s_getTotalAllocatedBytesMethod != null)
            {
                try
                {
                    if (ValidateAllocationCounter(AllocationCounterKind.ProcessGross))
                    {
                        return AllocationCounterKind.ProcessGross;
                    }
                }
                catch (TargetInvocationException error)
                {
                    UnityEngine.Debug.LogWarning(
                        $"The process-wide allocation counter failed: {error.InnerException?.Message ?? error.Message}");
                }
            }

            if (ValidateAllocationCounter(AllocationCounterKind.CurrentThread))
            {
                return AllocationCounterKind.CurrentThread;
            }

            UnityEngine.Debug.LogWarning(
                "No managed-allocation counter passed the 1 MiB positive control. The DI benchmark will report timings only.");
            return AllocationCounterKind.Unavailable;
        }

        private static bool ValidateAllocationCounter(AllocationCounterKind counter)
        {
            ForceFullGc();
            long before = ReadAllocatedBytes(counter);
            byte[][] probes = new byte[k_allocationProbeCount][];

            for (int i = 0; i < probes.Length; i++)
            {
                probes[i] = new byte[k_allocationProbeBytes];
            }

            long allocated = ReadAllocatedBytes(counter) - before;
            GC.KeepAlive(probes);
            ForceFullGc();

            if (allocated < (long)k_allocationProbeCount * k_allocationProbeBytes)
            {
                UnityEngine.Debug.LogWarning(
                    $"The {counter} allocation counter observed only {allocated} bytes of a 1 MiB positive control.");
                return false;
            }

            return true;
        }

        private static int ValidateAllocationEventRecorder()
        {
            using (ProfilerRecorder emptyRecorder = new ProfilerRecorder(
                ProfilerCategory.Internal,
                "GC.Alloc",
                k_allocationEventProbeCapacity,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                if (emptyRecorder.Valid == false)
                {
                    throw new NotSupportedException("The GC.Alloc profiler marker is unavailable.");
                }

                emptyRecorder.Start();
                emptyRecorder.Stop();

                if (emptyRecorder.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"The GC.Alloc empty control recorded {emptyRecorder.Count} events.");
                }
            }

            using (ProfilerRecorder allocationRecorder = new ProfilerRecorder(
                ProfilerCategory.Internal,
                "GC.Alloc",
                k_allocationEventProbeCapacity,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                if (allocationRecorder.Valid == false)
                {
                    throw new NotSupportedException("The GC.Alloc profiler marker is unavailable.");
                }

                allocationRecorder.Start();
                byte[][] probes = new byte[k_allocationProbeCount][];

                for (int i = 0; i < probes.Length; i++)
                {
                    probes[i] = new byte[k_allocationProbeBytes];
                }

                allocationRecorder.Stop();
                GC.KeepAlive(probes);

                if (allocationRecorder.WrappedAround
                    || allocationRecorder.Count >= k_allocationEventProbeCapacity
                    || allocationRecorder.Count < k_allocationProbeCount)
                {
                    throw new InvalidOperationException(
                        $"The GC.Alloc positive control recorded {allocationRecorder.Count} events for {k_allocationProbeCount} new arrays.");
                }

                return allocationRecorder.Count;
            }
        }

        // Boxed argument reused for the precise gross-allocation read so the
        // reflected call adds no per-sample allocation of its own.
        private static readonly object[] s_preciseArgs = { true };

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static Stats CalculateStats(double[] values)
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            double sum = 0d;

            for (int i = 0; i < values.Length; i++)
            {
                double value = values[i];

                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }

                sum += value;
            }

            double mean = values.Length > 0 ? sum / values.Length : 0d;
            double varianceSum = 0d;

            for (int i = 0; i < values.Length; i++)
            {
                double delta = values[i] - mean;
                varianceSum += delta * delta;
            }

            double standardDeviation = values.Length > 1 ? Math.Sqrt(varianceSum / (values.Length - 1)) : 0d;
            return new Stats(mean, min, max, standardDeviation);
        }

        private static Stats CalculateStats(long[] values)
        {
            double[] converted = new double[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                converted[i] = values[i];
            }

            return CalculateStats(converted);
        }

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string generatedAtUtc;
            public string unityVersion;
            public string platform;
            public string scope;
            public int samplesPerCase;
            public int warmupIterations;
            public bool allocationMeasured;
            public string allocationCounter;
            public ScenarioReport[] scenarios;
        }

        [Serializable]
        private sealed class AllocationEventReport
        {
            public string generatedAtUtc;
            public string unityVersion;
            public string platform;
            public string eventSource;
            public int emptyControlEvents;
            public int positiveControlBytes;
            public int positiveControlEvents;
            public int iterationsPerSample;
            public int samplesPerCase;
            public int warmupIterations;
            public AllocationEventScenarioReport[] scenarios;
        }

        [Serializable]
        private sealed class AllocationEventScenarioReport
        {
            public string scenario;
            public string displayName;
            public AllocationEventMetricReport[] results;
        }

        [Serializable]
        private sealed class AllocationEventMetricReport
        {
            public string container;
            public double meanEventsPerSample;
            public double minEventsPerSample;
            public double maxEventsPerSample;
            public double meanEventsPerOperation;
            public long[] eventCountsPerSample;
        }

        private sealed class AllocationByteCase
        {
            public readonly ScenarioConfig Config;
            public readonly BenchmarkContainerKind ContainerKind;
            public readonly OnityResolveMode ResolveMode;
            public readonly long[] BytesPerSample = new long[k_allocationEventSamplesPerCase];
            public readonly long[] EventsPerSample = new long[k_allocationEventSamplesPerCase];

            public AllocationByteCase(
                ScenarioConfig config,
                BenchmarkContainerKind containerKind,
                OnityResolveMode resolveMode)
            {
                Config = config;
                ContainerKind = containerKind;
                ResolveMode = resolveMode;
            }
        }

        [Serializable]
        private sealed class AllocationByteReport
        {
            public string generatedAtUtc;
            public string unityVersion;
            public string platform;
            public string byteSource;
            public long emptyControlBytes;
            public long positiveControlExpectedBytes;
            public int positiveControlEvents;
            public long positiveControlBytes;
            public int doubleControlEvents;
            public long doubleControlBytes;
            public int iterationsPerSample;
            public int samplesPerCase;
            public int warmupIterations;
            public AllocationByteCaseReport[] cases;
        }

        [Serializable]
        private sealed class PlayerAllocationByteReport
        {
            public string generatedAtUtc;
            public string unityVersion;
            public string platform;
            public string scriptingBackend;
            public string buildConfiguration;
            public string byteSource;
            public string rawProfilePath;
            public int emptyControlEvents;
            public long emptyControlBytes;
            public long positiveControlExpectedBytes;
            public int positiveControlEvents;
            public long positiveControlBytes;
            public int doubleControlEvents;
            public long doubleControlBytes;
            public int iterationsPerSample;
            public int samplesPerCase;
            public int warmupIterations;
            public AllocationByteCaseReport[] cases;
        }

        [Serializable]
        private sealed class AllocationByteCaseReport
        {
            public string scenario;
            public string displayName;
            public string container;
            public double meanBytesPerOperation;
            public double meanEventsPerOperation;
            public long[] bytesPerSample;
            public long[] eventsPerSample;
        }

        private enum AllocationCounterKind
        {
            Unavailable,
            ProcessGross,
            CurrentThread
        }

        [Serializable]
        private sealed class ScenarioReport
        {
            public string scenario;
            public string displayName;
            public int iterationsPerSample;
            public MetricReport[] results;
        }

        [Serializable]
        private sealed class MetricReport
        {
            public string container;
            public double meanMilliseconds;
            public double minMilliseconds;
            public double maxMilliseconds;
            public double standardDeviationMilliseconds;
            public double nanosecondsPerOperation;
            public double allocBytesPerSampleMean;
            public double allocBytesPerOperationMean;
            public double medianNanosecondsPerOperation;
            public double q1NanosecondsPerOperation;
            public double q3NanosecondsPerOperation;
            public double[] sampleNanosecondsPerOperation;
            public int[] gen0CollectionsPerSample;
            public int[] gen1CollectionsPerSample;
            public int[] gen2CollectionsPerSample;
        }

        private readonly struct ScenarioConfig
        {
            public readonly BenchmarkScenario scenario;
            public readonly string displayName;
            public readonly int iterationsPerSample;

            public ScenarioConfig(BenchmarkScenario scenario, string displayName, int iterationsPerSample)
            {
                this.scenario = scenario;
                this.displayName = displayName;
                this.iterationsPerSample = iterationsPerSample;
            }
        }

        private readonly struct Stats
        {
            public readonly double mean;
            public readonly double min;
            public readonly double max;
            public readonly double standardDeviation;

            public Stats(double mean, double min, double max, double standardDeviation)
            {
                this.mean = mean;
                this.min = min;
                this.max = max;
                this.standardDeviation = standardDeviation;
            }
        }

        private enum BenchmarkContainerKind
        {
            Onity = 0,
            VContainer = 1,
            Zenject = 2
        }

        private enum OnityResolveMode
        {
            Reflection = 0,
            Baked = 1
        }

        private enum BenchmarkScenario
        {
            ResolveSingleton = 0,
            ResolveTransient = 1,
            ResolveCombined = 2,
            ResolveComplex = 3,
            PrepareAndRegisterComplex = 4,
            ResolveKeyedSingleton = 5,
            ResolveScopedSingleton = 6
        }

        private sealed class BenchmarkOperation : IDisposable
        {
            private readonly Action m_disposeAction;

            public Action Invoke { get; }

            public BenchmarkOperation(Action invoke, Action disposeAction = null)
            {
                Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
                m_disposeAction = disposeAction;
            }

            public void Dispose()
            {
                m_disposeAction?.Invoke();
            }
        }

        private static class BenchmarkBlackhole
        {
            private static object s_lastValue;

            public static void Consume(object value)
            {
                s_lastValue = value;
            }
        }

        private interface IBenchmarkSingletonService
        {
        }

        private interface IBenchmarkTransientService
        {
        }

        private sealed class BenchmarkSingletonService : IBenchmarkSingletonService
        {
        }

        private sealed class BenchmarkTransientService : IBenchmarkTransientService
        {
            public BenchmarkTransientService(IBenchmarkSingletonService singletonService)
            {
                BenchmarkBlackhole.Consume(singletonService);
            }
        }

        private interface ISharedSettings
        {
        }

        private interface ISharedClock
        {
        }

        private interface ISharedRandom
        {
        }

        private sealed class SharedSettings : ISharedSettings
        {
        }

        private sealed class SharedClock : ISharedClock
        {
        }

        private sealed class SharedRandom : ISharedRandom
        {
        }

        private interface ILeafA
        {
        }

        private interface ILeafB
        {
        }

        private interface ILeafC
        {
        }

        private interface ILeafD
        {
        }

        private interface ILeafE
        {
        }

        private interface ILeafF
        {
        }

        private interface ILeafG
        {
        }

        private interface ILeafH
        {
        }

        private sealed class LeafA : ILeafA
        {
            public LeafA(ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class LeafB : ILeafB
        {
            public LeafB(ISharedClock sharedClock)
            {
                BenchmarkBlackhole.Consume(sharedClock);
            }
        }

        private sealed class LeafC : ILeafC
        {
            public LeafC(ISharedRandom sharedRandom)
            {
                BenchmarkBlackhole.Consume(sharedRandom);
            }
        }

        private sealed class LeafD : ILeafD
        {
            public LeafD(ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class LeafE : ILeafE
        {
            public LeafE(ISharedClock sharedClock)
            {
                BenchmarkBlackhole.Consume(sharedClock);
            }
        }

        private sealed class LeafF : ILeafF
        {
            public LeafF(ISharedRandom sharedRandom)
            {
                BenchmarkBlackhole.Consume(sharedRandom);
            }
        }

        private sealed class LeafG : ILeafG
        {
            public LeafG(ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class LeafH : ILeafH
        {
            public LeafH(ISharedClock sharedClock)
            {
                BenchmarkBlackhole.Consume(sharedClock);
            }
        }

        private interface IComplexServiceA
        {
        }

        private interface IComplexServiceB
        {
        }

        private interface IComplexServiceC
        {
        }

        private interface IComplexServiceD
        {
        }

        private interface IComplexServiceE
        {
        }

        private interface IComplexRoot
        {
        }

        private sealed class ComplexServiceA : IComplexServiceA
        {
            public ComplexServiceA(ILeafA leafA, ILeafB leafB, ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(leafA);
                BenchmarkBlackhole.Consume(leafB);
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class ComplexServiceB : IComplexServiceB
        {
            public ComplexServiceB(ILeafC leafC, ILeafD leafD, ISharedClock sharedClock)
            {
                BenchmarkBlackhole.Consume(leafC);
                BenchmarkBlackhole.Consume(leafD);
                BenchmarkBlackhole.Consume(sharedClock);
            }
        }

        private sealed class ComplexServiceC : IComplexServiceC
        {
            public ComplexServiceC(ILeafE leafE, ILeafF leafF, ISharedRandom sharedRandom)
            {
                BenchmarkBlackhole.Consume(leafE);
                BenchmarkBlackhole.Consume(leafF);
                BenchmarkBlackhole.Consume(sharedRandom);
            }
        }

        private sealed class ComplexServiceD : IComplexServiceD
        {
            public ComplexServiceD(ILeafG leafG, ILeafH leafH, IComplexServiceA complexServiceA)
            {
                BenchmarkBlackhole.Consume(leafG);
                BenchmarkBlackhole.Consume(leafH);
                BenchmarkBlackhole.Consume(complexServiceA);
            }
        }

        private sealed class ComplexServiceE : IComplexServiceE
        {
            public ComplexServiceE(IComplexServiceB complexServiceB, IComplexServiceC complexServiceC, ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(complexServiceB);
                BenchmarkBlackhole.Consume(complexServiceC);
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class ComplexRoot : IComplexRoot
        {
            public ComplexRoot(
                IComplexServiceA complexServiceA,
                IComplexServiceB complexServiceB,
                IComplexServiceC complexServiceC,
                IComplexServiceD complexServiceD,
                IComplexServiceE complexServiceE,
                IBenchmarkTransientService benchmarkTransientService)
            {
                BenchmarkBlackhole.Consume(complexServiceA);
                BenchmarkBlackhole.Consume(complexServiceB);
                BenchmarkBlackhole.Consume(complexServiceC);
                BenchmarkBlackhole.Consume(complexServiceD);
                BenchmarkBlackhole.Consume(complexServiceE);
                BenchmarkBlackhole.Consume(benchmarkTransientService);
            }
        }
    }
}
