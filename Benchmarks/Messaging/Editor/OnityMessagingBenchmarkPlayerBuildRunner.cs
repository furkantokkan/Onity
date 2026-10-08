using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Onity.Benchmarks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Editor.Benchmarks
{
    /// <summary>
    /// Builds the messaging comparison Player: Windows x64, Release (no Development flag), Mono or IL2CPP.
    /// A temporary empty scene is built and removed afterwards; the scripting backend and the active build
    /// target are restored. Writes the <c>&lt;exe&gt;.build.json</c> sidecar
    /// (<see cref="OnityMessagingBenchmarkBuildMetadata"/>) that the Player embeds in its report, including the
    /// MessagePipe flavor and version and the SHA-256 of the MessagePipe package that was built in: the
    /// MessagePipe.dll for the NuGet flavor, the <c>.unitypackage</c> for the Unity package flavor.
    /// </summary>
    public static class OnityMessagingBenchmarkPlayerBuildRunner
    {
        private const string k_backendArgument = "-onityMessagingBenchmarkBackend";
        private const string k_buildPathArgument = "-onityMessagingBenchmarkBuildPath";
        private const string k_sourceHeadArgument = "-onityMessagingBenchmarkSourceHead";
        private const string k_benchmarkDefine = "ONITY_MESSAGING_BENCHMARKS";
        private const string k_benchmarkScenePath = "Assets/OnityBenchmarkTemp/OnityMessagingBenchmarkPlayer.unity";
        private const string k_onityPackageName = "com.onity.framework";
        private const string k_messagePipeAssemblyName = "MessagePipe";
        private const string k_messagePipeFileName = "MessagePipe.dll";
        private const string k_unknown = "Unknown";
#if ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK
        private const string k_messagePipePackageArgument = "-onityMessagingBenchmarkMessagePipePackage";
        private const string k_uniTaskPackageName = "com.cysharp.unitask";
        private const string k_uniTaskAssemblyName = "UniTask";
        private const int k_tarBlockSize = 512;
#else
        private const string k_notUsed = "not used (MessagePipe NuGet build)";
#endif

        /// <summary>
        /// Command-line entry point. Requires <c>-onityMessagingBenchmarkBackend Mono|IL2CPP</c> and
        /// <c>-onityMessagingBenchmarkBuildPath &lt;exe&gt;</c>; <c>-onityMessagingBenchmarkSourceHead &lt;sha&gt;</c>
        /// is recorded in the sidecar when given. The Unity package flavor (host define
        /// <c>ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK</c>) also requires
        /// <c>-onityMessagingBenchmarkMessagePipePackage &lt;.unitypackage&gt;</c>, the package the project's
        /// MessagePipe sources were extracted from. Throws (non-zero Editor exit) on any failure.
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

            // Resolve and verify MessagePipe before building, so a missing, duplicate or modified copy fails fast.
            OnityMessagingBenchmarkBuildMetadata metadata = new OnityMessagingBenchmarkBuildMetadata();
            RecordMessagePipe(metadata);
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
                    throw new InvalidOperationException(backendLabel + " messaging benchmark Player build failed: "
                        + report.summary.result + ". See the Editor log.");
                }

                WriteBuildMetadata(buildPath + ".build.json", metadata, report, backendLabel, targetGroup, sourceHead, buildPath);
                Debug.Log("Onity messaging benchmark " + backendLabel + " Player built: " + buildPath
                    + " (GUID " + report.summary.guid + ", MessagePipe " + metadata.messagePipeFlavor + ").");
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
            OnityMessagingBenchmarkBuildMetadata metadata,
            BuildReport report,
            string backendLabel,
            BuildTargetGroup targetGroup,
            string sourceHead,
            string buildPath)
        {
            metadata.backend = backendLabel;
            metadata.unityVersion = Application.unityVersion;
            metadata.buildGuid = report.summary.guid.ToString();
            metadata.generatedAtUtc = DateTime.UtcNow.ToString("O");
            metadata.buildOptions = report.summary.options.ToString();
            metadata.development = (report.summary.options & BuildOptions.Development) != 0;
            metadata.scriptingDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
            metadata.il2CppCompilerConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(targetGroup).ToString();
            metadata.managedStrippingLevel = PlayerSettings.GetManagedStrippingLevel(targetGroup).ToString();
            metadata.onityPackageId = k_unknown;
            metadata.onityVersion = k_unknown;
            metadata.messagePipeSource = OnityMessagingBenchmarkPlayerRunner.MessagePipeSource;
            metadata.messagePipeSetup = OnityMessagingBenchmarkPlayerRunner.MessagePipeSetup;
            metadata.messagePipeAsyncApi = OnityMessagingBenchmarkPlayerRunner.MessagePipeAsyncApi;
            metadata.sourceHead = sourceHead;
            metadata.buildPath = buildPath;

            UnityEditor.PackageManager.PackageInfo onity = FindPackage(k_onityPackageName);
            if (onity != null)
            {
                metadata.onityPackageId = onity.packageId;
                metadata.onityVersion = onity.version;
            }

            File.WriteAllText(path, JsonUtility.ToJson(metadata, true));
        }

