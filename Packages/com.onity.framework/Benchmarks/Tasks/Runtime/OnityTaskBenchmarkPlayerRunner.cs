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
    /// suite with <c>-onityTaskBenchmarkSuite smoke</c>, writes the report to
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
                bool smoke = suite == "smoke";
                bool fullCycle = suite == "fullcycle";
                if (!threadSwitch && !smoke && !fullCycle && suite != "primary")
                {
                    throw new ArgumentException("Task benchmark suite must be primary, threadswitch, smoke, or fullcycle.");
                }
                string latestJson = GetArgumentValue(args, k_outputArgument);
                if (string.IsNullOrEmpty(latestJson))
                {
                    latestJson = Path.Combine(
                        Application.persistentDataPath,
                        fullCycle ? "onity-task-fullcycle-player-latest.json"
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

                if (fullCycle)
                {
                    OnityTaskFullCycleProfileRunner.Run(latestJson, Quit);
                }
                else if (smoke)
                {
                    OnityTaskPlayerSmokeRunner.Run(latestJson, Quit);
                }
                else if (threadSwitch)
                {
                    OnityThreadSwitchBenchmarkRunner.Run(latestJson, Quit, "Player");
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
