using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>Standalone Player correctness checks. These never run in a timing process.</summary>
    public sealed class OnityTaskPlayerSmokeRunner : MonoBehaviour
    {
        private const int k_workerTimeoutMilliseconds = 10000;
        private string m_output;
        private Action<string, Exception> m_completed;
        private bool m_originalFlow;
        private bool m_originalTracking;
        private bool m_originalStackTrace;
        private int m_originalCapacity;
        private bool m_hasSettings;
        private SynchronizationContext m_originalContext;

        /// <summary>Starts the smoke suite and reports its JSON path and any failure.</summary>
        /// <param name="output">Path for the smoke report.</param>
        /// <param name="completed">Callback receiving the report path and any failure.</param>
        public static void Run(string output, Action<string, Exception> completed)
        {
            GameObject runnerObject = new GameObject("Onity Task Player Smoke Runner");
            DontDestroyOnLoad(runnerObject);
            OnityTaskPlayerSmokeRunner runner = runnerObject.AddComponent<OnityTaskPlayerSmokeRunner>();
            runner.m_output = Path.GetFullPath(output);
            runner.m_completed = completed;
        }

        private IEnumerator Start()
        {
            SmokeReport report = new SmokeReport();
            Exception failure = null;
            m_originalFlow = OnityTask.FlowExecutionContext;
            m_originalTracking = OnityTaskTracker.IsEnabled;
            m_originalStackTrace = OnityTaskTracker.EnableStackTrace;
            m_originalCapacity = OnityTask.RunnerPoolCapacity;
            m_originalContext = SynchronizationContext.Current;
            m_hasSettings = true;
            try
            {
                OnityTask.FlowExecutionContext = true;
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTask.RunnerPoolCapacity = 128;
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                Require(report.environment.executionContextPath == "InternalPair"
                    || report.environment.executionContextPath == "PublicFallback",
                    "Execution-context path could not be proven: " + report.environment.executionContextEvidence);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            SmokeCase[] cases =
            {
                new SmokeCase("synchronous typed/untyped", Synchronous),
                new SmokeCase("critical typed/untyped suspension and stale tokens", CriticalSuspension),
                new SmokeCase("safe typed/untyped suspension", SafeSuspension),
                new SmokeCase("typed native/bridge exclusivity and bridge recycling", TypedBridge),
                new SmokeCase("untyped native/bridge exclusivity and bridge recycling", UntypedBridge),
                new SmokeCase("typed deferred pool return", TypedPoolReturn),
                new SmokeCase("untyped deferred pool return", UntypedPoolReturn),
                new SmokeCase("typed consume and re-rent during MoveNext", TypedReentrantConsumption),
                new SmokeCase("untyped consume and re-rent during MoveNext", UntypedReentrantConsumption),
                new SmokeCase("typed held-worker pool deferral", TypedHeldWorker),
                new SmokeCase("untyped held-worker pool deferral", UntypedHeldWorker),
                new SmokeCase("execution context flow on/off/suppressed and safe await", WorkerContext),
                new SmokeCase("AsyncLocal isolation and synchronization context restoration", MainThreadContext)
            };
            List<SmokeResult> results = new List<SmokeResult>();
            try
            {
                for (int i = 0; i < cases.Length && failure == null; i++)
                {
                    SmokeCase smokeCase = cases[i];
                    OnityTaskBenchmarkPlayerRunner.WriteStartupMarker("smoke-case-start", smokeCase.Name);
                    IEnumerator routine = smokeCase.Run();
                    try
                    {
                        while (true)
                        {
                            bool next = false;
                            try
                            {
                                next = routine.MoveNext();
                            }
                            catch (Exception exception)
                            {
                                failure = new InvalidOperationException("Player smoke failed: " + smokeCase.Name, exception);
                            }

                            if (failure != null || !next)
                            {
                                break;
                            }

                            yield return null;
                        }
                    }
                    finally
                    {
                        try
                        {
                            (routine as IDisposable)?.Dispose();
                        }
                        catch (Exception exception)
                        {
                            failure = failure ?? new InvalidOperationException(
                                "Player smoke cleanup failed: " + smokeCase.Name, exception);
                        }
                    }

                    results.Add(new SmokeResult
                    {
                        name = smokeCase.Name,
                        passed = failure == null,
                        failure = failure?.ToString()
                    });
                    OnityTaskBenchmarkPlayerRunner.WriteStartupMarker(
                        failure == null ? "smoke-case-pass" : "smoke-case-fail", smokeCase.Name);
                }
            }
            finally
            {
                RestoreSettings();
            }

            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            report.unityVersion = Application.unityVersion;
            report.expectedCaseCount = cases.Length;
            report.passed = failure == null && results.Count == cases.Length;
            report.failure = failure?.ToString();
            report.cases = results.ToArray();
            try
            {
                File.WriteAllText(m_output, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                m_completed?.Invoke(m_output, failure);
            }
            finally
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            RestoreSettings();
        }

        private void RestoreSettings()
        {
            if (!m_hasSettings)
            {
                return;
            }

            OnityTask.FlowExecutionContext = m_originalFlow;
            OnityTaskTracker.IsEnabled = m_originalTracking;
            OnityTaskTracker.EnableStackTrace = m_originalStackTrace;
            OnityTask.RunnerPoolCapacity = m_originalCapacity;
            SynchronizationContext.SetSynchronizationContext(m_originalContext);
            m_hasSettings = false;
        }

        private static IEnumerator Synchronous()
        {
            OnityTask untyped = SynchronousUntypedAsync();
            OnityTask<int> typed = SynchronousTypedAsync();
            Require(untyped.IsCompleted && typed.IsCompleted, "Synchronous methods suspended.");
            untyped.GetAwaiter().GetResult();
            Require(typed.GetAwaiter().GetResult() == 17, "Synchronous typed result mismatch.");
            yield return null;
        }

        private static IEnumerator CriticalSuspension()
        {
            CriticalGate typedGate = new CriticalGate();
            CriticalGate untypedGate = new CriticalGate();
            OnityTask<int> typed = PoolTypedAsync(typedGate, 23);
            OnityTask untyped = PoolUntypedAsync(untypedGate);
            Require(!typed.IsCompleted && !untyped.IsCompleted, "Critical awaits did not suspend.");
            yield return null;
            typedGate.Complete();
            untypedGate.Complete();
            Require(typed.GetAwaiter().GetResult() == 23, "Critical typed result mismatch.");
            untyped.GetAwaiter().GetResult();
            ThrowsInvalid(() => _ = typed.IsCompleted, "Typed token did not retire.");
            ThrowsInvalid(() => _ = untyped.IsCompleted, "Untyped token did not retire.");
            yield return null;
            yield return null;
        }

        private static IEnumerator SafeSuspension()
        {
            SafeGate typedGate = new SafeGate();
            SafeGate untypedGate = new SafeGate();
            OnityTask<int> typed = SafeTypedAsync(typedGate);
            OnityTask untyped = SafeUntypedAsync(untypedGate);
            Require(!typed.IsCompleted && !untyped.IsCompleted, "Safe awaits did not suspend.");
            yield return null;
            typedGate.Complete();
            untypedGate.Complete();
            Require(typed.GetAwaiter().GetResult() == 29, "Safe typed result mismatch.");
            untyped.GetAwaiter().GetResult();
            yield return null;
            yield return null;
        }

        private static IEnumerator TypedBridge()
        {
            CriticalGate gate = new CriticalGate();
            OnityTask<int> first = PoolTypedAsync(gate, 31);
            object runner = State(first);
            Task<int> bridge = first.AsTask();
            Require(!bridge.IsCompleted && ReferenceEquals(first.AsTask(), bridge), "Bridge was not shared while pending.");
            ThrowsInvalid(() => first.GetAwaiter().OnCompleted(() => { }), "Bridge accepted a native subscriber.");
            ThrowsInvalid(() => first.GetAwaiter().GetResult(), "Bridge accepted native consumption.");
            ThrowsInvalid(() => first.Preserve(), "Bridge accepted Preserve.");
            gate.Complete();
            Require(bridge.IsCompleted && bridge.GetAwaiter().GetResult() == 31, "Bridge result mismatch.");
            yield return null;
            yield return null;
            CriticalGate nextGate = new CriticalGate();
            OnityTask<int> next = PoolTypedAsync(nextGate, 37);
            Require(ReferenceEquals(State(next), runner), "Typed bridge did not recycle its runner.");
            ThrowsInvalid(() => _ = first.IsCompleted, "Bridged token survived recycling.");
            int observedResult = 0;
            bool observed = false;
            Exception callbackFailure = null;
            next.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    observedResult = next.GetAwaiter().GetResult();
                    observed = true;
                }
                catch (Exception exception)
                {
                    callbackFailure = exception;
                }
            });
            ThrowsInvalid(() => next.AsTask(), "Native subscriber accepted a bridge.");
            nextGate.Complete();
            Require(callbackFailure == null && observed && observedResult == 37,
                "Typed native subscriber did not consume result 37: " + callbackFailure);
            yield return null;
            yield return null;
        }

        private static IEnumerator UntypedBridge()
        {
            CriticalGate gate = new CriticalGate();
            OnityTask first = PoolUntypedAsync(gate);
            object runner = State(first);
            Task bridge = first.AsTask();
            Require(!bridge.IsCompleted && ReferenceEquals(first.AsTask(), bridge), "Untyped bridge was not shared.");
            ThrowsInvalid(() => first.GetAwaiter().OnCompleted(() => { }), "Untyped bridge accepted subscriber.");
            ThrowsInvalid(() => first.GetAwaiter().GetResult(), "Untyped bridge accepted native consumption.");
            gate.Complete();
            Require(bridge.IsCompleted, "Untyped bridge did not complete.");
            bridge.GetAwaiter().GetResult();
            yield return null;
            yield return null;
            CriticalGate nextGate = new CriticalGate();
            OnityTask next = PoolUntypedAsync(nextGate);
            Require(ReferenceEquals(State(next), runner), "Untyped bridge did not recycle its runner.");
            ThrowsInvalid(() => _ = first.IsCompleted, "Untyped bridged token survived recycling.");
            bool observed = false;
            Exception callbackFailure = null;
            next.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    next.GetAwaiter().GetResult();
                    observed = true;
                }
                catch (Exception exception)
                {
                    callbackFailure = exception;
                }
            });
            ThrowsInvalid(() => next.AsTask(), "Untyped native subscriber accepted a bridge.");
            nextGate.Complete();
            Require(callbackFailure == null && observed,
                "Untyped native subscriber did not consume its result: " + callbackFailure);
            yield return null;
            yield return null;
        }

        private static IEnumerator TypedPoolReturn()
        {
            CriticalGate gate = new CriticalGate();
            OnityTask<int> first = PoolTypedAsync(gate, 41);
            object runner = State(first);
            gate.Complete();
            Require(first.GetAwaiter().GetResult() == 41, "Pool result mismatch.");
            CriticalGate earlyGate = new CriticalGate();
            OnityTask<int> early = PoolTypedAsync(earlyGate, 43);
#if ENABLE_IL2CPP
            Require(!ReferenceEquals(State(early), runner), "IL2CPP typed runner returned before dispatcher drain.");
#else
            Require(ReferenceEquals(State(early), runner), "Mono typed runner did not return immediately.");
            earlyGate.Complete();
            early.GetAwaiter().GetResult();
#endif
            yield return null;
            yield return null;
            CriticalGate lateGate = new CriticalGate();
            OnityTask<int> late = PoolTypedAsync(lateGate, 47);
            Require(ReferenceEquals(State(late), runner), "Typed runner did not return after frames.");
#if ENABLE_IL2CPP
            earlyGate.Complete();
            early.GetAwaiter().GetResult();
#endif
            lateGate.Complete();
            Require(late.GetAwaiter().GetResult() == 47, "Recycled typed result mismatch.");
            yield return null;
            yield return null;
        }

        private static IEnumerator UntypedPoolReturn()
        {
            CriticalGate gate = new CriticalGate();
            OnityTask first = PoolUntypedAsync(gate);
            object runner = State(first);
            gate.Complete();
            first.GetAwaiter().GetResult();
            CriticalGate earlyGate = new CriticalGate();
            OnityTask early = PoolUntypedAsync(earlyGate);
#if ENABLE_IL2CPP
            Require(!ReferenceEquals(State(early), runner), "IL2CPP untyped runner returned before dispatcher drain.");
#else
            Require(ReferenceEquals(State(early), runner), "Mono untyped runner did not return immediately.");
            earlyGate.Complete();
            early.GetAwaiter().GetResult();
#endif
            yield return null;
            yield return null;
            CriticalGate lateGate = new CriticalGate();
            OnityTask late = PoolUntypedAsync(lateGate);
            Require(ReferenceEquals(State(late), runner), "Untyped runner did not return after frames.");
#if ENABLE_IL2CPP
            earlyGate.Complete();
            early.GetAwaiter().GetResult();
#endif
            lateGate.Complete();
            late.GetAwaiter().GetResult();
            yield return null;
            yield return null;
        }

        private static IEnumerator TypedReentrantConsumption()
        {
            CriticalGate firstGate = new CriticalGate();
            CriticalGate nextGate = new CriticalGate();
            OnityTask<int> first = PoolTypedAsync(firstGate, 53);
            OnityTask<int> next = default;
            object runner = State(first);
            int observed = 0;
            first.GetAwaiter().OnCompleted(() =>
            {
                observed = first.GetAwaiter().GetResult();
                next = PoolTypedAsync(nextGate, 59);
            });
            firstGate.Complete();
            Require(observed == 53 && !next.IsCompleted, "Reentrant typed consumption failed.");
#if ENABLE_IL2CPP
            Require(!ReferenceEquals(State(next), runner), "Typed runner was re-rented inside its IL2CPP MoveNext.");
#endif
            nextGate.Complete();
            Require(next.GetAwaiter().GetResult() == 59, "Reentrant typed copy-back corrupted result.");
            yield return null;
            yield return null;
        }

        private static IEnumerator UntypedReentrantConsumption()
        {
            CriticalGate firstGate = new CriticalGate();
            CriticalGate nextGate = new CriticalGate();
            OnityTask first = PoolUntypedAsync(firstGate);
            OnityTask next = default;
            object runner = State(first);
            bool observed = false;
            first.GetAwaiter().OnCompleted(() =>
            {
                first.GetAwaiter().GetResult();
                observed = true;
                next = PoolUntypedAsync(nextGate);
            });
            firstGate.Complete();
            Require(observed && !next.IsCompleted, "Reentrant untyped consumption failed.");
#if ENABLE_IL2CPP
            Require(!ReferenceEquals(State(next), runner), "Untyped runner was re-rented inside IL2CPP MoveNext.");
#endif
            nextGate.Complete();
            next.GetAwaiter().GetResult();
            yield return null;
            yield return null;
        }

        private static IEnumerator TypedHeldWorker()
        {
            using ManualResetEventSlim consumed = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            CriticalGate gate = new CriticalGate();
            OnityTask<int> first = PoolTypedAsync(gate, 61);
            object runner = State(first);
            Exception callbackFailure = null;
#if ENABLE_IL2CPP
            CriticalGate probeGate = new CriticalGate();
            OnityTask<int> probe = default;
#endif
            first.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    Require(first.GetAwaiter().GetResult() == 61, "Held worker typed result mismatch.");
                }
                catch (Exception exception)
                {
                    callbackFailure = exception;
                }
                finally
                {
                    consumed.Set();
                    release.Wait(k_workerTimeoutMilliseconds);
                }
            });
            Task worker = Task.Run(() => gate.Complete());
            try
            {
                Require(consumed.Wait(k_workerTimeoutMilliseconds), "Held typed worker did not reach consumer.");
                Require(callbackFailure == null, "Held typed consumer failed: " + callbackFailure);
#if ENABLE_IL2CPP
                Require(MoveNextDepth(runner) > 0, "Typed held worker did not retain MoveNext depth.");
                yield return null;
                yield return null;
                probe = PoolTypedAsync(probeGate, 67);
                Require(!ReferenceEquals(State(probe), runner), "Typed runner pooled while worker MoveNext was held.");
#endif
            }
            finally
            {
                release.Set();
                Require(worker.Wait(k_workerTimeoutMilliseconds), "Typed worker did not unwind after release.");
            }

            yield return null;
            yield return null;
            CriticalGate lateGate = new CriticalGate();
            OnityTask<int> late = PoolTypedAsync(lateGate, 71);
            Require(ReferenceEquals(State(late), runner), "Held typed runner was not pooled after unwind.");
