using System.IO;
using Onity.Benchmarks;
using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Benchmarks
{
    [InitializeOnLoad]
    public static class OnityDiBenchmarkPlayModeMenu
    {
        private const string k_resultsDirectory = "Packages/com.onity.framework/Benchmarks/Results";
        private const string k_latestJsonFileName = "di-benchmark-playmode-latest.json";
        private const string k_pendingSessionKey = "Onity.Benchmarks.PendingPlayModeProfilerRun";

        static OnityDiBenchmarkPlayModeMenu()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        [MenuItem("Onity/Benchmarks/Run DI Benchmarks (Play Mode Profiler)")]
        private static void RunFromMenu()
        {
            if (!EditorApplication.isPlaying)
            {
                SessionState.SetBool(k_pendingSessionKey, true);
                EditorApplication.isPlaying = true;
                Debug.Log("Entering Play Mode. Onity DI benchmark will run after Play Mode starts.");
                return;
            }

            RunInCurrentPlayMode();
        }

        [MenuItem("Onity/Benchmarks/Run DI Benchmarks (Play Mode Profiler)", true)]
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

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string latestJson = Path.Combine(projectRoot, k_resultsDirectory, k_latestJsonFileName);
            OnityDiBenchmarkPlayModeDriver.Run(latestJson);

            Debug.Log(
                "Queued Onity DI benchmark for the next Play Mode frame. Select the benchmark spike frame and search Profiler for 'Onity.DI.Benchmark'.");
        }
    }
}
