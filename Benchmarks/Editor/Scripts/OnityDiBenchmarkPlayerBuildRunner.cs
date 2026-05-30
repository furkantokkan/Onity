using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Onity.Editor.Benchmarks
{
    /// <summary>
    /// Builds and runs the DI benchmark inside a Standalone player.
    /// </summary>
    public static class OnityDiBenchmarkPlayerBuildRunner
    {
        private const string k_resultsDirectory = "Packages/com.onity.framework/Benchmarks/Results";
        private const string k_latestPlayerJsonFileName = "di-benchmark-player-latest.json";
        private const string k_buildPathArgument = "-onityBenchmarkBuildPath";
        private const string k_outputArgument = "-onityBenchmarkOutput";
        private const string k_playerRunArgument = "-onityRunDiBenchmark";

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
            BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;
            BuildTarget target = BuildTarget.StandaloneWindows64;
            ScriptingImplementation originalBackend = PlayerSettings.GetScriptingBackend(targetGroup);

            string buildPath = GetArgumentValue(k_buildPathArgument);
            string latestJson = GetArgumentValue(k_outputArgument);

            if (string.IsNullOrEmpty(buildPath))
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                buildPath = Path.Combine("Temp", "OnityBenchmarks", $"Player-{stamp}", "OnityDiBenchmarkPlayer.exe");
            }

            if (string.IsNullOrEmpty(latestJson))
            {
                latestJson = Path.Combine(k_resultsDirectory, k_latestPlayerJsonFileName);
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

                PlayerSettings.SetScriptingBackend(targetGroup, ScriptingImplementation.IL2CPP);

                string[] scenes = GetEnabledScenes();

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = buildPath,
                    target = target,
                    targetGroup = targetGroup,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"IL2CPP player benchmark build failed: {report.summary.result}. See the Unity editor log for details.");
                }

                RunPlayer(buildPath, latestJson);
                AssetDatabase.Refresh();

                UnityEngine.Debug.Log($"Onity DI IL2CPP player benchmark completed. Latest report: {latestJson}");
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(targetGroup, originalBackend);
            }
        }

        private static void RunPlayer(string buildPath, string latestJson)
        {
            string logPath = Path.ChangeExtension(latestJson, ".player.log");
            StringBuilder output = new StringBuilder(4096);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = buildPath,
                Arguments = $"-batchmode -nographics {k_playerRunArgument} {k_outputArgument} \"{latestJson}\"",
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

            File.WriteAllText(logPath, output.ToString(), Encoding.UTF8);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Player benchmark exited with code {process.ExitCode}. Log: {logPath}");
            }

            if (!File.Exists(latestJson))
            {
                throw new FileNotFoundException("Player benchmark did not write the expected report.", latestJson);
            }
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

            if (count == 0)
            {
                throw new InvalidOperationException("No enabled scenes are configured in EditorBuildSettings.");
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
