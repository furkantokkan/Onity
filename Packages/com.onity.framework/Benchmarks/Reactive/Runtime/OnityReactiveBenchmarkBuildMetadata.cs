using System;

namespace Onity.Benchmarks
{
    /// <summary>
    /// The <c>&lt;exe&gt;.build.json</c> sidecar that
    /// <c>Onity.Editor.Benchmarks.OnityReactiveBenchmarkPlayerBuildRunner</c> writes next to each reactive
    /// comparison Player. The Player copies it into its report, so a report names the exact build it came
    /// from. Fields are public for <see cref="UnityEngine.JsonUtility"/>.
    /// </summary>
    [Serializable]
    public sealed class OnityReactiveBenchmarkBuildMetadata
    {
        /// <summary>Sidecar schema version.</summary>
        public int schemaVersion = 1;

        /// <summary>Scripting backend of the build: Mono or IL2CPP.</summary>
        public string backend;

        /// <summary>Unity Editor version that built the Player.</summary>
        public string unityVersion;

        /// <summary>Build GUID reported by the build pipeline.</summary>
        public string buildGuid;

        /// <summary>UTC time the build finished (ISO 8601).</summary>
        public string generatedAtUtc;

        /// <summary>Build options of the build (None for the Release Player).</summary>
        public string buildOptions;

        /// <summary>True when the Player was built with the Development flag.</summary>
        public bool development;

        /// <summary>Standalone scripting define symbols in force for the build.</summary>
        public string scriptingDefines;

        /// <summary>IL2CPP compiler configuration setting (Release, Debug or Master).</summary>
        public string il2CppCompilerConfiguration;

        /// <summary>Managed stripping level of the Standalone target.</summary>
        public string managedStrippingLevel;

        /// <summary>Package id of the Onity package that was compiled into the Player.</summary>
        public string onityPackageId;

        /// <summary>Version of the Onity package that was compiled into the Player.</summary>
        public string onityVersion;

        /// <summary>Informational version of the R3 assembly the Editor loaded.</summary>
        public string r3Version;

        /// <summary>Assembly version of the R3 assembly the Editor loaded.</summary>
        public string r3AssemblyVersion;

        /// <summary>UniRx version the host imported (source import; the code carries no version).</summary>
        public string uniRxVersion;

        /// <summary>Source commit the staged package came from, or Unknown.</summary>
        public string sourceHead;

        /// <summary>Full path of the built Player executable.</summary>
        public string buildPath;
    }
}