#if ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK
        // Unity package flavor: MessagePipe compiles from the sources of the .unitypackage named on the command
        // line. Every package entry must be in the project byte for byte with its GUID, the package's folder must
        // hold no other file, the compiled MessagePipe assembly definition must come from the package, no
        // MessagePipe.dll may exist, and UniTask must be a registered package that the UniTask assembly comes from.
        private static void RecordMessagePipe(OnityMessagingBenchmarkBuildMetadata metadata)
        {
            string packagePath = GetArgumentValue(k_messagePipePackageArgument);
            if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            {
                throw new ArgumentException(k_messagePipePackageArgument
                    + " <.unitypackage> is required for the Unity package flavor and must exist; got '" + packagePath + "'.");
            }

            packagePath = Path.GetFullPath(packagePath);
            List<string> dlls = FindMessagePipeDlls();
            if (dlls.Count != 0)
            {
                throw new InvalidOperationException("The Unity package flavor compiles MessagePipe from source, but the project holds a "
                    + k_messagePipeFileName + " plugin: '" + dlls[0] + "'.");
            }

            string assemblyDefinition = UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(k_messagePipeAssemblyName);
            if (string.IsNullOrEmpty(assemblyDefinition))
            {
                throw new InvalidOperationException("No assembly definition compiles the " + k_messagePipeAssemblyName + " assembly.");
            }

            List<UnityPackageEntry> entries = ReadUnityPackage(packagePath);
            string packageRoot = VerifyUnityPackage(entries, assemblyDefinition);
            ReadMessagePipeVersion(out string informationalVersion, out metadata.messagePipeAssemblyVersion);
            metadata.messagePipeFlavor = OnityMessagingBenchmarkPlayerRunner.UnityPackageFlavor;
            metadata.messagePipeVersion = ReadPackageVersion(entries, packageRoot);
            metadata.messagePipeAssemblyPath = assemblyDefinition;
            metadata.messagePipeDllPath = string.Empty;
            metadata.messagePipeDllSha256 = string.Empty;
            metadata.messagePipePackagePath = packagePath;
            metadata.messagePipePackageSha256 = ComputeSha256(File.ReadAllBytes(packagePath));
            metadata.messagePipePackageEntries = entries.Count;
            Debug.Log("Onity messaging benchmark: MessagePipe " + metadata.messagePipeVersion + " (assembly " + informationalVersion
                + ") verified against " + packagePath + ": " + entries.Count + " entries under " + packageRoot + ".");

            UnityEditor.PackageManager.PackageInfo uniTask = FindPackage(k_uniTaskPackageName);
            if (uniTask == null)
            {
                throw new InvalidOperationException("The Unity package flavor needs UniTask (" + k_uniTaskPackageName
                    + "), which is not a registered package.");
            }

            string uniTaskDefinition = UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(k_uniTaskAssemblyName);
            if (string.IsNullOrEmpty(uniTaskDefinition) || !uniTaskDefinition.StartsWith(uniTask.assetPath + "/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The " + k_uniTaskAssemblyName + " assembly is not compiled from the registered "
                    + k_uniTaskPackageName + " package (" + uniTask.assetPath + "): '" + uniTaskDefinition + "'.");
            }

            metadata.uniTaskVersion = uniTask.version;
            metadata.uniTaskPackageId = uniTask.packageId;
            metadata.uniTaskTreeSha256 = ComputeTreeSha256(uniTask.resolvedPath);
        }

        // A .unitypackage is a gzip tar of <guid>/asset, <guid>/asset.meta and <guid>/pathname entries. Only regular
        // files and directories are accepted; anything else (links, long names, extended headers) is refused.
        private static List<UnityPackageEntry> ReadUnityPackage(string packagePath)
        {
            Dictionary<string, UnityPackageEntry> entriesByGuid = new Dictionary<string, UnityPackageEntry>(StringComparer.Ordinal);
            using (FileStream file = File.OpenRead(packagePath))
            using (System.IO.Compression.GZipStream archive = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress))
            {
                byte[] header = new byte[k_tarBlockSize];
                while (true)
                {
                    if (!ReadExactly(archive, header, k_tarBlockSize))
                    {
                        throw new InvalidDataException("The package ends without a tar end block: " + packagePath);
                    }

                    if (IsZeroBlock(header))
                    {
                        break;
                    }

                    string name = ReadTarString(header, 0, 100);
                    int size = ReadTarSize(header, name);
                    byte type = header[156];
                    byte[] content = new byte[size];
                    if (!ReadExactly(archive, content, size) || !SkipTarPadding(archive, size))
                    {
                        throw new InvalidDataException("The package is truncated in '" + name + "'.");
                    }

                    if (type == (byte)'5')
                    {
                        continue;
                    }

                    if (type != (byte)'0' && type != 0)
                    {
                        throw new InvalidDataException("Unsupported tar entry type '" + (char)type + "' for '" + name + "'.");
                    }

                    AddPackageFile(entriesByGuid, name, content);
                }
            }

            List<UnityPackageEntry> entries = new List<UnityPackageEntry>(entriesByGuid.Count);
            foreach (KeyValuePair<string, UnityPackageEntry> pair in entriesByGuid)
            {
                UnityPackageEntry entry = pair.Value;
                if (entry.PathName == null || entry.Asset == null || entry.Meta == null)
                {
                    throw new InvalidDataException("Package entry " + pair.Key + " lacks its pathname, asset or asset.meta.");
                }

                entries.Add(entry);
            }

            if (entries.Count == 0)
            {
                throw new InvalidDataException("The package holds no entries: " + packagePath);
            }

            return entries;
        }

        private static void AddPackageFile(Dictionary<string, UnityPackageEntry> entriesByGuid, string name, byte[] content)
        {
            string relative = name.StartsWith("./", StringComparison.Ordinal) ? name.Substring(2) : name;
            string[] parts = relative.Split('/');
            if (parts.Length != 2 || parts[0].Length == 0)
            {
                throw new InvalidDataException("Unexpected tar entry '" + name + "' in the package.");
            }

            if (!entriesByGuid.TryGetValue(parts[0], out UnityPackageEntry entry))
            {
                entry = new UnityPackageEntry(parts[0]);
                entriesByGuid.Add(parts[0], entry);
            }

            switch (parts[1])
            {
                case "asset":
                    entry.Asset = content;
                    break;
                case "asset.meta":
                    entry.Meta = content;
                    break;
                case "pathname":
                    string text = Encoding.UTF8.GetString(content);
                    int lineEnd = text.IndexOf('\n');
                    entry.PathName = (lineEnd >= 0 ? text.Substring(0, lineEnd) : text).Trim();
                    break;
                default:
                    throw new InvalidDataException("Unexpected file '" + name + "' in the package.");
            }
        }

        // Checks the project against the package and returns the package's root folder (the common folder of
        // all entries). Metas must carry the package GUID; folder metas, which the package lacks, are Unity's.
        private static string VerifyUnityPackage(List<UnityPackageEntry> entries, string assemblyDefinition)
        {
            string projectRoot = Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));
            HashSet<string> packagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string root = null;
            for (int i = 0; i < entries.Count; i++)
            {
                UnityPackageEntry entry = entries[i];
                string pathName = entry.PathName;
                if (!pathName.StartsWith("Assets/", StringComparison.Ordinal) || pathName.Contains("..") || pathName.Contains("\\"))
                {
                    throw new InvalidDataException("Package entry " + entry.Guid + " has an unexpected path: '" + pathName + "'.");
                }

                packagePaths.Add(pathName);
                root = root == null ? GetFolder(pathName) : GetCommonFolder(root, pathName);
                string projectFile = Path.Combine(projectRoot, pathName.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(projectFile))
                {
                    throw new InvalidOperationException("MessagePipe package entry missing from the project: " + pathName);
                }

                if (!BytesEqual(File.ReadAllBytes(projectFile), entry.Asset))
                {
                    throw new InvalidOperationException("The project's " + pathName + " differs from the package.");
                }

                string packageGuid = ReadMetaGuid(Encoding.UTF8.GetString(entry.Meta));
                string projectGuid = File.Exists(projectFile + ".meta") ? ReadMetaGuid(File.ReadAllText(projectFile + ".meta")) : null;
                if (packageGuid == null || !string.Equals(packageGuid, projectGuid, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The meta of " + pathName + " has GUID '" + projectGuid
                        + "', the package has '" + packageGuid + "'.");
                }
            }

            if (string.IsNullOrEmpty(root) || !root.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The package entries share no folder below Assets/.");
            }

            if (!packagePaths.Contains(assemblyDefinition))
            {
                throw new InvalidOperationException("The compiled " + k_messagePipeAssemblyName + " assembly definition '"
                    + assemblyDefinition + "' is not from the package.");
            }

            string rootFolder = Path.GetFullPath(Path.Combine(projectRoot, root.Replace('/', Path.DirectorySeparatorChar)))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string[] files = Directory.GetFiles(rootFolder, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string fullPath = Path.GetFullPath(files[i]);
                if (fullPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = root + "/" + fullPath.Substring(rootFolder.Length).Replace(Path.DirectorySeparatorChar, '/');
                if (!packagePaths.Contains(relative))
                {
                    throw new InvalidOperationException("The MessagePipe folder holds a file that is not from the package: " + relative);
                }
            }

            return root;
        }

        // The version in the package's own package.json, when the package carries one.
        private static string ReadPackageVersion(List<UnityPackageEntry> entries, string packageRoot)
        {
            string manifestPath = packageRoot + "/package.json";
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].PathName, manifestPath, StringComparison.Ordinal))
                {
                    PackageManifest manifest = JsonUtility.FromJson<PackageManifest>(Encoding.UTF8.GetString(entries[i].Asset));
                    if (manifest != null && !string.IsNullOrEmpty(manifest.version))
                    {
                        return manifest.version;
                    }
                }
            }

            return k_unknown + " (the package has no package.json version)";
        }

        // SHA-256 over the sorted lines 'relative/path<TAB>sha256<LF>' (UTF-8) of the folder's non-meta files.
        private static string ComputeTreeSha256(string folder)
        {
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            List<string> lines = new List<string>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                string fullPath = Path.GetFullPath(files[i]);
                if (fullPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = fullPath.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
                lines.Add(relative + "\t" + ComputeSha256(File.ReadAllBytes(fullPath)));
            }

            lines.Sort(StringComparer.Ordinal);
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append(lines[i]).Append('\n');
            }

            return ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        private static string GetFolder(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : string.Empty;
        }

        private static string GetCommonFolder(string folder, string path)
        {
            while (folder.Length > 0 && !path.StartsWith(folder + "/", StringComparison.Ordinal))
            {
                folder = GetFolder(folder);
            }

            return folder;
        }

        private static string ReadMetaGuid(string metaText)
        {
            string[] lines = metaText.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                {
                    return line.Substring("guid:".Length).Trim();
                }
            }

            return null;
        }

        private static bool BytesEqual(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
            {
                return false;
            }

            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }

        private static bool SkipTarPadding(Stream stream, int size)
        {
            int padding = (k_tarBlockSize - (size % k_tarBlockSize)) % k_tarBlockSize;
            return padding == 0 || ReadExactly(stream, new byte[padding], padding);
        }

        private static bool IsZeroBlock(byte[] block)
        {
            for (int i = 0; i < block.Length; i++)
            {
                if (block[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ReadTarString(byte[] header, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && header[end] != 0)
            {
                end++;
            }

            return Encoding.ASCII.GetString(header, offset, end - offset);
        }

        // The size field is octal ASCII; base-256 sizes (high bit set) are refused.
        private static int ReadTarSize(byte[] header, string name)
        {
            long size = 0;
            bool digits = false;
            for (int i = 124; i < 136; i++)
            {
                byte value = header[i];
                if (value == 0 || (value == (byte)' ' && digits))
                {
                    break;
                }

                if (value == (byte)' ')
                {
                    continue;
                }

                if (value < (byte)'0' || value > (byte)'7')
                {
                    throw new InvalidDataException("Unsupported tar size field for '" + name + "'.");
                }

                size = (size * 8) + (value - (byte)'0');
                digits = true;
                if (size > int.MaxValue)
                {
                    throw new InvalidDataException("Tar entry '" + name + "' is too large.");
                }
            }

            return (int)size;
        }

        private sealed class UnityPackageEntry
        {
            internal UnityPackageEntry(string guid)
            {
                Guid = guid;
            }

            internal string Guid { get; }

            internal string PathName { get; set; }

            internal byte[] Asset { get; set; }

            internal byte[] Meta { get; set; }
        }

        // The one package.json field the sidecar records; filled by JsonUtility.
        [Serializable]
        private sealed class PackageManifest
        {
            public string version = string.Empty;
        }
#else
        // NuGet flavor: MessagePipe is the one precompiled MessagePipe.dll plugin in the project.
        private static void RecordMessagePipe(OnityMessagingBenchmarkBuildMetadata metadata)
        {
            List<string> dlls = FindMessagePipeDlls();
            if (dlls.Count == 0)
            {
                throw new InvalidOperationException("No " + k_messagePipeFileName + " plugin in the project; the messaging benchmark needs it.");
            }

            if (dlls.Count > 1)
            {
                throw new InvalidOperationException("More than one " + k_messagePipeFileName + " plugin: '" + dlls[0] + "' and '" + dlls[1] + "'.");
            }

            string dllPath = dlls[0];
            string dllSha256 = ComputeSha256(File.ReadAllBytes(Path.GetFullPath(dllPath)));
            metadata.messagePipeFlavor = OnityMessagingBenchmarkPlayerRunner.NuGetFlavor;
            ReadMessagePipeVersion(out metadata.messagePipeVersion, out metadata.messagePipeAssemblyVersion);
            metadata.messagePipeAssemblyPath = dllPath;
            metadata.messagePipeDllPath = dllPath;
            metadata.messagePipeDllSha256 = dllSha256;
            metadata.messagePipePackagePath = dllPath;
            metadata.messagePipePackageSha256 = dllSha256;
            metadata.messagePipePackageEntries = 0;
            metadata.uniTaskVersion = k_notUsed;
            metadata.uniTaskPackageId = k_notUsed;
            metadata.uniTaskTreeSha256 = k_notUsed;
        }
#endif

        // The project paths of every MessagePipe.dll plugin.
        private static List<string> FindMessagePipeDlls()
        {
            List<string> found = new List<string>();
            PluginImporter[] importers = PluginImporter.GetAllImporters();
            for (int i = 0; i < importers.Length; i++)
            {
                string assetPath = importers[i].assetPath;
                if (string.Equals(Path.GetFileName(assetPath), k_messagePipeFileName, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(assetPath);
                }
            }

            return found;
        }

        private static UnityEditor.PackageManager.PackageInfo FindPackage(string packageName)
        {
            UnityEditor.PackageManager.PackageInfo[] packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            for (int i = 0; i < packages.Length; i++)
            {
                if (packages[i].name == packageName)
                {
                    return packages[i];
                }
            }

            return null;
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static void ReadMessagePipeVersion(out string informationalVersion, out string assemblyVersion)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                AssemblyName name = assemblies[i].GetName();
                if (name.Name != k_messagePipeAssemblyName)
                {
                    continue;
                }

                assemblyVersion = name.Version.ToString();
                AssemblyInformationalVersionAttribute attribute =
                    assemblies[i].GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                informationalVersion = attribute != null ? attribute.InformationalVersion : assemblyVersion;
                return;
            }

            informationalVersion = k_unknown + " (MessagePipe is not loaded in the Editor)";
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