#if ENABLE_IL2CPP
            probeGate.Complete();
            Require(probe.GetAwaiter().GetResult() == 67, "Held-worker early rental result mismatch.");
#endif
            lateGate.Complete();
            late.GetAwaiter().GetResult();
            yield return null;
            yield return null;
        }

        private static IEnumerator UntypedHeldWorker()
        {
            using ManualResetEventSlim consumed = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            CriticalGate gate = new CriticalGate();
            OnityTask first = PoolUntypedAsync(gate);
            object runner = State(first);
            Exception callbackFailure = null;
#if ENABLE_IL2CPP
            CriticalGate probeGate = new CriticalGate();
            OnityTask probe = default;
#endif
            first.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    first.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    callbackFailure = exception;
                }
                finally
                {
                    consumed.Set();
                    release.Wait(k_workerTimeoutMilliseconds);
                }
            });
            Task worker = Task.Run(() => gate.Complete());
            try
            {
                Require(consumed.Wait(k_workerTimeoutMilliseconds), "Held untyped worker did not reach consumer.");
                Require(callbackFailure == null, "Held untyped consumer failed: " + callbackFailure);
#if ENABLE_IL2CPP
                Require(MoveNextDepth(runner) > 0, "Untyped worker did not retain MoveNext depth.");
                yield return null;
                yield return null;
                probe = PoolUntypedAsync(probeGate);
                Require(!ReferenceEquals(State(probe), runner), "Untyped runner pooled while worker MoveNext held.");
#endif
            }
            finally
            {
                release.Set();
                Require(worker.Wait(k_workerTimeoutMilliseconds), "Untyped worker did not unwind after release.");
            }

            yield return null;
            yield return null;
            CriticalGate lateGate = new CriticalGate();
            OnityTask late = PoolUntypedAsync(lateGate);
            Require(ReferenceEquals(State(late), runner), "Held untyped runner was not pooled after unwind.");
