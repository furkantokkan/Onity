using System;

namespace Onity.Benchmarks
{
    // Report DTOs written with JsonUtility, so the fields are public and lower camel case like the other
    // benchmark reports. tools/benchmark-host/messaging-summary.py reads this schema.

    [Serializable]
    internal sealed class OnityMessagingBenchmarkReport
    {
        public int schemaVersion = 1;
        public string benchmark = "onity-messaging-comparison";
        public bool completed;
        public string failure = string.Empty;
        public bool selfTest;
        public bool goldenPassed;
        public string generatedAtUtc;
        public OnityMessagingBenchmarkEnvironment environment;
        public bool buildMetadataFound;
        public bool buildMetadataMatchesPlayer;
        public string buildMetadataPath;
        public OnityMessagingBenchmarkBuildMetadata build;
        public string messagePipeSetup;
        public string messagePipeFlavor;
        public OnityMessagingBenchmarkLibraryInfo[] libraries;
        public OnityMessagingBenchmarkSettings settings;
        public OnityMessagingBenchmarkAllocationInfo allocation;
        public OnityMessagingBenchmarkScenarioResult[] scenarios;
    }

    [Serializable]
    internal sealed class OnityMessagingBenchmarkEnvironment
    {
        public string unityVersion;
        public string scriptingBackend;
        public bool isDevelopment;
        public bool isEditor;
        public string platform;
        public string operatingSystem;
        public string processorType;
        public int processorCount;
        public int processorFrequencyMHz;
        public string buildGuid;
        public string utcTime;
        public string commandLine;
        public bool selfTest;
        public long stopwatchFrequency;
        public bool stopwatchIsHighResolution;
        public string gcMode;
        public bool gcIncremental;
    }

    [Serializable]
    internal sealed class OnityMessagingBenchmarkLibraryInfo
    {
        public string name;
        public string version;
        public string runtimeAssembly;
        public string source;
    }

    [Serializable]
    internal sealed class OnityMessagingBenchmarkSettings
    {
        public int warmupSamples;
        public int measuredSamples;
        public string scenarioFilter;
        public string order;
        public string timing;
    }

    [Serializable]
    internal sealed class OnityMessagingBenchmarkAllocationInfo
    {
        public string counter;
        public bool available;
        public long positiveControlBytes;
        public long emptyControlBytes;
        public string detail;
    }

    [Serializable]
    internal sealed class OnityMessagingBenchmarkScenarioResult
    {
        public string id;
        public string description;
        public int operationsPerSample;
        public long expectedChecksum;
        public long expectedNotifications;
        public long expectedProbeChecksum;
        public long expectedProbeNotifications;
        public bool goldenPassed;
        public string failure = string.Empty;
        public double medianRatioOnityToMessagePipe;
        public OnityMessagingBenchmarkLibraryResult[] libraries;
    }

    [Serializable]
    internal sealed class OnityMessagingBenchmarkLibraryResult
    {
        public string library;
        public double[] samplesNsPerOp;
        public long[] sampleTicks;
        public int[] sampleOrderPositions;
        public int gcCollectionsDuringSamples;
        public double medianNsPerOp;
        public double meanNsPerOp;
        public double minNsPerOp;
        public double maxNsPerOp;
        public long checksum;
        public long notifications;
        public long probeChecksum;
        public long probeNotifications;
        public int checkedRuns;
        public int mismatchedRuns;
        public string firstMismatch = string.Empty;
        public bool allocationMeasured;
        public long allocatedBytes;
        public double allocatedBytesPerOp;
    }
}
