using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Menu
{
    /// <summary>
    /// Mirrors Onity top-level menu commands under Tools/Onity for a unified editor workflow.
    /// </summary>
    public static class OnityToolsMenuAliases
    {
        private const string k_toolsRoot = "Tools/Onity/";

        [MenuItem(k_toolsRoot + "Diagnostics/Monitor", false, 4000)]
        private static void OpenMonitor()
        {
            ExecuteMenuItem("Onity/Tools/Monitor");
        }

        [MenuItem(k_toolsRoot + "Diagnostics/Container Diagnostics", false, 4001)]
        private static void OpenContainerDiagnostics()
        {
            ExecuteMenuItem("Onity/Tools/Container Diagnostics");
        }

        [MenuItem(k_toolsRoot + "Diagnostics/Task Tracker", false, 4002)]
        private static void OpenTaskTracker()
        {
            ExecuteMenuItem("Onity/Tools/Task Tracker");
        }

        [MenuItem(k_toolsRoot + "Diagnostics/Pool Monitor", false, 4003)]
        private static void OpenPoolMonitor()
        {
            ExecuteMenuItem("Onity/Tools/Pool Monitor");
        }

        [MenuItem(k_toolsRoot + "Diagnostics/Observable Tracker", false, 4004)]
        private static void OpenObservableTracker()
        {
            ExecuteMenuItem("Onity/Tools/Observable Tracker");
        }

        [MenuItem(k_toolsRoot + "Diagnostics/Scene Flow Manager", false, 4005)]
        private static void OpenSceneFlowManager()
        {
            ExecuteMenuItem("Onity/Tools/Scene Flow Manager");
        }

        [MenuItem(k_toolsRoot + "Benchmarks/Run DI Benchmarks (Editor)", false, 4100)]
        private static void RunDiBenchmarks()
        {
            ExecuteMenuItem("Onity/Benchmarks/Run DI Benchmarks (Editor)");
        }

        [MenuItem(k_toolsRoot + "Samples/Setup Samples", false, 4200)]
        private static void SetupSamples()
        {
            ExecuteMenuItem("Onity/Samples/Setup Samples");
        }

        private static void ExecuteMenuItem(string menuPath)
        {
            if (EditorApplication.ExecuteMenuItem(menuPath))
            {
                return;
            }

            Debug.LogWarning(
                $"Could not execute menu item '{menuPath}'. Ensure corresponding package/editor code is imported.");
        }
    }
}
