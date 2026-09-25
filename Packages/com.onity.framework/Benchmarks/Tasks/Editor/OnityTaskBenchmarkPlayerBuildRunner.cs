using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Editor.Benchmarks
{
    /// <summary>
    /// Builds a non-development Standalone player with the Mono or IL2CPP backend, runs the task
    /// benchmark inside it, and restores the project's build settings afterwards. Editor timings
    /// come from the Editor's own script runtime; a player is the only source of the numbers the
    /// package's performance claims are about.
    /// </summary>
    public static class OnityTaskBenchmarkPlayerBuildRunner
    {
        private const string k_resultsDirectory = "Packages/com.onity.framework/Benchmarks/Results";
        private const string k_latestJsonFileName = "onity-task-benchmark-player-latest.json";
        private const string k_latestThreadSwitchJsonFileName = "onity-thread-switch-benchmark-player-latest.json";
        private const string k_buildPathArgument = "-onityTaskBenchmarkBuildPath";
        private const string k_outputArgument = "-onityTaskBenchmarkOutput";
        private const string k_backendArgument = "-onityTaskBenchmarkBackend";
        private const string k_suiteArgument = "-onityTaskBenchmarkSuite";
        private const string k_playerRunArgument = "-onityRunTaskBenchmark";
        private const string k_threadSwitchSuite = "threadswitch";
        private const string k_benchmarkDefine = "ONITY_TASK_BENCHMARKS";
        private const string k_benchmarkScenePath = "Assets/OnityBenchmarkTemp/OnityTaskBenchmarkPlayer.unity";
        private const int k_playerTimeoutMilliseconds = 900000;

        [MenuItem("Onity/Benchmarks/Build and Run OnityTask Benchmarks (Mono Player)")]
        private static void BuildAndRunMonoFromMenu()
        {
            BuildAndRunPlayerBenchmark(ScriptingImplementation.Mono2x, false);
        }

        [MenuItem("Onity/Benchmarks/Build and Run OnityTask Benchmarks (IL2CPP Player)")]
        private static void BuildAndRunIl2CppFromMenu()
        {
            BuildAndRunPlayerBenchmark(ScriptingImplementation.IL2CPP, false);
        }

        [MenuItem("Onity/Benchmarks/Build and Run OnityTask Thread Switch Benchmarks (IL2CPP Player)")]
        private static void BuildAndRunThreadSwitchIl2CppFromMenu()
        {
            BuildAndRunPlayerBenchmark(ScriptingImplementation.IL2CPP, true);
        }

        /// <summary>
        /// Command-line entry point. Reads <c>-onityTaskBenchmarkBackend Mono|IL2CPP</c> (default
        /// IL2CPP), <c>-onityTaskBenchmarkSuite primary|threadswitch</c> (default primary),
        /// <c>-onityTaskBenchmarkBuildPath</c>, and <c>-onityTaskBenchmarkOutput</c>.
        /// </summary>
        public static void BuildAndRunFromCommandLine()
        {
            string backend = GetArgumentValue(k_backendArgument);
            ScriptingImplementation implementation = string.Equals(backend, "Mono", StringComparison.OrdinalIgnoreCase)
                ? ScriptingImplementation.Mono2x
                : ScriptingImplementation.IL2CPP;
            bool threadSwitch = string.Equals(
                GetArgumentValue(k_suiteArgument), k_threadSwitchSuite, StringComparison.OrdinalIgnoreCase);
            BuildAndRunPlayerBenchmark(implementation, threadSwitch);
        }

        private static void BuildAndRunPlayerBenchmark(ScriptingImplementation backend, bool threadSwitch)
        {
            if (Application.isBatchMode == false
                && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false)
            {
                return;
            }

            BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;
            BuildTarget target = BuildTarget.StandaloneWindows64;
            BuildTarget originalTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup originalTargetGroup = BuildPipeline.GetBuildTargetGroup(originalTarget);
            ScriptingImplementation originalBackend = PlayerSettings.GetScriptingBackend(targetGroup);
            string originalDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
            SceneSetup[] originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            string benchmarkScene = null;
            bool benchmarkFolderCreated = false;

            string suiteLabel = threadSwitch ? "thread-switch" : "primary";
            string backendLabel = backend == ScriptingImplementation.IL2CPP ? "IL2CPP" : "Mono";
            string buildPath = GetArgumentValue(k_buildPathArgument);
            string latestJson = GetArgumentValue(k_outputArgument);

            if (string.IsNullOrEmpty(buildPath))
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                buildPath = Path.Combine("Temp", "OnityBenchmarks", $"TaskPlayer-{backendLabel}-{stamp}", "OnityTaskBenchmarkPlayer.exe");
            }

            if (string.IsNullOrEmpty(latestJson))
            {
                latestJson = Path.Combine(
                    k_resultsDirectory, threadSwitch ? k_latestThreadSwitchJsonFileName : k_latestJsonFileName);
            }

            buildPath = Path.GetFullPath(buildPath);
            latestJson = Path.GetFullPath(latestJson);
            Directory.CreateDirectory(Path.GetDirectoryName(buildPath));
            Directory.CreateDirectory(Path.GetDirectoryName(latestJson));

            try
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target))
                {
                    throw new InvalidOperationException("Failed to switch active build target to StandaloneWindows64.");
                }

                PlayerSettings.SetScriptingBackend(targetGroup, backend);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, AddDefine(originalDefines, k_benchmarkDefine));

                benchmarkScene = CreateBenchmarkScene(out benchmarkFolderCreated);
                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = new[] { benchmarkScene },
                    locationPathName = buildPath,
                    target = target,
                    targetGroup = targetGroup,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"{backendLabel} player benchmark build failed: {report.summary.result}. See the Unity editor log for details.");
                }

                RunPlayer(buildPath, latestJson, threadSwitch);
                AssetDatabase.Refresh();
                UnityEngine.Debug.Log(
                    $"Onity task {suiteLabel} {backendLabel} player benchmark completed. Latest report: {latestJson}");
            }
            finally
            {
                try
                {
                    DeleteBenchmarkScene(benchmarkScene, benchmarkFolderCreated);
                }
                finally
                {
                    try
                    {
                        PlayerSettings.SetScriptingBackend(targetGroup, originalBackend);
                    }
                    finally
                    {
                        try
                        {
                            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, originalDefines);
                        }
                        finally
                        {
                            try
                            {
                                if (originalTarget != target
                                    && EditorUserBuildSettings.SwitchActiveBuildTarget(originalTargetGroup, originalTarget) == false)
                                {
                                    throw new InvalidOperationException(
                                        $"Failed to restore active build target to {originalTarget}.");
                                }
                            }
                            finally
                            {
                                if (Application.isBatchMode == false && originalSceneSetup.Length > 0)
                                {
                                    EditorSceneManager.RestoreSceneManagerSetup(originalSceneSetup);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void RunPlayer(string buildPath, string latestJson, bool threadSwitch)
        {
            string logPath = Path.ChangeExtension(latestJson, ".player.log");
            StringBuilder output = new StringBuilder(4096);
            string suite = threadSwitch ? k_threadSwitchSuite : "primary";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = buildPath,
                Arguments = $"-batchmode -nographics -logFile \"{logPath}\" {k_playerRunArgument} "
                    + $"{k_outputArgument} \"{latestJson}\" {k_suiteArgument} {suite}",
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

            if (!process.WaitForExit(k_playerTimeoutMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the timeout and Kill.
                }

                TryWriteProcessOutput(logPath, output);
                throw new TimeoutException($"Player benchmark did not exit within 15 minutes. Log: {logPath}");
            }

            TryWriteProcessOutput(logPath, output);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Player benchmark exited with code {process.ExitCode}. Log: {logPath}");
            }

            if (!File.Exists(latestJson))
            {
                throw new FileNotFoundException("Player benchmark did not write the expected report.", latestJson);
            }
        }

        private static string CreateBenchmarkScene(out bool benchmarkFolderCreated)
        {
            string benchmarkFolder = Path.GetDirectoryName(k_benchmarkScenePath);
            benchmarkFolderCreated = Directory.Exists(benchmarkFolder) == false;
            Directory.CreateDirectory(benchmarkFolder);

            // The runtime entry point starts from RuntimeInitializeOnLoadMethod; the scene only
            // needs to exist so the player has something to load.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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
            if (string.IsNullOrEmpty(benchmarkScenePath) == false)
            {
                Scene scene = SceneManager.GetSceneByPath(benchmarkScenePath);
                if (scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                AssetDatabase.DeleteAsset(benchmarkScenePath);
            }

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

        private static string AddDefine(string defines, string define)
        {
            if (string.IsNullOrEmpty(defines))
            {
                return define;
            }

            string[] parts = defines.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], define, StringComparison.Ordinal))
                {
                    return defines;
                }
            }

            return defines + ";" + define;
        }

        private static void TryWriteProcessOutput(string logPath, StringBuilder output)
        {
            if (output.Length == 0)
            {
                return;
            }

            try
            {
                File.AppendAllText(logPath, output.ToString(), Encoding.UTF8);
            }
            catch (IOException)
            {
                // The player owns -logFile; keep its log when it is still flushing.
            }
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
