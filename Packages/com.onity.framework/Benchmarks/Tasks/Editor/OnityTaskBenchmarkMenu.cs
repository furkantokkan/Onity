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
        private const string k_typedWhenAllJsonFileName = "onity-typed-whenall-benchmark-latest.json";
        private const string k_prebridgeDiagnosticJsonFileName =
            "onity-typed-prebridge-diagnostic-latest.json";
        private const string k_prebridgeAttributionJsonFileName =
            "onity-typed-prebridge-attribution-latest.json";
        private const string k_pendingSessionKey = "Onity.Benchmarks.PendingOnityTaskRun";
        private const string k_outputSessionKey = "Onity.Benchmarks.OnityTaskOutput";
        private const string k_commandLineSessionKey = "Onity.Benchmarks.OnityTaskCommandLine";
        private const string k_typedWhenAllSessionKey = "Onity.Benchmarks.TypedWhenAll";
        private const string k_allocationOnlySessionKey = "Onity.Benchmarks.TypedWhenAllAllocationsOnly";
        private const string k_prebridgeDiagnosticSessionKey =
            "Onity.Benchmarks.TypedPrebridgeDiagnostic";
        private const string k_prebridgeAttributionSessionKey =
            "Onity.Benchmarks.TypedPrebridgeAttribution";
        private const string k_commandLineStartTicksSessionKey = "Onity.Benchmarks.OnityTaskCommandLineStartTicks";
        private const string k_outputArgument = "-onityTaskBenchmarkOutput";
        private const string k_typedWhenAllArgument = "-onityTypedWhenAllBenchmark";
        private const string k_allocationOnlyArgument = "-onityTaskAllocationsOnly";
        private const string k_prebridgeDiagnosticArgument = "-onityTypedPrebridgeDiagnostic";
        private const string k_prebridgeAttributionArgument = "-onityTypedPrebridgeAttribution";
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

            string latestJson = GetLatestJsonPath();
            bool commandLineRun = SessionState.GetBool(k_commandLineSessionKey, false);
            if (SessionState.GetBool(k_typedWhenAllSessionKey, false))
            {
                OnityTypedWhenAllBenchmarkRunner.Run(latestJson,
                    commandLineRun ? HandleCommandLineCompleted : null,
                    SessionState.GetBool(k_allocationOnlySessionKey, false),
                    SessionState.GetBool(k_prebridgeDiagnosticSessionKey, false),
                    SessionState.GetBool(k_prebridgeAttributionSessionKey, false));
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
            bool prebridgeDiagnostic = HasArgument(k_prebridgeDiagnosticArgument);
            bool prebridgeAttribution = HasArgument(k_prebridgeAttributionArgument);
            bool typedWhenAll = HasArgument(k_typedWhenAllArgument) || prebridgeDiagnostic ||
                prebridgeAttribution;
            bool allocationOnly = HasArgument(k_allocationOnlyArgument);
            if (prebridgeAttribution && (prebridgeDiagnostic || allocationOnly))
            {
                throw new ArgumentException(
                    "Prebridge attribution requires its own benchmark process.");
            }
            if (prebridgeDiagnostic && allocationOnly)
            {
                throw new ArgumentException(
                    "Prebridge diagnostic and allocation-only modes are mutually exclusive.");
            }
            if (allocationOnly && !typedWhenAll)
            {
                throw new ArgumentException("Allocation-only mode requires typed WhenAll mode.");
            }

            if ((allocationOnly || prebridgeDiagnostic || prebridgeAttribution) &&
                !HasArgument("-profiler-enable"))
            {
                throw new ArgumentException("Allocation measurement requires -profiler-enable.");
            }
            if (prebridgeAttribution && HasArgument("-deepprofiling"))
            {
                throw new ArgumentException("Prebridge attribution requires Deep Profiling off.");
            }

            string latestJson = GetArgumentValue(k_outputArgument);
            if (string.IsNullOrEmpty(latestJson))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string fileName = prebridgeAttribution
                    ? k_prebridgeAttributionJsonFileName
                    : prebridgeDiagnostic ? k_prebridgeDiagnosticJsonFileName
                    : typedWhenAll ? k_typedWhenAllJsonFileName : k_latestJsonFileName;
                latestJson = Path.Combine(projectRoot, k_resultsDirectory, fileName);
            }

            latestJson = Path.GetFullPath(latestJson);
            if (allocationOnly && !File.Exists(latestJson))
            {
                throw new FileNotFoundException(
                    "Allocation pass requires an existing timing report.", latestJson);
            }
            if ((prebridgeDiagnostic || prebridgeAttribution) && File.Exists(latestJson))
            {
                throw new IOException(
                    "Prebridge measurement requires a new report path: " + latestJson);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(latestJson));

            SessionState.SetBool(k_pendingSessionKey, true);
            SessionState.SetString(k_outputSessionKey, latestJson);
            SessionState.SetBool(k_commandLineSessionKey, true);
            SessionState.SetBool(k_typedWhenAllSessionKey, typedWhenAll);
            SessionState.SetBool(k_allocationOnlySessionKey, allocationOnly);
            SessionState.SetBool(k_prebridgeDiagnosticSessionKey, prebridgeDiagnostic);
            SessionState.SetBool(k_prebridgeAttributionSessionKey, prebridgeAttribution);
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

        private static string GetLatestJsonPath()
        {
            string latestJson = SessionState.GetString(k_outputSessionKey, string.Empty);
            if (!string.IsNullOrEmpty(latestJson))
            {
                return latestJson;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, k_resultsDirectory, k_latestJsonFileName);
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
            SessionState.EraseBool(k_typedWhenAllSessionKey);
            SessionState.EraseBool(k_allocationOnlySessionKey);
            SessionState.EraseBool(k_prebridgeDiagnosticSessionKey);
            SessionState.EraseBool(k_prebridgeAttributionSessionKey);
            SessionState.EraseString(k_outputSessionKey);
            SessionState.EraseString(k_commandLineStartTicksSessionKey);
            EditorApplication.update -= HandleCommandLineTimeout;
        }
    }
}
