using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;

namespace Onity.Benchmarks
{
    /// <summary>Graphics-enabled functional EOF checks; no timing or allocation claim is made.</summary>
    public sealed class OnityTaskEndOfFramePlayerRunner : MonoBehaviour
    {
        private const int k_expectedCaseCount = 6;
        private const double k_caseDeadlineSeconds = 10;
        private const double k_suiteDeadlineSeconds = 60;
        private const BindingFlags k_staticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags k_instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo s_host = typeof(OnityTaskPlayerLoop).GetField("s_endOfFrameRunner", k_staticPrivate);
        private static readonly FieldInfo s_taskState = typeof(OnityTask).GetField("m_state", k_instance);
        private string m_output;
        private Action<string, Exception> m_completed;
        private readonly Report m_report = new Report();
        private readonly Stopwatch m_suiteClock = new Stopwatch();
        private readonly Stopwatch m_caseClock = new Stopwatch();
        private IEnumerator m_case;
        private int m_caseIndex;
        private int m_main;
        private bool m_finished;
        private Exception m_failure;
        private Camera m_camera;
        private RenderTexture m_target;
        private Texture2D m_pixel;
        private RenderTexture m_originalActive;
        private int m_renderFrame = -1;
        private int m_lateFrame = -1;
        private int m_pixelRegistration = -1;
        private Color m_expectedColor;
        private Outcome m_pixelWait;

        /// <summary>Starts the graphics verification and reports its JSON path and any failure.</summary>
        public static void Run(string output, Action<string, Exception> completed)
        {
            var owner = new GameObject("Onity EOF Graphics Verification");
            DontDestroyOnLoad(owner);
            var runner = owner.AddComponent<OnityTaskEndOfFramePlayerRunner>();
            runner.m_output = Path.GetFullPath(output);
            runner.m_completed = completed;
        }

        private void OnEnable()
        {
            Camera.onPostRender += CameraRendered;
            RenderPipelineManager.endCameraRendering += PipelineCameraRendered;
        }

        private void OnDisable()
        {
            Camera.onPostRender -= CameraRendered;
            RenderPipelineManager.endCameraRendering -= PipelineCameraRendered;
        }

        private void Start()
        {
            m_main = Thread.CurrentThread.ManagedThreadId;
            m_originalActive = RenderTexture.active;
            m_suiteClock.Start();
            try
            {
                m_report.environment = OnityTaskBenchmarkEnvironment.Capture();
                m_report.expectedBackend = Argument("-onityTaskExpectedBackend");
                m_report.expectedBuildGuid = Argument("-onityTaskExpectedBuildGuid");
                m_report.isBatchMode = Application.isBatchMode;
                m_report.hasBatchModeArgument = HasArgument("-batchmode");
                m_report.hasNoGraphicsArgument = HasArgument("-nographics");
                Require(!Application.isEditor && !Application.isBatchMode && !UnityEngine.Debug.isDebugBuild,
                    "EOF proof requires a non-development, non-batch Standalone Player.");
                Require(!HasArgument("-batchmode") && !HasArgument("-nographics"), "Forbidden EOF launch flags.");
                Require(!string.IsNullOrEmpty(m_report.expectedBackend) && !string.IsNullOrEmpty(m_report.expectedBuildGuid),
                    "Expected build/backend arguments are required; there is no self-derived fallback.");
                Require(m_report.environment.scriptingBackend == m_report.expectedBackend,
                    "Actual scripting backend differs from launcher expectation.");
                Require(string.Equals(Application.buildGUID, m_report.expectedBuildGuid, StringComparison.OrdinalIgnoreCase),
                    "Actual build GUID differs from the freshly built Player.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Null graphics cannot prove EOF rendering.");
                m_report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
                m_report.graphicsDevice = SystemInfo.graphicsDeviceName;
                m_report.pipeline = GraphicsSettings.currentRenderPipeline != null
                    ? GraphicsSettings.currentRenderPipeline.GetType().FullName : "Built-in (no active pipeline asset)";
                m_report.colorSpace = QualitySettings.activeColorSpace.ToString();
                m_report.screenWidth = Screen.width;
                m_report.screenHeight = Screen.height;
                m_target = new RenderTexture(16, 16, 16, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1, useMipMap = false, autoGenerateMips = false
                };
                Require(m_target.Create() && m_target.IsCreated(), "Owned render target could not be created.");
                m_pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                m_camera = gameObject.AddComponent<Camera>();
                m_camera.clearFlags = CameraClearFlags.SolidColor;
                m_camera.backgroundColor = Color.black;
                m_camera.cullingMask = 0;
                m_camera.allowHDR = false;
                m_camera.allowMSAA = false;
                m_camera.targetTexture = m_target;
                m_report.textureWidth = m_target.width;
                m_report.textureHeight = m_target.height;
                OnityTaskPlayerLoop.Initialize();
            }
            catch (Exception exception)
            {
                Finish(exception);
            }
        }

