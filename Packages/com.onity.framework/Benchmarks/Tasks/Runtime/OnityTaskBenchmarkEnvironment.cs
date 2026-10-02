using System;
using System.IO;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Benchmarks
{
    [Serializable]
    internal sealed class OnityTaskBenchmarkEnvironment
    {
        public string buildGuid;
        public bool isDevelopment;
        public string scriptingBackend;
        public bool trackerEnabled;
        public bool trackerStackTraceEnabled;
        public bool flowExecutionContext;
        public int runnerPoolCapacity;
        public string executionContextPath;
        public string executionContextEvidence;
        public OnityTaskBenchmarkBuildMetadata build;

        internal static OnityTaskBenchmarkEnvironment Capture()
        {
            OnityTaskBenchmarkEnvironment environment = new OnityTaskBenchmarkEnvironment
            {
                buildGuid = Application.buildGUID,
                isDevelopment = Debug.isDebugBuild,
                trackerEnabled = OnityTaskTracker.IsEnabled,
                trackerStackTraceEnabled = OnityTaskTracker.EnableStackTrace,
                flowExecutionContext = OnityTask.FlowExecutionContext,
                runnerPoolCapacity = OnityTask.RunnerPoolCapacity,
#if ENABLE_IL2CPP
                scriptingBackend = "IL2CPP",
#else
                scriptingBackend = "Mono",
#endif
                executionContextPath = "Unknown",
                build = new OnityTaskBenchmarkBuildMetadata()
            };

            // The internal probe never throws; a runtime without it reports Unknown with the reason.
            OnityTaskBenchmarkInternals.CaptureExecutionContextPath(
                out environment.executionContextPath, out environment.executionContextEvidence);

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-onityTaskBenchmarkBuildMetadata", StringComparison.OrdinalIgnoreCase))
                {
                    environment.build = JsonUtility.FromJson<OnityTaskBenchmarkBuildMetadata>(File.ReadAllText(args[i + 1]));
                    break;
                }
            }

            return environment;
        }
    }

    [Serializable]
    internal sealed class OnityTaskBenchmarkBuildMetadata
    {
        public string generatedAtUtc = "Unknown (no build sidecar)";
        public string codeOptimization = "Unknown (no build sidecar)";
        public string managedCompilerOptimization = "Unknown (no build sidecar)";
        public string il2CppCompilerConfiguration = "Unknown (no build sidecar)";
        public string managedStrippingLevel = "Unknown (no build sidecar)";
        public string onityPackageId = "Unknown (no build sidecar)";
        public string onityVersion = "Unknown (no build sidecar)";
        public string uniTaskPackageId = "Unknown (no build sidecar)";
        public string uniTaskVersion = "Unknown (no build sidecar)";
    }
}
