using System;
using System.IO;
using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Player-side entry point for the task benchmarks. A player started with
    /// <c>-onityRunTaskBenchmark</c> runs the primary suite, or the thread-switch suite when
    /// <c>-onityTaskBenchmarkSuite threadswitch</c> is passed, writes the report to
    /// <c>-onityTaskBenchmarkOutput</c>, and quits with exit code 0 on success or 1 on failure.
    /// </summary>
    public static class OnityTaskBenchmarkPlayerRunner
    {
        private const string k_runArgument = "-onityRunTaskBenchmark";
        private const string k_outputArgument = "-onityTaskBenchmarkOutput";
        private const string k_suiteArgument = "-onityTaskBenchmarkSuite";
        private const string k_threadSwitchSuite = "threadswitch";
        private const string k_latestJsonFileName = "onity-task-benchmark-player-latest.json";
        private const string k_latestThreadSwitchJsonFileName = "onity-thread-switch-benchmark-player-latest.json";

        private static bool s_isRunning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!HasArgument(args, k_runArgument) || s_isRunning)
            {
                return;
            }

            s_isRunning = true;
            Application.runInBackground = true;
            Application.targetFrameRate = -1;

            try
            {
                bool threadSwitch = string.Equals(
                    GetArgumentValue(args, k_suiteArgument), k_threadSwitchSuite, StringComparison.OrdinalIgnoreCase);
                string latestJson = GetArgumentValue(args, k_outputArgument);
                if (string.IsNullOrEmpty(latestJson))
                {
                    latestJson = Path.Combine(
                        Application.persistentDataPath,
                        threadSwitch ? k_latestThreadSwitchJsonFileName : k_latestJsonFileName);
                }

                latestJson = Path.GetFullPath(latestJson);
                Directory.CreateDirectory(Path.GetDirectoryName(latestJson));
                Debug.Log("Onity task player benchmark started: " + (threadSwitch ? "thread switch" : "primary")
                    + " suite, report " + latestJson);

                if (threadSwitch)
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
                Debug.LogException(exception);
                Application.Quit(1);
            }
        }

        private static void Quit(string latestJson, Exception failure)
        {
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