        private void Update()
        {
            if (m_finished || !m_suiteClock.IsRunning)
            {
                return;
            }
            try
            {
                Require(m_suiteClock.Elapsed.TotalSeconds < k_suiteDeadlineSeconds, "EOF suite deadline exceeded.");
                if (m_failure != null)
                {
                    throw m_failure;
                }
                if (m_case == null)
                {
                    if (m_caseIndex == k_expectedCaseCount)
                    {
                        Finish(null);
                        return;
                    }
                    m_caseClock.Restart();
                    m_case = CreateCase(m_caseIndex);
                    OnityTaskBenchmarkPlayerRunner.WriteStartupMarker("eof-case-start", Name(m_caseIndex));
                }
                Require(m_caseClock.Elapsed.TotalSeconds < k_caseDeadlineSeconds, "EOF case deadline exceeded: " + Name(m_caseIndex));
                if (!m_case.MoveNext())
                {
                    (m_case as IDisposable)?.Dispose();
                    m_case = null;
                    m_report.cases.Add(new CaseResult { name = Name(m_caseIndex), passed = true });
                    OnityTaskBenchmarkPlayerRunner.WriteStartupMarker("eof-case-pass", Name(m_caseIndex));
                    m_caseIndex++;
                }
            }
            catch (Exception exception)
            {
                Finish(exception);
            }
        }

        private void LateUpdate()
        {
            if (m_finished || m_pixelWait == null || m_pixelRegistration != Time.frameCount)
            {
                return;
            }
            try
            {
                Require(m_pixelWait.count == 0, "EOF published before LateUpdate.");
                m_camera.backgroundColor = m_expectedColor;
                m_lateFrame = Time.frameCount;
            }
            catch (Exception exception)
            {
                m_failure = exception;
            }
        }

        private void CameraRendered(Camera camera)
        {
            if (camera == m_camera)
            {
                m_renderFrame = Time.frameCount;
            }
        }

        private void PipelineCameraRendered(ScriptableRenderContext context, Camera camera)
        {
            CameraRendered(camera);
        }

        private IEnumerator CreateCase(int index)
        {
            switch (index)
            {
                case 0: return Pixels();
                case 1: return Reentrant();
                case 2: return StalledCancellation();
                case 3: return Reuse();
                case 4: return ManualReplacement();
                default: return GlobalRetirement();
            }
        }

        private IEnumerator Pixels()
        {
            yield return null;
            yield return null;
            for (int sample = 0; sample < 4; sample++)
            {
                m_expectedColor = sample % 2 == 0 ? Color.red : Color.blue;
                m_pixelRegistration = Time.frameCount;
                m_pixelWait = Observe(OnityTask.WaitForEndOfFrame(), result =>
                {
                    Require(result.error == null && result.thread == m_main, "Pixel EOF did not succeed on main.");
                    Require(m_lateFrame == result.frame && m_renderFrame == result.frame,
                        "EOF frame did not follow this camera's LateUpdate and render callback.");
                    Color pixel;
                    RenderTexture previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = m_target;
                        m_pixel.ReadPixels(new Rect(8, 8, 1, 1), 0, 0, false);
                        pixel = m_pixel.GetPixel(0, 0);
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                    }
                    bool red = m_expectedColor.r > 0.5f;
                    Require(pixel.g < 0.2f && (red ? pixel.r > 0.8f && pixel.b < 0.2f : pixel.b > 0.8f && pixel.r < 0.2f),
                        "EOF readback did not contain the alternating scheduled camera clear color.");
                    m_report.pixels.Add(new PixelResult
                    {
                        registeredFrame = m_pixelRegistration, lateFrame = m_lateFrame,
                        renderedFrame = m_renderFrame, completedFrame = result.frame,
                        thread = result.thread, red = pixel.r, green = pixel.g, blue = pixel.b,
                        expected = red ? "red" : "blue"
                    });
                });
                while (m_pixelWait.count == 0)
                {
                    yield return null;
                }
                Require(m_pixelWait.error == null, "Pixel request failed.");
                m_pixelWait = null;
            }
        }

