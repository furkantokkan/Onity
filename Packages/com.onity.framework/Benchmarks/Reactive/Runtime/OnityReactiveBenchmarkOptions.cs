using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Command-line options of the reactive comparison Player. Invalid values throw, so a run is never
    /// silently mislabelled.
    /// </summary>
    internal sealed class OnityReactiveBenchmarkOptions
    {
        internal const int DefaultSamples = 15;
        internal const int SelfTestSamples = 3;
        internal const int MaxSamples = 1000;

        private OnityReactiveBenchmarkOptions()
        {
        }

        /// <summary>Full path of the JSON report.</summary>
        internal string OutputPath { get; private set; }

        /// <summary>Full path of the build sidecar to embed, or null when none was found.</summary>
        internal string BuildMetadataPath { get; private set; }

        /// <summary>True for a harness self-test (tiny counts, never performance evidence).</summary>
        internal bool SelfTest { get; private set; }

        /// <summary>Measured samples per library and scenario.</summary>
        internal int Samples { get; private set; }

        /// <summary>The scenario id passed on the command line, or empty for all scenarios.</summary>
        internal string ScenarioFilter { get; private set; }

        /// <summary>Scenarios to run, in table order.</summary>
        internal OnityReactiveBenchmarkScenario[] Scenarios { get; private set; }

        /// <summary>Reads the options from <paramref name="args"/>.</summary>
        internal static OnityReactiveBenchmarkOptions Parse(string[] args)
        {
            OnityReactiveBenchmarkOptions options = new OnityReactiveBenchmarkOptions
            {
                SelfTest = HasArgument(args, OnityReactiveBenchmarkPlayerRunner.SelfTestArgument)
            };

            options.OutputPath = ResolveOutputPath(args);
            options.Samples = options.SelfTest ? SelfTestSamples : DefaultSamples;
            string samples = GetArgumentValue(args, OnityReactiveBenchmarkPlayerRunner.SamplesArgument);
            if (HasArgument(args, OnityReactiveBenchmarkPlayerRunner.SamplesArgument))
            {
                if (!int.TryParse(samples, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    || parsed < 1 || parsed > MaxSamples)
                {
                    throw new ArgumentException(OnityReactiveBenchmarkPlayerRunner.SamplesArgument
                        + " must be an integer from 1 to " + MaxSamples + ", got '" + samples + "'.");
                }

                options.Samples = parsed;
            }

            options.ScenarioFilter = string.Empty;
            options.Scenarios = OnityReactiveBenchmarkScenarios.All;
            if (HasArgument(args, OnityReactiveBenchmarkPlayerRunner.ScenarioArgument))
            {
                string id = GetArgumentValue(args, OnityReactiveBenchmarkPlayerRunner.ScenarioArgument);
                OnityReactiveBenchmarkScenario scenario = OnityReactiveBenchmarkScenarios.Find(id);
                if (scenario == null)
                {
                    throw new ArgumentException(OnityReactiveBenchmarkPlayerRunner.ScenarioArgument
                        + " names an unknown scenario: '" + id + "'.");
                }

                options.ScenarioFilter = scenario.Id;
                options.Scenarios = new[] { scenario };
            }

            string metadata = GetArgumentValue(args, OnityReactiveBenchmarkPlayerRunner.BuildMetadataArgument);
            if (string.IsNullOrEmpty(metadata))
            {
                // Default: <exe>.build.json next to the Player; the data folder is <exe name>_Data.
                string dataPath = Application.dataPath;
                if (!string.IsNullOrEmpty(dataPath) && dataPath.EndsWith("_Data", StringComparison.Ordinal))
                {
                    metadata = dataPath.Substring(0, dataPath.Length - "_Data".Length) + ".exe.build.json";
                }
            }

            options.BuildMetadataPath = string.IsNullOrEmpty(metadata) ? null : Path.GetFullPath(metadata);
            return options;
        }

        /// <summary>
        /// Full path of the report: <c>-onityReactiveBenchmarkOutput</c>, or a file in the Player's
        /// persistent data folder. Resolved before the other options so a rejected run still reports.
        /// </summary>
        internal static string ResolveOutputPath(string[] args)
        {
            string output = GetArgumentValue(args, OnityReactiveBenchmarkPlayerRunner.OutputArgument);
            return Path.GetFullPath(string.IsNullOrEmpty(output)
                ? Path.Combine(Application.persistentDataPath, "onity-reactive-benchmark-latest.json")
                : output);
        }

        /// <summary>The operation count of <paramref name="scenario"/> for this run.</summary>
        internal int OperationsFor(OnityReactiveBenchmarkScenario scenario)
        {
            return SelfTest ? scenario.SelfTestOperations : scenario.Operations;
        }

        internal static bool HasArgument(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static string GetArgumentValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
