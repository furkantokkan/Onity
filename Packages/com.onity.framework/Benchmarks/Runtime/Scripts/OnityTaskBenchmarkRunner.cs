using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Measures OnityTask against UniTask for Unity async hot-path primitives.
    /// </summary>
    public sealed class OnityTaskBenchmarkRunner : MonoBehaviour
    {
        private const int k_warmupIterations = 512;
        private const int k_samplesPerCase = 8;
        private const int k_synchronousIterationsPerSample = 1000000;
        private const int k_frameIterationsPerSample = 10000;

        private static OnityTaskAwaiter[] s_onityAwaiters;
        private static UniTask.Awaiter[] s_uniTaskAwaiters;
        private static int s_lastInt;

        private string m_latestJson;
        private Action<string, Exception> m_completed;

        /// <summary>
        /// Queues an OnityTask benchmark run in Play Mode.
        /// </summary>
        /// <param name="latestJson">Latest JSON output path.</param>
        /// <param name="completed">Optional completion callback for command-line runners.</param>
        public static void Run(string latestJson, Action<string, Exception> completed = null)
        {
            if (string.IsNullOrEmpty(latestJson))
            {
                throw new ArgumentException("Benchmark output path cannot be null or empty.", nameof(latestJson));
            }

            GameObject runnerObject = new GameObject("Onity Task Benchmark Runner");
            DontDestroyOnLoad(runnerObject);

            OnityTaskBenchmarkRunner runner = runnerObject.AddComponent<OnityTaskBenchmarkRunner>();
            runner.m_latestJson = latestJson;
            runner.m_completed = completed;
        }

        private IEnumerator Start()
        {
            yield return null;

            Exception exception = null;
            TaskBenchmarkReport report = null;

            try
            {
                report = RunSynchronousBenchmarks();
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
            }

            if (exception == null)
            {
                IEnumerator nextFrameBenchmarks = RunNextFrameBenchmarks(report);
                while (exception == null)
                {
                    bool moveNext;
                    object current;

                    try
                    {
                        moveNext = nextFrameBenchmarks.MoveNext();
                        current = nextFrameBenchmarks.Current;
                    }
                    catch (Exception caughtException)
                    {
                        exception = caughtException;
                        break;
                    }

                    if (!moveNext)
                    {
                        break;
                    }

                    yield return current;
                }
            }

            if (exception == null)
            {
                try
                {
                    SaveReport(report, m_latestJson);
                    Debug.Log($"OnityTask benchmark completed. Latest report: {m_latestJson}", this);
                }
                catch (Exception caughtException)
                {
                    exception = caughtException;
                }
            }

            if (exception != null)
            {
                Debug.LogException(exception, this);
            }

            try
            {
                m_completed?.Invoke(m_latestJson, exception);
            }
            finally
            {
                Destroy(gameObject);
            }
        }

        private static TaskBenchmarkReport RunSynchronousBenchmarks()
        {
            TaskBenchmarkReport report = CreateReport(4);

            int scenarioIndex = 0;
            report.scenarios[scenarioIndex++] = MeasureScenario(
                "Completed GetResult",
                MeasureOnityCompletedGetResult,
                MeasureUniTaskCompletedGetResult,
                k_synchronousIterationsPerSample);

            report.scenarios[scenarioIndex++] = MeasureScenario(
                "FromResult<int> GetResult",
                MeasureOnityFromResultGetResult,
                MeasureUniTaskFromResultGetResult,
                k_synchronousIterationsPerSample);

            return report;
        }

        private static IEnumerator RunNextFrameBenchmarks(TaskBenchmarkReport report)
        {
            WarmupNextFrameAwaiters();
            yield return WaitForNextFrameCompletion();
            ConsumeNextFrameAwaiters(k_warmupIterations);

            yield return MeasureNextFrameCreateAwaiterScenario(report, 2);
            yield return MeasureNextFrameGetResultScenario(report, 3);
        }

        private static TaskBenchmarkReport CreateReport(int scenarioCount)
        {
            return new TaskBenchmarkReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                scriptingBackend = GetScriptingBackendLabel(),
                samplesPerCase = k_samplesPerCase,
                warmupIterations = k_warmupIterations,
                iterationsPerSample = k_frameIterationsPerSample,
                scenarios = new TaskBenchmarkScenarioReport[scenarioCount]
            };
        }

        private static TaskBenchmarkScenarioReport MeasureScenario(
            string displayName,
            Action onityOperation,
            Action uniTaskOperation,
            int iterationsPerSample)
        {
            for (int i = 0; i < k_warmupIterations; i++)
            {
                onityOperation();
                uniTaskOperation();
            }

            TaskBenchmarkMetricReport onity = MeasureMetric("OnityTask", onityOperation, iterationsPerSample);
            TaskBenchmarkMetricReport uniTask = MeasureMetric("UniTask", uniTaskOperation, iterationsPerSample);

            return new TaskBenchmarkScenarioReport
            {
                displayName = displayName,
                iterationsPerSample = iterationsPerSample,
                results = new[] { onity, uniTask }
            };
        }

        private static TaskBenchmarkMetricReport MeasureMetric(string label, Action operation, int iterationsPerSample)
        {
            double[] samples = new double[k_samplesPerCase];

            for (int sampleIndex = 0; sampleIndex < k_samplesPerCase; sampleIndex++)
            {
                ForceFullGc();

                Stopwatch stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterationsPerSample; i++)
                {
                    operation();
                }

                stopwatch.Stop();
                samples[sampleIndex] = stopwatch.Elapsed.TotalMilliseconds;
            }

            TaskBenchmarkStats stats = CalculateStats(samples);
            return new TaskBenchmarkMetricReport
            {
                library = label,
                meanMilliseconds = stats.mean,
                minMilliseconds = stats.min,
                maxMilliseconds = stats.max,
                standardDeviationMilliseconds = stats.standardDeviation,
                nanosecondsPerOperation = stats.mean * 1000000d / iterationsPerSample
            };
        }

        private static IEnumerator MeasureNextFrameCreateAwaiterScenario(
            TaskBenchmarkReport report,
            int scenarioIndex)
        {
            EnsureAwaiterCapacity(k_frameIterationsPerSample);

            double[] onitySamples = new double[k_samplesPerCase];
            double[] uniTaskSamples = new double[k_samplesPerCase];

            for (int sampleIndex = 0; sampleIndex < k_samplesPerCase; sampleIndex++)
            {
                ForceFullGc();
                onitySamples[sampleIndex] = MeasureOnityNextFrameCreateAwaiterSample();
                yield return WaitForNextFrameCompletion();
                ConsumeOnityNextFrameAwaiters(k_frameIterationsPerSample);

                ForceFullGc();
                uniTaskSamples[sampleIndex] = MeasureUniTaskNextFrameCreateAwaiterSample();
                yield return WaitForNextFrameCompletion();
                ConsumeUniTaskNextFrameAwaiters(k_frameIterationsPerSample);
            }

            report.scenarios[scenarioIndex] = new TaskBenchmarkScenarioReport
            {
                displayName = "NextFrame CreateAwaiter",
                iterationsPerSample = k_frameIterationsPerSample,
                results = new[]
                {
                    BuildMetric("OnityTask", onitySamples),
                    BuildMetric("UniTask", uniTaskSamples)
                }
            };
        }

        private static IEnumerator MeasureNextFrameGetResultScenario(
            TaskBenchmarkReport report,
            int scenarioIndex)
        {
            double[] onitySamples = new double[k_samplesPerCase];
            double[] uniTaskSamples = new double[k_samplesPerCase];

            for (int sampleIndex = 0; sampleIndex < k_samplesPerCase; sampleIndex++)
            {
                ScheduleOnityNextFrameAwaiters(k_frameIterationsPerSample);
                yield return WaitForNextFrameCompletion();
                ForceFullGc();
                onitySamples[sampleIndex] = MeasureGetResultSample(
                    () => ConsumeOnityNextFrameAwaiters(k_frameIterationsPerSample));

                ScheduleUniTaskNextFrameAwaiters(k_frameIterationsPerSample);
                yield return WaitForNextFrameCompletion();
                ForceFullGc();
                uniTaskSamples[sampleIndex] = MeasureGetResultSample(
                    () => ConsumeUniTaskNextFrameAwaiters(k_frameIterationsPerSample));
            }

            report.scenarios[scenarioIndex] = new TaskBenchmarkScenarioReport
            {
                displayName = "NextFrame GetResult",
                iterationsPerSample = k_frameIterationsPerSample,
                results = new[]
                {
                    BuildMetric("OnityTask", onitySamples),
                    BuildMetric("UniTask", uniTaskSamples)
                }
            };
        }

        private static double MeasureOnityNextFrameCreateAwaiterSample()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            ScheduleOnityNextFrameAwaiters(k_frameIterationsPerSample);
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static double MeasureUniTaskNextFrameCreateAwaiterSample()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            ScheduleUniTaskNextFrameAwaiters(k_frameIterationsPerSample);
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static double MeasureGetResultSample(Action operation)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            operation();
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static TaskBenchmarkMetricReport BuildMetric(string label, double[] samples)
        {
            TaskBenchmarkStats stats = CalculateStats(samples);
            return new TaskBenchmarkMetricReport
            {
                library = label,
                meanMilliseconds = stats.mean,
                minMilliseconds = stats.min,
                maxMilliseconds = stats.max,
                standardDeviationMilliseconds = stats.standardDeviation,
                nanosecondsPerOperation = stats.mean * 1000000d / k_frameIterationsPerSample
            };
        }

        private static void ScheduleOnityNextFrameAwaiters(int count)
        {
            for (int i = 0; i < count; i++)
            {
                s_onityAwaiters[i] = OnityTask.NextFrame().GetAwaiter();
            }
        }

        private static void ScheduleUniTaskNextFrameAwaiters(int count)
        {
            for (int i = 0; i < count; i++)
            {
                s_uniTaskAwaiters[i] = UniTask.NextFrame().GetAwaiter();
            }
        }

        private static void WarmupNextFrameAwaiters()
        {
            EnsureAwaiterCapacity(k_warmupIterations);
            ScheduleOnityNextFrameAwaiters(k_warmupIterations);
            ScheduleUniTaskNextFrameAwaiters(k_warmupIterations);
        }

        private static IEnumerator WaitForNextFrameCompletion()
        {
            yield return null;
        }

        private static void ConsumeNextFrameAwaiters(int count)
        {
            ConsumeOnityNextFrameAwaiters(count);
            ConsumeUniTaskNextFrameAwaiters(count);
        }

        private static void ConsumeOnityNextFrameAwaiters(int count)
        {
            for (int i = 0; i < count; i++)
            {
                s_onityAwaiters[i].GetResult();
            }
        }

        private static void ConsumeUniTaskNextFrameAwaiters(int count)
        {
            for (int i = 0; i < count; i++)
            {
                s_uniTaskAwaiters[i].GetResult();
            }
        }

        private static void EnsureAwaiterCapacity(int count)
        {
            if (s_onityAwaiters == null || s_onityAwaiters.Length < count)
            {
                s_onityAwaiters = new OnityTaskAwaiter[count];
            }

            if (s_uniTaskAwaiters == null || s_uniTaskAwaiters.Length < count)
            {
                s_uniTaskAwaiters = new UniTask.Awaiter[count];
            }
        }

        private static void MeasureOnityCompletedGetResult()
        {
            OnityTask.CompletedTask.GetAwaiter().GetResult();
        }

        private static void MeasureUniTaskCompletedGetResult()
        {
            UniTask.CompletedTask.GetAwaiter().GetResult();
        }

        private static void MeasureOnityFromResultGetResult()
        {
            int value = OnityTask.FromResult(42).GetAwaiter().GetResult();
            s_lastInt = value;
        }

        private static void MeasureUniTaskFromResultGetResult()
        {
            int value = UniTask.FromResult(42).GetAwaiter().GetResult();
            s_lastInt = value;
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static TaskBenchmarkStats CalculateStats(double[] values)
        {
            if (values == null || values.Length == 0)
            {
                return new TaskBenchmarkStats(0d, 0d, 0d, 0d);
            }

            double sum = 0d;
            double min = double.MaxValue;
            double max = double.MinValue;

            for (int i = 0; i < values.Length; i++)
            {
                double value = values[i];
                sum += value;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            double mean = sum / values.Length;
            double varianceSum = 0d;

            for (int i = 0; i < values.Length; i++)
            {
                double delta = values[i] - mean;
                varianceSum += delta * delta;
            }

            double standardDeviation = Math.Sqrt(varianceSum / values.Length);
            return new TaskBenchmarkStats(mean, min, max, standardDeviation);
        }

        private static string GetScriptingBackendLabel()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#elif ENABLE_MONO
            return "Mono";
#else
            return "Unknown";
#endif
        }

        private static void SaveReport(TaskBenchmarkReport report, string latestJson)
        {
            string directory = Path.GetDirectoryName(latestJson);
            Directory.CreateDirectory(directory);

            string fileStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string versionedJson = Path.Combine(directory, $"onity-task-benchmark-{fileStamp}.json");
            string latestCsv = Path.ChangeExtension(latestJson, ".csv");
            string latestMarkdown = Path.ChangeExtension(latestJson, ".md");

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(versionedJson, json, Encoding.UTF8);
            File.WriteAllText(latestJson, json, Encoding.UTF8);
            File.WriteAllText(latestCsv, BuildCsv(report), Encoding.UTF8);
            File.WriteAllText(latestMarkdown, BuildMarkdown(report), Encoding.UTF8);
        }

        private static string BuildCsv(TaskBenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder(1024);
            builder.AppendLine("scenario,library,iterations,mean_ms,min_ms,max_ms,stddev_ms,ns_per_op");

            for (int scenarioIndex = 0; scenarioIndex < report.scenarios.Length; scenarioIndex++)
            {
                TaskBenchmarkScenarioReport scenario = report.scenarios[scenarioIndex];
                for (int metricIndex = 0; metricIndex < scenario.results.Length; metricIndex++)
                {
                    TaskBenchmarkMetricReport metric = scenario.results[metricIndex];
                    builder.Append(EscapeCsv(scenario.displayName)).Append(',');
                    builder.Append(EscapeCsv(metric.library)).Append(',');
                    builder.Append(scenario.iterationsPerSample).Append(',');
                    builder.Append(ToInvariant(metric.meanMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.minMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.maxMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.standardDeviationMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.nanosecondsPerOperation)).AppendLine();
                }
            }

            return builder.ToString();
        }

        private static string BuildMarkdown(TaskBenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder(2048);
            builder.AppendLine("# OnityTask Benchmark");
            builder.AppendLine();
            builder.AppendLine($"- Generated (UTC): `{report.generatedAtUtc}`");
            builder.AppendLine($"- Unity: `{report.unityVersion}`");
            builder.AppendLine($"- Platform: `{report.platform}`");
            builder.AppendLine($"- Scripting backend: `{report.scriptingBackend}`");
            builder.AppendLine($"- Samples per case: `{report.samplesPerCase}`");
            builder.AppendLine($"- Warmup iterations: `{report.warmupIterations}`");
            builder.AppendLine($"- Sync iterations per sample: `{k_synchronousIterationsPerSample}`");
            builder.AppendLine($"- Frame iterations per sample: `{k_frameIterationsPerSample}`");
            builder.AppendLine();
            builder.AppendLine("| Scenario | Iterations | Library | Mean (ms) | ns/op | Result |");
            builder.AppendLine("|---|---:|---|---:|---:|---|");

            for (int scenarioIndex = 0; scenarioIndex < report.scenarios.Length; scenarioIndex++)
            {
                TaskBenchmarkScenarioReport scenario = report.scenarios[scenarioIndex];
                double onityNs = FindNanosecondsPerOperation(scenario, "OnityTask");
                double uniTaskNs = FindNanosecondsPerOperation(scenario, "UniTask");

                for (int metricIndex = 0; metricIndex < scenario.results.Length; metricIndex++)
                {
                    TaskBenchmarkMetricReport metric = scenario.results[metricIndex];
                    string result = BuildResultLabel(metric.library, metric.nanosecondsPerOperation, onityNs, uniTaskNs);
                    builder.Append("| ").Append(scenario.displayName).Append(" | ");
                    builder.Append(scenario.iterationsPerSample).Append(" | ");
                    builder.Append(metric.library).Append(" | ");
                    builder.Append(metric.meanMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ");
                    builder.Append(metric.nanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture)).Append(" | ");
                    builder.Append(result).AppendLine(" |");
                }
            }

            return builder.ToString();
        }

        private static string BuildResultLabel(string library, double ownNs, double onityNs, double uniTaskNs)
        {
            if (onityNs <= 0d || uniTaskNs <= 0d)
            {
                return string.Empty;
            }

            if (string.Equals(library, "OnityTask", StringComparison.Ordinal))
            {
                if (onityNs >= uniTaskNs)
                {
                    return "baseline";
                }

                double faster = ((uniTaskNs - onityNs) / uniTaskNs) * 100d;
                return $"OnityTask ~{faster:F0}% faster";
            }

            if (uniTaskNs >= onityNs)
            {
                return "baseline";
            }

            double uniTaskFaster = ((onityNs - uniTaskNs) / onityNs) * 100d;
            return $"UniTask ~{uniTaskFaster:F0}% faster";
        }

        private static double FindNanosecondsPerOperation(TaskBenchmarkScenarioReport scenario, string library)
        {
            for (int i = 0; i < scenario.results.Length; i++)
            {
                TaskBenchmarkMetricReport metric = scenario.results[i];
                if (string.Equals(metric.library, library, StringComparison.Ordinal))
                {
                    return metric.nanosecondsPerOperation;
                }
            }

            return 0d;
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private static string ToInvariant(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        [Serializable]
        private sealed class TaskBenchmarkReport
        {
            public string generatedAtUtc;
            public string unityVersion;
            public string platform;
            public string scriptingBackend;
            public int samplesPerCase;
            public int warmupIterations;
            public int iterationsPerSample;
            public TaskBenchmarkScenarioReport[] scenarios;
        }

        [Serializable]
        private sealed class TaskBenchmarkScenarioReport
        {
            public string displayName;
            public int iterationsPerSample;
            public TaskBenchmarkMetricReport[] results;
        }

        [Serializable]
        private sealed class TaskBenchmarkMetricReport
        {
            public string library;
            public double meanMilliseconds;
            public double minMilliseconds;
            public double maxMilliseconds;
            public double standardDeviationMilliseconds;
            public double nanosecondsPerOperation;
        }

        private readonly struct TaskBenchmarkStats
        {
            public readonly double mean;
            public readonly double min;
            public readonly double max;
            public readonly double standardDeviation;

            public TaskBenchmarkStats(double mean, double min, double max, double standardDeviation)
            {
                this.mean = mean;
                this.min = min;
                this.max = max;
                this.standardDeviation = standardDeviation;
            }
        }
    }
}
