using System;
using System.Globalization;
using System.IO;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Onity.Benchmarks
{
    /// <summary>
    /// Player-side entry point of the reactive comparison (Onity.Reactive vs R3 vs UniRx). A Player started
    /// with <c>-onityRunReactiveBenchmark</c> runs every scenario (or the one named by
    /// <c>-onityReactiveBenchmarkScenario</c>), writes the JSON report to
    /// <c>-onityReactiveBenchmarkOutput</c> and quits with exit code 0 when every golden check passed, or 1
    /// otherwise. <c>-onityReactiveBenchmarkSelfTest</c> runs tiny counts and marks the report as a self-test;
    /// <c>-onityReactiveBenchmarkSamples</c> overrides the measured sample count;
    /// <c>-onityReactiveBenchmarkBuildMetadata</c> names the build sidecar (default: the
    /// <c>&lt;exe&gt;.build.json</c> next to the Player).
    /// </summary>
    public static class OnityReactiveBenchmarkPlayerRunner
    {
        /// <summary>Starts the benchmark when present on the Player command line.</summary>
        public const string RunArgument = "-onityRunReactiveBenchmark";

        /// <summary>Path of the JSON report.</summary>
        public const string OutputArgument = "-onityReactiveBenchmarkOutput";

        /// <summary>Marks a harness self-test: tiny counts, never performance evidence.</summary>
        public const string SelfTestArgument = "-onityReactiveBenchmarkSelfTest";

        /// <summary>Measured samples per library and scenario (default 15, self-test 3).</summary>
        public const string SamplesArgument = "-onityReactiveBenchmarkSamples";

        /// <summary>Runs only the scenario with this id.</summary>
        public const string ScenarioArgument = "-onityReactiveBenchmarkScenario";

        /// <summary>Path of the build sidecar written by the build runner.</summary>
        public const string BuildMetadataArgument = "-onityReactiveBenchmarkBuildMetadata";

        /// <summary>R3 version the harness is written against; reports also record the loaded assembly.</summary>
        public const string R3Version = "1.3.0";

        /// <summary>UniRx version the harness is written against; a source import carries no version.</summary>
        public const string UniRxVersion = "7.1.0";

        private static bool s_isRunning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            s_isRunning = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (s_isRunning || !OnityReactiveBenchmarkOptions.HasArgument(args, RunArgument))
            {
                return;
            }

            s_isRunning = true;
            Application.runInBackground = true;
            Application.targetFrameRate = -1;
            int exitCode = Execute(args);
            Application.Quit(exitCode);
        }

        private static int Execute(string[] args)
        {
            OnityReactiveBenchmarkReport report = new OnityReactiveBenchmarkReport();
            string outputPath = null;
            try
            {
                outputPath = OnityReactiveBenchmarkOptions.ResolveOutputPath(args);
                report.selfTest = OnityReactiveBenchmarkOptions.HasArgument(args, SelfTestArgument);
                OnityReactiveBenchmarkOptions options = OnityReactiveBenchmarkOptions.Parse(args);
                Debug.Log("Onity reactive benchmark started" + (options.SelfTest ? " (self-test)" : string.Empty)
                    + ", report " + outputPath);
                OnityReactiveBenchmarkRunner.Run(options, report);
            }
            catch (Exception exception)
            {
                report.failure = exception.ToString();
                Debug.LogException(exception);
            }

            report.generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            bool succeeded = report.completed && report.goldenPassed && string.IsNullOrEmpty(report.failure);
            if (outputPath == null)
            {
                Debug.LogError("Onity reactive benchmark: no report path; nothing was written.");
                return 1;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return 1;
            }

            Debug.Log("Onity reactive benchmark " + (succeeded ? "completed" : "FAILED") + ": " + outputPath);
            return succeeded ? 0 : 1;
        }
    }
}