        private IEnumerator Reentrant()
        {
            Outcome child = null;
            Outcome first = Observe(OnityTask.WaitForEndOfFrame(), result =>
            {
                Require(result.error == null, "First reentrant EOF failed.");
                child = Observe(OnityTask.WaitForEndOfFrame());
            });
            while (child == null || child.count == 0)
            {
                yield return null;
            }
            Require(first.count == 1 && child.count == 1 && child.error == null && child.frame > first.frame,
                "Reentrant request did not wait for the next EOF drain.");
        }

        private IEnumerator StalledCancellation()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                Outcome pending = Observe(OnityTask.WaitForEndOfFrame(cancellation.Token));
                Component host = Host();
                StopPump(host);
                Task worker = null;
                try
                {
                    worker = Task.Run(() => cancellation.Cancel());
                    Require(worker.Wait(TimeSpan.FromSeconds(5)), "EOF cancellation worker timed out.");
                    Require(pending.count == 0, "Worker cancellation published outside the main Update pass.");
                    while (pending.count == 0)
                    {
                        yield return null;
                    }
                    Require(pending.error is OperationCanceledException canceled
                        && canceled.CancellationToken == cancellation.Token && pending.thread == m_main && pending.count == 1,
                        "Stalled coroutine cancellation did not preserve its token and main-thread publication.");
                }
                finally
                {
                    cancellation.Cancel();
                    if (worker != null)
                    {
                        Require(worker.Wait(TimeSpan.FromSeconds(5)), "EOF worker cleanup timed out.");
                    }
                    RetireHost(host);
                }
            }
        }

        private IEnumerator Reuse()
        {
            using (var originalCancellation = new CancellationTokenSource())
            using (var freshCancellation = new CancellationTokenSource())
            {
                OnityTask firstTask = OnityTask.WaitForEndOfFrame(originalCancellation.Token);
                object originalSource = s_taskState.GetValue(firstTask);
                object freshSource = null;
                Outcome fresh = null;
                Outcome first = Observe(firstTask, result =>
                {
                    Require(result.error == null, "First reuse request failed.");
                    OnityTask next = OnityTask.WaitForEndOfFrame(freshCancellation.Token);
                    freshSource = s_taskState.GetValue(next);
                    fresh = Observe(next);
                    originalCancellation.Cancel();
                });
                try
                {
                    while (fresh == null || fresh.count == 0)
                    {
                        yield return null;
                    }
                    Require(ReferenceEquals(originalSource, freshSource), "EOF test did not exercise same-source reuse.");
                    Require(fresh.error == null && fresh.frame > first.frame && first.count == 1 && fresh.count == 1,
                        "Disposed old registration or old pump activity affected a reused source.");
                    Require(Catch(() => firstTask.GetAwaiter().GetResult()) is InvalidOperationException,
                        "Consumed EOF token did not become stale.");
                }
                finally
                {
                    RetireHost(Host());
                    freshCancellation.Cancel();
                }
            }
        }

        private IEnumerator ManualReplacement()
        {
            using (var token = new CancellationTokenSource())
            {
                var timerProducer = new OnityTaskCompletionSource<int>();
                Outcome timing = Observe(OnityTask.NextFrame(OnityPlayerLoopTiming.Update, default));
                Outcome timer = Observe(timerProducer.Task.TimeoutWithoutException(1000f));
                Exception reentry = null;
                Outcome oldPlain = Observe(OnityTask.WaitForEndOfFrame(), result =>
                {
                    reentry = Catch(() => OnityTask.WaitForEndOfFrame());
                });
                Outcome oldToken = Observe(OnityTask.WaitForEndOfFrame(token.Token));
                Component oldHost = Host();
                try
                {
                    ((Behaviour)oldHost).enabled = false;
                    Require(oldPlain.error is OperationCanceledException
                        && oldToken.error is OperationCanceledException canceled && canceled.CancellationToken == token.Token,
                        "Manual host retirement did not cancel only its EOF waits.");
                    Require(reentry is InvalidOperationException && !token.IsCancellationRequested,
                        "Retirement allowed reentry or canceled the caller's token owner.");
                    Require(timing.count == 0 && timer.count == 0, "Manual EOF retirement affected independent lanes.");
                    Outcome replacement = Observe(OnityTask.WaitForEndOfFrame());
                    Require(!ReferenceEquals(Host(), oldHost), "EOF host was not replaced.");
                    timerProducer.TrySetResult(7);
                    while (replacement.count == 0 || timing.count == 0)
                    {
                        yield return null;
                    }
                    Require(replacement.error == null && timing.error == null && timer.error == null,
                        "Replacement EOF/timing/timeout lanes did not remain live.");
                    // Retire an executing pump in its first successful publication, then replace it.
                    Outcome child = null;
                    int replacedFrame = -1;
                    int firstPublication = 0;
                    Component activeHost = null;
                    Action<Outcome> replace = result =>
                    {
                        if (result.error == null && Interlocked.CompareExchange(ref firstPublication, 1, 0) == 0)
                        {
                            replacedFrame = result.frame;
                            RetireHost(activeHost);
                            child = Observe(OnityTask.WaitForEndOfFrame());
                        }
                    };
                    Outcome one = Observe(OnityTask.WaitForEndOfFrame(), replace);
                    Outcome two = Observe(OnityTask.WaitForEndOfFrame(), replace);
                    activeHost = Host();
                    while (child == null || child.count == 0)
                    {
                        yield return null;
                    }
                    Require(one.count == 1 && two.count == 1 && child.error == null && child.frame > replacedFrame,
                        "Old executing pump drained or corrupted the replacement host.");
                    Require((one.error == null) != (two.error == null), "Old pump did not stop after its host was retired.");
                }
                finally
                {
                    timerProducer.TrySetResult(7);
                    RetireHost(Host());
                    RetireHost(oldHost);
                }
            }
        }

        private IEnumerator GlobalRetirement()
        {
            for (int failureMode = 0; failureMode < 2; failureMode++)
            {
                var producer = new OnityTaskCompletionSource<int>();
                Component oldHost = null;
                int publications = 0;
                Action<Outcome> first = result =>
                {
                    if (++publications == 1)
                    {
                        Require(Host() == null, "EOF host was not detached before global publication.");
                        Require(oldHost.GetType().GetField("m_pending", k_instance).GetValue(oldHost) == null,
                            "Old EOF queue remained attached during publication.");
                        Array phases = (Array)typeof(OnityTaskPlayerLoop).GetField("s_phases", k_staticPrivate).GetValue(null);
                        foreach (object phase in phases)
                        {
                            var pending = (ICollection)phase.GetType().GetField("Pending", k_instance).GetValue(phase);
                            Require(pending.Count == 0, "Timing queue remained attached during publication.");
                        }
                        Require(((ICollection)typeof(OnityTaskPlayerLoop).GetField("s_timers", k_staticPrivate)
                            .GetValue(null)).Count == 0, "Timer queue remained attached during publication.");
                        Require(Catch(() => OnityTask.WaitForEndOfFrame()) is InvalidOperationException,
                            "Retiring session accepted EOF reentry.");
                    }
                };
                Outcome eof = Observe(OnityTask.WaitForEndOfFrame(), first);
                oldHost = Host();
                StopPump(oldHost);
                Outcome timing = Observe(OnityTask.DelayFrames(1000, OnityPlayerLoopTiming.Update, default), first);
                Outcome timer = Observe(producer.Task.TimeoutWithoutException(1000f), first);
                PlayerLoopSystem removed = default;
                int removedIndex = -1;
                Exception repair = null;
                bool triggered = false;
                int selectedMode = failureMode;
                OnityTask signal = OnityTask.Yield(OnityPlayerLoopTiming.Update);
                Observe(signal, result =>
                {
                    Require(result.error == null, "Global-retirement trigger failed.");
                    if (selectedMode == 0)
                    {
                        InvokeOwner("CloseSession");
                    }
                    else
                    {
                        try
                        {
                            var loop = PlayerLoop.GetCurrentPlayerLoop();
                            Require(RemoveAnchor(ref loop, out removed, out removedIndex), "Update anchor removal failed.");
                            PlayerLoop.SetPlayerLoop(loop);
                            repair = Catch(() => OnityTaskPlayerLoop.Initialize());
                        }
                        finally
                        {
                            // Restore before return so the harness Update/deadline keeps running.
                            if (removedIndex >= 0)
                            {
                                RestoreAnchor(removed, removedIndex);
                            }
                        }
                    }
                    triggered = true;
                });
                try
                {
                    while (!triggered || eof.count == 0 || timing.count == 0 || timer.count == 0)
                    {
                        yield return null;
                    }
                    foreach (Outcome outcome in new[] { eof, timing, timer })
                    {
                        Require(outcome.count == 1 && outcome.thread == m_main, "Global outcome published more than once or off main.");
                        Require(failureMode == 0 ? outcome.error is OperationCanceledException
                            : ReferenceEquals(outcome.error, repair) && repair is InvalidOperationException,
                            "Global close/failed repair produced the wrong outcome.");
                    }
                    if (removedIndex >= 0)
                    {
                        RestoreAnchor(removed, removedIndex);
                    }
                    InvokeOwner("BeginSession", true);
                    OnityTaskPlayerLoop.Initialize();
                    Outcome fresh = Observe(OnityTask.WaitForEndOfFrame());
                    while (fresh.count == 0)
                    {
                        yield return null;
                    }
                    Require(fresh.error == null && publications == 3, "Fresh EOF control or old global outcomes failed.");
                }
                finally
                {
                    producer.TrySetResult(42);
                    if (removedIndex >= 0)
                    {
                        RestoreAnchor(removed, removedIndex);
                    }
                    InvokeOwner("BeginSession", true);
                    OnityTaskPlayerLoop.Initialize();
                    RetireHost(Host());
                }
            }
        }

        private Outcome Observe(OnityTask task, Action<Outcome> after = null)
        {
            return Observe(callback => task.GetAwaiter().UnsafeOnCompleted(callback), () =>
            {
                task.GetAwaiter().GetResult();
                return null;
            }, after);
        }

        private Outcome Observe<T>(OnityTask<T> task, Action<Outcome> after = null)
        {
            return Observe(callback => task.GetAwaiter().UnsafeOnCompleted(callback),
                () => task.GetAwaiter().GetResult(), after);
        }

        private Outcome Observe(Action<Action> register, Func<object> consume, Action<Outcome> after)
        {
            var result = new Outcome();
            register(() =>
            {
                try
                {
                    result.thread = Thread.CurrentThread.ManagedThreadId;
                    result.frame = Time.frameCount;
                    try
                    {
                        result.value = consume();
                    }
                    catch (Exception exception)
                    {
                        result.error = exception;
                    }
                    result.count++;
                    m_report.trace.Add(Name(m_caseIndex) + ":frame=" + result.frame + ":thread=" + result.thread
                        + ":outcome=" + (result.error == null ? "success" : result.error.GetType().Name));
                    after?.Invoke(result);
                }
                catch (Exception exception)
                {
                    m_failure = exception;
                }
            });
            return result;
        }

        private void Finish(Exception failure)
        {
            if (m_finished)
            {
                return;
            }
            m_finished = true;
            failure = failure ?? m_failure;
            try
            {
                (m_case as IDisposable)?.Dispose();
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }
            Clean(() => RetireHost(Host()), ref failure);
            Clean(() => Camera.onPostRender -= CameraRendered, ref failure);
            Clean(() => RenderPipelineManager.endCameraRendering -= PipelineCameraRendered, ref failure);
            Clean(() => RenderTexture.active = m_originalActive, ref failure);
            if (m_camera != null)
            {
                Clean(() => m_camera.enabled = false, ref failure);
                Clean(() => m_camera.targetTexture = null, ref failure);
                Clean(() => Destroy(m_camera), ref failure);
            }
            if (m_target != null)
            {
                Clean(() => m_target.Release(), ref failure);
                Clean(() => Destroy(m_target), ref failure);
            }
            if (m_pixel != null)
            {
                Clean(() => Destroy(m_pixel), ref failure);
            }
            failure = failure ?? m_failure;
            if (failure != null && m_report.cases.Count < k_expectedCaseCount)
            {
                m_report.cases.Add(new CaseResult { name = Name(m_caseIndex), passed = false, failure = failure.ToString() });
            }
            m_report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            m_report.passed = failure == null && m_report.cases.Count == k_expectedCaseCount;
            m_report.failure = failure?.ToString();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_output));
                File.WriteAllText(m_output, JsonUtility.ToJson(m_report, true));
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }
            m_completed(m_output, failure);
            Destroy(gameObject);
        }

        private static Component Host() => (Component)s_host.GetValue(null);

        private static void Clean(Action cleanup, ref Exception failure)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }
        }

        private static void StopPump(Component host)
        {
            Require(host != null, "EOF host reflection returned no host.");
            Coroutine coroutine = (Coroutine)host.GetType().GetField("m_coroutine", k_instance).GetValue(host);
            Require(coroutine != null, "EOF shared coroutine reflection failed.");
            ((MonoBehaviour)host).StopCoroutine(coroutine);
        }

        private static void RetireHost(Component host)
        {
            if (host != null)
            {
                ((Behaviour)host).enabled = false;
                Destroy(host.gameObject);
            }
        }

        private static void InvokeOwner(string method, params object[] arguments)
        {
            typeof(OnityTaskPlayerLoop).GetMethod(method, k_staticPrivate).Invoke(null, arguments.Length == 0 ? null : arguments);
        }

        private static bool RemoveAnchor(ref PlayerLoopSystem loop, out PlayerLoopSystem removed, out int index)
        {
            removed = default;
            index = -1;
            if (loop.subSystemList == null)
            {
                return false;
            }
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.type == typeof(UnityEngine.PlayerLoop.Update)
                    && loop.subSystemList[i].type == typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate))
                {
                    removed = loop.subSystemList[i];
                    index = i;
                    var nodes = new List<PlayerLoopSystem>(loop.subSystemList);
                    nodes.RemoveAt(i);
                    loop.subSystemList = nodes.ToArray();
                    return true;
                }
                var child = loop.subSystemList[i];
                if (RemoveAnchor(ref child, out removed, out index))
                {
                    loop.subSystemList[i] = child;
                    return true;
                }
            }
            return false;
        }

        private static void RestoreAnchor(PlayerLoopSystem anchor, int index)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Require(RestoreInParent(ref loop, anchor, index), "Update anchor restoration failed.");
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool RestoreInParent(ref PlayerLoopSystem loop, PlayerLoopSystem anchor, int index)
        {
            if (loop.subSystemList == null)
            {
                return false;
            }
            if (loop.type == typeof(UnityEngine.PlayerLoop.Update))
            {
                foreach (var node in loop.subSystemList)
                {
                    if (node.type == anchor.type)
                    {
                        return true;
                    }
                }
                var nodes = new List<PlayerLoopSystem>(loop.subSystemList);
                nodes.Insert(Math.Min(index, nodes.Count), anchor);
                loop.subSystemList = nodes.ToArray();
                return true;
            }
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var child = loop.subSystemList[i];
                if (RestoreInParent(ref child, anchor, index))
                {
                    loop.subSystemList[i] = child;
                    return true;
                }
            }
            return false;
        }

        private static string Name(int index)
        {
            string[] names = { "rendered alternating pixels", "reentrant next drain", "stalled pump Update cancellation",
                "source reuse and old token", "manual host replacement and old pump", "global detach close and failed repair" };
            return index < names.Length ? names[index] : "cleanup";
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static bool HasArgument(string name)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static Exception Catch(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class Outcome
        {
            internal int count;
            internal int frame;
            internal int thread;
            internal object value;
            internal Exception error;
        }

        [Serializable]
        private sealed class Report
        {
            public string generatedAtUtc;
            public bool passed;
            public string failure;
            public int expectedCaseCount = k_expectedCaseCount;
            public double caseDeadlineSeconds = k_caseDeadlineSeconds;
            public double suiteDeadlineSeconds = k_suiteDeadlineSeconds;
            public string expectedBackend;
            public string expectedBuildGuid;
            public bool isBatchMode;
            public bool hasBatchModeArgument;
            public bool hasNoGraphicsArgument;
            public OnityTaskBenchmarkEnvironment environment;
            public string graphicsApi;
            public string graphicsDevice;
            public string pipeline;
            public string colorSpace;
            public int screenWidth;
            public int screenHeight;
            public int textureWidth;
            public int textureHeight;
            public List<CaseResult> cases = new List<CaseResult>();
            public List<PixelResult> pixels = new List<PixelResult>();
            public List<string> trace = new List<string>();
        }

        [Serializable]
        private sealed class CaseResult
        {
            public string name;
            public bool passed;
            public string failure;
        }

        [Serializable]
        private sealed class PixelResult
        {
            public int registeredFrame;
            public int lateFrame;
            public int renderedFrame;
            public int completedFrame;
            public int thread;
            public string expected;
            public float red;
            public float green;
            public float blue;
        }
    }
}
