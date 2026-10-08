using System;

namespace Onity.Benchmarks
{
    /// <summary>
    /// The <c>&lt;exe&gt;.build.json</c> sidecar that
    /// <c>Onity.Editor.Benchmarks.OnityMessagingBenchmarkPlayerBuildRunner</c> writes next to each messaging
    /// comparison Player. The Player copies it into its report, so a report names the exact build it came
    /// from, including the MessagePipe flavor and the SHA-256 of the MessagePipe package it was built from.
    /// Fields are public for <see cref="UnityEngine.JsonUtility"/>.
    /// </summary>
    [Serializable]
    public sealed class OnityMessagingBenchmarkBuildMetadata
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

        /// <summary>
        /// MessagePipe flavor built into the Player: <c>nuget-netstandard2.0</c> (the precompiled NuGet dll) or
        /// <c>unity-package</c> (the Unity package's sources compiled against UniTask).
        /// </summary>
        public string messagePipeFlavor;

        /// <summary>
        /// MessagePipe version: the informational version of the loaded assembly for the NuGet flavor, the
        /// version in the Unity package's <c>package.json</c> for the Unity package flavor.
        /// </summary>
        public string messagePipeVersion;

        /// <summary>Assembly version of the MessagePipe assembly the Editor loaded.</summary>
        public string messagePipeAssemblyVersion;

        /// <summary>Project path of what MessagePipe was compiled from: the dll, or the package's assembly definition.</summary>
        public string messagePipeAssemblyPath;

        /// <summary>Project path of the MessagePipe.dll plugin that was built into the Player (NuGet flavor; empty otherwise).</summary>
        public string messagePipeDllPath;

        /// <summary>SHA-256 (lower-case hex) of that MessagePipe.dll (NuGet flavor; empty otherwise).</summary>
        public string messagePipeDllSha256;

        /// <summary>The MessagePipe package the build came from: the dll (NuGet flavor) or the <c>.unitypackage</c> file.</summary>
        public string messagePipePackagePath;

        /// <summary>SHA-256 (lower-case hex) of that package: the dll for the NuGet flavor, the <c>.unitypackage</c> otherwise.</summary>
        public string messagePipePackageSha256;

        /// <summary>
        /// Unity package flavor: the number of package entries found in the project byte-identical with their
        /// GUIDs (the build refuses any difference or extra file). Zero for the NuGet flavor.
        /// </summary>
        public int messagePipePackageEntries;

        /// <summary>Version of the registered UniTask package the Unity package flavor compiles against.</summary>
        public string uniTaskVersion;

        /// <summary>Package id of that UniTask package.</summary>
        public string uniTaskPackageId;

        /// <summary>
        /// SHA-256 over the sorted lines <c>relative/path&lt;TAB&gt;sha256&lt;LF&gt;</c> of the UniTask package's
        /// non-meta files.
        /// </summary>
        public string uniTaskTreeSha256;

        /// <summary>Where the MessagePipe assembly comes from.</summary>
        public string messagePipeSource;

        /// <summary>How the benchmark creates the MessagePipe brokers.</summary>
        public string messagePipeSetup;

        /// <summary>The async API of the measured MessagePipe build: ValueTask (NuGet) or UniTask (Unity package).</summary>
        public string messagePipeAsyncApi;

        /// <summary>Source commit the staged package came from, or Unknown.</summary>
        public string sourceHead;

        /// <summary>Full path of the built Player executable.</summary>
        public string buildPath;
    }
}
