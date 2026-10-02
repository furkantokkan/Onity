using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Onity.DI;
using Onity.Pooling;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// PlayMode coverage proving an <see cref="OnityContext" /> drives container lifecycle
    /// entry points: a bound singleton's <see cref="IOnityInitializable.Initialize" /> runs once
    /// at build and its <see cref="IOnityTickable.Tick" /> is pumped each frame from the context's
    /// <c>Update</c>.
    /// </summary>
    [TestFixture]
    public sealed class OnityContextLifecyclePlayModeTests
    {
        /// <summary>
        /// Builds a context with a lifecycle service installer, runs several frames, and asserts
        /// the service initialized exactly once and ticked across multiple frames.
        /// </summary>
        /// <returns>Frame-yielding enumerator for the PlayMode runner.</returns>
        [UnityTest]
        public IEnumerator ContextPump_RunsInitializeOnceAndTicksEachFrame()
        {
            bool previousDiagnostics = OnityContainer.DiagnosticsCollectionEnabled;
            OnityContainer.DiagnosticsCollectionEnabled = false;

            GameObject contextObject = new GameObject(nameof(OnityContextLifecyclePlayModeTests));

            try
            {
                // Build the object inactive so adding components does not trigger Awake before
                // the installer is wired into the context's serialized installer list.
                contextObject.SetActive(false);

                LifecycleInstaller installer = contextObject.AddComponent<LifecycleInstaller>();
                LifecycleContext context = contextObject.AddComponent<LifecycleContext>();

                SetContextInstallers(context, installer);

                // Activation runs OnityContext.Awake: container is created, bindings installed,
                // Build runs and fires Initialize on the bound lifecycle service.
                contextObject.SetActive(true);

                Assert.That(context.Container, Is.Not.Null, "Context did not create a container on Awake.");

                LifecycleService service = context.Container.Resolve<LifecycleService>();
                Assert.That(service, Is.Not.Null, "Lifecycle service was not bound by the installer.");
                Assert.That(
                    service.InitializeCount,
                    Is.EqualTo(1),
                    "Initialize must run exactly once at container build.");

                int initialTicks = service.TickCount;

                const int framesToRun = 5;

                for (int frame = 0; frame < framesToRun; frame++)
                {
                    yield return null;
                }

                Assert.That(
                    service.InitializeCount,
                    Is.EqualTo(1),
                    "Initialize must not run again after the first build.");
                Assert.That(
                    service.TickCount,
                    Is.GreaterThanOrEqualTo(initialTicks + framesToRun),
                    "Tick must increment at least once per frame via the context Update pump.");
            }
            finally
            {
                Object.Destroy(contextObject);
                OnityContainer.DiagnosticsCollectionEnabled = previousDiagnostics;
            }
        }

        [Test]
        public void NestedContext_InjectsChildConsumerOnlyFromChildScope()
        {
            GameObject parentObject = new GameObject(nameof(NestedContext_InjectsChildConsumerOnlyFromChildScope));

            try
            {
                parentObject.SetActive(false);
                parentObject.AddComponent<SceneContext>();

                GameObject childObject = new GameObject("Child Context");
                childObject.transform.SetParent(parentObject.transform);
                GameObjectContext childContext = childObject.AddComponent<GameObjectContext>();
                ChildOnlyInstaller installer = childObject.AddComponent<ChildOnlyInstaller>();
                ChildConsumer consumer = childObject.AddComponent<ChildConsumer>();
                SetContextInstallers(childContext, installer);

                parentObject.SetActive(true);

                Assert.That(consumer.InjectionCount, Is.EqualTo(1));
                Assert.That(consumer.Service, Is.SameAs(childContext.Container.Resolve<IChildService>()));
            }
            finally
            {
                Object.Destroy(parentObject);
            }
        }

        private static void SetContextInstallers(OnityContext context, params MonoInstaller[] installers)
        {
            FieldInfo installersField = typeof(OnityContext).GetField(
                "m_installers",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(
                installersField,
                Is.Not.Null,
                "Expected OnityContext to hold installers in the private field 'm_installers'.");

            installersField.SetValue(context, installers);
        }

        /// <summary>
        /// Minimal concrete context used only by this PlayMode fixture.
        /// </summary>
        private sealed class LifecycleContext : OnityContext
        {
        }

        private interface IChildService
        {
        }

        private sealed class ChildService : IChildService
        {
        }

        private sealed class ChildOnlyInstaller : MonoInstaller
        {
            public override void InstallBindings(OnityContainer container)
            {
                container.BindInstance<IChildService>(new ChildService());
            }
        }

        private sealed class ChildConsumer : MonoBehaviour
        {
            public int InjectionCount { get; private set; }

            public IChildService Service { get; private set; }

            [Inject]
            private void SetService(IChildService service)
            {
                Service = service;
                InjectionCount++;
            }
        }

        /// <summary>
        /// Installer that binds the lifecycle service as a single instance so the container
        /// collects it for both initialization and tick pumping.
        /// </summary>
        private sealed class LifecycleInstaller : MonoInstaller
        {
            /// <inheritdoc />
            public override void InstallBindings(OnityContainer container)
            {
                container.BindInterfacesAndSelfTo<LifecycleService>().AsSingle();
            }
        }

        /// <summary>
        /// Test service implementing both lifecycle entry points and counting invocations.
        /// </summary>
        private sealed class LifecycleService : IOnityInitializable, IOnityTickable
        {
            /// <summary>
            /// Number of times <see cref="Initialize" /> has run.
            /// </summary>
            public int InitializeCount { get; private set; }

            /// <summary>
            /// Number of times <see cref="Tick" /> has run.
            /// </summary>
            public int TickCount { get; private set; }

            /// <inheritdoc />
            public void Initialize()
            {
                InitializeCount++;
            }

            /// <inheritdoc />
            public void Tick()
            {
                TickCount++;
            }
        }
    }

    [TestFixture]
    public sealed class PrefabComponentPoolPlayModeTests
    {
        private const int k_maximumFrames = 10;

        [Test]
        public void ActivePrefab_FirstParameterizedGet_InitializesBeforeOnEnable()
        {
            GameObject prefabRoot = new GameObject("ActivePoolPrefab");
            PoolEnableProbe prefab = prefabRoot.AddComponent<PoolEnableProbe>();
            PrefabComponentPool<PoolEnableProbe> pool = null;
            PoolEnableProbe instance = null;

            try
            {
                pool = new PrefabComponentPool<PoolEnableProbe>(prefab, maxSize: 1, fixedSize: true);
                instance = pool.Get(7, (item, value) => item.Value = value);

                Assert.That(instance.EnableCount, Is.EqualTo(1));
                Assert.That(instance.FirstSeenOnEnable, Is.EqualTo(7));
                pool.Release(instance);
                instance = null;
            }
            finally
            {
                pool?.Dispose();

                if (instance != null)
                {
                    Object.Destroy(instance.gameObject);
                }

                Object.Destroy(prefabRoot);
            }
        }

        [UnityTest]
        public IEnumerator DestroyingGameObjectContext_DisposesPoolCreatedByBindPooledFactory()
        {
            return RunContextDestroyScenario<GameObjectContext>(
                nameof(DestroyingGameObjectContext_DisposesPoolCreatedByBindPooledFactory));
        }

        [UnityTest]
        public IEnumerator DestroyingSceneContext_DisposesPoolCreatedByBindPooledFactory()
        {
            return RunContextDestroyScenario<SceneContext>(
                nameof(DestroyingSceneContext_DisposesPoolCreatedByBindPooledFactory));
        }

        private static IEnumerator RunContextDestroyScenario<TContext>(string scenarioName)
            where TContext : OnityContext
        {
            GameObject prefabRoot = new GameObject(scenarioName + "-Prefab-" + Guid.NewGuid().ToString("N"));
            ScopedPoolProbe prefab = prefabRoot.AddComponent<ScopedPoolProbe>();
            GameObject contextObject = new GameObject(scenarioName);
            ScopedPoolProbe inactive = null;

            try
            {
                // Inactive while components are added, so Awake runs after the installer is wired in.
                contextObject.SetActive(false);
                ScopedPoolInstaller installer = contextObject.AddComponent<ScopedPoolInstaller>();
                installer.Prefab = prefab;
                TContext context = contextObject.AddComponent<TContext>();
                SetContextInstallers(context, installer);

                contextObject.SetActive(true);

                IPool<ScopedPoolProbe> pool = context.Container.Resolve<IPool<ScopedPoolProbe>>();
                IOnityPoolDiagnosticsSource diagnostics = (IOnityPoolDiagnosticsSource)pool;
                string poolName = diagnostics.GetDiagnosticsSnapshot().PoolName;
                inactive = pool.Get();
                pool.Release(inactive);

                Assert.That(IsRegisteredInDiagnostics(poolName), Is.True);

                Object.Destroy(contextObject);

                for (int frame = 0; frame < k_maximumFrames && IsRegisteredInDiagnostics(poolName); frame++)
                {
                    yield return null;
                }

                Assert.That(IsRegisteredInDiagnostics(poolName), Is.False);
                Assert.That(diagnostics.GetDiagnosticsSnapshot().IsDisposed, Is.True);
            }
            finally
            {
                if (contextObject != null)
                {
                    Object.Destroy(contextObject);
                }

                if (inactive != null)
                {
                    Object.Destroy(inactive.gameObject);
                }

                Object.Destroy(prefabRoot);
            }
        }

        private static bool IsRegisteredInDiagnostics(string poolName)
        {
            System.Collections.Generic.List<OnityPoolDiagnosticsSnapshot> snapshots =
                new System.Collections.Generic.List<OnityPoolDiagnosticsSnapshot>();
            OnityPoolDiagnosticsRegistry.GetSnapshots(snapshots);

            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].PoolName == poolName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetContextInstallers(OnityContext context, params MonoInstaller[] installers)
        {
            FieldInfo installersField = typeof(OnityContext).GetField(
                "m_installers",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(installersField, Is.Not.Null);
            installersField.SetValue(context, installers);
        }

        private sealed class ScopedPoolProbe : MonoBehaviour
        {
        }

        private sealed class ScopedPoolInstaller : MonoInstaller
        {
            public ScopedPoolProbe Prefab { get; set; }

            public override void InstallBindings(OnityContainer container)
            {
                container.BindPooledFactory(Prefab);
            }
        }

        private sealed class PoolEnableProbe : MonoBehaviour
        {
            public int Value { get; set; }

            public int EnableCount { get; private set; }

            public int FirstSeenOnEnable { get; private set; }

            private void OnEnable()
            {
                EnableCount++;

                if (EnableCount == 1)
                {
                    FirstSeenOnEnable = Value;
                }
            }
        }
    }

    /// <summary>Explicit warmed pool comparison for a Unity test player.</summary>
    [TestFixture]
    public sealed class OnityPoolBenchmarkPlayModeTests
    {
        private const int k_allocationSampleCount = 64;
        private const int k_sampleCount = 11;
        private const int k_splitSampleCount = 16;
        private const int k_splitWarmupIterations = 10000;
        private const int k_burstSize = 32;
        private const int k_burstWarmupIterations = 2000;
        private const int k_burstMeasuredIterations = 20000;
        private const int k_zenjectBurstSampleCount = 12;
        private const int k_measuredIterations = 250000;
        private const string k_runArgument = "-onityRunPoolBenchmark";
        private const string k_runSplitArgument = "-onityRunPoolSplitBenchmark";
        private const string k_runBurstArgument = "-onityRunPoolFactoryBurstBenchmark";
        private const string k_runBurstTimingArgument = "-onityRunPoolFactoryBurstTiming";
        private const string k_runZenjectBurstArgument = "-onityRunPoolZenjectBurstBenchmark";
        private const string k_runZenjectBurstTimingArgument = "-onityRunPoolZenjectBurstTiming";
        private const string k_outputArgument = "-onityPoolBenchmarkOutput";
        private const string k_buildPathArgument = "-onityPoolBenchmarkBuildPath";
        private const string k_releaseBuildArgument = "-onityPoolBenchmarkRelease";
        private const string k_zenjectBuildArgument = "-onityPoolBenchmarkZenject";
        private const string k_zenjectBridgeProtocol = "onity-zenject-int-array-v1";
        private static byte[] s_allocationProbe;

        [Test]
        [Explicit("Run by exact name in an IL2CPP test player for a checked pool comparison.")]
        public void MeasureCheckedWarmedRentReturnAgainstUnity()
        {
            PoolBenchmarkReport report = RunMeasurement();
            TestContext.WriteLine(JsonUtility.ToJson(report, true));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RunFromCommandLine()
        {
            if (Application.isEditor)
            {
                return;
            }

            string[] args = Environment.GetCommandLineArgs();
            bool runBaseline = HasArgument(args, k_runArgument);
            bool runSplit = HasArgument(args, k_runSplitArgument);
            bool runBurst = HasArgument(args, k_runBurstArgument);
            bool runBurstTiming = HasArgument(args, k_runBurstTimingArgument);
            bool runZenjectBurst = HasArgument(args, k_runZenjectBurstArgument);
            bool runZenjectBurstTiming = HasArgument(args, k_runZenjectBurstTimingArgument);
            if (!runBaseline && !runSplit && !runBurst && !runBurstTiming &&
                !runZenjectBurst && !runZenjectBurstTiming)
            {
                return;
            }

            string reportPath = null;
            try
            {
                reportPath = GetRequiredTempPath(args, k_outputArgument);
#if !ENABLE_IL2CPP
                throw new InvalidOperationException("The pool benchmark requires an IL2CPP player.");
#endif
                if ((runBaseline ? 1 : 0) + (runSplit ? 1 : 0) +
                    (runBurst ? 1 : 0) + (runBurstTiming ? 1 : 0) +
                    (runZenjectBurst ? 1 : 0) + (runZenjectBurstTiming ? 1 : 0) != 1)
                {
                    throw new ArgumentException("Select only one pool benchmark mode.");
                }

                bool timingOnly = runBurstTiming || runZenjectBurstTiming;
                if (timingOnly == UnityEngine.Debug.isDebugBuild)
                {
                    throw new InvalidOperationException(timingOnly
                        ? "The pool timing benchmark requires a non-Development player."
                        : "The pool allocation benchmark requires a Development player.");
                }

                object report;
                if (runZenjectBurst || runZenjectBurstTiming)
                {
                    report = RunZenjectBurstMeasurement(runZenjectBurst);
                }
                else if (runBurst || runBurstTiming)
                {
                    report = RunFactoryBurstMeasurement(runBurst);
                }
                else if (runSplit)
                {
                    report = RunSplitMeasurement();
                }
                else
                {
                    report = RunMeasurement();
                }

                SaveReport(reportPath, report);
                UnityEngine.Debug.Log($"Onity pool player benchmark completed: {reportPath}");
                Application.Quit(0);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                if (reportPath != null)
                {
                    try
                    {
                        SaveReport(reportPath, new PoolBenchmarkReport
                        {
                            status = "failed",
                            error = exception.ToString(),
                            unityVersion = Application.unityVersion,
                            platform = Application.platform.ToString(),
                            backend = GetBackend(),
                            developmentBuild = UnityEngine.Debug.isDebugBuild
                        });
                    }
                    catch (Exception saveException)
                    {
                        UnityEngine.Debug.LogException(saveException);
                    }
                }

                Application.Quit(1);
            }
        }

#if UNITY_EDITOR
        /// <summary>Builds a standalone IL2CPP pool benchmark player without changing project settings.</summary>
        public static void BuildPoolBenchmarkPlayer()
        {
            string[] args = Environment.GetCommandLineArgs();
            string buildPath = GetRequiredTempPath(args, k_buildPathArgument);
            bool releaseBuild = HasArgument(args, k_releaseBuildArgument);
            bool zenjectBuild = HasArgument(args, k_zenjectBuildArgument);
            if (!string.Equals(Path.GetExtension(buildPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The pool benchmark build path must end in .exe.");
            }

            if (UnityEditor.EditorUserBuildSettings.activeBuildTarget != UnityEditor.BuildTarget.StandaloneWindows64)
            {
                throw new InvalidOperationException("The active build target must already be StandaloneWindows64.");
            }

            if (UnityEditor.PlayerSettings.GetScriptingBackend(UnityEditor.BuildTargetGroup.Standalone) !=
                UnityEditor.ScriptingImplementation.IL2CPP)
            {
                throw new InvalidOperationException("The Standalone scripting backend must already be IL2CPP.");
            }

            const string scenePath = "Assets/Scenes/SampleScene.unity";
            if (!File.Exists(Path.GetFullPath(scenePath)))
            {
                throw new FileNotFoundException("The pool benchmark scene was not found.", scenePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(buildPath));
            UnityEditor.BuildPlayerOptions options = new UnityEditor.BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = buildPath,
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                targetGroup = UnityEditor.BuildTargetGroup.Standalone,
                options = UnityEditor.BuildOptions.IncludeTestAssemblies |
                    (releaseBuild ? UnityEditor.BuildOptions.None : UnityEditor.BuildOptions.Development),
                extraScriptingDefines = zenjectBuild ? new[] { "ONITY_BENCHMARKS" } : Array.Empty<string>()
            };
            UnityEditor.Build.NamedBuildTarget namedTarget = UnityEditor.Build.NamedBuildTarget.Standalone;
            UnityEditor.Il2CppCompilerConfiguration compilerConfiguration =
                UnityEditor.PlayerSettings.GetIl2CppCompilerConfiguration(namedTarget);
            UnityEditor.Build.Il2CppCodeGeneration codeGeneration =
                UnityEditor.PlayerSettings.GetIl2CppCodeGeneration(namedTarget);
            UnityEngine.Debug.Log($"Onity pool benchmark build configuration: " +
                $"Development={(!releaseBuild)}, IL2CPP compiler={compilerConfiguration}, " +
                $"IL2CPP code generation={codeGeneration}, options={options.options}, " +
                $"Zenject benchmark define={zenjectBuild}");

            UnityEditor.Build.Reporting.BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(options);
            if (report == null || report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Pool benchmark player build failed. See the Unity Editor log.");
            }

            UnityEngine.Debug.Log($"Onity pool benchmark player built: {buildPath}; Development={(!releaseBuild)}");
        }
#endif

        private static PoolBenchmarkReport RunMeasurement()
        {
            PoolBenchmarkReport report = new PoolBenchmarkReport
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                backend = GetBackend(),
                developmentBuild = UnityEngine.Debug.isDebugBuild,
                profilerEnabled = UnityEngine.Profiling.Profiler.enabled,
                stopwatchFrequency = Stopwatch.Frequency,
                allocationSampleCount = k_allocationSampleCount,
                sampleCount = k_sampleCount,
                measuredIterationsPerSample = k_measuredIterations,
                utcRecordedAt = DateTime.UtcNow.ToString("O")
            };

            Check(Stopwatch.Frequency > 0, "The stopwatch frequency must be positive.");

            using ObjectPool<PooledValue> unity = new ObjectPool<PooledValue>(
                () => new PooledValue(), null, null, null, true, 16, 16);
            using OnityObjectPool<PooledValue> onity = new OnityObjectPool<PooledValue>(
                () => new PooledValue(), collectionCheck: true,
                defaultCapacity: 16, maxSize: 16, initialSize: 1);
            PooledValue prewarmed = unity.Get();
            unity.Release(prewarmed);

            Func<PooledValue> unityGet = unity.Get;
            Action<PooledValue> unityRelease = unity.Release;
            Func<PooledValue> onityGet = onity.Get;
            Action<PooledValue> onityRelease = onity.Release;
            long trackedGets = 0;
            long trackedReleases = 0;
            Action unityOperation = () =>
            {
                PooledValue item = unityGet();
                Interlocked.Increment(ref trackedGets);
                unityRelease(item);
                Interlocked.Increment(ref trackedReleases);
            };
            Action onityOperation = () =>
            {
                PooledValue item = onityGet();
                onityRelease(item);
            };

            Action positiveControl = () => s_allocationProbe = new byte[1024];
            report.positiveAllocationEvents = CountAllocEvents(positiveControl);
            report.unityAllocationEvents = CountAllocEvents(unityOperation);
            report.onityAllocationEvents = CountAllocEvents(onityOperation);
            Check(report.positiveAllocationEvents > 0, "The allocation positive control recorded no events.");
            Check(report.unityAllocationEvents == 0, "The warmed Unity control allocated.");
            Check(report.onityAllocationEvents == 0, "The warmed Onity pool allocated.");

            report.unityNanosecondsPerPair = new double[k_sampleCount];
            report.onityNanosecondsPerPair = new double[k_sampleCount];
            for (int i = 0; i < 10000; i++)
            {
                unityOperation();
                onityOperation();
            }

            for (int sample = 0; sample < k_sampleCount; sample++)
            {
                if ((sample & 1) == 0)
                {
                    report.unityNanosecondsPerPair[sample] = Measure(unityOperation);
                    report.onityNanosecondsPerPair[sample] = Measure(onityOperation);
                }
                else
                {
                    report.onityNanosecondsPerPair[sample] = Measure(onityOperation);
                    report.unityNanosecondsPerPair[sample] = Measure(unityOperation);
                }
            }

            report.pairedOnityOverUnityRatios = new double[k_sampleCount];
            for (int sample = 0; sample < k_sampleCount; sample++)
            {
                double unityTime = report.unityNanosecondsPerPair[sample];
                double onityTime = report.onityNanosecondsPerPair[sample];
                Check(IsPositiveFinite(unityTime) && IsPositiveFinite(onityTime),
                    $"Sample {sample} has an invalid elapsed time.");
                report.pairedOnityOverUnityRatios[sample] = onityTime / unityTime;
                Check(IsPositiveFinite(report.pairedOnityOverUnityRatios[sample]),
                    $"Sample {sample} has an invalid paired ratio.");
            }

            double[] sortedUnity = (double[])report.unityNanosecondsPerPair.Clone();
            double[] sortedOnity = (double[])report.onityNanosecondsPerPair.Clone();
            double[] sortedRatios = (double[])report.pairedOnityOverUnityRatios.Clone();
            Array.Sort(sortedUnity);
            Array.Sort(sortedOnity);
            Array.Sort(sortedRatios);
            report.unityMedianNanosecondsPerPair = sortedUnity[k_sampleCount / 2];
            report.onityMedianNanosecondsPerPair = sortedOnity[k_sampleCount / 2];
            report.pairedRatioMedian = sortedRatios[k_sampleCount / 2];
            report.pairedRatioQ1 = sortedRatios[2];
            report.pairedRatioQ3 = sortedRatios[8];

            OnityPoolDiagnosticsSnapshot final = onity.GetDiagnosticsSnapshot();
            report.onityCountAll = final.CountAll;
            report.onityCountActive = final.CountActive;
            report.onityCountInactive = final.CountInactive;
            report.onityGetCount = final.GetCount;
            report.onityReleaseCount = final.ReleaseCount;
            report.unityCountAll = unity.CountAll;
            report.unityCountActive = unity.CountActive;
            report.unityCountInactive = unity.CountInactive;
            report.trackedUnityGets = trackedGets;
            report.trackedUnityReleases = trackedReleases;
            Check(final.CountAll == 1 && final.CountActive == 0 && final.CountInactive == 1,
                "The Onity pool ended in an unexpected state.");
            Check(final.GetCount == final.ReleaseCount && final.GetCount == trackedGets,
                "The Onity pool operation counts do not match the Unity control.");
            Check(unity.CountAll == 1 && unity.CountActive == 0 && unity.CountInactive == 1,
                "The Unity pool ended in an unexpected state.");
            Check(trackedGets == trackedReleases, "The Unity control operation counts do not match.");
            report.status = "passed";
            return report;
        }

        private static PoolSplitBenchmarkReport RunSplitMeasurement()
        {
            FieldInfo innerField = typeof(OnityObjectPool<PooledValue>).GetField(
                "m_pool", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(innerField != null, "The Onity pool backend field is unavailable in this player.");

            PoolSplitBenchmarkReport report = new PoolSplitBenchmarkReport
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                backend = GetBackend(),
                developmentBuild = UnityEngine.Debug.isDebugBuild,
                profilerEnabled = UnityEngine.Profiling.Profiler.enabled,
                stopwatchFrequency = Stopwatch.Frequency,
                allocationSampleCount = k_allocationSampleCount,
                sampleCount = k_splitSampleCount,
                measuredIterationsPerSample = k_measuredIterations,
                utcRecordedAt = DateTime.UtcNow.ToString("O"),
                uuNanosecondsPerPair = new double[k_splitSampleCount],
                ouNanosecondsPerPair = new double[k_splitSampleCount],
                uoNanosecondsPerPair = new double[k_splitSampleCount],
                ooNanosecondsPerPair = new double[k_splitSampleCount],
                ooOverUuRatios = new double[k_splitSampleCount],
                getDeltaWithUnityRelease = new double[k_splitSampleCount],
                getDeltaWithOnityRelease = new double[k_splitSampleCount],
                releaseDeltaWithUnityGet = new double[k_splitSampleCount],
                releaseDeltaWithOnityGet = new double[k_splitSampleCount],
                interactionDelta = new double[k_splitSampleCount],
                allocationEventsByArm = new int[4],
                finalCountAllByArm = new int[4],
                finalCountActiveByArm = new int[4],
                finalCountInactiveByArm = new int[4],
                onityGetCountByArm = new long[4],
                onityReleaseCountByArm = new long[4],
                externalGetCountByArm = new long[4],
                externalReleaseCountByArm = new long[4]
            };

            Check(Stopwatch.Frequency > 0, "The stopwatch frequency must be positive.");
            using SplitPoolArm uu = new SplitPoolArm(innerField, false, false);
            using SplitPoolArm ou = new SplitPoolArm(innerField, true, false);
            using SplitPoolArm uo = new SplitPoolArm(innerField, false, true);
            using SplitPoolArm oo = new SplitPoolArm(innerField, true, true);
            SplitPoolArm[] arms = { uu, ou, uo, oo };
            double[][] times =
            {
                report.uuNanosecondsPerPair,
                report.ouNanosecondsPerPair,
                report.uoNanosecondsPerPair,
                report.ooNanosecondsPerPair
            };

            Action positiveControl = () => s_allocationProbe = new byte[1024];
            report.positiveAllocationEvents = CountAllocEvents(positiveControl);
            Check(report.positiveAllocationEvents > 0, "The allocation positive control recorded no events.");
            for (int arm = 0; arm < arms.Length; arm++)
            {
                report.allocationEventsByArm[arm] = CountAllocEvents(arms[arm].Operation);
                Check(report.allocationEventsByArm[arm] == 0, $"Split arm {arm} allocated when warm.");
            }

            for (int i = 0; i < k_splitWarmupIterations; i++)
            {
                for (int arm = 0; arm < arms.Length; arm++)
                {
                    arms[arm].Operation();
                }
            }

            for (int round = 0; round < k_splitSampleCount; round++)
            {
                for (int step = 0; step < arms.Length; step++)
                {
                    int arm = (round + step) & 3;
                    times[arm][round] = Measure(arms[arm].Operation);
                }

                double uuTime = report.uuNanosecondsPerPair[round];
                double ouTime = report.ouNanosecondsPerPair[round];
                double uoTime = report.uoNanosecondsPerPair[round];
                double ooTime = report.ooNanosecondsPerPair[round];
                Check(IsPositiveFinite(uuTime) && IsPositiveFinite(ouTime) &&
                    IsPositiveFinite(uoTime) && IsPositiveFinite(ooTime),
                    $"Split round {round} has an invalid elapsed time.");
                report.ooOverUuRatios[round] = ooTime / uuTime;
                report.getDeltaWithUnityRelease[round] = ouTime - uuTime;
                report.getDeltaWithOnityRelease[round] = ooTime - uoTime;
                report.releaseDeltaWithUnityGet[round] = uoTime - uuTime;
                report.releaseDeltaWithOnityGet[round] = ooTime - ouTime;
                report.interactionDelta[round] = ooTime - ouTime - uoTime + uuTime;
                Check(IsPositiveFinite(report.ooOverUuRatios[round]) &&
                    IsFinite(report.getDeltaWithUnityRelease[round]) &&
                    IsFinite(report.getDeltaWithOnityRelease[round]) &&
                    IsFinite(report.releaseDeltaWithUnityGet[round]) &&
                    IsFinite(report.releaseDeltaWithOnityGet[round]) &&
                    IsFinite(report.interactionDelta[round]),
                    $"Split round {round} has an invalid derived value.");
            }

            GetQuartiles(report.uuNanosecondsPerPair, out report.uuMedian, out report.uuQ1, out report.uuQ3);
            GetQuartiles(report.ouNanosecondsPerPair, out report.ouMedian, out report.ouQ1, out report.ouQ3);
            GetQuartiles(report.uoNanosecondsPerPair, out report.uoMedian, out report.uoQ1, out report.uoQ3);
            GetQuartiles(report.ooNanosecondsPerPair, out report.ooMedian, out report.ooQ1, out report.ooQ3);
            GetQuartiles(report.ooOverUuRatios, out report.ooOverUuMedian,
                out report.ooOverUuQ1, out report.ooOverUuQ3);
            report.getDeltaUnityReleaseMedian = GetMedian(report.getDeltaWithUnityRelease);
            report.getDeltaOnityReleaseMedian = GetMedian(report.getDeltaWithOnityRelease);
            report.releaseDeltaUnityGetMedian = GetMedian(report.releaseDeltaWithUnityGet);
            report.releaseDeltaOnityGetMedian = GetMedian(report.releaseDeltaWithOnityGet);
            report.interactionDeltaMedian = GetMedian(report.interactionDelta);

            int expectedPairs = k_allocationSampleCount * 2 + k_splitWarmupIterations +
                k_splitSampleCount * k_measuredIterations;
            report.successfulPairsPerArm = expectedPairs;
            for (int arm = 0; arm < arms.Length; arm++)
            {
                arms[arm].Validate(expectedPairs, report, arm);
            }

            report.status = "passed";
            return report;
        }

        private static PoolFactoryBurstReport RunFactoryBurstMeasurement(bool captureAllocation)
        {
            PoolFactoryBurstReport report = new PoolFactoryBurstReport
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                backend = GetBackend(),
                developmentBuild = UnityEngine.Debug.isDebugBuild,
                profilerEnabled = UnityEngine.Profiling.Profiler.enabled,
                stopwatchFrequency = Stopwatch.Frequency,
                utcRecordedAt = DateTime.UtcNow.ToString("O"),
                burstSize = k_burstSize,
                warmupBursts = k_burstWarmupIterations,
                measuredBurstsPerSample = k_burstMeasuredIterations,
                sampleCount = k_sampleCount,
                allocationSampleCount = k_allocationSampleCount,
                allocationMeasurementAvailable = captureAllocation,
                allocationEventsByArm = new int[3],
                factoryNanosecondsPerBurst = new double[k_sampleCount],
                directNanosecondsPerBurst = new double[k_sampleCount],
                unityNanosecondsPerBurst = new double[k_sampleCount],
                factoryOverDirectRatios = new double[k_sampleCount],
                factoryOverUnityRatios = new double[k_sampleCount],
                createdByArm = new int[3],
                activeByArm = new int[3],
                inactiveByArm = new int[3],
                getCountByArm = new long[3],
                releaseCountByArm = new long[3],
                resetCountByArm = new long[3],
                checksumByArm = new long[3]
            };

            Check(Stopwatch.Frequency > 0, "The stopwatch frequency must be positive.");
            Check(!UnityEngine.Profiling.Profiler.enabled, "The profiler must be disabled during timing.");
            using BurstPoolArm factory = new BurstPoolArm(BurstPath.Factory);
            using BurstPoolArm direct = new BurstPoolArm(BurstPath.Direct);
            using BurstPoolArm unity = new BurstPoolArm(BurstPath.Unity);
            BurstPoolArm[] arms = { factory, direct, unity };
            double[][] times =
            {
                report.factoryNanosecondsPerBurst,
                report.directNanosecondsPerBurst,
                report.unityNanosecondsPerBurst
            };

            for (int arm = 0; arm < arms.Length; arm++)
            {
                arms[arm].ValidateInitial();
            }

            if (captureAllocation)
            {
                Action positiveControl = () => s_allocationProbe = new byte[1024];
                report.positiveAllocationEvents = CountAllocEvents(positiveControl);
                Check(report.positiveAllocationEvents > 0, "The allocation positive control recorded no events.");
            }
            else
            {
                report.positiveAllocationEvents = -1;
            }

            for (int arm = 0; arm < arms.Length; arm++)
            {
                if (captureAllocation)
                {
                    report.allocationEventsByArm[arm] = CountAllocEvents(arms[arm].Operation);
                    Check(report.allocationEventsByArm[arm] == 0, $"Burst arm {arm} allocated when warm.");
                }
                else
                {
                    report.allocationEventsByArm[arm] = -1;
                    for (int i = 0; i < 2 * k_allocationSampleCount; i++)
                    {
                        arms[arm].Operation();
                    }
                }
            }

            for (int i = 0; i < k_burstWarmupIterations; i++)
            {
                for (int arm = 0; arm < arms.Length; arm++)
                {
                    arms[arm].Operation();
                }
            }

            for (int sample = 0; sample < k_sampleCount; sample++)
            {
                for (int step = 0; step < arms.Length; step++)
                {
                    int arm = (sample + step) % arms.Length;
                    times[arm][sample] = Measure(arms[arm].Operation, k_burstMeasuredIterations);
                }

                double factoryTime = report.factoryNanosecondsPerBurst[sample];
                double directTime = report.directNanosecondsPerBurst[sample];
                double unityTime = report.unityNanosecondsPerBurst[sample];
                Check(IsPositiveFinite(factoryTime) && IsPositiveFinite(directTime) &&
                    IsPositiveFinite(unityTime), $"Burst sample {sample} has an invalid elapsed time.");
                report.factoryOverDirectRatios[sample] = factoryTime / directTime;
                report.factoryOverUnityRatios[sample] = factoryTime / unityTime;
                Check(IsPositiveFinite(report.factoryOverDirectRatios[sample]) &&
                    IsPositiveFinite(report.factoryOverUnityRatios[sample]),
                    $"Burst sample {sample} has an invalid paired ratio.");
            }

            GetElevenQuartiles(report.factoryNanosecondsPerBurst,
                out report.factoryMedian, out report.factoryQ1, out report.factoryQ3);
            GetElevenQuartiles(report.directNanosecondsPerBurst,
                out report.directMedian, out report.directQ1, out report.directQ3);
            GetElevenQuartiles(report.unityNanosecondsPerBurst,
                out report.unityMedian, out report.unityQ1, out report.unityQ3);
            GetElevenQuartiles(report.factoryOverDirectRatios,
                out report.factoryOverDirectMedian, out report.factoryOverDirectQ1,
                out report.factoryOverDirectQ3);
            GetElevenQuartiles(report.factoryOverUnityRatios,
                out report.factoryOverUnityMedian, out report.factoryOverUnityQ1,
                out report.factoryOverUnityQ3);

            int totalBursts = k_allocationSampleCount * 2 + k_burstWarmupIterations +
                k_sampleCount * k_burstMeasuredIterations;
            report.totalBurstsPerArm = totalBursts;
            for (int arm = 0; arm < arms.Length; arm++)
            {
                arms[arm].ValidateFinal(totalBursts, report, arm);
            }

            Check(report.checksumByArm[0] == report.checksumByArm[1] &&
                report.checksumByArm[0] == report.checksumByArm[2],
                "The burst payload checksums differ between arms.");
            report.status = "passed";
            return report;
        }

        private static PoolZenjectBurstReport RunZenjectBurstMeasurement(bool captureAllocation)
        {
            PoolZenjectBurstReport report = new PoolZenjectBurstReport
            {
                scenario = "FourArmIntArray32",
                utcRecordedAt = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                backend = GetBackend(),
                developmentBuild = UnityEngine.Debug.isDebugBuild,
                profilerEnabled = UnityEngine.Profiling.Profiler.enabled,
                stopwatchFrequency = Stopwatch.Frequency,
                quartileMethod = "Tukey median of each six-sample half",
                burstSize = k_burstSize,
                warmupBursts = k_burstWarmupIterations,
                measuredBurstsPerSample = k_burstMeasuredIterations,
                sampleCount = k_zenjectBurstSampleCount,
                allocationSampleCount = k_allocationSampleCount,
                allocationMeasurementAvailable = captureAllocation,
                positiveAllocationEvents = -1,
                armNames = new[] { "OnityFactory", "OnityDirect", "UnityUncheckedWithAtomics", "ZenjectMemoryPool" },
                allocationEventsByArm = new int[4],
                factoryNanosecondsPerBurst = new double[k_zenjectBurstSampleCount],
                directNanosecondsPerBurst = new double[k_zenjectBurstSampleCount],
                unityNanosecondsPerBurst = new double[k_zenjectBurstSampleCount],
                zenjectNanosecondsPerBurst = new double[k_zenjectBurstSampleCount],
                factoryOverZenjectRatios = new double[k_zenjectBurstSampleCount],
                directOverZenjectRatios = new double[k_zenjectBurstSampleCount],
                unityOverZenjectRatios = new double[k_zenjectBurstSampleCount],
                medianNanosecondsByArm = new double[4],
                q1NanosecondsByArm = new double[4],
                q3NanosecondsByArm = new double[4],
                createdByArm = new int[4],
                activeByArm = new int[4],
                inactiveByArm = new int[4],
                totalByArm = new int[4],
                getCountByArm = new long[4],
                releaseCountByArm = new long[4],
                resetCountByArm = new long[4],
                checksumByArm = new long[4],
                duplicateRejectedByArm = new bool[4],
                duplicateCheckSupportedByArm = new[] { true, true, false, true },
                duplicateExceptionByArm = new string[4],
                capacityExceptionByArm = new string[4]
            };

            Check(Stopwatch.Frequency > 0, "The stopwatch frequency must be positive.");
            Check(!UnityEngine.Profiling.Profiler.enabled, "The profiler must be disabled during timing.");
            using IntArrayBurstArm factory = new IntArrayBurstArm(IntArrayBurstPath.Factory);
            using IntArrayBurstArm direct = new IntArrayBurstArm(IntArrayBurstPath.Direct);
            using IntArrayBurstArm unity = new IntArrayBurstArm(IntArrayBurstPath.Unity);
            using IntArrayBurstArm zenject = new IntArrayBurstArm(IntArrayBurstPath.Zenject);
            IntArrayBurstArm[] arms = { factory, direct, unity, zenject };
            double[][] times =
            {
                report.factoryNanosecondsPerBurst,
                report.directNanosecondsPerBurst,
                report.unityNanosecondsPerBurst,
                report.zenjectNanosecondsPerBurst
            };
            report.zenjectBridgeAssembly = zenject.BridgeAssembly;
            report.zenjectBridgeProtocol = zenject.BridgeProtocol;
            report.zenjectStripAssertsInBuilds = zenject.StripAssertsInBuilds;
            report.zenjectInternalProfiling = zenject.InternalProfiling;

            for (int arm = 0; arm < arms.Length; arm++)
            {
                arms[arm].ValidateInitial(report, arm);
            }

            if (captureAllocation)
            {
                Action positiveControl = () => s_allocationProbe = new byte[1024];
                report.positiveAllocationEvents = CountAllocEvents(positiveControl);
                Check(report.positiveAllocationEvents > 0, "The allocation positive control recorded no events.");
            }

            for (int arm = 0; arm < arms.Length; arm++)
            {
                if (captureAllocation)
                {
                    report.allocationEventsByArm[arm] = CountAllocEvents(arms[arm].Operation);
                    Check(report.allocationEventsByArm[arm] == 0,
                        $"Int-array burst arm {arm} allocated when warm.");
                }
                else
                {
                    report.allocationEventsByArm[arm] = -1;
                    for (int i = 0; i < 2 * k_allocationSampleCount; i++)
                    {
                        arms[arm].Operation();
                    }
                }
            }

            for (int i = 0; i < k_burstWarmupIterations; i++)
            {
                for (int arm = 0; arm < arms.Length; arm++)
                {
                    arms[arm].Operation();
                }
            }

            for (int sample = 0; sample < k_zenjectBurstSampleCount; sample++)
            {
                for (int step = 0; step < arms.Length; step++)
                {
                    int arm = (sample + step) % arms.Length;
                    times[arm][sample] = Measure(arms[arm].Operation, k_burstMeasuredIterations);
                    if (!IsPositiveFinite(times[arm][sample]))
                    {
                        throw new InvalidOperationException(
                            $"Int-array burst arm {arm}, sample {sample} has invalid elapsed time.");
                    }
                }

                double zenjectTime = report.zenjectNanosecondsPerBurst[sample];
                report.factoryOverZenjectRatios[sample] =
                    report.factoryNanosecondsPerBurst[sample] / zenjectTime;
                report.directOverZenjectRatios[sample] =
                    report.directNanosecondsPerBurst[sample] / zenjectTime;
                report.unityOverZenjectRatios[sample] =
                    report.unityNanosecondsPerBurst[sample] / zenjectTime;
                if (!IsPositiveFinite(report.factoryOverZenjectRatios[sample]) ||
                    !IsPositiveFinite(report.directOverZenjectRatios[sample]) ||
                    !IsPositiveFinite(report.unityOverZenjectRatios[sample]))
                {
                    throw new InvalidOperationException(
                        $"Int-array burst sample {sample} has an invalid paired ratio.");
                }
            }

            for (int arm = 0; arm < arms.Length; arm++)
            {
                GetTwelveQuartiles(times[arm], out report.medianNanosecondsByArm[arm],
                    out report.q1NanosecondsByArm[arm], out report.q3NanosecondsByArm[arm]);
            }

            GetTwelveQuartiles(report.factoryOverZenjectRatios,
                out report.factoryOverZenjectMedian, out report.factoryOverZenjectQ1,
                out report.factoryOverZenjectQ3);
            GetTwelveQuartiles(report.directOverZenjectRatios,
                out report.directOverZenjectMedian, out report.directOverZenjectQ1,
                out report.directOverZenjectQ3);
            GetTwelveQuartiles(report.unityOverZenjectRatios,
                out report.unityOverZenjectMedian, out report.unityOverZenjectQ1,
                out report.unityOverZenjectQ3);

            report.totalBurstsPerArm = 2 * k_allocationSampleCount + k_burstWarmupIterations +
                k_zenjectBurstSampleCount * k_burstMeasuredIterations;
            for (int arm = 0; arm < arms.Length; arm++)
            {
                arms[arm].ValidateFinal(report.totalBurstsPerArm, report, arm);
                Check(report.checksumByArm[arm] == report.checksumByArm[0],
                    $"Int-array burst arm {arm} has a different checksum.");
            }

            report.status = "passed";
            return report;
        }

        private static double Measure(Action operation)
        {
            return Measure(operation, k_measuredIterations);
        }

        private static double Measure(Action operation, int iterations)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                operation();
            }

            return (Stopwatch.GetTimestamp() - start) * 1000000000d /
                (Stopwatch.Frequency * iterations);
        }

        private static int CountAllocEvents(Action operation)
        {
            for (int i = 0; i < k_allocationSampleCount; i++)
            {
                operation();
            }

            using ProfilerRecorder recorder = new ProfilerRecorder(
                ProfilerCategory.Internal, "GC.Alloc", 1024,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            Check(recorder.Valid, "The GC.Alloc recorder is invalid.");
            recorder.Start();
            for (int i = 0; i < k_allocationSampleCount; i++)
            {
                operation();
            }

            recorder.Stop();
            Check(!recorder.WrappedAround, "The GC.Alloc recorder wrapped around.");
            return recorder.Count;
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double GetMedian(double[] values)
        {
            GetQuartiles(values, out double median, out _, out _);
            return median;
        }

        private static void GetElevenQuartiles(double[] values, out double median, out double q1, out double q3)
        {
            Check(values != null && values.Length == k_sampleCount,
                "The burst benchmark must have 11 samples.");
            double[] sorted = (double[])values.Clone();
            Array.Sort(sorted);
            median = sorted[5];
            q1 = sorted[2];
            q3 = sorted[8];
        }

        private static void GetTwelveQuartiles(double[] values, out double median, out double q1, out double q3)
        {
            Check(values != null && values.Length == k_zenjectBurstSampleCount,
                "The Zenject burst benchmark must have 12 samples.");
            double[] sorted = (double[])values.Clone();
            Array.Sort(sorted);
            median = (sorted[5] + sorted[6]) / 2d;
            q1 = (sorted[2] + sorted[3]) / 2d;
            q3 = (sorted[8] + sorted[9]) / 2d;
        }

        private static void GetQuartiles(double[] values, out double median, out double q1, out double q3)
        {
            Check(values != null && values.Length == k_splitSampleCount,
                "The split benchmark must have 16 samples.");
            double[] sorted = (double[])values.Clone();
            Array.Sort(sorted);
            median = (sorted[7] + sorted[8]) / 2d;
            q1 = (sorted[3] + sorted[4]) / 2d;
            q3 = (sorted[11] + sorted[12]) / 2d;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static string GetBackend()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#else
            return "Mono";
#endif
        }

        private static bool HasArgument(string[] args, string argument)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], argument, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetRequiredTempPath(string[] args, string argument)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], argument, StringComparison.Ordinal))
                {
                    continue;
                }

                string path = args[i + 1];
                if (!Path.IsPathRooted(path))
                {
                    throw new ArgumentException($"{argument} requires an absolute path.");
                }

                string fullPath = Path.GetFullPath(path);
                string tempPath = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"{argument} must point inside the system Temp directory.");
                }

                return fullPath;
            }

            throw new ArgumentException($"Missing {argument} path.");
        }

        private static void SaveReport(string path, object report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }

        [Serializable]
        private sealed class PoolBenchmarkReport
        {
            public string status;
            public string error;
            public string utcRecordedAt;
            public string unityVersion;
            public string platform;
            public string backend;
            public bool developmentBuild;
            public bool profilerEnabled;
            public long stopwatchFrequency;
            public int allocationSampleCount;
            public int sampleCount;
            public int measuredIterationsPerSample;
            public int positiveAllocationEvents;
            public int unityAllocationEvents;
            public int onityAllocationEvents;
            public double[] unityNanosecondsPerPair;
            public double[] onityNanosecondsPerPair;
            public double[] pairedOnityOverUnityRatios;
            public double unityMedianNanosecondsPerPair;
            public double onityMedianNanosecondsPerPair;
            public double pairedRatioMedian;
            public double pairedRatioQ1;
            public double pairedRatioQ3;
            public int onityCountAll;
            public int onityCountActive;
            public int onityCountInactive;
            public long onityGetCount;
            public long onityReleaseCount;
            public int unityCountAll;
            public int unityCountActive;
            public int unityCountInactive;
            public long trackedUnityGets;
            public long trackedUnityReleases;
        }

        [Serializable]
        private sealed class PoolSplitBenchmarkReport
        {
            public string status;
            public string utcRecordedAt;
            public string unityVersion;
            public string platform;
            public string backend;
            public bool developmentBuild;
            public bool profilerEnabled;
            public long stopwatchFrequency;
            public int allocationSampleCount;
            public int sampleCount;
            public int measuredIterationsPerSample;
            public int successfulPairsPerArm;
            public int positiveAllocationEvents;
            public int[] allocationEventsByArm;
            public double[] uuNanosecondsPerPair;
            public double[] ouNanosecondsPerPair;
            public double[] uoNanosecondsPerPair;
            public double[] ooNanosecondsPerPair;
            public double[] ooOverUuRatios;
            public double[] getDeltaWithUnityRelease;
            public double[] getDeltaWithOnityRelease;
            public double[] releaseDeltaWithUnityGet;
            public double[] releaseDeltaWithOnityGet;
            public double[] interactionDelta;
            public double uuMedian;
            public double uuQ1;
            public double uuQ3;
            public double ouMedian;
            public double ouQ1;
            public double ouQ3;
            public double uoMedian;
            public double uoQ1;
            public double uoQ3;
            public double ooMedian;
            public double ooQ1;
            public double ooQ3;
            public double ooOverUuMedian;
            public double ooOverUuQ1;
            public double ooOverUuQ3;
            public double getDeltaUnityReleaseMedian;
            public double getDeltaOnityReleaseMedian;
            public double releaseDeltaUnityGetMedian;
            public double releaseDeltaOnityGetMedian;
            public double interactionDeltaMedian;
            public int[] finalCountAllByArm;
            public int[] finalCountActiveByArm;
            public int[] finalCountInactiveByArm;
            public long[] onityGetCountByArm;
            public long[] onityReleaseCountByArm;
            public long[] externalGetCountByArm;
            public long[] externalReleaseCountByArm;
        }

        [Serializable]
        private sealed class PoolFactoryBurstReport
        {
            public string status;
            public string utcRecordedAt;
            public string unityVersion;
            public string platform;
            public string backend;
            public bool developmentBuild;
            public bool profilerEnabled;
            public long stopwatchFrequency;
            public int burstSize;
            public int warmupBursts;
            public int measuredBurstsPerSample;
            public int sampleCount;
            public int allocationSampleCount;
            public bool allocationMeasurementAvailable;
            public int totalBurstsPerArm;
            public int positiveAllocationEvents;
            public int[] allocationEventsByArm;
            public double[] factoryNanosecondsPerBurst;
            public double[] directNanosecondsPerBurst;
            public double[] unityNanosecondsPerBurst;
            public double[] factoryOverDirectRatios;
            public double[] factoryOverUnityRatios;
            public double factoryMedian;
            public double factoryQ1;
            public double factoryQ3;
            public double directMedian;
            public double directQ1;
            public double directQ3;
            public double unityMedian;
            public double unityQ1;
            public double unityQ3;
            public double factoryOverDirectMedian;
            public double factoryOverDirectQ1;
            public double factoryOverDirectQ3;
            public double factoryOverUnityMedian;
            public double factoryOverUnityQ1;
            public double factoryOverUnityQ3;
            public int[] createdByArm;
            public int[] activeByArm;
            public int[] inactiveByArm;
            public long[] getCountByArm;
            public long[] releaseCountByArm;
            public long[] resetCountByArm;
            public long[] checksumByArm;
        }

        [Serializable]
        private sealed class PoolZenjectBurstReport
        {
            public string status;
            public string scenario;
            public string utcRecordedAt;
            public string unityVersion;
            public string platform;
            public string backend;
            public bool developmentBuild;
            public bool profilerEnabled;
            public long stopwatchFrequency;
            public string zenjectBridgeAssembly;
            public string zenjectBridgeProtocol;
            public bool zenjectStripAssertsInBuilds;
            public bool zenjectInternalProfiling;
            public string quartileMethod;
            public int burstSize;
            public int warmupBursts;
            public int measuredBurstsPerSample;
            public int sampleCount;
            public int allocationSampleCount;
            public bool allocationMeasurementAvailable;
            public int totalBurstsPerArm;
            public int positiveAllocationEvents;
            public string[] armNames;
            public int[] allocationEventsByArm;
            public double[] factoryNanosecondsPerBurst;
            public double[] directNanosecondsPerBurst;
            public double[] unityNanosecondsPerBurst;
            public double[] zenjectNanosecondsPerBurst;
            public double[] factoryOverZenjectRatios;
            public double[] directOverZenjectRatios;
            public double[] unityOverZenjectRatios;
            public double[] medianNanosecondsByArm;
            public double[] q1NanosecondsByArm;
            public double[] q3NanosecondsByArm;
            public double factoryOverZenjectMedian;
            public double factoryOverZenjectQ1;
            public double factoryOverZenjectQ3;
            public double directOverZenjectMedian;
            public double directOverZenjectQ1;
            public double directOverZenjectQ3;
            public double unityOverZenjectMedian;
            public double unityOverZenjectQ1;
            public double unityOverZenjectQ3;
            public int[] createdByArm;
            public int[] activeByArm;
            public int[] inactiveByArm;
            public int[] totalByArm;
            public long[] getCountByArm;
            public long[] releaseCountByArm;
            public long[] resetCountByArm;
            public long[] checksumByArm;
            public bool[] duplicateRejectedByArm;
            public bool[] duplicateCheckSupportedByArm;
            public string[] duplicateExceptionByArm;
            public string[] capacityExceptionByArm;
        }

        private sealed class SplitPoolArm : IDisposable
        {
            private readonly OnityObjectPool<PooledValue> m_onity;
            private readonly ObjectPool<PooledValue> m_inner;
            private readonly Action m_operation;
            private long m_externalGets;
            private long m_externalReleases;

            public Action Operation => m_operation;

            public SplitPoolArm(FieldInfo innerField, bool useOnityGet, bool useOnityRelease)
            {
                m_onity = new OnityObjectPool<PooledValue>(
                    () => new PooledValue(), collectionCheck: true,
                    defaultCapacity: 16, maxSize: 16, initialSize: 1);
                try
                {
                    m_inner = innerField.GetValue(m_onity) as ObjectPool<PooledValue>;
                    Check(m_inner != null, "The Onity pool backend is unavailable in this player.");
                    Func<PooledValue> unityGet = m_inner.Get;
                    Action<PooledValue> unityRelease = m_inner.Release;
                    Func<PooledValue> onityGet = m_onity.Get;
                    Action<PooledValue> onityRelease = m_onity.Release;

                    if (useOnityGet)
                    {
                        if (useOnityRelease)
                        {
                            m_operation = () =>
                            {
                                PooledValue item = onityGet();
                                onityRelease(item);
                            };
                        }
                        else
                        {
                            m_operation = () =>
                            {
                                PooledValue item = onityGet();
                                unityRelease(item);
                                Interlocked.Increment(ref m_externalReleases);
                            };
                        }
                    }
                    else if (useOnityRelease)
                    {
                        m_operation = () =>
                        {
                            PooledValue item = unityGet();
                            Interlocked.Increment(ref m_externalGets);
                            onityRelease(item);
                        };
                    }
                    else
                    {
                        m_operation = () =>
                        {
                            PooledValue item = unityGet();
                            Interlocked.Increment(ref m_externalGets);
                            unityRelease(item);
                            Interlocked.Increment(ref m_externalReleases);
                        };
                    }
                }
                catch
                {
                    m_onity.Dispose();
                    throw;
                }
            }

            public void Validate(int expectedPairs, PoolSplitBenchmarkReport report, int index)
            {
                OnityPoolDiagnosticsSnapshot state = m_onity.GetDiagnosticsSnapshot();
                report.finalCountAllByArm[index] = state.CountAll;
                report.finalCountActiveByArm[index] = state.CountActive;
                report.finalCountInactiveByArm[index] = state.CountInactive;
                report.onityGetCountByArm[index] = state.GetCount;
                report.onityReleaseCountByArm[index] = state.ReleaseCount;
                report.externalGetCountByArm[index] = m_externalGets;
                report.externalReleaseCountByArm[index] = m_externalReleases;
                Check(state.CountAll == 1 && state.CountActive == 0 && state.CountInactive == 1 &&
                    m_inner.CountAll == 1 && m_inner.CountActive == 0 && m_inner.CountInactive == 1,
                    $"Split arm {index} ended in an unexpected pool state.");
                Check(state.GetCount + m_externalGets == expectedPairs &&
                    state.ReleaseCount + m_externalReleases == expectedPairs,
                    $"Split arm {index} has unexpected operation counts.");
            }

            public void Dispose()
            {
                m_onity.Dispose();
            }
        }

        private enum BurstPath
        {
            Factory,
            Direct,
            Unity
        }

        private sealed class BurstPoolArm : IDisposable
        {
            private readonly Projectile[] m_createdItems = new Projectile[k_burstSize];
            private readonly Projectile[] m_held = new Projectile[k_burstSize];
            private readonly Action<Projectile, int, int> m_initialize;
            private readonly Func<int, int, Projectile> m_acquire;
            private readonly Action<Projectile> m_release;
            private readonly OnityObjectPool<Projectile> m_onity;
            private readonly ObjectPool<Projectile> m_unity;
            private readonly PooledFactory<int, int, Projectile> m_factory;
            private readonly Action m_operation;
            private int m_created;
            private int m_createAttempts;
            private int m_sequence;
            private long m_unityGets;
            private long m_unityReleases;
            private long m_resetCalls;
            private long m_checksum;

            public Action Operation => m_operation;

            public BurstPoolArm(BurstPath path)
            {
                m_initialize = (item, damage, ownerId) =>
                {
                    item.Damage = damage;
                    item.OwnerId = ownerId;
                };
                Func<Projectile> create = Create;
                Action<Projectile> reset = Reset;

                if (path == BurstPath.Unity)
                {
                    m_unity = new ObjectPool<Projectile>(create, null, reset, null, true,
                        k_burstSize, k_burstSize);
                    for (int i = 0; i < k_burstSize; i++)
                    {
                        m_held[i] = m_unity.Get();
                    }

                    for (int i = k_burstSize - 1; i >= 0; i--)
                    {
                        m_unity.Release(m_held[i]);
                    }

                    m_resetCalls = 0;
                    for (int i = 0; i < k_burstSize; i++)
                    {
                        m_createdItems[i].ResetCount = 0;
                    }

                    m_acquire = (damage, ownerId) =>
                    {
                        if (m_unity.CountInactive == 0 && m_unity.CountAll >= k_burstSize)
                        {
                            throw new InvalidOperationException("The Unity control has reached its fixed capacity.");
                        }

                        Projectile item = m_unity.Get();
                        Interlocked.Increment(ref m_unityGets);
                        m_initialize(item, damage, ownerId);
                        return item;
                    };
                    m_release = item =>
                    {
                        m_unity.Release(item);
                        Interlocked.Increment(ref m_unityReleases);
                    };
                }
                else
                {
                    m_onity = new OnityObjectPool<Projectile>(create, actionOnRelease: reset,
                        collectionCheck: true, defaultCapacity: k_burstSize, maxSize: k_burstSize,
                        initialSize: k_burstSize, fixedSize: true);
                    if (path == BurstPath.Factory)
                    {
                        m_factory = new PooledFactory<int, int, Projectile>(m_onity, m_initialize);
                        m_acquire = m_factory.Create;
                    }
                    else
                    {
                        m_acquire = (damage, ownerId) => m_onity.Get(damage, ownerId, m_initialize);
                    }

                    m_release = m_onity.Release;
                }

                Check(m_created == k_burstSize, "The burst pool did not prewarm 32 distinct objects.");
                m_operation = RunBurst;
            }

            public void ValidateInitial()
            {
                for (int i = 0; i < k_burstSize; i++)
                {
                    Projectile item = m_acquire(100 + i, 200 + i);
                    Check(item != null && item.Damage == 100 + i && item.OwnerId == 200 + i,
                        "The burst initializer did not apply both parameters.");
                    for (int previous = 0; previous < i; previous++)
                    {
                        Check(!ReferenceEquals(item, m_held[previous]),
                            "The burst returned an item that is already active.");
                    }

                    m_held[i] = item;
                }

                int createAttemptsBeforeLimit = m_createAttempts;
                bool rejected = false;
                try
                {
                    m_acquire(999, 999);
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }

                Check(rejected && m_createAttempts == createAttemptsBeforeLimit,
                    "The 33rd outstanding burst acquisition reached the creator or was not rejected.");
                Projectile released = m_held[0];
                m_release(released);
                Check(released.Damage == 0 && released.OwnerId == 0 && released.ResetCount == 1,
                    "The release callback did not reset its item exactly once.");
                Projectile reused = m_acquire(900, 901);
                Check(ReferenceEquals(reused, released) && reused.Damage == 900 && reused.OwnerId == 901,
                    "An item was not reused correctly after one return.");
                m_held[0] = reused;
                for (int i = k_burstSize - 1; i >= 0; i--)
                {
                    m_release(m_held[i]);
                }

                Check(m_resetCalls == k_burstSize + 1 && m_created == k_burstSize,
                    "The initial burst changed its creation or reset count.");
                m_sequence = 0;
                m_checksum = 0;
                m_resetCalls = 0;
                for (int i = 0; i < k_burstSize; i++)
                {
                    m_createdItems[i].ResetCount = 0;
                }
            }

            public void ValidateFinal(int totalBursts, PoolFactoryBurstReport report, int index)
            {
                OnityPoolDiagnosticsSnapshot onityState = m_onity != null
                    ? m_onity.GetDiagnosticsSnapshot()
                    : default;
                int active = m_onity != null ? onityState.CountActive : m_unity.CountActive;
                int inactive = m_onity != null ? onityState.CountInactive : m_unity.CountInactive;
                int countAll = m_onity != null ? onityState.CountAll : m_unity.CountAll;
                long gets = m_onity != null ? onityState.GetCount : m_unityGets;
                long releases = m_onity != null ? onityState.ReleaseCount : m_unityReleases;
                long expectedPairs = k_burstSize + 1L + (long)totalBursts * k_burstSize;
                long expectedChecksum = 1088L * totalBursts * (totalBursts + 1L) / 2L +
                    15872L * totalBursts;

                report.createdByArm[index] = m_created;
                report.activeByArm[index] = active;
                report.inactiveByArm[index] = inactive;
                report.getCountByArm[index] = gets;
                report.releaseCountByArm[index] = releases;
                report.resetCountByArm[index] = m_resetCalls;
                report.checksumByArm[index] = m_checksum;
                Check(m_created == k_burstSize && m_createAttempts == k_burstSize &&
                    countAll == k_burstSize &&
                    active == 0 && inactive == k_burstSize,
                    $"Burst arm {index} ended in an unexpected pool state.");
                Check(gets == expectedPairs && releases == expectedPairs &&
                    m_resetCalls == (long)totalBursts * k_burstSize,
                    $"Burst arm {index} has unexpected operation or reset counts.");
                Check(m_sequence == totalBursts && m_checksum == expectedChecksum,
                    $"Burst arm {index} has an unexpected payload checksum.");
                for (int i = 0; i < k_burstSize; i++)
                {
                    Check(m_createdItems[i].ResetCount == totalBursts,
                        $"Burst arm {index} did not reset every item once per burst.");
                }
            }

            public void Dispose()
            {
                m_onity?.Dispose();
                m_unity?.Dispose();
            }

            private Projectile Create()
            {
                m_createAttempts++;
                Check(m_created < k_burstSize, "The burst pool created more than 32 objects.");
                Projectile item = new Projectile();
                m_createdItems[m_created++] = item;
                return item;
            }

            private void Reset(Projectile item)
            {
                item.Damage = 0;
                item.OwnerId = 0;
                item.ResetCount++;
                m_resetCalls++;
            }

            private void RunBurst()
            {
                int sequence = ++m_sequence;
                long checksum = 0;
                for (int i = 0; i < k_burstSize; i++)
                {
                    Projectile item = m_acquire(sequence + i, sequence * 3 + i);
                    m_held[i] = item;
                    checksum += item.Damage * 31L + item.OwnerId;
                }

                for (int i = k_burstSize - 1; i >= 0; i--)
                {
                    m_release(m_held[i]);
                }

                m_checksum += checksum;
            }
        }

        private enum IntArrayBurstPath
        {
            Factory,
            Direct,
            Unity,
            Zenject
        }

        private sealed class IntArrayBurstArm : IDisposable
        {
            private readonly int[][] m_createdItems = new int[k_burstSize][];
            private readonly int[][] m_held = new int[k_burstSize][];
            private readonly IntArrayBurstPath m_path;
            private readonly Action<int[], int, int> m_initialize;
            private readonly Func<int, int, int[]> m_acquire;
            private readonly Action<int[]> m_release;
            private readonly OnityObjectPool<int[]> m_onity;
            private readonly ObjectPool<int[]> m_unity;
            private readonly IDisposable m_zenject;
            private readonly Func<int> m_zenjectActive;
            private readonly Func<int> m_zenjectInactive;
            private readonly Func<int> m_zenjectTotal;
            private readonly Action m_operation;
            private int m_created;
            private int m_createAttempts;
            private int m_sequence;
            private long m_unityGets;
            private long m_unityReleases;
            private long m_resetCalls;
            private long m_checksum;

            public Action Operation => m_operation;
            public string BridgeAssembly { get; }
            public string BridgeProtocol { get; }
            public bool StripAssertsInBuilds { get; }
            public bool InternalProfiling { get; }

            public IntArrayBurstArm(IntArrayBurstPath path)
            {
                m_path = path;
                m_initialize = (item, damage, ownerId) =>
                {
                    item[0] = damage;
                    item[1] = ownerId;
                };
                Func<int[]> create = Create;
                Action<int[]> reset = Reset;

                if (path == IntArrayBurstPath.Unity)
                {
                    m_unity = new ObjectPool<int[]>(create, null, reset, null, false,
                        k_burstSize, k_burstSize);
                    for (int i = 0; i < k_burstSize; i++)
                    {
                        m_held[i] = m_unity.Get();
                    }

                    for (int i = k_burstSize - 1; i >= 0; i--)
                    {
                        m_unity.Release(m_held[i]);
                    }

                    m_resetCalls = 0;
                    for (int i = 0; i < k_burstSize; i++)
                    {
                        m_createdItems[i][2] = 0;
                    }

                    m_acquire = (damage, ownerId) =>
                    {
                        if (m_unity.CountInactive == 0 && m_unity.CountAll >= k_burstSize)
                        {
                            throw new InvalidOperationException("The Unity control reached fixed capacity.");
                        }

                        int[] item = m_unity.Get();
                        Interlocked.Increment(ref m_unityGets);
                        m_initialize(item, damage, ownerId);
                        return item;
                    };
                    m_release = item =>
                    {
                        m_unity.Release(item);
                        Interlocked.Increment(ref m_unityReleases);
                    };
                }
                else if (path == IntArrayBurstPath.Zenject)
                {
                    Type bridgeType = Type.GetType(
                        "Onity.Benchmarks.OnityDiBenchmarkPlayerRunner, Onity.Benchmarks", false);
                    Check(bridgeType != null &&
                        string.Equals(bridgeType.Assembly.GetName().Name, "Onity.Benchmarks", StringComparison.Ordinal),
                        "The Zenject benchmark bridge assembly is missing from this player.");
                    MethodInfo bridgeMethod = bridgeType.GetMethod("CreateZenjectIntArrayPool",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    Check(bridgeMethod != null, "The Zenject benchmark bridge method is missing.");
                    object[] bridge = bridgeMethod.Invoke(null, new object[] { create, m_initialize, reset })
                        as object[];
                    Check(bridge != null && bridge.Length == 9,
                        "The Zenject benchmark bridge protocol is invalid.");
                    BridgeAssembly = bridgeType.Assembly.GetName().Name;
                    BridgeProtocol = bridge[0] as string;
                    Check(string.Equals(BridgeProtocol, k_zenjectBridgeProtocol, StringComparison.Ordinal),
                        "The Zenject benchmark bridge version does not match this player.");
                    m_acquire = bridge[1] as Func<int, int, int[]>;
                    m_release = bridge[2] as Action<int[]>;
                    m_zenjectActive = bridge[3] as Func<int>;
                    m_zenjectInactive = bridge[4] as Func<int>;
                    m_zenjectTotal = bridge[5] as Func<int>;
                    m_zenject = bridge[6] as IDisposable;
                    Check(m_acquire != null && m_release != null && m_zenjectActive != null &&
                        m_zenjectInactive != null && m_zenjectTotal != null && m_zenject != null &&
                        bridge[7] is bool && bridge[8] is bool,
                        "The Zenject benchmark bridge returned invalid delegates or metadata.");
                    StripAssertsInBuilds = (bool)bridge[7];
                    InternalProfiling = (bool)bridge[8];
                }
                else
                {
                    m_onity = new OnityObjectPool<int[]>(create, actionOnRelease: reset,
                        collectionCheck: true, defaultCapacity: k_burstSize, maxSize: k_burstSize,
                        initialSize: k_burstSize, fixedSize: true);
                    if (path == IntArrayBurstPath.Factory)
                    {
                        PooledFactory<int, int, int[]> factory =
                            new PooledFactory<int, int, int[]>(m_onity, m_initialize);
                        m_acquire = factory.Create;
                    }
                    else
                    {
                        m_acquire = (damage, ownerId) =>
                            m_onity.Get(damage, ownerId, m_initialize);
                    }

                    m_release = m_onity.Release;
                }

                Check(m_created == k_burstSize,
                    "The int-array burst pool did not prewarm 32 distinct objects.");
                m_operation = RunBurst;
            }

            public void ValidateInitial(PoolZenjectBurstReport report, int index)
            {
                for (int i = 0; i < k_burstSize; i++)
                {
                    int[] item = m_acquire(100 + i, 200 + i);
                    Check(item != null && item.Length == 3 && item[0] == 100 + i && item[1] == 200 + i,
                        $"Int-array burst arm {index} did not apply both parameters.");
                    for (int previous = 0; previous < i; previous++)
                    {
                        Check(!ReferenceEquals(item, m_held[previous]),
                            $"Int-array burst arm {index} returned an already active item.");
                    }

                    m_held[i] = item;
                }

                int createAttemptsBeforeLimit = m_createAttempts;
                bool rejectedCapacity = false;
                try
                {
                    m_acquire(999, 999);
                }
                catch (Exception exception)
                {
                    rejectedCapacity = m_path == IntArrayBurstPath.Zenject
                        ? exception.GetType().FullName == "Zenject.PoolExceededFixedSizeException"
                        : exception is InvalidOperationException;
                    report.capacityExceptionByArm[index] = exception.GetType().FullName;
                }

                Check(rejectedCapacity && m_createAttempts == createAttemptsBeforeLimit,
                    $"Int-array burst arm {index} did not reject its 33rd acquisition before creation.");
                int[] released = m_held[0];
                m_release(released);
                Check(released[0] == 0 && released[1] == 0 && released[2] == 1,
                    $"Int-array burst arm {index} did not reset its returned item once.");
                int[] reused = m_acquire(900, 901);
                Check(ReferenceEquals(reused, released) && reused[0] == 900 && reused[1] == 901,
                    $"Int-array burst arm {index} did not reuse and configure the returned item.");
                m_held[0] = reused;
                for (int i = k_burstSize - 1; i >= 0; i--)
                {
                    m_release(m_held[i]);
                }

                Check(m_resetCalls == k_burstSize + 1 && m_created == k_burstSize,
                    $"Int-array burst arm {index} changed its creation or reset count.");
                GetPoolState(out int totalBefore, out int activeBefore, out int inactiveBefore,
                    out long getsBefore, out long releasesBefore);
                long resetsBefore = m_resetCalls;
                int itemResetsBefore = reused[2];
                if (m_path != IntArrayBurstPath.Unity)
                {
                    bool rejectedDuplicate = false;
                    try
                    {
                        m_release(reused);
                    }
                    catch (Exception exception)
                    {
                        rejectedDuplicate = m_path == IntArrayBurstPath.Zenject
                            ? exception.GetType().FullName == "Zenject.ZenjectException" &&
                                exception.Message.IndexOf("twice", StringComparison.OrdinalIgnoreCase) >= 0
                            : exception is InvalidOperationException;
                        report.duplicateExceptionByArm[index] = exception.GetType().FullName;
                    }

                    report.duplicateRejectedByArm[index] = rejectedDuplicate;
                    Check(rejectedDuplicate, $"Int-array burst arm {index} accepted a duplicate return.");
                    GetPoolState(out int totalAfter, out int activeAfter, out int inactiveAfter,
                        out long getsAfter, out long releasesAfter);
                    Check(totalAfter == totalBefore && activeAfter == activeBefore &&
                        inactiveAfter == inactiveBefore && getsAfter == getsBefore &&
                        releasesAfter == releasesBefore && m_resetCalls == resetsBefore &&
                        reused[2] == itemResetsBefore,
                        $"Int-array burst arm {index} mutated state after a duplicate return.");
                }

                m_sequence = 0;
                m_checksum = 0;
                m_resetCalls = 0;
                for (int i = 0; i < k_burstSize; i++)
                {
                    m_createdItems[i][2] = 0;
                }
            }

            public void ValidateFinal(int totalBursts, PoolZenjectBurstReport report, int index)
            {
                GetPoolState(out int total, out int active, out int inactive,
                    out long gets, out long releases);
                long expectedPairs = k_burstSize + 1L + (long)totalBursts * k_burstSize;
                long expectedChecksum = 1088L * totalBursts * (totalBursts + 1L) / 2L +
                    15872L * totalBursts;

                report.createdByArm[index] = m_created;
                report.activeByArm[index] = active;
                report.inactiveByArm[index] = inactive;
                report.totalByArm[index] = total;
                report.getCountByArm[index] = gets;
                report.releaseCountByArm[index] = releases;
                report.resetCountByArm[index] = m_resetCalls;
                report.checksumByArm[index] = m_checksum;
                Check(m_created == k_burstSize && m_createAttempts == k_burstSize &&
                    total == k_burstSize && active == 0 && inactive == k_burstSize,
                    $"Int-array burst arm {index} ended in an unexpected pool state.");
                Check((gets == -1 && releases == -1) ||
                    (gets == expectedPairs && releases == expectedPairs),
                    $"Int-array burst arm {index} has unexpected operation counts.");
                Check(m_resetCalls == (long)totalBursts * k_burstSize &&
                    m_sequence == totalBursts && m_checksum == expectedChecksum,
                    $"Int-array burst arm {index} has unexpected reset or payload counts.");
                for (int i = 0; i < k_burstSize; i++)
                {
                    Check(m_createdItems[i][2] == totalBursts,
                        $"Int-array burst arm {index} did not reset each item once per burst.");
                }
            }

            public void Dispose()
            {
                m_onity?.Dispose();
                m_unity?.Dispose();
                m_zenject?.Dispose();
            }

            private int[] Create()
            {
                m_createAttempts++;
                Check(m_created < k_burstSize, "The int-array pool created more than 32 objects.");
                int[] item = new int[3];
                m_createdItems[m_created++] = item;
                return item;
            }

            private void Reset(int[] item)
            {
                item[0] = 0;
                item[1] = 0;
                item[2]++;
                m_resetCalls++;
            }

            private void RunBurst()
            {
                int sequence = ++m_sequence;
                long checksum = 0;
                for (int i = 0; i < k_burstSize; i++)
                {
                    int[] item = m_acquire(sequence + i, sequence * 3 + i);
                    m_held[i] = item;
                    checksum += item[0] * 31L + item[1];
                }

                for (int i = k_burstSize - 1; i >= 0; i--)
                {
                    m_release(m_held[i]);
                }

                m_checksum += checksum;
            }

            private void GetPoolState(out int total, out int active, out int inactive,
                out long gets, out long releases)
            {
                if (m_onity != null)
                {
                    OnityPoolDiagnosticsSnapshot snapshot = m_onity.GetDiagnosticsSnapshot();
                    total = snapshot.CountAll;
                    active = snapshot.CountActive;
                    inactive = snapshot.CountInactive;
                    gets = snapshot.GetCount;
                    releases = snapshot.ReleaseCount;
                }
                else if (m_unity != null)
                {
                    total = m_unity.CountAll;
                    active = m_unity.CountActive;
                    inactive = m_unity.CountInactive;
                    gets = m_unityGets;
                    releases = m_unityReleases;
                }
                else
                {
                    total = m_zenjectTotal();
                    active = m_zenjectActive();
                    inactive = m_zenjectInactive();
                    gets = -1;
                    releases = -1;
                }
            }
        }

        private sealed class Projectile
        {
            public int Damage;
            public int OwnerId;
            public int ResetCount;
        }

        private sealed class PooledValue
        {
        }
    }
}