#if ENABLE_IL2CPP
            probeGate.Complete();
            probe.GetAwaiter().GetResult();
#endif
            lateGate.Complete();
            late.GetAwaiter().GetResult();
            yield return null;
            yield return null;
        }

        private static IEnumerator WorkerContext()
        {
            AsyncLocal<string> local = new AsyncLocal<string>();
            try
            {
                for (int mode = 0; mode < 3; mode++)
                {
                    OnityTask.FlowExecutionContext = mode != 1;
                    local.Value = "captured";
                    CriticalGate gate = new CriticalGate();
                    OnityTask<ContextObservation> task;
                    if (mode == 2)
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            task = ObserveCriticalAsync(gate, local);
                        }
                    }
                    else
                    {
                        task = ObserveCriticalAsync(gate, local);
                    }

                    local.Value = "worker-ambient";
                    SynchronizationContext workerContext = null;
                    string workerAfter = null;
                    Task worker = Task.Run(() =>
                    {
                        workerContext = SynchronizationContext.Current;
                        gate.Complete();
                        workerAfter = local.Value;
                    });
                    Require(worker.Wait(k_workerTimeoutMilliseconds), "Execution-context worker timed out.");
                    ContextObservation observation = task.GetAwaiter().GetResult();
                    Require(observation.threadId != Thread.CurrentThread.ManagedThreadId, "Method did not resume on worker.");
                    Require(observation.localValue == (mode == 0 ? "captured" : "worker-ambient"),
                        "AsyncLocal flow mismatch for mode " + mode);
                    Require(ReferenceEquals(observation.context, workerContext), "Resuming thread context was replaced.");
                    Require(workerAfter == "worker-ambient", "Flowed AsyncLocal leaked into worker.");
                    yield return null;
                    yield return null;
                }

                OnityTask.FlowExecutionContext = true;
                local.Value = "safe-captured";
                SafeGate safeGate = new SafeGate();
                OnityTask<string> safe = ObserveSafeAsync(safeGate, local);
                local.Value = "safe-worker";
                Require(Task.Run(() => safeGate.Complete()).Wait(k_workerTimeoutMilliseconds), "Safe worker timed out.");
                Require(safe.GetAwaiter().GetResult() == "safe-captured", "Safe await did not flow AsyncLocal.");
            }
            finally
            {
                local.Value = null;
                OnityTask.FlowExecutionContext = true;
            }

            yield return null;
            yield return null;
        }

        private static IEnumerator MainThreadContext()
        {
            AsyncLocal<string> local = new AsyncLocal<string>();
            SynchronizationContext original = SynchronizationContext.Current;
            Require(original != null, "Player main thread has no synchronization context.");
            try
            {
                for (int flow = 0; flow < 2; flow++)
                {
                    OnityTask.FlowExecutionContext = flow == 0;
                    local.Value = "before";
                    SynchronizationContext replacement = new SynchronizationContext();
                    CriticalGate gate = new CriticalGate();
                    OnityTask<ContextObservation> task = ChangeContextAsync(gate, local, replacement);
                    gate.Complete();
                    ContextObservation observation = task.GetAwaiter().GetResult();
                    Require(observation.localValue == "before" && ReferenceEquals(observation.context, replacement),
                        "Resumed method did not observe expected context/AsyncLocal.");
                    Require(local.Value == (flow == 0 ? "before" : "inner"), "AsyncLocal isolation mismatch.");
                    Require(ReferenceEquals(SynchronizationContext.Current, flow == 0 ? original : replacement),
                        "Synchronization context restore/persist mismatch.");
                    SynchronizationContext.SetSynchronizationContext(original);
                    yield return null;
                    yield return null;
                }
            }
            finally
            {
                local.Value = null;
                SynchronizationContext.SetSynchronizationContext(original);
                OnityTask.FlowExecutionContext = true;
            }
        }

        private static async OnityTask SynchronousUntypedAsync()
        {
            await OnityTask.Completed;
        }

        private static async OnityTask<int> SynchronousTypedAsync()
        {
            return await OnityTask<int>.FromResult(17);
        }

        private static async OnityTask PoolUntypedAsync(CriticalGate gate)
        {
            await gate;
        }

        private static async OnityTask<int> PoolTypedAsync(CriticalGate gate, int value)
        {
            await gate;
            return value;
        }

        private static async OnityTask SafeUntypedAsync(SafeGate gate)
        {
            await gate;
        }

        private static async OnityTask<int> SafeTypedAsync(SafeGate gate)
        {
            await gate;
            return 29;
        }

        private static async OnityTask<ContextObservation> ObserveCriticalAsync(CriticalGate gate, AsyncLocal<string> local)
        {
            await gate;
            return new ContextObservation(Thread.CurrentThread.ManagedThreadId, local.Value, SynchronizationContext.Current);
        }

        private static async OnityTask<string> ObserveSafeAsync(SafeGate gate, AsyncLocal<string> local)
        {
            await gate;
            return local.Value;
        }

        private static async OnityTask<ContextObservation> ChangeContextAsync(
            CriticalGate gate, AsyncLocal<string> local, SynchronizationContext replacement)
        {
            await gate;
            string observed = local.Value;
            local.Value = "inner";
            SynchronizationContext.SetSynchronizationContext(replacement);
            return new ContextObservation(Thread.CurrentThread.ManagedThreadId, observed, SynchronizationContext.Current);
        }

        private static object State<T>(OnityTask<T> task)
        {
            FieldInfo field = typeof(OnityTask<T>).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Typed task state field unavailable.");
            return field.GetValue(task);
        }

        private static object State(OnityTask task)
        {
            FieldInfo field = typeof(OnityTask).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Untyped task state field unavailable.");
            return field.GetValue(task);
        }

