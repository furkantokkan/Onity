using System;
using System.IO;
using System.Reflection;
using Onity.Benchmarks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Editor.Benchmarks
{
    /// <summary>
    /// Builds the reactive comparison Player: Windows x64, Release (no Development flag), Mono or IL2CPP.
    /// A temporary empty scene is built and removed afterwards; the scripting backend and the active build
    /// target are restored. Writes the <c>&lt;exe&gt;.build.json</c> sidecar
    /// (<see cref="OnityReactiveBenchmarkBuildMetadata"/>) that the Player embeds in its report.
    /// </summary>
    public static class OnityReactiveBenchmarkPlayerBuildRunner
    {
        private const string k_backendArgument = "-onityReactiveBenchmarkBackend";
        private const string k_buildPathArgument = "-onityReactiveBenchmarkBuildPath";
        private const string k_sourceHeadArgument = "-onityReactiveBenchmarkSourceHead";
        private const string k_benchmarkDefine = "ONITY_REACTIVE_BENCHMARKS";
        private const string k_benchmarkScenePath = "Assets/OnityBenchmarkTemp/OnityReactiveBenchmarkPlayer.unity";
        private const string k_onityPackageName = "com.onity.framework";
        private const string k_r3AssemblyName = "R3";
        private const string k_unknown = "Unknown";

        /// <summary>
        /// Command-line entry point. Requires <c>-onityReactiveBenchmarkBackend Mono|IL2CPP</c> and
        /// <c>-onityReactiveBenchmarkBuildPath &lt;exe&gt;</c>; <c>-onityReactiveBenchmarkSourceHead &lt;sha&gt;</c>
        /// is recorded in the sidecar when given. Throws (non-zero Editor exit) on any failure.
        /// </summary>
        public static void BuildPlayerFromCommandLine()
        {
            ScriptingImplementation backend = ReadBackend();
            string buildPath = GetArgumentValue(k_buildPathArgument);
            if (string.IsNullOrEmpty(buildPath))
            {
                throw new ArgumentException(k_buildPathArgument + " <exe> is required.");
            }

            string sourceHead = GetArgumentValue(k_sourceHeadArgument);
            Build(backend, Path.GetFullPath(buildPath), string.IsNullOrEmpty(sourceHead) ? k_unknown : sourceHead);
        }

        private static ScriptingImplementation ReadBackend()
        {
            string backend = GetArgumentValue(k_backendArgument);
            if (string.Equals(backend, "Mono", StringComparison.OrdinalIgnoreCase))
            {
                return ScriptingImplementation.Mono2x;
            }

            if (string.Equals(backend, "IL2CPP", StringComparison.OrdinalIgnoreCase))
            {
                return ScriptingImplementation.IL2CPP;
            }

            throw new ArgumentException(k_backendArgument + " must be Mono or IL2CPP, got '" + backend + "'.");
        }

        private static void Build(ScriptingImplementation backend, string buildPath, string sourceHead)
        {
            BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;
            BuildTarget target = BuildTarget.StandaloneWindows64;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
            if (!HasDefine(defines, k_benchmarkDefine))
            {
                throw new InvalidOperationException("The Standalone scripting defines lack " + k_benchmarkDefine
                    + "; the Player would not contain the benchmark. Defines: " + defines);
            }

            string backendLabel = backend == ScriptingImplementation.IL2CPP ? "IL2CPP" : "Mono";
            BuildTarget originalTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup originalTargetGroup = BuildPipeline.GetBuildTargetGroup(originalTarget);
            ScriptingImplementation originalBackend = PlayerSettings.GetScriptingBackend(targetGroup);
            string benchmarkScene = null;
            bool benchmarkFolderCreated = false;
            Directory.CreateDirectory(Path.GetDirectoryName(buildPath));

            try
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target))
                {
                    throw new InvalidOperationException("Failed to switch the active build target to StandaloneWindows64.");
                }

                PlayerSettings.SetScriptingBackend(targetGroup, backend);
                benchmarkScene = CreateBenchmarkScene(out benchmarkFolderCreated);
                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = new[] { benchmarkScene },
                    locationPathName = buildPath,
                    target = target,
                    targetGroup = targetGroup,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(backendLabel + " reactive benchmark Player build failed: "
                        + report.summary.result + ". See the Editor log.");
                }

                WriteBuildMetadata(buildPath + ".build.json", report, backendLabel, targetGroup, sourceHead, buildPath);
                Debug.Log("Onity reactive benchmark " + backendLabel + " Player built: " + buildPath
                    + " (GUID " + report.summary.guid + ").");
            }
            finally
            {
                try
                {
                    DeleteBenchmarkScene(benchmarkScene, benchmarkFolderCreated);
                }
                finally
                {
                    try
                    {
                        PlayerSettings.SetScriptingBackend(targetGroup, originalBackend);
                    }
                    finally
                    {
                        if (originalTarget != target
                            && !EditorUserBuildSettings.SwitchActiveBuildTarget(originalTargetGroup, originalTarget))
                        {
                            Debug.LogError("Failed to restore the active build target to " + originalTarget + ".");
                        }
                    }
                }
            }
        }

        private static void WriteBuildMetadata(
            string path,
            BuildReport report,
            string backendLabel,
            BuildTargetGroup targetGroup,
            string sourceHead,
            string buildPath)
        {
            OnityReactiveBenchmarkBuildMetadata metadata = new OnityReactiveBenchmarkBuildMetadata
            {
                backend = backendLabel,
                unityVersion = Application.unityVersion,
                buildGuid = report.summary.guid.ToString(),
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                buildOptions = report.summary.options.ToString(),
                development = (report.summary.options & BuildOptions.Development) != 0,
                scriptingDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup),
                il2CppCompilerConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(targetGroup).ToString(),
                managedStrippingLevel = PlayerSettings.GetManagedStrippingLevel(targetGroup).ToString(),
                onityPackageId = k_unknown,
                onityVersion = k_unknown,
                uniRxVersion = OnityReactiveBenchmarkPlayerRunner.UniRxVersion,
                sourceHead = sourceHead,
                buildPath = buildPath
            };

            UnityEditor.PackageManager.PackageInfo[] packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            for (int i = 0; i < packages.Length; i++)
            {
                if (packages[i].name == k_onityPackageName)
                {
                    metadata.onityPackageId = packages[i].packageId;
                    metadata.onityVersion = packages[i].version;
                }
            }

            ReadR3Version(out metadata.r3Version, out metadata.r3AssemblyVersion);
            File.WriteAllText(path, JsonUtility.ToJson(metadata, true));
        }

        private static void ReadR3Version(out string informationalVersion, out string assemblyVersion)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                AssemblyName name = assemblies[i].GetName();
                if (name.Name != k_r3AssemblyName)
                {
                    continue;
                }

                assemblyVersion = name.Version.ToString();
                AssemblyInformationalVersionAttribute attribute =
                    assemblies[i].GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                informationalVersion = attribute != null ? attribute.InformationalVersion : assemblyVersion;
                return;
            }

            informationalVersion = k_unknown + " (R3 is not loaded in the Editor)";
            assemblyVersion = k_unknown;
        }

        private static string CreateBenchmarkScene(out bool benchmarkFolderCreated)
        {
            string benchmarkFolder = Path.GetDirectoryName(k_benchmarkScenePath);
            benchmarkFolderCreated = !Directory.Exists(benchmarkFolder);
            Directory.CreateDirectory(benchmarkFolder);

            // The Player starts the benchmark from RuntimeInitializeOnLoadMethod; the scene only gives it
            // something to load.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(k_benchmarkScenePath);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                EditorSceneManager.CloseScene(scene, true);
                throw new InvalidOperationException("Failed to save the benchmark scene at '" + scenePath + "'.");
            }

            AssetDatabase.Refresh();
            return scenePath;
        }

        private static void DeleteBenchmarkScene(string benchmarkScenePath, bool benchmarkFolderCreated)
        {
            if (!string.IsNullOrEmpty(benchmarkScenePath))
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

        private static bool HasDefine(string defines, string define)
        {
            string[] parts = (defines ?? string.Empty).Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Trim(), define, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
    }
}
