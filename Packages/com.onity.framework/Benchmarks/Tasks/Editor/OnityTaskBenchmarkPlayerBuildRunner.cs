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
        private const string k_frameLifecycleArmsArgument = "-onityTaskFrameLifecycleArms";
        private const string k_retentionArgument = "-onityTaskBenchmarkRetention";
        private const string k_selfTestArgument = "-onityTaskBenchmarkSelfTest";
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
        /// IL2CPP), <c>-onityTaskBenchmarkSuite primary|threadswitch|threadpool|jobs|whenall|whenany|timing|buildercycle|builderlifecycle|framelifecycle|throughput|reactive|readiness|readinesscycle|smoke|fullcycle</c> (default primary),
        /// <c>-onityTaskBenchmarkBuildPath</c>, and <c>-onityTaskBenchmarkOutput</c>. For framelifecycle, an optional
        /// <c>-onityTaskFrameLifecycleArms</c> comma-separated arm id list is forwarded to the Player. For primary and
        /// builderlifecycle, an optional <c>-onityTaskBenchmarkRetention default|matched</c> is validated here and
        /// forwarded to the Player; a missing or invalid value, or use with another suite, throws before the build.
        /// <c>-onityTaskBenchmarkSelfTest</c> is forwarded for primary, builderlifecycle and throughput and throws with
        /// any other suite.
        /// </summary>
        public static void BuildAndRunFromCommandLine()
        {
            ScriptingImplementation implementation = ReadBackend();
            string suite = GetArgumentValue(k_suiteArgument);
            suite = string.IsNullOrEmpty(suite) ? "primary" : suite.ToLowerInvariant();
            if (suite != "primary" && suite != k_threadSwitchSuite && suite != "threadpool"
                && suite != "jobs" && suite != "whenall" && suite != "whenany" && suite != "timing"
                && suite != "smoke" && suite != "fullcycle" && suite != "eof" && suite != "finite" && suite != "channels"
                && suite != "buildercycle" && suite != "builderlifecycle" && suite != "framelifecycle"
                && suite != "throughput" && suite != "reactive" && suite != "readiness" && suite != "readinesscycle")
            {
                throw new ArgumentException("Task benchmark suite must be primary, threadswitch, threadpool, jobs, whenall, whenany, timing, eof, finite, channels, buildercycle, builderlifecycle, framelifecycle, throughput, reactive, readiness, readinesscycle, smoke, or fullcycle.");
            }

            ForwardRetentionArgument(suite);
            ForwardSelfTestArgument(suite);
            BuildAndRunPlayerBenchmark(implementation, suite, false);
        }

        /// <summary>
        /// Command-line entry point that only builds the non-development Release Player, so one binary per backend
        /// can serve many Player processes. Reads <c>-onityTaskBenchmarkBackend Mono|IL2CPP</c> (default IL2CPP) and
        /// the required <c>-onityTaskBenchmarkBuildPath &lt;exe&gt;</c>. Writes the usual <c>&lt;exe&gt;.build.json</c>
        /// sidecar and <c>&lt;exe&gt;.buildreport.json</c> (backend, build GUID, result, size, time). The built Player
        /// accepts <c>-onityRunTaskBenchmark -onityTaskBenchmarkSuite &lt;suite&gt; -onityTaskBenchmarkOutput &lt;json&gt;</c>.
        /// </summary>
        public static void BuildPlayerFromCommandLine()
        {
            if (string.IsNullOrEmpty(GetArgumentValue(k_buildPathArgument)))
            {
                throw new ArgumentException(k_buildPathArgument + " is required for a build-only run.");
            }

            BuildAndRunPlayerBenchmark(ReadBackend(), "primary", true);
        }

        private static ScriptingImplementation ReadBackend()
        {
            string backend = GetArgumentValue(k_backendArgument);
            if (!string.IsNullOrEmpty(backend) && !string.Equals(backend, "Mono", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(backend, "IL2CPP", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(k_backendArgument + " must be Mono or IL2CPP, got '" + backend + "'.");
            }

            return string.Equals(backend, "Mono", StringComparison.OrdinalIgnoreCase)
                ? ScriptingImplementation.Mono2x
                : ScriptingImplementation.IL2CPP;
        }

        private static void BuildAndRunPlayerBenchmark(ScriptingImplementation backend, string suite)
        {
            BuildAndRunPlayerBenchmark(backend, suite, false);
        }

        private static void BuildAndRunPlayerBenchmark(ScriptingImplementation backend, string suite, bool buildOnly)
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
                    k_resultsDirectory, suite == "eof" ? "onity-task-eof-player-latest.json"
                        : suite == "throughput" ? "onity-task-throughput-player-latest.json"
                        : suite == "reactive" ? "onity-task-reactive-player-latest.json"
                        : suite == "builderlifecycle" ? "onity-task-builderlifecycle-player-latest.json"
                        : suite == "framelifecycle" ? "onity-task-framelifecycle-player-latest.json"
                        : suite == "readinesscycle" ? "onity-task-readinesscycle-player-latest.json"
                        : suite == "readiness" ? "onity-task-readiness-player-latest.json"
                        : suite == "buildercycle" ? "onity-task-buildercycle-player-latest.json"
                        : suite == "channels" ? "onity-channels-player-latest.json"
                        : suite == "finite" ? "onity-async-enumerable-player-latest.json"
                        : fullCycle ? "onity-task-fullcycle-player-latest.json"
                        : suite == "jobs" ? "onity-task-jobs-player-latest.json"
                        : suite == "whenall" ? "onity-task-whenall-player-latest.json"
                        : suite == "whenany" ? "onity-task-whenany-player-latest.json"
                        : suite == "timing" ? "onity-task-timing-player-latest.json"
                        : suite == "threadpool" ? "onity-thread-pool-benchmark-player-latest.json"
                        : suite == "smoke" ? k_latestSmokeJsonFileName
                        : suite == k_threadSwitchSuite ? k_latestThreadSwitchJsonFileName : k_latestJsonFileName);
            }

            buildPath = Path.GetFullPath(buildPath);
            latestJson = Path.GetFullPath(latestJson);
            Directory.CreateDirectory(Path.GetDirectoryName(buildPath));
            if (!buildOnly)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(latestJson));
            }

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
                string buildGuid = report.summary.guid.ToString();
                if (buildOnly)
                {
                    WriteBuildReport(buildPath + ".buildreport.json", report, backendLabel, targetGroup);
                    UnityEngine.Debug.Log($"Onity task {backendLabel} benchmark Player built: {buildPath} (GUID {buildGuid}).");
                    return;
                }

                RunPlayer(buildPath, latestJson, suite, backendLabel, buildGuid);
                if (suite == "eof" || suite == "finite" || suite == "channels")
                {
                    // Reuse the exact fresh binary for ordinary headless smoke, with separate evidence.
                    string smokeJson = Path.ChangeExtension(latestJson, ".smoke.json");
                    RunPlayer(buildPath, smokeJson, "smoke", backendLabel, buildGuid);
                    CheckSmokeReport(smokeJson);
                }
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

        private static void RunPlayer(string buildPath, string latestJson, string suite,
            string expectedBackend, string expectedBuildGuid)
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
            string frameLifecycleArms = suite == "framelifecycle" ? ForwardOptionalArgument(k_frameLifecycleArmsArgument)
                : string.Empty;
            string retentionArgument = ForwardRetentionArgument(suite);
            string selfTestArgument = ForwardSelfTestArgument(suite);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = buildPath,
                Arguments = (suite == "eof"
                    ? "-screen-fullscreen 0 -screen-width 320 -screen-height 240 "
                    : "-batchmode -nographics ") + $"-logFile \"{logPath}\" {k_playerRunArgument} "
                    + $"{k_outputArgument} \"{latestJson}\" {k_suiteArgument} {suite} "
                    + $"{k_startupTraceArgument} \"{startupTracePath}\" "
                    + $"-onityTaskBenchmarkBuildMetadata \"{buildPath}.build.json\""
                    + (suite == "eof" ? $" -onityTaskExpectedBackend {expectedBackend} -onityTaskExpectedBuildGuid {expectedBuildGuid}" : string.Empty)
                    + drainArgument + profileArguments + frameLifecycleArms + retentionArgument + selfTestArgument,
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
            if (suite == "eof")
            {
                CheckEndOfFrameReport(latestJson, expectedBackend, expectedBuildGuid);
            }
            else if (suite == "finite")
            {
                CheckFiniteReport(latestJson, expectedBackend, expectedBuildGuid);
            }
            else if (suite == "channels")
            {
                CheckChannelReport(latestJson, expectedBackend, expectedBuildGuid);
            }
        }

        private static void CheckEndOfFrameReport(string path, string expectedBackend, string expectedBuildGuid)
        {
            EndOfFrameReport report = JsonUtility.FromJson<EndOfFrameReport>(File.ReadAllText(path));
            if (report == null || !report.passed || report.expectedCaseCount != 6
                || report.cases == null || report.cases.Length != 6 || report.pixels == null || report.pixels.Length != 4
                || report.expectedBackend != expectedBackend || report.expectedBuildGuid != expectedBuildGuid
                || report.environment == null || report.environment.scriptingBackend != expectedBackend
                || !string.Equals(report.environment.buildGuid, expectedBuildGuid, StringComparison.OrdinalIgnoreCase)
                || report.environment.isDevelopment)
            {
                throw new InvalidOperationException("EOF report does not prove all six cases for the expected fresh Release Player: " + path);
            }
            foreach (ReportCase result in report.cases)
            {
                if (result == null || !result.passed)
                {
                    throw new InvalidOperationException("EOF report contains a failed/missing case: " + path);
                }
            }
        }

        private static void CheckSmokeReport(string path)
        {
            SmokeReport report = JsonUtility.FromJson<SmokeReport>(File.ReadAllText(path));
            if (report == null || !report.passed || report.expectedCaseCount != 35
                || report.cases == null || report.cases.Length != 35)
            {
                throw new InvalidOperationException("Paired headless smoke did not pass all 35 cases: " + path);
            }
            foreach (ReportCase result in report.cases)
            {
                if (result == null || !result.passed)
                {
                    throw new InvalidOperationException("Paired smoke report contains a failed/missing case: " + path);
                }
            }
        }

        private static void CheckChannelReport(string path, string expectedBackend, string expectedBuildGuid)
        {
            ChannelReport report = JsonUtility.FromJson<ChannelReport>(File.ReadAllText(path));
            if (report == null || !report.completed || !string.IsNullOrEmpty(report.failure)
                || !report.counterCalibrated || report.counterCalibrationBytes < 65536 || report.counterEmptyBytes != 0
                || (report.counterKind != "PerThread" && report.counterKind != "HeapDelta")
                || report.cyclesPerWindow != 4096 || report.warmups != 3 || report.runs != 2 || report.samplesPerRun != 8
                || report.scenarios == null || report.scenarios.Length != 2 || report.environment == null
                || report.environment.isDevelopment || report.environment.scriptingBackend != expectedBackend
                || !string.Equals(report.environment.buildGuid, expectedBuildGuid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Channel report lacks calibrated work or the expected fresh Release identity: " + path);
            }
            for (int scenarioIndex = 0; scenarioIndex < report.scenarios.Length; scenarioIndex++)
            {
                ChannelScenario scenario = report.scenarios[scenarioIndex];
                int capacity = scenarioIndex == 0 ? 1 : 128;
                int count = 4096 * capacity;
                long checksum = (long)count * (count - 1) / 2;
                long primeChecksum = -(long)capacity * (capacity + 1) / 2;
                if (scenario == null || scenario.capacity != capacity || scenario.expectedItemsPerWindow != count
                    || scenario.expectedChecksum != checksum || scenario.expectedPrimingChecksum != primeChecksum
                    || scenario.warmedItems != (long)count * 3 || scenario.measuredItems != (long)count * 16
                    || scenario.warmedPrimingItems != capacity * 3 || scenario.measuredPrimingItems != capacity * 16
                    || !scenario.controlsPassed || !scenario.emptyValid || scenario.emptyBytes != 0
                    || !scenario.positiveValid || scenario.positiveBytes < 65536
                    || scenario.samples == null || scenario.samples.Length != 16)
                {
                    throw new InvalidOperationException("Channel scenario has missing work, priming or controls: " + path);
                }
                bool zeros = true;
                for (int index = 0; index < scenario.samples.Length; index++)
                {
                    ChannelSample sample = scenario.samples[index];
                    if (sample == null || sample.run != index / 8 || sample.index != index % 8
                        || !sample.allocationValid || sample.rawBytes < 0 || sample.writeCalls != count || sample.readCalls != count
                        || sample.successfulWrites != count || sample.successfulReads != count || sample.failedOperations != 0
                        || sample.checksum != checksum || sample.primingWrites != capacity || sample.primingReads != capacity
                        || sample.primingChecksum != primeChecksum)
                    {
                        throw new InvalidOperationException("Channel report contains an invalid raw window: " + path);
                    }
                    zeros &= sample.rawBytes == 0;
                }
                if (scenario.allRawSamplesZero != zeros
                    || scenario.zeroAllocationProven != (zeros && report.counterKind == "PerThread"))
                {
                    throw new InvalidOperationException("Channel report overstates its counter evidence: " + path);
                }
            }
        }

        [Serializable]
        private sealed class ChannelReport
        {
            public bool completed;
            public string failure;
            public string counterKind;
            public bool counterCalibrated;
            public long counterCalibrationBytes;
            public long counterEmptyBytes;
            public int cyclesPerWindow;
            public int warmups;
            public int runs;
            public int samplesPerRun;
            public ReportEnvironment environment;
            public ChannelScenario[] scenarios;
        }

        [Serializable]
        private sealed class ChannelScenario
        {
            public int capacity;
            public int expectedItemsPerWindow;
            public long expectedChecksum;
            public long expectedPrimingChecksum;
            public long warmedItems;
            public long measuredItems;
            public int warmedPrimingItems;
            public int measuredPrimingItems;
            public bool controlsPassed;
            public bool emptyValid;
            public long emptyBytes;
            public bool positiveValid;
            public long positiveBytes;
            public bool allRawSamplesZero;
            public bool zeroAllocationProven;
            public ChannelSample[] samples;
        }

        [Serializable]
        private sealed class ChannelSample
        {
            public int run;
            public int index;
            public long rawBytes;
            public bool allocationValid;
            public int writeCalls;
            public int readCalls;
            public int successfulWrites;
            public int successfulReads;
            public int failedOperations;
            public long checksum;
            public int primingWrites;
            public int primingReads;
            public long primingChecksum;
        }

        private static void CheckFiniteReport(string path, string expectedBackend, string expectedBuildGuid)
        {
            FiniteReport report = JsonUtility.FromJson<FiniteReport>(File.ReadAllText(path));
            if (report == null || !report.completed || !report.counterCalibrated
                || report.counterCalibrationBytes < 65536 || report.counterEmptyBytes != 0
                || report.itemsPerWindow != 4096 || report.warmups != 3 || report.runs != 2 || report.samplesPerRun != 8
                || report.primingItemsPerWindow != 1
                || report.scenarios == null || report.scenarios.Length != 2 || report.environment == null
                || report.environment.isDevelopment || report.environment.scriptingBackend != expectedBackend
                || !string.Equals(report.environment.buildGuid, expectedBuildGuid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Finite report has missing controls/work or the wrong fresh Release identity: " + path);
            }
            for (int scenarioIndex = 0; scenarioIndex < report.scenarios.Length; scenarioIndex++)
            {
                FiniteScenario scenario = report.scenarios[scenarioIndex];
                long checksum = scenarioIndex == 0 ? 8386560 : 33546240;
                string name = scenarioIndex == 0 ? "Range" : "Select/Where/Take";
                int primingValue = scenarioIndex == 0 ? -1 : -4;
                if (scenario == null || scenario.name != name || scenario.expectedChecksum != checksum
                    || scenario.expectedPrimingValue != primingValue
                    || scenario.sourceStart != (scenarioIndex == 0 ? -1 : -2)
                    || scenario.sourceCount != (scenarioIndex == 0 ? 4097 : 16386)
                    || scenario.takeCount != (scenarioIndex == 0 ? 0 : 4097)
                    || scenario.warmedPrimingItems != 3 || scenario.measuredPrimingItems != 16
                    || !scenario.controlsPassed || !scenario.emptyValid || scenario.emptyBytes != 0
                    || !scenario.positiveValid || scenario.positiveBytes < 65536
                    || scenario.warmedItems != 12288 || scenario.measuredItems != 65536
                    || scenario.samples == null || scenario.samples.Length != 16)
                {
                    throw new InvalidOperationException("Finite scenario is missing bounded work or bracket controls: " + path);
                }
                for (int index = 0; index < scenario.samples.Length; index++)
                {
                    FiniteSample sample = scenario.samples[index];
                    if (sample == null || sample.run != index / 8 || sample.index != index % 8
                        || !sample.allocationValid || sample.rawBytes < 0 || sample.moveCalls != 4097
                        || sample.successfulMoves != 4096 || sample.currentReads != 4096
                        || sample.endMoves != 1 || sample.checksum != checksum
                        || sample.primingMoves != 1 || sample.primingCurrentReads != 1 || sample.primingValue != primingValue)
                    {
                        throw new InvalidOperationException("Finite report contains an invalid/missing raw sample: " + path);
                    }
                }
            }
        }

        [Serializable]
        private sealed class FiniteReport
        {
            public bool completed;
            public bool counterCalibrated;
            public long counterCalibrationBytes;
            public long counterEmptyBytes;
            public int itemsPerWindow;
            public int warmups;
            public int runs;
            public int samplesPerRun;
            public int primingItemsPerWindow;
            public ReportEnvironment environment;
            public FiniteScenario[] scenarios;
        }

        [Serializable]
        private sealed class FiniteScenario
        {
            public string name;
            public long expectedChecksum;
            public int expectedPrimingValue;
            public int sourceStart;
            public int sourceCount;
            public int takeCount;
            public int warmedPrimingItems;
            public int measuredPrimingItems;
            public bool controlsPassed;
            public bool emptyValid;
            public long emptyBytes;
            public bool positiveValid;
            public long positiveBytes;
            public int warmedItems;
            public int measuredItems;
            public FiniteSample[] samples;
        }

        [Serializable]
        private sealed class FiniteSample
        {
            public int run;
            public int index;
            public bool allocationValid;
            public long rawBytes;
            public int moveCalls;
            public int successfulMoves;
            public int currentReads;
            public int endMoves;
            public long checksum;
            public int primingMoves;
            public int primingCurrentReads;
            public int primingValue;
        }

        [Serializable]
        private sealed class EndOfFrameReport
        {
            public bool passed;
            public int expectedCaseCount;
            public string expectedBackend;
            public string expectedBuildGuid;
            public ReportEnvironment environment;
            public ReportCase[] cases;
            public ReportPixel[] pixels;
        }

        [Serializable]
        private sealed class SmokeReport
        {
            public bool passed;
            public int expectedCaseCount;
            public ReportCase[] cases;
        }

        [Serializable]
        private sealed class ReportCase
        {
            public bool passed;
        }

        [Serializable]
        private sealed class ReportEnvironment
        {
            public string buildGuid;
            public string scriptingBackend;
            public bool isDevelopment;
        }

        [Serializable]
        private sealed class ReportPixel
        {
            public int completedFrame;
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

        private static string ForwardOptionalArgument(string argumentName)
        {
            string value = GetArgumentValue(argumentName);
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            if (value.IndexOf('"') >= 0)
            {
                throw new ArgumentException("Invalid forwarded argument value: " + argumentName);
            }

            return " " + argumentName + " \"" + value + "\"";
        }

        /// <summary>
        /// Validates <c>-onityTaskBenchmarkRetention</c> and returns the text to append to the Player arguments.
        /// Absent returns empty. A trailing flag with no value, a value other than default or matched, or use
        /// with a suite other than primary or builderlifecycle throws, so a launch can never silently run at the
        /// default.
        /// </summary>
        private static string ForwardRetentionArgument(string suite)
        {
            if (!HasArgument(k_retentionArgument))
            {
                return string.Empty;
            }

            string value = GetArgumentValue(k_retentionArgument);
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(k_retentionArgument + " requires a value: default or matched.");
            }
            if (!string.Equals(value, "default", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "matched", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(k_retentionArgument + " must be default or matched, got '" + value + "'.");
            }
            if (suite != "builderlifecycle" && suite != "primary")
            {
                throw new ArgumentException(k_retentionArgument + " applies to the primary and builderlifecycle suites only.");
            }

            return " " + k_retentionArgument + " " + value.ToLowerInvariant();
        }

        /// <summary>
        /// Forwards <c>-onityTaskBenchmarkSelfTest</c> (tiny harness self-test samples, report marked selfTest) for
        /// the suites that honour it; any other suite throws so an unmarked report can never pass as a self-test.
        /// </summary>
        private static string ForwardSelfTestArgument(string suite)
        {
            if (!HasArgument(k_selfTestArgument))
            {
                return string.Empty;
            }
            if (suite != "primary" && suite != "builderlifecycle" && suite != "throughput")
            {
                throw new ArgumentException(k_selfTestArgument + " applies to the primary, builderlifecycle and throughput suites only.");
            }

            return " " + k_selfTestArgument;
        }

        private static void WriteBuildReport(string path, BuildReport report, string backend, BuildTargetGroup targetGroup)
        {
            BuildReportFile file = new BuildReportFile
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                backend = backend,
                buildGuid = report.summary.guid.ToString(),
                result = report.summary.result.ToString(),
                outputPath = report.summary.outputPath,
                totalSizeBytes = (long)report.summary.totalSize,
                totalTimeSeconds = report.summary.totalTime.TotalSeconds,
                totalErrors = report.summary.totalErrors,
                totalWarnings = report.summary.totalWarnings,
                options = report.summary.options.ToString(),
                scriptingDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup),
                il2CppCompilerConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(targetGroup).ToString(),
                managedStrippingLevel = PlayerSettings.GetManagedStrippingLevel(targetGroup).ToString()
            };
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
        }

        [Serializable]
        private sealed class BuildReportFile
        {
            public string generatedAtUtc;
            public string unityVersion;
            public string backend;
            public string buildGuid;
            public string result;
            public string outputPath;
            public long totalSizeBytes;
            public double totalTimeSeconds;
            public int totalErrors;
            public int totalWarnings;
            public string options;
            public string scriptingDefines;
            public string il2CppCompilerConfiguration;
            public string managedStrippingLevel;
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
