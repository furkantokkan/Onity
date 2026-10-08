using System;
using System.Globalization;
using System.IO;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Onity.Benchmarks
{
    /// <summary>
    /// Player-side entry point of the messaging comparison (Onity.Messaging vs MessagePipe). A Player
    /// started with <c>-onityRunMessagingBenchmark</c> runs every scenario (or the one named by
    /// <c>-onityMessagingBenchmarkScenario</c>), writes the JSON report to
    /// <c>-onityMessagingBenchmarkOutput</c> and quits with exit code 0 when every golden check passed, or 1
    /// otherwise. <c>-onityMessagingBenchmarkSelfTest</c> runs tiny counts and marks the report as a
    /// self-test; <c>-onityMessagingBenchmarkSamples</c> overrides the measured sample count;
    /// <c>-onityMessagingBenchmarkBuildMetadata</c> names the build sidecar (default: the
    /// <c>&lt;exe&gt;.build.json</c> next to the Player).
    /// </summary>
    public static class OnityMessagingBenchmarkPlayerRunner
    {
        /// <summary>Starts the benchmark when present on the Player command line.</summary>
        public const string RunArgument = "-onityRunMessagingBenchmark";

        /// <summary>Path of the JSON report.</summary>
        public const string OutputArgument = "-onityMessagingBenchmarkOutput";

        /// <summary>Marks a harness self-test: tiny counts, never performance evidence.</summary>
        public const string SelfTestArgument = "-onityMessagingBenchmarkSelfTest";

        /// <summary>Measured samples per library and scenario (default 15, self-test 3).</summary>
        public const string SamplesArgument = "-onityMessagingBenchmarkSamples";

        /// <summary>Runs only the scenario with this id.</summary>
        public const string ScenarioArgument = "-onityMessagingBenchmarkScenario";

        /// <summary>Path of the build sidecar written by the build runner.</summary>
        public const string BuildMetadataArgument = "-onityMessagingBenchmarkBuildMetadata";

        /// <summary>MessagePipe version the harness is written against; reports also record the loaded assembly.</summary>
        public const string MessagePipeVersion = "1.8.1";

        /// <summary>Flavor name of MessagePipe's NuGet build: the precompiled netstandard2.0 <c>MessagePipe.dll</c> (gate A).</summary>
        public const string NuGetFlavor = "nuget-netstandard2.0";

        /// <summary>Flavor name of MessagePipe's Unity package: its sources compiled by Unity against UniTask (gate B).</summary>
        public const string UnityPackageFlavor = "unity-package";

        private const string k_setupConstruction =
            "Once per workload (the singletons AddMessagePipe registers): new MessagePipeOptions() "
            + "with defaults (no global filters, EnableCaptureStackTrace false, HandlingSubscribeDisposedPolicy Ignore), "
            + "new MessagePipeDiagnosticsInfo(options), new FilterAttachedMessageHandlerFactory(options, "
            + "new AttributeFilterProvider<MessageHandlerFilterAttribute>(), provider) and the matching "
            + "FilterAttachedAsyncMessageHandlerFactory, where provider is an IServiceProvider that throws if asked "
            + "(no filter is registered, so MessagePipe never asks). Per sample: a new MessageBrokerCore<T>, "
            + "MessageBrokerCore<TKey, T> or AsyncMessageBrokerCore<T> with one MessageBroker / AsyncMessageBroker wrapper "
            + "for the publisher interface and one for the subscriber interface, the types AddMessagePipe registers for "
            + "IPublisher, ISubscriber, IPublisher<TKey, T>, ISubscriber<TKey, T>, IAsyncPublisher and IAsyncSubscriber. ";

        private const string k_setupStrategy =
            " with no filters. Async publishes pass AsyncPublishStrategy.Sequential explicitly (the default is Parallel; "
            + "Onity's async channel is sequential).";

#if ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK
        /// <summary>
        /// The MessagePipe flavor this assembly is compiled against: <see cref="UnityPackageFlavor"/>, selected by
        /// the host-only define <c>ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK</c>.
        /// </summary>
        public const string MessagePipeFlavor = UnityPackageFlavor;

        /// <summary>Where the MessagePipe assembly comes from.</summary>
        public const string MessagePipeSource =
            "MessagePipe 1.8.1 Unity package: the sources of MessagePipe.1.8.1.unitypackage from the official Cysharp "
            + "GitHub release 1.8.1, compiled by Unity against UniTask; the build sidecar records the package SHA-256, "
            + "the verified package entries and the UniTask version";

        /// <summary>How the MessagePipe brokers are created; recorded in every report and build sidecar.</summary>
        public const string MessagePipeSetup =
            "No DI container. The Unity package has the same public constructors as the NuGet build, so the public broker "
            + "types are constructed directly, exactly as for the NuGet build; its BuiltinContainerBuilder is not used. "
            + k_setupConstruction
            + "Handlers use the Subscribe(Action<T>) and Subscribe(Func<T, CancellationToken, UniTask>) extensions"
            + k_setupStrategy;

        /// <summary>The async API of the measured MessagePipe build.</summary>
        public const string MessagePipeAsyncApi =
            "MessagePipe's Unity package (UniTask build): IAsyncPublisher.PublishAsync returns Cysharp.Threading.Tasks.UniTask, "
            + "and AsyncMessageBrokerCore.PublishAsync is an async UniTask method (UniTask's builder calls MoveNext directly, "
            + "with no ExecutionContext scope). Its async handlers are Func<T, CancellationToken, UniTask> returning "
            + "default(UniTask), an already completed UniTask; the timed loop checks UniTask.Status == Succeeded and calls "
            + "GetAwaiter().GetResult(). The sync code is the same source as the NuGet build. Onity's rows are unchanged.";
#else
        /// <summary>
        /// The MessagePipe flavor this assembly is compiled against: <see cref="NuGetFlavor"/> (the host does not
        /// define <c>ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK</c>).
        /// </summary>
        public const string MessagePipeFlavor = NuGetFlavor;

        /// <summary>Where the MessagePipe assembly comes from.</summary>
        public const string MessagePipeSource =
            "MessagePipe.dll from the official NuGet package MessagePipe 1.8.1 (lib/netstandard2.0, repository commit "
            + "e901746b927bcd84ad94d642512a7a7a7600d15b), taken from the local NuGet cache";

        /// <summary>How the MessagePipe brokers are created; recorded in every report and build sidecar.</summary>
        public const string MessagePipeSetup =
            "No DI container. The netstandard2.0 MessagePipe.dll has no BuiltinContainerBuilder, so the public broker types "
            + "are constructed directly. "
            + k_setupConstruction
            + "Handlers use the Subscribe(Action<T>) and Subscribe(Func<T, CancellationToken, ValueTask>) extensions"
            + k_setupStrategy;

        /// <summary>The difference between the measured async API and the one MessagePipe's Unity package compiles.</summary>
        public const string MessagePipeAsyncApi =
            "The NuGet build measured here exposes System.Threading.Tasks.ValueTask in its async API; MessagePipe's Unity "
            + "package compiles the same sync source but replaces ValueTask with UniTask in the async API. The async rows "
            + "therefore measure the ValueTask build.";
#endif

        private static bool s_isRunning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            s_isRunning = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (s_isRunning || !OnityMessagingBenchmarkOptions.HasArgument(args, RunArgument))
            {
                return;
            }

            s_isRunning = true;
            Application.runInBackground = true;
            Application.targetFrameRate = -1;
            int exitCode = Execute(args);
            Application.Quit(exitCode);
        }

        private static int Execute(string[] args)
        {
            OnityMessagingBenchmarkReport report = new OnityMessagingBenchmarkReport();
            string outputPath = null;
            try
            {
                outputPath = OnityMessagingBenchmarkOptions.ResolveOutputPath(args);
                report.selfTest = OnityMessagingBenchmarkOptions.HasArgument(args, SelfTestArgument);
                OnityMessagingBenchmarkOptions options = OnityMessagingBenchmarkOptions.Parse(args);
                Debug.Log("Onity messaging benchmark started" + (options.SelfTest ? " (self-test)" : string.Empty)
                    + ", report " + outputPath);
                OnityMessagingBenchmarkRunner.Run(options, report);
            }
            catch (Exception exception)
            {
                report.failure = exception.ToString();
                Debug.LogException(exception);
            }

            report.generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            bool succeeded = report.completed && report.goldenPassed && string.IsNullOrEmpty(report.failure);
            if (outputPath == null)
            {
                Debug.LogError("Onity messaging benchmark: no report path; nothing was written.");
                return 1;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return 1;
            }

            Debug.Log("Onity messaging benchmark " + (succeeded ? "completed" : "FAILED") + ": " + outputPath);
            return succeeded ? 0 : 1;
        }
    }
}
