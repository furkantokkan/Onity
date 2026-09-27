using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
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

            // Initialize the runtime probe before any smoke case suppresses flow. Read the fields
            // actually used by Capture/TryRunPreservingContext; an unused getter may be stripped.
            Type contextType = typeof(OnityTask).Assembly.GetType("Onity.Unity.Async.OnityAsyncExecutionContext", true);
            RuntimeHelpers.RunClassConstructor(contextType.TypeHandle);
            FieldInfo capture = contextType.GetField("s_fastCapture", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo run = contextType.GetField("s_runPreservingContext", BindingFlags.Static | BindingFlags.NonPublic);
            if (capture != null && run != null)
            {
                bool hasCapture = capture.GetValue(null) != null;
                bool hasRun = run.GetValue(null) != null;
                environment.executionContextPath = hasCapture && hasRun ? "InternalPair" : "PublicFallback";
                environment.executionContextEvidence = "s_fastCapture=" + hasCapture
                    + "; s_runPreservingContext=" + hasRun + "; runtime probe initialized before suppression";
            }
            else
            {
                environment.executionContextEvidence = "Runtime capture/run fields could not be inspected.";
            }

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
