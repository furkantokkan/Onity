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
        private const string k_latestSmokeJsonFileName = "onity-task-smoke-player-latest.json";
        private const string k_buildPathArgument = "-onityTaskBenchmarkBuildPath";
        private const string k_outputArgument = "-onityTaskBenchmarkOutput";
        private const string k_backendArgument = "-onityTaskBenchmarkBackend";
        private const string k_suiteArgument = "-onityTaskBenchmarkSuite";
        private const string k_playerRunArgument = "-onityRunTaskBenchmark";
        private const string k_startupTraceArgument = "-onityTaskBenchmarkStartupTrace";
        private const string k_threadSwitchSuite = "threadswitch";
        private const string k_benchmarkDefine = "ONITY_TASK_BENCHMARKS";
        private const string k_benchmarkScenePath = "Assets/OnityBenchmarkTemp/OnityTaskBenchmarkPlayer.unity";
        private const int k_playerPollMilliseconds = 100;

        [MenuItem("Onity/Benchmarks/Build and Run OnityTask Benchmarks (Mono Player)")]
        private static void BuildAndRunMonoFromMenu()
        {
            BuildAndRunPlayerBenchmark(ScriptingImplementation.Mono2x, "primary");
        }

        [MenuItem("Onity/Benchmarks/Build and Run OnityTask Benchmarks (IL2CPP Player)")]
        private static void BuildAndRunIl2CppFromMenu()
        {
            BuildAndRunPlayerBenchmark(ScriptingImplementation.IL2CPP, "primary");
        }

        [MenuItem("Onity/Benchmarks/Build and Run OnityTask Thread Switch Benchmarks (IL2CPP Player)")]
        private static void BuildAndRunThreadSwitchIl2CppFromMenu()
        {
            BuildAndRunPlayerBenchmark(ScriptingImplementation.IL2CPP, k_threadSwitchSuite);
        }

        /// <summary>
        /// Command-line entry point. Reads <c>-onityTaskBenchmarkBackend Mono|IL2CPP</c> (default
        /// IL2CPP), <c>-onityTaskBenchmarkSuite primary|threadswitch|smoke</c> (default primary),
        /// <c>-onityTaskBenchmarkBuildPath</c>, and <c>-onityTaskBenchmarkOutput</c>.
        /// </summary>
        public static void BuildAndRunFromCommandLine()
        {
            string backend = GetArgumentValue(k_backendArgument);
            ScriptingImplementation implementation = string.Equals(backend, "Mono", StringComparison.OrdinalIgnoreCase)
                ? ScriptingImplementation.Mono2x
                : ScriptingImplementation.IL2CPP;
            string suite = GetArgumentValue(k_suiteArgument);
            suite = string.IsNullOrEmpty(suite) ? "primary" : suite.ToLowerInvariant();
            if (suite != "primary" && suite != k_threadSwitchSuite && suite != "smoke" && suite != "fullcycle")
            {
                throw new ArgumentException("Task benchmark suite must be primary, threadswitch, smoke, or fullcycle.");
            }

            BuildAndRunPlayerBenchmark(implementation, suite);
        }

        private static void BuildAndRunPlayerBenchmark(ScriptingImplementation backend, string suite)
        {
            bool fullCycle = suite == "fullcycle";
            if (fullCycle && backend != ScriptingImplementation.IL2CPP)
            {
                throw new ArgumentException("The fullcycle diagnostic build requires IL2CPP.");
            }
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
            string[] originalCompilerArguments = fullCycle
                ? PlayerSettings.GetAdditionalCompilerArguments(UnityEditor.Build.NamedBuildTarget.Standalone)
                : null;
            SceneSetup[] originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            string benchmarkScene = null;
            bool benchmarkFolderCreated = false;

            string suiteLabel = suite;
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
                    k_resultsDirectory, fullCycle ? "onity-task-fullcycle-player-latest.json"
                        : suite == "smoke" ? k_latestSmokeJsonFileName
                        : suite == k_threadSwitchSuite ? k_latestThreadSwitchJsonFileName : k_latestJsonFileName);
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
                if (fullCycle)
                {
                    string[] compilerArguments = new string[originalCompilerArguments.Length + 1];
                    Array.Copy(originalCompilerArguments, compilerArguments, originalCompilerArguments.Length);
                    compilerArguments[compilerArguments.Length - 1] = "/optimize+";
                    PlayerSettings.SetAdditionalCompilerArguments(
                        UnityEditor.Build.NamedBuildTarget.Standalone, compilerArguments);
                }

                benchmarkScene = CreateBenchmarkScene(out benchmarkFolderCreated);
                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = new[] { benchmarkScene },
                    locationPathName = buildPath,
                    target = target,
                    targetGroup = targetGroup,
                    options = fullCycle
                        ? BuildOptions.Development | BuildOptions.EnableDeepProfilingSupport
                        : BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"{backendLabel} player benchmark build failed: {report.summary.result}. See the Unity editor log for details.");
                }

                WriteBuildMetadata(buildPath + ".build.json", targetGroup, fullCycle);
                RunPlayer(buildPath, latestJson, suite);
                AssetDatabase.Refresh();
                UnityEngine.Debug.Log(
                    $"Onity task {suiteLabel} {backendLabel} player benchmark completed. Latest report: {latestJson}");
            }
            finally
            {
                try
                {
                    try
                    {
                        DeleteBenchmarkScene(benchmarkScene, benchmarkFolderCreated);
                    }
                    finally
                    {
                        if (fullCycle)
                        {
                            PlayerSettings.SetAdditionalCompilerArguments(
                                UnityEditor.Build.NamedBuildTarget.Standalone, originalCompilerArguments);
                        }
                    }
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

        private static void RunPlayer(string buildPath, string latestJson, string suite)
        {
            string logPath = Path.ChangeExtension(latestJson, ".player.log");
            string startupTracePath = Path.ChangeExtension(latestJson, ".startup.log");
            // A prior process's handshake must never satisfy this launch's startup watchdog.
            File.WriteAllText(startupTracePath, string.Empty);
            StringBuilder output = new StringBuilder(4096);
            string drainArgument = HasArgument("-onityTaskBenchmarkDrainBetweenBatches")
                ? " -onityTaskBenchmarkDrainBetweenBatches" : string.Empty;
            string profileArguments = suite == "fullcycle"
                ? " -deepprofiling"
                    + ForwardRequiredArgument("-onityTaskProfileConcurrency")
                    + ForwardRequiredArgument("-onityTaskProfileFlow")
                    + ForwardRequiredArgument("-onityTaskProfileReuse")
                : string.Empty;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = buildPath,
                Arguments = $"-batchmode -nographics -logFile \"{logPath}\" {k_playerRunArgument} "
                    + $"{k_outputArgument} \"{latestJson}\" {k_suiteArgument} {suite} "
                    + $"{k_startupTraceArgument} \"{startupTracePath}\" "
                    + $"-onityTaskBenchmarkBuildMetadata \"{buildPath}.build.json\"" + drainArgument + profileArguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process process = new Process();
            process.StartInfo = startInfo;
            process.OutputDataReceived += (_, args) => AppendLine(output, args.Data);
            process.ErrorDataReceived += (_, args) => AppendLine(output, args.Data);

            DateTime launchTimeUtc = DateTime.UtcNow;
            process.Start();
            Stopwatch elapsed = Stopwatch.StartNew();
            OnityTaskBenchmarkPlayerWatchdog watchdog = new OnityTaskBenchmarkPlayerWatchdog();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            string startupTrace = string.Empty;
            while (true)
            {
                bool exited = process.WaitForExit(k_playerPollMilliseconds);
                if (!watchdog.HasEnteredBenchmark || exited)
                {
                    startupTrace = ReadStartupTrace(startupTracePath, startupTrace);
                }
                PlayerBenchmarkTimeout timeout = watchdog.Check(
                    elapsed.ElapsedMilliseconds, watchdog.HasEnteredBenchmark
                        || OnityTaskBenchmarkPlayerWatchdog.HasEntryMarker(startupTrace));
                if (exited)
                {
                    // Drain redirected output before writing it; the timed overload does not wait for readers.
                    process.WaitForExit();
                    break;
                }

                if (timeout != PlayerBenchmarkTimeout.None)
                {
                    startupTrace = ReadStartupTrace(startupTracePath, startupTrace);
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between the timeout and Kill.
                    }

                    TryWriteProcessOutput(logPath, output);
                    string reason = timeout == PlayerBenchmarkTimeout.Startup
                        ? "Player benchmark never reached benchmark entry within 60 seconds."
                        : "Player benchmark did not exit within 15 minutes after benchmark entry.";
                    throw new TimeoutException(reason + StartupEvidence(startupTracePath, startupTrace, logPath));
                }
            }

            TryWriteProcessOutput(logPath, output);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Player benchmark exited with code {process.ExitCode}."
                    + StartupEvidence(startupTracePath, startupTrace, logPath));
            }

            if (!watchdog.HasEnteredBenchmark)
            {
                throw new InvalidOperationException("Player exited without a benchmark-entry handshake."
                    + StartupEvidence(startupTracePath, startupTrace, logPath));
            }

            if (!File.Exists(latestJson))
            {
                throw new FileNotFoundException("Player benchmark did not write the expected report.", latestJson);
            }

            if (File.GetLastWriteTimeUtc(latestJson) < launchTimeUtc)
            {
                throw new InvalidOperationException("Player benchmark left a stale report from an earlier run: "
                    + latestJson + StartupEvidence(startupTracePath, startupTrace, logPath));
            }
        }

        private static string ReadStartupTrace(string tracePath, string previousTrace)
        {
            try
            {
                using FileStream stream = new FileStream(
                    tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException)
            {
                // The player may be appending a marker. Preserve the last readable evidence.
                return previousTrace;
            }
        }

        private static void WriteBuildMetadata(string path, BuildTargetGroup targetGroup, bool fullCycle)
        {
            BuildMetadata metadata = new BuildMetadata
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                codeOptimization = fullCycle
                    ? "Development diagnostic Player; EnableDeepProfilingSupport; attribution only"
                    : "Release (non-development Player; BuildOptions.None)",
                managedCompilerOptimization = fullCycle
                    ? "/optimize+ requested; verify effective response file and generated state-machine shape"
                    : "Unity non-development default; additional compiler arguments not modified",
                il2CppCompilerConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(targetGroup).ToString(),
                managedStrippingLevel = PlayerSettings.GetManagedStrippingLevel(targetGroup).ToString(),
                onityPackageId = "Unknown (package not registered)",
                onityVersion = "Unknown (package not registered)",
                uniTaskPackageId = "Unknown (package not registered)",
                uniTaskVersion = "Unknown (package not registered)"
            };
            UnityEditor.PackageManager.PackageInfo[] packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            for (int i = 0; i < packages.Length; i++)
            {
                if (packages[i].name == "com.onity.framework")
                {
                    metadata.onityPackageId = packages[i].packageId;
                    metadata.onityVersion = packages[i].version;
                }
                else if (packages[i].name == "com.cysharp.unitask")
                {
                    metadata.uniTaskPackageId = packages[i].packageId;
                    metadata.uniTaskVersion = packages[i].version;
                }
            }

            File.WriteAllText(path, JsonUtility.ToJson(metadata, true));
        }

        [Serializable]
        private sealed class BuildMetadata
        {
            public string generatedAtUtc;
            public string codeOptimization;
            public string managedCompilerOptimization;
            public string il2CppCompilerConfiguration;
            public string managedStrippingLevel;
            public string onityPackageId;
            public string onityVersion;
            public string uniTaskPackageId;
            public string uniTaskVersion;
        }

        private static string StartupEvidence(string tracePath, string trace, string logPath)
        {
            string lastMarker = "none (no readable startup marker)";
            if (!string.IsNullOrWhiteSpace(trace))
            {
                string trimmedTrace = trace.TrimEnd('\r', '\n');
                int lineStart = trimmedTrace.LastIndexOf('\n');
                lastMarker = trimmedTrace.Substring(lineStart + 1);
            }

            return $" Last startup marker: {lastMarker}. Startup trace: {tracePath}. Log: {logPath}";
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

        private static string ForwardRequiredArgument(string argumentName)
        {
            string value = GetArgumentValue(argumentName);
            if (string.IsNullOrEmpty(value) || value.IndexOf('"') >= 0)
            {
                throw new ArgumentException("Required fullcycle argument missing or invalid: " + argumentName);
            }

            return " " + argumentName + " \"" + value + "\"";
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
            string capturedOutput;
            lock (output)
            {
                if (output.Length == 0)
                {
                    return;
                }

                capturedOutput = output.ToString();
            }

            try
            {
                File.AppendAllText(logPath, capturedOutput, Encoding.UTF8);
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
                lock (builder)
                {
                    builder.AppendLine(value);
                }
            }
        }
    }
}
