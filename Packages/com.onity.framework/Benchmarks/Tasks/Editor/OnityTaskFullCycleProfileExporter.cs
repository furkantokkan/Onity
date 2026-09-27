using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Onity.Editor.Benchmarks
{
    /// <summary>Exports bounded lifecycle windows from Unity's binary Player profiler data.</summary>
    public static class OnityTaskFullCycleProfileExporter
    {
        /// <summary>
        /// Reads -onityTaskProfileInput (Player JSON) and -onityTaskProfileExport (export JSON).
        /// Alternatively reads -onityTaskProfileInputDirectory and -onityTaskProfileExportDirectory
        /// to export completed fullcycle-*.json reports in one Editor session.
        /// This synchronous entry point supports a headless Editor with -quit.
        /// </summary>
        public static void ExportFromCommandLine()
        {
            string inputDirectory = OptionalArgument("-onityTaskProfileInputDirectory");
            string exportDirectory = OptionalArgument("-onityTaskProfileExportDirectory");
            if (inputDirectory != null || exportDirectory != null)
            {
                if (inputDirectory == null || exportDirectory == null
                    || OptionalArgument("-onityTaskProfileInput") != null
                    || OptionalArgument("-onityTaskProfileExport") != null)
                {
                    throw new ArgumentException("Supply both directory arguments, without single-file arguments.");
                }

                string[] paths = Directory.GetFiles(inputDirectory, "fullcycle-*.json", SearchOption.TopDirectoryOnly);
                Array.Sort(paths, StringComparer.Ordinal);
                int exported = 0;
                for (int i = 0; i < paths.Length; i++)
                {
                    if (Path.GetFileName(paths[i]).EndsWith("-attribution.json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string output = Path.Combine(exportDirectory,
                        Path.GetFileNameWithoutExtension(paths[i]) + "-attribution.json");
                    ExportProfile(paths[i], output);
                    exported++;
                }

                if (exported == 0)
                {
                    throw new InvalidDataException("No fullcycle-*.json Player reports found in " + inputDirectory);
                }

                Debug.Log("Onity fullcycle directory export completed: " + exported + " reports.");
                return;
            }

            string inputPath = RequiredArgument("-onityTaskProfileInput");
            string outputPath = RequiredArgument("-onityTaskProfileExport");
            ExportProfile(inputPath, outputPath);
        }

        private static void ExportProfile(string inputPath, string outputPath)
        {
            PlayerReport input = JsonUtility.FromJson<PlayerReport>(File.ReadAllText(inputPath));
            if (input == null || !input.completed || input.windows == null || input.windows.Length != 2)
            {
                throw new InvalidDataException("A completed two-library fullcycle report is required.");
            }

            ProfilerDriver.ClearAllFrames();
            if (!ProfilerDriver.LoadProfile(input.rawProfile, false))
            {
                throw new InvalidDataException("Unity could not load raw profile: " + input.rawProfile);
            }

            int first = ProfilerDriver.firstFrameIndex;
            int last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < first)
            {
                throw new InvalidDataException("Raw profile contains no available frames.");
            }

            WindowExport[] windows = new WindowExport[input.windows.Length];
            for (int i = 0; i < windows.Length; i++)
            {
                PlayerWindow window = input.windows[i];
                if (!window.completed || window.completedBatches != 2 || window.consumedOperations != input.concurrency * 2)
                {
                    throw new InvalidDataException("Incomplete Player window: " + window.library);
                }

                windows[i] = ExportWindow(window, input.concurrency, first, last);
            }

            ExportReport report = new ExportReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                playerReport = Path.GetFullPath(inputPath),
                rawProfile = input.rawProfile,
                concurrency = input.concurrency,
                flowExecutionContext = input.flowExecutionContext,
                reuse = input.reuse,
                firstRawFrame = first,
                lastRawFrame = last,
                windows = windows
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            Debug.Log("Onity fullcycle profile exported: " + outputPath);
        }

        private static WindowExport ExportWindow(PlayerWindow window, int concurrency, int first, int last)
        {
            WindowExport result = new WindowExport { library = window.library };
            int beginCount = 0;
            int endCount = 0;
            // Match raw timestamps through marker names, never through Time.frameCount. That
            // count is Player evidence only and has no guaranteed relation to raw frame indices.
            for (int frame = first; frame <= last; frame++)
            {
                for (int thread = 0; ; thread++)
                {
                    using RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!data.valid)
                    {
                        break;
                    }

                    for (int sample = 0; sample < data.sampleCount; sample++)
                    {
                        string name = data.GetSampleName(sample);
                        if (name == window.beginMarker)
                        {
                            beginCount++;
                            result.beginRawFrame = frame;
                            result.beginTimestampNs = Convert.ToDouble(data.GetSampleStartTimeNs(sample));
                        }
                        else if (name == window.endMarker)
                        {
                            endCount++;
                            result.endRawFrame = frame;
                            result.endTimestampNs = Convert.ToDouble(data.GetSampleStartTimeNs(sample));
                        }
                    }
                }
            }

            if (beginCount != 1 || endCount != 1 || result.endTimestampNs <= result.beginTimestampNs)
            {
                throw new InvalidDataException("Missing, duplicated, or reversed capture stamps for " + window.library);
            }

            Dictionary<string, SampleMetric> metrics = new Dictionary<string, SampleMetric>();
            string prefix = "Onity.FullCycle." + window.library;
            int scheduleCount = 0;
            int consumeCount = 0;
            int resumeCount = 0;
            for (int frame = result.beginRawFrame; frame <= result.endRawFrame; frame++)
            {
                for (int thread = 0; ; thread++)
                {
                    using RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!data.valid)
                    {
                        break;
                    }

                    int allocationMarker = data.GetMarkerId("GC.Alloc");
                    Stack<SampleParent> parents = new Stack<SampleParent>();
                    for (int sample = 0; sample < data.sampleCount; sample++)
                    {
                        while (parents.Count > 0 && sample > parents.Peek().LastDescendant)
                        {
                            parents.Pop();
                        }

                        string name = data.GetSampleName(sample);
                        string path = parents.Count == 0 ? name : parents.Peek().Path + "/" + name;
                        string phase = parents.Count == 0 ? "Lifecycle" : parents.Peek().Phase;
                        if (name == prefix + ".Schedule")
                        {
                            phase = "Schedule";
                        }
                        else if (name == prefix + ".Consume")
                        {
                            phase = "Consume";
                        }

                        int descendantEnd = sample + data.GetSampleChildrenCountRecursive(sample);
                        if (descendantEnd > sample)
                        {
                            parents.Push(new SampleParent(descendantEnd, path, phase));
                        }

                        double start = Convert.ToDouble(data.GetSampleStartTimeNs(sample));
                        double duration = Convert.ToDouble(data.GetSampleTimeNs(sample));
                        double overlap = Overlap(start, start + duration, result.beginTimestampNs, result.endTimestampNs);
                        bool beginsInside = start >= result.beginTimestampNs && start < result.endTimestampNs;
                        if (overlap <= 0 && !beginsInside)
                        {
                            continue;
                        }

                        if (name == prefix + ".Schedule")
                        {
                            scheduleCount++;
                        }
                        if (name == prefix + ".Consume")
                        {
                            consumeCount++;
                        }
                        if (name == prefix + ".Resumed")
                        {
                            resumeCount++;
                        }
                        if (IsDeepRuntimeSample(name, window.library))
                        {
                            result.deepRuntimeSampleCount++;
                        }
                        string key = data.threadName + "\n" + phase + "\n" + path;
                        if (!metrics.TryGetValue(key, out SampleMetric metric))
                        {
                            metric = new SampleMetric { thread = data.threadName, phase = phase, path = path, name = name };
                            metrics.Add(key, metric);
                        }

                        metric.sampleCount++;
                        metric.inclusiveMilliseconds += overlap / 1000000d;
                        // Subtract each DIRECT child's clipped interval, never all descendants.
                        // Self durations sum without parent/child inclusive double counting.
                        double children = DirectChildrenTime(data, sample, descendantEnd,
                            result.beginTimestampNs, result.endTimestampNs);
                        metric.selfMilliseconds += Math.Max(0, overlap - children) / 1000000d;
                        if (beginsInside && allocationMarker >= 0 && data.GetSampleMarkerId(sample) == allocationMarker)
                        {
                            result.gcAllocationSamples++;
                            if (data.GetSampleMetadataCount(sample) == 0)
                            {
                                metric.gcMetadataMissing++;
                                result.gcMetadataMissing++;
                            }
                            else
                            {
                                long bytes = data.GetSampleMetadataAsLong(sample, 0);
                                metric.gcKnownAllocatedBytes += bytes;
                                result.gcKnownAllocatedBytes += bytes;
                            }
                        }
                    }
                }
            }

            if (scheduleCount != 2 || consumeCount != 2 || resumeCount != concurrency * 2)
            {
                throw new InvalidDataException("Incomplete raw capture for " + window.library
                    + ": Schedule=" + scheduleCount + ", Consume=" + consumeCount + ", Resumed=" + resumeCount);
            }

            result.scheduleSamples = scheduleCount;
            result.consumeSamples = consumeCount;
            result.resumeSamples = resumeCount;
            result.deepRuntimeSamplesPresent = result.deepRuntimeSampleCount > 0;
            result.deepRuntimeEvidence = window.library == "Onity"
                ? "Managed OnityAsyncStateMachineRunner/OnityTaskSourceBase/OnityRunnerPool samples required"
                : "Managed AsyncUniTask/UniTaskCompletionSourceCore samples required";
            if (!result.deepRuntimeSamplesPresent)
            {
                throw new InvalidDataException("Deep runtime detail is missing for " + window.library
                    + "; custom phase markers alone cannot establish lifecycle attribution.");
            }
            result.gcAllocationBytes = result.gcMetadataMissing == 0 && result.gcAllocationSamples > 0
                ? result.gcKnownAllocatedBytes : -1;
            List<SampleMetric> rows = new List<SampleMetric>(metrics.Values);
            rows.Sort((left, right) => right.selfMilliseconds.CompareTo(left.selfMilliseconds));
            result.samples = rows.ToArray();
            return result;
        }

        private static bool IsDeepRuntimeSample(string name, string library)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("Onity.FullCycle.", StringComparison.Ordinal))
            {
                return false;
            }

            return library == "Onity"
                ? name.IndexOf("OnityAsyncStateMachineRunner", StringComparison.Ordinal) >= 0
                    || name.IndexOf("OnityTaskSourceBase", StringComparison.Ordinal) >= 0
                    || name.IndexOf("OnityRunnerPool", StringComparison.Ordinal) >= 0
                : name.IndexOf("AsyncUniTask", StringComparison.Ordinal) >= 0
                    || name.IndexOf("UniTaskCompletionSourceCore", StringComparison.Ordinal) >= 0;
        }

        private static double DirectChildrenTime(RawFrameDataView data, int sample, int lastDescendant,
            double begin, double end)
        {
            double total = 0;
            for (int child = sample + 1; child <= lastDescendant;)
            {
                double start = Convert.ToDouble(data.GetSampleStartTimeNs(child));
                double duration = Convert.ToDouble(data.GetSampleTimeNs(child));
                total += Overlap(start, start + duration, begin, end);
                child += data.GetSampleChildrenCountRecursive(child) + 1;
            }

            return total;
        }

        private static double Overlap(double start, double end, double begin, double finish)
        {
            return Math.Max(0, Math.Min(end, finish) - Math.Max(start, begin));
        }

        private static string RequiredArgument(string name)
        {
            string value = OptionalArgument(name);
            return value ?? throw new ArgumentException("Required exporter argument missing: " + name);
        }

        private static string OptionalArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private readonly struct SampleParent
        {
            internal readonly int LastDescendant;
            internal readonly string Path;
            internal readonly string Phase;
            internal SampleParent(int last, string path, string phase)
            {
                LastDescendant = last;
                Path = path;
                Phase = phase;
            }
        }

        [Serializable]
        private sealed class PlayerReport
        {
            public bool completed;
            public int concurrency;
            public bool flowExecutionContext;
            public string reuse;
            public string rawProfile;
            public PlayerWindow[] windows;
        }

        [Serializable]
        private sealed class PlayerWindow
        {
            public string library;
            public string beginMarker;
            public string endMarker;
            public bool completed;
            public int completedBatches;
            public int consumedOperations;
        }

        [Serializable]
        private sealed class ExportReport
        {
            public int schemaVersion = 1;
            public bool attributionOnly = true;
            public string generatedAtUtc;
            public string playerReport;
            public string rawProfile;
            public int concurrency;
            public bool flowExecutionContext;
            public string reuse;
            public int firstRawFrame;
            public int lastRawFrame;
            public WindowExport[] windows;
        }

        [Serializable]
        private sealed class WindowExport
        {
            public string library;
            public int beginRawFrame;
            public int endRawFrame;
            public double beginTimestampNs;
            public double endTimestampNs;
            public int scheduleSamples;
            public int consumeSamples;
            public int resumeSamples;
            public bool deepRuntimeSamplesPresent;
            public int deepRuntimeSampleCount;
            public string deepRuntimeEvidence;
            public int gcAllocationSamples;
            public int gcMetadataMissing;
            public long gcKnownAllocatedBytes;
            public long gcAllocationBytes;
            public SampleMetric[] samples;
        }

        [Serializable]
        private sealed class SampleMetric
        {
            public string thread;
            public string phase;
            public string name;
            public string path;
            public long sampleCount;
            public double inclusiveMilliseconds;
            public double selfMilliseconds;
            public long gcKnownAllocatedBytes;
            public int gcMetadataMissing;
        }
    }
}
