using System;
using System.Globalization;
using System.IO;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Onity.Benchmarks
{
    /// <summary>
    /// Player-side entry point for the task benchmarks. A player started with
    /// <c>-onityRunTaskBenchmark</c> runs the primary suite, or the thread-switch suite when
    /// <c>-onityTaskBenchmarkSuite threadswitch</c> is passed, or the standalone correctness smoke
    /// suite with <c>-onityTaskBenchmarkSuite smoke</c>, or worker-only performance probes with
    /// <c>-onityTaskBenchmarkSuite threadpool</c>, or Jobs/Burst probes with
    /// <c>-onityTaskBenchmarkSuite jobs</c>, or pending composition construction with
    /// <c>-onityTaskBenchmarkSuite whenall</c>, or array composition cycles with
    /// <c>-onityTaskBenchmarkSuite whenany</c>, or explicit Update timing allocation with
    /// <c>-onityTaskBenchmarkSuite timing</c>, or a manual-awaitable logical builder cycle with
    /// <c>-onityTaskBenchmarkSuite buildercycle</c>, or synchronous readiness feasibility with
    /// <c>-onityTaskBenchmarkSuite readiness</c>, or actual native consumers of controlled readiness cycles with
    /// <c>-onityTaskBenchmarkSuite readinesscycle</c>, or sequential builder lifetimes with matched native return passes using
    /// <c>-onityTaskBenchmarkSuite builderlifecycle</c>, or real PlayerLoop frame lifecycles of both libraries with
    /// <c>-onityTaskBenchmarkSuite framelifecycle</c> (optional <c>-onityTaskFrameLifecycleArms</c> id list), or
    /// end-to-end concurrent-loop throughput with <c>-onityTaskBenchmarkSuite throughput</c>, or reactive adapter
    /// pending-delivery evidence with <c>-onityTaskBenchmarkSuite reactive</c>. Each suite writes the report to
    /// <c>-onityTaskBenchmarkOutput</c>, persists startup stages to
    /// <c>-onityTaskBenchmarkStartupTrace</c> (default: report path with a .startup.log extension),
    /// and quits with exit code 0 on success or 1 on failure.
    /// </summary>
    public static class OnityTaskBenchmarkPlayerRunner
    {
        private const string k_runArgument = "-onityRunTaskBenchmark";
        private const string k_outputArgument = "-onityTaskBenchmarkOutput";
        private const string k_suiteArgument = "-onityTaskBenchmarkSuite";
        private const string k_startupTraceArgument = "-onityTaskBenchmarkStartupTrace";
        private const string k_threadSwitchSuite = "threadswitch";
        private const string k_latestJsonFileName = "onity-task-benchmark-player-latest.json";
        private const string k_latestThreadSwitchJsonFileName = "onity-thread-switch-benchmark-player-latest.json";

        private static bool s_isRunning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void TraceEarlyInitialization()
        {
            s_isRunning = false;
            WriteStartupMarker("early-initialization");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void TraceBeforeSceneLoad()
        {
            WriteStartupMarker("before-scene-load");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunFromCommandLine()
        {
            WriteStartupMarker("after-scene-load");
            string[] args = Environment.GetCommandLineArgs();
            if (!HasArgument(args, k_runArgument) || s_isRunning)
            {
                return;
            }

            s_isRunning = true;
            WriteStartupMarker("arguments-detected");
            Application.runInBackground = true;
            Application.targetFrameRate = -1;

            try
            {
                string suite = GetArgumentValue(args, k_suiteArgument);
                suite = string.IsNullOrEmpty(suite) ? "primary" : suite.ToLowerInvariant();
                bool threadSwitch = suite == k_threadSwitchSuite;
                bool threadPool = suite == "threadpool";
                bool smoke = suite == "smoke";
                bool fullCycle = suite == "fullcycle";
                bool jobs = suite == "jobs";
                bool whenAll = suite == "whenall";
                bool whenAny = suite == "whenany";
                bool timing = suite == "timing";
                bool endOfFrame = suite == "eof";
                bool finite = suite == "finite";
                bool channels = suite == "channels";
                bool builderCycle = suite == "buildercycle";
                bool builderLifecycle = suite == "builderlifecycle";
                bool frameLifecycle = suite == "framelifecycle";
                bool readiness = suite == "readiness";
                bool readinessCycle = suite == "readinesscycle";
                bool throughput = suite == "throughput";
                bool reactive = suite == "reactive";
                if (!threadSwitch && !threadPool && !smoke && !fullCycle && !jobs
                    && !whenAll && !whenAny && !timing && !endOfFrame && !finite && !channels && !builderCycle
                    && !readiness && !readinessCycle && !builderLifecycle && !frameLifecycle && !throughput && !reactive
                    && suite != "primary")
                {
                    throw new ArgumentException("Task benchmark suite must be primary, threadswitch, threadpool, jobs, whenall, whenany, timing, eof, finite, channels, buildercycle, builderlifecycle, framelifecycle, throughput, reactive, readiness, readinesscycle, smoke, or fullcycle.");
                }
                string latestJson = GetArgumentValue(args, k_outputArgument);
                if (string.IsNullOrEmpty(latestJson))
                {
                    latestJson = Path.Combine(
                        Application.persistentDataPath,
                        endOfFrame ? "onity-task-eof-player-latest.json"
                            : throughput ? "onity-task-throughput-player-latest.json"
                            : reactive ? "onity-task-reactive-player-latest.json"
                            : builderLifecycle ? "onity-task-builderlifecycle-player-latest.json"
                            : frameLifecycle ? "onity-task-framelifecycle-player-latest.json"
                            : readinessCycle ? "onity-task-readinesscycle-player-latest.json"
                            : readiness ? "onity-task-readiness-player-latest.json"
                            : builderCycle ? "onity-task-buildercycle-player-latest.json"
                            : channels ? "onity-channels-player-latest.json"
                            : finite ? "onity-async-enumerable-player-latest.json"
                            : fullCycle ? "onity-task-fullcycle-player-latest.json"
                            : jobs ? "onity-task-jobs-player-latest.json"
                            : whenAll ? "onity-task-whenall-player-latest.json"
                            : whenAny ? "onity-task-whenany-player-latest.json"
                            : timing ? "onity-task-timing-player-latest.json"
                            : threadPool ? "onity-thread-pool-benchmark-player-latest.json"
                            : smoke ? "onity-task-smoke-player-latest.json"
                            : threadSwitch ? k_latestThreadSwitchJsonFileName : k_latestJsonFileName);
                }

                latestJson = Path.GetFullPath(latestJson);
                Directory.CreateDirectory(Path.GetDirectoryName(latestJson));
                Debug.Log("Onity task player benchmark started: " + suite
                    + " suite, report " + latestJson);
                // The launcher uses this persisted handshake before starting its measurement timeout.
                // This is outside the benchmark's scheduling/GetResult timing slices.
                WriteStartupMarker("benchmark-entry", suite);

                if (builderLifecycle)
                {
                    OnityTaskBuilderLifecycleBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (throughput)
                {
                    OnityTaskThroughputBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (reactive)
                {
                    OnityReactiveAdapterBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (frameLifecycle)
                {
                    OnityTaskFrameLifecycleBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (readinessCycle)
                {
                    OnityTaskReadinessCycleBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (readiness)
                {
                    OnityTaskReadinessBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (builderCycle)
                {
                    OnityTaskBuilderCycleBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (channels)
                {
                    OnityChannelBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (finite)
                {
                    OnityAsyncEnumerableBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (endOfFrame)
                {
                    OnityTaskEndOfFramePlayerRunner.Run(latestJson, Quit);
                }
                else if (fullCycle)
                {
                    OnityTaskFullCycleProfileRunner.Run(latestJson, Quit);
                }
                else if (timing)
                {
                    OnityTaskPlayerLoopBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (whenAny)
                {
                    OnityTaskWhenAnyBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (whenAll)
                {
                    OnityTaskWhenAllBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (jobs)
                {
                    OnityTaskJobBenchmarkRunner.Run(latestJson, Quit);
                }
                else if (smoke)
                {
                    OnityTaskPlayerSmokeRunner.Run(latestJson, Quit);
                }
                else if (threadSwitch || threadPool)
                {
                    OnityThreadSwitchBenchmarkRunner.Run(latestJson, Quit, "Player", threadPool);
                }
                else
                {
                    OnityTaskBenchmarkRunner.Run(latestJson, Quit);
                }
            }
            catch (Exception exception)
            {
                WriteStartupMarker("benchmark-failed", exception.ToString());
                Debug.LogException(exception);
                Application.Quit(1);
            }
        }

        private static void Quit(string latestJson, Exception failure)
        {
            WriteStartupMarker(failure == null ? "benchmark-completed" : "benchmark-failed", latestJson);
            if (failure == null)
            {
                Debug.Log("Onity task player benchmark completed: " + latestJson);
            }
            else
            {
                Debug.LogException(failure);
            }

            Application.Quit(failure == null ? 0 : 1);
        }

        internal static void WriteStartupMarker(string marker, string detail = "")
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!HasArgument(args, k_runArgument))
            {
                return;
            }

            try
            {
                string tracePath = GetArgumentValue(args, k_startupTraceArgument);
                if (string.IsNullOrEmpty(tracePath))
                {
                    string reportPath = GetArgumentValue(args, k_outputArgument);
                    tracePath = string.IsNullOrEmpty(reportPath)
                        ? Path.Combine(Application.persistentDataPath, "onity-task-benchmark-player.startup.log")
                        : Path.ChangeExtension(reportPath, ".startup.log");
                }

                tracePath = Path.GetFullPath(tracePath);
                Directory.CreateDirectory(Path.GetDirectoryName(tracePath));
                // AppendAllText closes the file after each stage so startup evidence survives a hang.
                File.AppendAllText(tracePath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    + "\t" + marker + "\t" + detail.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not persist Onity benchmark startup marker '" + marker + "': " + exception);
            }
        }

        private static bool HasArgument(string[] args, string argumentName)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetArgumentValue(string[] args, string argumentName)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
