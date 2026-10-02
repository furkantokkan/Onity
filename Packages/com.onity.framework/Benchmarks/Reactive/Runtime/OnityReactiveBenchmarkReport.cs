using System;

namespace Onity.Benchmarks
{
    // Report DTOs written with JsonUtility, so the fields are public and lower camel case like the other
    // benchmark reports. tools/benchmark-host/reactive-summary.py reads this schema.

    [Serializable]
    internal sealed class OnityReactiveBenchmarkReport
    {
        public int schemaVersion = 1;
        public string benchmark = "onity-reactive-comparison";
        public bool completed;
        public string failure = string.Empty;
        public bool selfTest;
        public bool goldenPassed;
        public string generatedAtUtc;
        public OnityReactiveBenchmarkEnvironment environment;
        public bool buildMetadataFound;
        public bool buildMetadataMatchesPlayer;
        public string buildMetadataPath;
        public OnityReactiveBenchmarkBuildMetadata build;
        public OnityReactiveBenchmarkLibraryInfo[] libraries;
        public OnityReactiveBenchmarkSettings settings;
        public OnityReactiveBenchmarkAllocationInfo allocation;
        public OnityReactiveBenchmarkScenarioResult[] scenarios;
    }

    [Serializable]
    internal sealed class OnityReactiveBenchmarkEnvironment
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
    internal sealed class OnityReactiveBenchmarkLibraryInfo
    {
        public string name;
        public string version;
        public string runtimeAssembly;
        public string source;
    }

    [Serializable]
    internal sealed class OnityReactiveBenchmarkSettings
    {
        public int warmupSamples;
        public int measuredSamples;
        public string scenarioFilter;
        public string order;
        public string timing;
    }

    [Serializable]
    internal sealed class OnityReactiveBenchmarkAllocationInfo
    {
        public string counter;
        public bool available;
        public long positiveControlBytes;
        public long emptyControlBytes;
        public string detail;
    }

    [Serializable]
    internal sealed class OnityReactiveBenchmarkScenarioResult
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
        public double medianRatioOnityToR3;
        public double medianRatioOnityToUniRx;
        public OnityReactiveBenchmarkLibraryResult[] libraries;
    }

    [Serializable]
    internal sealed class OnityReactiveBenchmarkLibraryResult
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
