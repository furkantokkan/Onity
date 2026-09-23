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
        private const string k_completionSourceJsonFileName = "onity-completion-source-benchmark-latest.json";
        private const string k_preserveJsonFileName = "onity-preserve-benchmark-latest.json";
        private const string k_lifecycleJsonFileName = "onity-native-lifecycle-benchmark-latest.json";
        private const string k_builderAttributionJsonFileName = "onity-task-builder-attribution-latest.json";
        private const string k_pendingSessionKey = "Onity.Benchmarks.PendingOnityTaskRun";
        private const string k_outputSessionKey = "Onity.Benchmarks.OnityTaskOutput";
        private const string k_commandLineSessionKey = "Onity.Benchmarks.OnityTaskCommandLine";
        private const string k_commandLineStartTicksSessionKey = "Onity.Benchmarks.OnityTaskCommandLineStartTicks";
        private const string k_allocationOnlySessionKey = "Onity.Benchmarks.OnityTaskAllocationOnly";
        private const string k_completionSourceSessionKey = "Onity.Benchmarks.OnityCompletionSource";
        private const string k_attributionSessionKey = "Onity.Benchmarks.OnityCompletionSourceAttribution";
        private const string k_statusProbeSessionKey = "Onity.Benchmarks.OnityCompletionSourceStatusProbe";
        private const string k_preserveSessionKey = "Onity.Benchmarks.OnityPreserve";
        private const string k_lifecycleSessionKey = "Onity.Benchmarks.OnityNativeLifecycle";
        private const string k_builderAttributionSessionKey = "Onity.Benchmarks.OnityTaskBuilderAttribution";
        private const string k_outputArgument = "-onityTaskBenchmarkOutput";
        private const string k_allocationOnlyArgument = "-onityTaskAllocationsOnly";
        private const string k_completionSourceArgument = "-onityCompletionSourceBenchmark";
        private const string k_attributionArgument = "-onityCompletionSourceAttribution";
        private const string k_preserveAttributionArgument = "-onityPreserveAttribution";
        private const string k_statusProbeArgument = "-onityCompletionSourceStatusProbe";
        private const string k_preserveArgument = "-onityPreserveBenchmark";
        private const string k_lifecycleArgument = "-onityNativeLifecycleBenchmark";
        private const string k_builderAttributionArgument = "-onityTaskBuilderAttribution";
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

        [MenuItem("Onity/Benchmarks/Run Completion Source Benchmarks (Play Mode)")]
        private static void RunCompletionSourcesFromMenu()
        {
            SessionState.SetBool(k_completionSourceSessionKey, true);
            RunFromMenu();
        }

        [MenuItem("Onity/Benchmarks/Run Preserve Benchmarks (Play Mode)")]
        private static void RunPreserveFromMenu()
        {
            SessionState.SetBool(k_preserveSessionKey, true);
            RunFromMenu();
        }

        [MenuItem("Onity/Benchmarks/Run Native Task Lifecycle Benchmarks (Play Mode)")]
        private static void RunLifecycleFromMenu()
        {
            SessionState.SetBool(k_lifecycleSessionKey, true);
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
            bool allocationOnly = SessionState.GetBool(k_allocationOnlySessionKey, false);
            bool completionSource = SessionState.GetBool(k_completionSourceSessionKey, false);
            bool attribution = SessionState.GetBool(k_attributionSessionKey, false);
            bool statusProbe = SessionState.GetBool(k_statusProbeSessionKey, false);
            bool preserve = SessionState.GetBool(k_preserveSessionKey, false);
            bool lifecycle = SessionState.GetBool(k_lifecycleSessionKey, false);
            bool builderAttribution = SessionState.GetBool(k_builderAttributionSessionKey, false);
            string latestJson = GetLatestJsonPath(completionSource, preserve, lifecycle,
                builderAttribution);
            if (!commandLineRun)
            {
                SessionState.EraseBool(k_completionSourceSessionKey);
                SessionState.EraseBool(k_attributionSessionKey);
                SessionState.EraseBool(k_statusProbeSessionKey);
                SessionState.EraseBool(k_preserveSessionKey);
                SessionState.EraseBool(k_lifecycleSessionKey);
                SessionState.EraseBool(k_builderAttributionSessionKey);
            }

            if (completionSource || preserve || lifecycle)
            {
                OnityCompletionSourceBenchmarkRunner.Run(
                    latestJson,
                    commandLineRun ? HandleCommandLineCompleted : null,
                    allocationOnly,
                    attribution,
                    statusProbe,
                    preserve,
                    lifecycle);
            }
            else
            {
                OnityTaskBenchmarkRunner.Run(
                    latestJson,
                    commandLineRun ? HandleCommandLineCompleted : null,
                    allocationOnly,
                    builderAttribution);
            }

            Debug.Log("Queued OnityTask benchmark for the next Play Mode frame.");
        }

        /// <summary>
        /// Command-line entry point for running the OnityTask benchmark in Play Mode.
        /// Pass -onityTaskAllocationsOnly with -profiler-enable to add allocation
        /// samples to an existing timing report in a separate invocation.
        /// Do not pass -quit; the runner exits Unity after writing the report.
        /// </summary>
        public static void RunFromCommandLine()
        {
            string latestJson = GetArgumentValue(k_outputArgument);
            bool allocationOnly = HasArgument(k_allocationOnlyArgument);
            bool completionSource = HasArgument(k_completionSourceArgument);
            bool completionAttribution = HasArgument(k_attributionArgument);
            bool preserveAttribution = HasArgument(k_preserveAttributionArgument);
            bool attribution = completionAttribution || preserveAttribution;
            bool statusProbe = HasArgument(k_statusProbeArgument);
            bool preserve = HasArgument(k_preserveArgument);
            bool lifecycle = HasArgument(k_lifecycleArgument);
            bool builderAttribution = HasArgument(k_builderAttributionArgument);
            if ((allocationOnly || attribution || builderAttribution) && !HasArgument("-profiler-enable"))
            {
                throw new ArgumentException("Allocation pass requires Unity's -profiler-enable startup flag.");
            }
            if (completionAttribution && !completionSource)
            {
                throw new ArgumentException("Completion-source attribution requires -onityCompletionSourceBenchmark.");
            }
            if (preserveAttribution && !preserve)
            {
                throw new ArgumentException("Preserve attribution requires -onityPreserveBenchmark.");
            }
            if (attribution && allocationOnly)
            {
                throw new ArgumentException("Attribution and allocation-only modes cannot run together.");
            }
            if (statusProbe && !completionSource)
            {
                throw new ArgumentException("Completion-source status probe requires -onityCompletionSourceBenchmark.");
            }
            if (statusProbe && attribution)
            {
                throw new ArgumentException("Status probe and attribution cannot run together.");
            }
            if (preserve && (completionSource || statusProbe || completionAttribution))
            {
                throw new ArgumentException("Preserve benchmark must run without completion-source modes.");
            }
            if (lifecycle && (preserve || completionSource || statusProbe || attribution))
            {
                throw new ArgumentException("Native lifecycle benchmark requires its own mode.");
            }
            if (builderAttribution && (allocationOnly || completionSource || preserve || lifecycle || attribution))
            {
                throw new ArgumentException("Async builder attribution requires its own mode.");
            }
            if (string.IsNullOrEmpty(latestJson))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                latestJson = Path.Combine(projectRoot, k_resultsDirectory,
                    lifecycle ? k_lifecycleJsonFileName :
                    preserve ? k_preserveJsonFileName :
                    completionSource ? k_completionSourceJsonFileName :
                    builderAttribution ? k_builderAttributionJsonFileName : k_latestJsonFileName);
            }

            latestJson = Path.GetFullPath(latestJson);
            if (allocationOnly && !File.Exists(latestJson))
            {
                throw new FileNotFoundException("Allocation pass requires an existing timing report.", latestJson);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(latestJson));

            SessionState.SetBool(k_pendingSessionKey, true);
            SessionState.SetString(k_outputSessionKey, latestJson);
            SessionState.SetBool(k_commandLineSessionKey, true);
            SessionState.SetBool(k_allocationOnlySessionKey, allocationOnly);
            SessionState.SetBool(k_completionSourceSessionKey, completionSource);
            SessionState.SetBool(k_attributionSessionKey, attribution);
            SessionState.SetBool(k_statusProbeSessionKey, statusProbe);
            SessionState.SetBool(k_preserveSessionKey, preserve);
            SessionState.SetBool(k_lifecycleSessionKey, lifecycle);
            SessionState.SetBool(k_builderAttributionSessionKey, builderAttribution);
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

        private static string GetLatestJsonPath(bool completionSource, bool preserve,
            bool lifecycle, bool builderAttribution)
        {
            string latestJson = SessionState.GetString(k_outputSessionKey, string.Empty);
            if (!string.IsNullOrEmpty(latestJson))
            {
                return latestJson;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, k_resultsDirectory,
                lifecycle ? k_lifecycleJsonFileName :
                preserve ? k_preserveJsonFileName :
                completionSource ? k_completionSourceJsonFileName :
                builderAttribution ? k_builderAttributionJsonFileName : k_latestJsonFileName);
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
            SessionState.EraseBool(k_allocationOnlySessionKey);
            SessionState.EraseBool(k_completionSourceSessionKey);
            SessionState.EraseBool(k_attributionSessionKey);
            SessionState.EraseBool(k_statusProbeSessionKey);
            SessionState.EraseBool(k_preserveSessionKey);
            SessionState.EraseBool(k_lifecycleSessionKey);
            SessionState.EraseBool(k_builderAttributionSessionKey);
            SessionState.EraseString(k_outputSessionKey);
            SessionState.EraseString(k_commandLineStartTicksSessionKey);
            EditorApplication.update -= HandleCommandLineTimeout;
        }
    }
}
