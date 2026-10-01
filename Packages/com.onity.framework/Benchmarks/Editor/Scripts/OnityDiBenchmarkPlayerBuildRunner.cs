using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Onity.Benchmarks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Editor.Benchmarks
{
    /// <summary>
    /// Builds and runs the DI benchmark inside a Standalone player.
    /// </summary>
    public static class OnityDiBenchmarkPlayerBuildRunner
    {
        private const string k_resultsDirectory = "Packages/com.onity.framework/Benchmarks/Results";
        private const string k_latestPlayerJsonFileName = "di-benchmark-player-latest.json";
        private const string k_latestPlayerAllocationJsonFileName = "di-allocation-bytes-player-latest.json";
        private const string k_buildPathArgument = "-onityBenchmarkBuildPath";
        private const string k_outputArgument = "-onityBenchmarkOutput";
        private const string k_playerRunArgument = "-onityRunDiBenchmark";
        private const string k_allocationCaptureArgument = "-onityCaptureDiAllocationBytes";
        private const string k_hotPathsOnlyArgument = "-onityBenchmarkHotPathsOnly";
        private const string k_benchmarkScenePath = "Assets/OnityBenchmarkTemp/OnityDiBenchmarkPlayer.unity";
        private static readonly string[] s_passthroughValueArguments =
        {
            "-onityBenchmarkIterations",
            "-onityBenchmarkSamples",
            "-onityBenchmarkWarmup",
            "-onityBenchmarkScenario"
        };

        [MenuItem("Onity/Benchmarks/Build and Run DI Benchmarks (IL2CPP Player)")]
        private static void BuildAndRunFromMenu()
        {
            BuildAndRunPlayerBenchmark();
        }

        /// <summary>
        /// Command-line entry point for building and running the player benchmark.
        /// </summary>
        public static void BuildAndRunFromCommandLine()
        {
            BuildAndRunPlayerBenchmark();
        }

        private static void BuildAndRunPlayerBenchmark()
        {
            if (Application.isBatchMode == false
                && UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false)
            {
                return;
            }

            bool captureAllocations = HasArgument(k_allocationCaptureArgument);
            BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;
            BuildTarget target = BuildTarget.StandaloneWindows64;
            BuildTarget originalTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup originalTargetGroup = BuildPipeline.GetBuildTargetGroup(originalTarget);
            ScriptingImplementation originalBackend = PlayerSettings.GetScriptingBackend(targetGroup);
            string benchmarkScene = null;
            bool benchmarkFolderCreated = false;

            string buildPath = GetArgumentValue(k_buildPathArgument);
            string latestJson = GetArgumentValue(k_outputArgument);

            if (string.IsNullOrEmpty(buildPath))
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                string buildFolder = captureAllocations ? $"AllocationPlayer-{stamp}" : $"Player-{stamp}";
                buildPath = Path.Combine("Temp", "OnityBenchmarks", buildFolder, "OnityDiBenchmarkPlayer.exe");
            }

            if (string.IsNullOrEmpty(latestJson))
            {
                latestJson = captureAllocations
                    ? Path.Combine("Temp", "OnityBenchmarks", k_latestPlayerAllocationJsonFileName)
                    : Path.Combine(k_resultsDirectory, k_latestPlayerJsonFileName);
            }

            buildPath = Path.GetFullPath(buildPath);
            latestJson = Path.GetFullPath(latestJson);

            Directory.CreateDirectory(Path.GetDirectoryName(buildPath));
            Directory.CreateDirectory(Path.GetDirectoryName(latestJson));
            string rawProfilePath = captureAllocations
                ? Path.Combine(
                    Path.GetDirectoryName(latestJson),
                    $"di-allocation-player-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.raw")
                : null;

            try
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target))
                {
                    throw new InvalidOperationException("Failed to switch active build target to StandaloneWindows64.");
                }

                PlayerSettings.SetScriptingBackend(targetGroup, ScriptingImplementation.IL2CPP);

                // Projects without an enabled build scene get a generated scene whose bootstrap
                // starts the run; otherwise the runner starts from RuntimeInitializeOnLoad.
                string[] scenes = GetEnabledScenes();
                if (scenes.Length == 0)
                {
                    benchmarkScene = CreateBenchmarkScene(out benchmarkFolderCreated);
                    scenes = new[] { benchmarkScene };
                }

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = buildPath,
                    target = target,
                    targetGroup = targetGroup,
                    options = captureAllocations ? BuildOptions.Development : BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"IL2CPP player benchmark build failed: {report.summary.result}. See the Unity editor log for details.");
                }

                RunPlayer(buildPath, captureAllocations ? rawProfilePath : latestJson, latestJson, captureAllocations);
                if (captureAllocations)
                {
                    OnityDiBenchmarkRunner.ImportIl2CppAllocationProfile(rawProfilePath, latestJson);
                }
                else
                {
                    AssetDatabase.Refresh();
                }

                UnityEngine.Debug.Log(
                    $"Onity DI IL2CPP {(captureAllocations ? "Development allocation" : "release timing")} benchmark completed. Latest report: {latestJson}");
            }
            finally
            {
                try
                {
                    DeleteBenchmarkScene(benchmarkScene, benchmarkFolderCreated);
                    PlayerSettings.SetScriptingBackend(targetGroup, originalBackend);
                }
                finally
                {
                    if (originalTarget != target
                        && EditorUserBuildSettings.SwitchActiveBuildTarget(originalTargetGroup, originalTarget) == false)
                    {
                        UnityEngine.Debug.LogError($"Failed to restore active build target to {originalTarget}.");
                    }
                }
            }
        }

        private static string BuildPassthroughArguments()
        {
            StringBuilder builder = new StringBuilder();

            if (HasArgument(k_hotPathsOnlyArgument))
            {
                builder.Append(' ').Append(k_hotPathsOnlyArgument);
            }

            for (int i = 0; i < s_passthroughValueArguments.Length; i++)
            {
                string value = GetArgumentValue(s_passthroughValueArguments[i]);
                if (string.IsNullOrEmpty(value) == false)
                {
                    builder.Append(' ').Append(s_passthroughValueArguments[i]).Append(" \"").Append(value).Append('"');
                }
            }

            return builder.ToString();
        }

        private static void RunPlayer(
            string buildPath,
            string playerOutputPath,
            string latestJson,
            bool captureAllocations)
        {
            string logPath = Path.ChangeExtension(latestJson, ".player.log");
            StringBuilder output = new StringBuilder(4096);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = buildPath,
                Arguments = $"-batchmode -nographics {k_playerRunArgument} "
                    + (captureAllocations ? $"{k_allocationCaptureArgument} " : string.Empty)
                    + $"{k_outputArgument} \"{playerOutputPath}\""
                    + (captureAllocations ? string.Empty : BuildPassthroughArguments()),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process process = new Process();
            process.StartInfo = startInfo;
            process.OutputDataReceived += (_, args) => AppendLine(output, args.Data);
            process.ErrorDataReceived += (_, args) => AppendLine(output, args.Data);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(900000))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between the timeout and Kill.
                }

                File.WriteAllText(logPath, output.ToString(), Encoding.UTF8);
                throw new TimeoutException($"Player benchmark did not exit within 15 minutes. Log: {logPath}");
            }

            process.WaitForExit();
            File.WriteAllText(logPath, output.ToString(), Encoding.UTF8);

            if (captureAllocations &&
                output.ToString().IndexOf("Skipping profile frame", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(
                    $"Player profiler dropped a frame. Allocation results are invalid. Log: {logPath}");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Player benchmark exited with code {process.ExitCode}. Log: {logPath}");
            }

            if (!File.Exists(playerOutputPath) || new FileInfo(playerOutputPath).Length == 0)
            {
                throw new FileNotFoundException("Player benchmark did not write the expected output.", playerOutputPath);
            }
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

        private static string[] GetEnabledScenes()
        {
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            int count = 0;

            for (int i = 0; i < buildScenes.Length; i++)
            {
                if (buildScenes[i].enabled && !string.IsNullOrEmpty(buildScenes[i].path))
                {
                    count++;
                }
            }

            string[] scenes = new string[count];
            int sceneIndex = 0;

            for (int i = 0; i < buildScenes.Length; i++)
            {
                if (buildScenes[i].enabled && !string.IsNullOrEmpty(buildScenes[i].path))
                {
                    scenes[sceneIndex] = buildScenes[i].path;
                    sceneIndex++;
                }
            }

            return scenes;
        }

        private static string CreateBenchmarkScene(out bool benchmarkFolderCreated)
        {
            string benchmarkFolder = Path.GetDirectoryName(k_benchmarkScenePath);
            benchmarkFolderCreated = Directory.Exists(benchmarkFolder) == false;
            Directory.CreateDirectory(benchmarkFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject runner = new GameObject("Onity DI Benchmark Player");
            runner.AddComponent<OnityDiBenchmarkPlayerBootstrap>();
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(k_benchmarkScenePath);
            if (EditorSceneManager.SaveScene(scene, scenePath) == false)
            {
                EditorSceneManager.CloseScene(scene, true);
                throw new InvalidOperationException($"Failed to save benchmark scene at '{scenePath}'.");
            }

            AssetDatabase.Refresh();
            return scenePath;
        }

        private static void DeleteBenchmarkScene(string benchmarkScenePath, bool benchmarkFolderCreated)
        {
            if (string.IsNullOrEmpty(benchmarkScenePath))
            {
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(benchmarkScenePath);
            if (scene.IsValid())
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.DeleteAsset(benchmarkScenePath);

            string benchmarkFolder = Path.GetDirectoryName(k_benchmarkScenePath);
            if (benchmarkFolderCreated
                && Directory.Exists(benchmarkFolder)
                && Directory.GetFileSystemEntries(benchmarkFolder).Length == 0)
            {
                AssetDatabase.DeleteAsset(benchmarkFolder);
            }
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

        private static void AppendLine(StringBuilder builder, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                builder.AppendLine(value);
            }
        }
    }
}