#if ENABLE_IL2CPP
        private static int MoveNextDepth(object runner)
        {
            FieldInfo field = runner.GetType().GetField("m_moveNextDepth", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "IL2CPP runner depth field unavailable.");
            return (int)field.GetValue(runner);
        }
#endif

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void ThrowsInvalid(Action action, string message)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        private sealed class CriticalGate : ICriticalNotifyCompletion
        {
            private Action m_continuation;
            public bool IsCompleted => false;
            public CriticalGate GetAwaiter() => this;

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void Complete()
            {
                Action continuation = Interlocked.Exchange(ref m_continuation, null);
                Require(continuation != null, "Critical gate has no continuation.");
                continuation();
            }
        }

        private sealed class SafeGate : INotifyCompletion
        {
            private Action m_continuation;
            public bool IsCompleted => false;
            public SafeGate GetAwaiter() => this;

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void Complete()
            {
                Action continuation = Interlocked.Exchange(ref m_continuation, null);
                Require(continuation != null, "Safe gate has no continuation.");
                continuation();
            }
        }

        private readonly struct ContextObservation
        {
            internal readonly int threadId;
            internal readonly string localValue;
            internal readonly SynchronizationContext context;
            internal ContextObservation(int id, string value, SynchronizationContext synchronizationContext)
            {
                threadId = id;
                localValue = value;
                context = synchronizationContext;
            }
        }

        private readonly struct SmokeCase
        {
            internal readonly string Name;
            internal readonly Func<IEnumerator> Run;
            internal SmokeCase(string name, Func<IEnumerator> run)
            {
                Name = name;
                Run = run;
            }
        }

        [Serializable]
        private sealed class SmokeReport
        {
            public int schemaVersion = 1;
            public string generatedAtUtc;
            public string unityVersion;
            public int expectedCaseCount;
            public bool passed;
            public string failure;
            public OnityTaskBenchmarkEnvironment environment;
            public SmokeResult[] cases;
        }

        [Serializable]
        private sealed class SmokeResult
        {
            public string name;
            public bool passed;
            public string failure;
        }
    }
}
