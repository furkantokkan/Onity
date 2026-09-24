using System;
using System.IO;
using Onity.Benchmarks;
using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Benchmarks
{
    /// <summary>
    /// Editor entry points for the independently enabled OnityTask benchmark.
    /// </summary>
    [InitializeOnLoad]
    public static class OnityTaskBenchmarkMenu
    {
        private const string k_resultsDirectory = "Packages/com.onity.framework/Benchmarks/Results";
        private const string k_latestJsonFileName = "onity-task-benchmark-latest.json";
        private const string k_pendingSessionKey = "Onity.Benchmarks.PendingOnityTaskRun";
        private const string k_outputSessionKey = "Onity.Benchmarks.OnityTaskOutput";
        private const string k_commandLineSessionKey = "Onity.Benchmarks.OnityTaskCommandLine";
        private const string k_commandLineStartTicksSessionKey = "Onity.Benchmarks.OnityTaskCommandLineStartTicks";
        private const string k_typedWhenAnySessionKey = "Onity.Benchmarks.TypedWhenAny";
        private const string k_allocationOnlySessionKey = "Onity.Benchmarks.TaskAllocationOnly";
        private const string k_collectorDiagnosticSessionKey =
            "Onity.Benchmarks.TypedWhenAnyCollectorDiagnostic";
        private const string k_outputArgument = "-onityTaskBenchmarkOutput";
        private const string k_typedWhenAnyArgument = "-onityTypedWhenAnyBenchmark";
        private const string k_allocationOnlyArgument = "-onityTaskAllocationsOnly";
        private const string k_collectorDiagnosticArgument =
            "-onityTypedWhenAnyCollectorDiagnostic";
        private const double k_commandLineTimeoutSeconds = 900d;

        static OnityTaskBenchmarkMenu()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

            if (SessionState.GetBool(k_commandLineSessionKey, false))
            {
                EditorApplication.update += HandleCommandLineTimeout;
            }
        }

        [MenuItem("Onity/Benchmarks/Run OnityTask Benchmarks (Play Mode)")]
        private static void RunFromMenu()
        {
            if (!EditorApplication.isPlaying)
            {
                SessionState.SetBool(k_pendingSessionKey, true);
                EditorApplication.isPlaying = true;
                Debug.Log("Entering Play Mode. OnityTask benchmark will run after Play Mode starts.");
                return;
            }

            RunInCurrentPlayMode();
        }

        [MenuItem("Onity/Benchmarks/Run OnityTask Benchmarks (Play Mode)", true)]
        private static bool ValidateRunFromMenu()
        {
            return !EditorApplication.isCompiling;
        }

        [MenuItem("Onity/Benchmarks/Run Typed WhenAny Benchmarks (Play Mode)")]
        private static void RunTypedWhenAnyFromMenu()
        {
            SessionState.SetBool(k_typedWhenAnySessionKey, true);
            RunFromMenu();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode)
            {
                return;
            }

            if (!SessionState.GetBool(k_pendingSessionKey, false))
            {
                return;
            }

            SessionState.SetBool(k_pendingSessionKey, false);
            EditorApplication.delayCall += RunInCurrentPlayMode;
        }

        private static void RunInCurrentPlayMode()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            bool commandLineRun = SessionState.GetBool(k_commandLineSessionKey, false);
            bool typedWhenAny = SessionState.GetBool(k_typedWhenAnySessionKey, false);
            bool allocationOnly = SessionState.GetBool(k_allocationOnlySessionKey, false);
            bool collectorDiagnostic = SessionState.GetBool(k_collectorDiagnosticSessionKey, false);
            string latestJson = GetLatestJsonPath(typedWhenAny);
            if (!commandLineRun)
            {
                SessionState.EraseBool(k_typedWhenAnySessionKey);
                SessionState.EraseBool(k_allocationOnlySessionKey);
                SessionState.EraseBool(k_collectorDiagnosticSessionKey);
            }

            if (typedWhenAny)
            {
                OnityTypedWhenAnyBenchmarkRunner.Run(
                    latestJson,
                    commandLineRun ? HandleCommandLineCompleted : null,
                    allocationOnly, collectorDiagnostic);
            }
            else
            {
                OnityTaskBenchmarkRunner.Run(
                    latestJson,
                    commandLineRun ? HandleCommandLineCompleted : null);
            }

            Debug.Log("Queued OnityTask benchmark for the next Play Mode frame.");
        }

        /// <summary>
        /// Command-line entry point for running the OnityTask benchmark in Play Mode.
        /// Do not pass -quit; the runner exits Unity after writing the report.
        /// </summary>
        public static void RunFromCommandLine()
        {
            string latestJson = GetArgumentValue(k_outputArgument);
            bool typedWhenAny = HasArgument(k_typedWhenAnyArgument);
            bool allocationOnly = HasArgument(k_allocationOnlyArgument);
            bool collectorDiagnostic = HasArgument(k_collectorDiagnosticArgument);
            if (allocationOnly && (!typedWhenAny || !HasArgument("-profiler-enable")))
            {
                throw new ArgumentException(
                    "Typed WhenAny allocation pass requires its mode and -profiler-enable.");
            }

            if (collectorDiagnostic && (!typedWhenAny || allocationOnly ||
                !HasArgument("-profiler-enable")))
            {
                throw new ArgumentException(
                    "Typed WhenAny collector diagnostic requires its mode and -profiler-enable "
                    + "without -onityTaskAllocationsOnly.");
            }

            if (string.IsNullOrEmpty(latestJson))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                latestJson = Path.Combine(projectRoot, k_resultsDirectory,
                    collectorDiagnostic ? "onity-typed-whenany-collector-diagnostic-latest.json" :
                    typedWhenAny ? "onity-typed-whenany-benchmark-latest.json" :
                        k_latestJsonFileName);
            }

            latestJson = Path.GetFullPath(latestJson);
            if (typedWhenAny && allocationOnly && !File.Exists(latestJson))
            {
                throw new FileNotFoundException(
                    "Typed WhenAny allocations require a timing report.", latestJson);
            }

            if (typedWhenAny && !allocationOnly && File.Exists(latestJson))
            {
                throw new IOException("Typed WhenAny timing output already exists: " + latestJson);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(latestJson));

            SessionState.SetBool(k_pendingSessionKey, true);
            SessionState.SetString(k_outputSessionKey, latestJson);
            SessionState.SetBool(k_commandLineSessionKey, true);
            SessionState.SetBool(k_typedWhenAnySessionKey, typedWhenAny);
            SessionState.SetBool(k_allocationOnlySessionKey, allocationOnly);
            SessionState.SetBool(k_collectorDiagnosticSessionKey, collectorDiagnostic);
            SessionState.SetString(k_commandLineStartTicksSessionKey, DateTime.UtcNow.Ticks.ToString());
            EditorApplication.update -= HandleCommandLineTimeout;
            EditorApplication.update += HandleCommandLineTimeout;

            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
                Debug.Log("Entering Play Mode. OnityTask command-line benchmark will run after Play Mode starts.");
                return;
            }

            RunInCurrentPlayMode();
        }

        private static string GetArgumentValue(string argumentName)
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool HasArgument(string argumentName)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetLatestJsonPath(bool typedWhenAny)
        {
            string latestJson = SessionState.GetString(k_outputSessionKey, string.Empty);
            if (!string.IsNullOrEmpty(latestJson))
            {
                return latestJson;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, k_resultsDirectory,
                typedWhenAny ? "onity-typed-whenany-benchmark-latest.json" : k_latestJsonFileName);
        }

        private static void HandleCommandLineCompleted(string latestJson, Exception exception)
        {
            AssetDatabase.Refresh();
            ClearCommandLineSession();

            if (exception != null)
            {
                Debug.LogError($"OnityTask command-line benchmark failed. Latest report path: {latestJson}");
                EditorApplication.Exit(1);
                return;
            }

            if (!File.Exists(latestJson))
            {
                Debug.LogError($"OnityTask command-line benchmark did not write the expected report: {latestJson}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"OnityTask command-line benchmark completed. Latest report: {latestJson}");
            EditorApplication.Exit(0);
        }

        private static void HandleCommandLineTimeout()
        {
            if (!SessionState.GetBool(k_commandLineSessionKey, false))
            {
                EditorApplication.update -= HandleCommandLineTimeout;
                return;
            }

            string ticksText = SessionState.GetString(k_commandLineStartTicksSessionKey, string.Empty);
            if (!long.TryParse(ticksText, out long startedTicks))
            {
                return;
            }

            DateTime startedAt = new DateTime(startedTicks, DateTimeKind.Utc);
            if ((DateTime.UtcNow - startedAt).TotalSeconds < k_commandLineTimeoutSeconds)
            {
                return;
            }

            Debug.LogError("OnityTask command-line benchmark timed out.");
            ClearCommandLineSession();
            EditorApplication.Exit(1);
        }

        private static void ClearCommandLineSession()
        {
            SessionState.EraseBool(k_commandLineSessionKey);
            SessionState.EraseBool(k_typedWhenAnySessionKey);
            SessionState.EraseBool(k_allocationOnlySessionKey);
            SessionState.EraseBool(k_collectorDiagnosticSessionKey);
            SessionState.EraseString(k_outputSessionKey);
            SessionState.EraseString(k_commandLineStartTicksSessionKey);
            EditorApplication.update -= HandleCommandLineTimeout;
        }
    }
}
