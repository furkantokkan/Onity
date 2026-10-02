using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
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
                // Smoke cases run at the library default; cases that need flow on set it explicitly.
                OnityTask.FlowExecutionContext = false;
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
                new SmokeCase("typed immediate pool return", TypedPoolReturn),
                new SmokeCase("untyped immediate pool return", UntypedPoolReturn),
                new SmokeCase("typed consume and re-rent during MoveNext", TypedReentrantConsumption),
                new SmokeCase("untyped consume and re-rent during MoveNext", UntypedReentrantConsumption),
                new SmokeCase("typed held-worker immediate reuse", TypedHeldWorker),
                new SmokeCase("untyped held-worker immediate reuse", UntypedHeldWorker),
                new SmokeCase("worker unwind and immediate re-rent x1000", WorkerUnwindRecycle),
                new SmokeCase("stateless default frame waits", StatelessDefaultWaits),
                new SmokeCase("reference-free Yield awaitable", YieldAwaitableSmoke),
                new SmokeCase("cctor-free task value types", TaskShape),
                new SmokeCase("appended timings install on use, items and posted actions", AppendedTimingsSmoke),
                new SmokeCase("immediate cancellation publishes on the canceling worker", CancelImmediatelySmoke),
                new SmokeCase("timed waits, polled predicates, timers and timed timeouts", TimedWaitsSmoke),
                new SmokeCase("execution context flow on/off/suppressed and safe await", WorkerContext),
                new SmokeCase("AsyncLocal isolation and synchronization context restoration", MainThreadContext),
                new SmokeCase("thread-pool switch and main-thread return", ThreadPoolSwitch),
                new SmokeCase("thread-pool typed/untyped outcomes and completion threads", ThreadPoolOutcomes),
                new SmokeCase("thread-pool execution context on/off/suppressed", ThreadPoolContext),
                new SmokeCase("typed array WhenAny mixed sources and late losers", TypedArrayWhenAny),
                new SmokeCase("untyped array WhenAny worker winner and late losers", UntypedArrayWhenAny),
                new SmokeCase("external cancellation token identity and late producer faults", ExternalCancellation),
                new SmokeCase("cancellation suppression and faulted OCE status", CancellationSuppression),
                new SmokeCase("injected timing phases and rendered frame distances", PlayerLoopFrameDistances),
                new SmokeCase("paused fixed cancellation and reentrant Update deferral", PlayerLoopPausedCancellation),
                new SmokeCase("legacy runner retirement preserves tokens and native operation", LegacyRunnerRetirement),
                new SmokeCase("legacy active predicate retirement and replacement", LegacyActiveRetirement),
                new SmokeCase("timeout four shapes and producer fault/cancellation identity", TimeoutOutcomes),
                new SmokeCase("timeout paused clocks and worker/reentrant timing", TimeoutClocksAndPublication),
                new SmokeCase("headless EOF rejection before precancellation", EndOfFrameHeadlessGuard),
                new SmokeCase("finite native await foreach, covariance and cleanup precedence", FiniteInlineStreams),
                new SmokeCase("finite native disposal settles pending move and shares cleanup", FinitePendingDisposal),
                new SmokeCase("EveryUpdate on-demand, reentrant pass and worker disposal", EveryUpdateAdapterSmoke),
                new SmokeCase("BCL adapters real iterator, reusable ValueTask and actual outcomes", BclAdapterSmoke),
                new SmokeCase("channel bounded FIFO, cancellation and faulted closure", ChannelBoundedSmoke),
                new SmokeCase("channel ReadAll token pairs, disposal and actual foreach", ChannelReadAllSmoke),
                new SmokeCase("await operators sequential filtering and actual delegate status", AwaitOperatorsSmoke),
                new SmokeCase("await operator pending disposal and upstream cleanup orders", AwaitOperatorDisposalSmoke)
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
            // Runners return to their pool at consumption on every backend (PERF-7).
            CriticalGate earlyGate = new CriticalGate();
            OnityTask<int> early = PoolTypedAsync(earlyGate, 43);
            Require(ReferenceEquals(State(early), runner), "Typed runner did not return at consumption.");
            earlyGate.Complete();
            Require(early.GetAwaiter().GetResult() == 43, "Immediately recycled typed result mismatch.");
            yield return null;
            yield return null;
            CriticalGate lateGate = new CriticalGate();
            OnityTask<int> late = PoolTypedAsync(lateGate, 47);
            Require(ReferenceEquals(State(late), runner), "Typed runner did not return after frames.");
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
            Require(ReferenceEquals(State(early), runner), "Untyped runner did not return at consumption.");
            earlyGate.Complete();
            early.GetAwaiter().GetResult();
            yield return null;
            yield return null;
            CriticalGate lateGate = new CriticalGate();
            OnityTask late = PoolUntypedAsync(lateGate);
            Require(ReferenceEquals(State(late), runner), "Untyped runner did not return after frames.");
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
            // The runner is rented again while its MoveNext is still unwinding: the copy-back canary.
            Require(ReferenceEquals(State(next), runner), "Typed runner was not re-rented inside its MoveNext.");
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
            Require(ReferenceEquals(State(next), runner), "Untyped runner was not re-rented inside its MoveNext.");
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
            CriticalGate probeGate = new CriticalGate();
            OnityTask<int> probe = default;
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
                // The worker's MoveNext is still on its stack; the consumed runner is rented again now.
                probe = PoolTypedAsync(probeGate, 67);
                Require(ReferenceEquals(State(probe), runner), "Typed runner was not pooled at consumption.");
            }
            finally
            {
                release.Set();
                Require(worker.Wait(k_workerTimeoutMilliseconds), "Typed worker did not unwind after release.");
            }

            yield return null;
            Require(!probe.IsCompleted, "The unwinding worker completed the new rental.");
            probeGate.Complete();
            Require(probe.GetAwaiter().GetResult() == 67, "Held-worker rental result corrupted by the unwind.");
            CriticalGate lateGate = new CriticalGate();
            OnityTask<int> late = PoolTypedAsync(lateGate, 71);
            Require(ReferenceEquals(State(late), runner), "Held typed runner was not pooled after unwind.");
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
            CriticalGate probeGate = new CriticalGate();
            OnityTask probe = default;
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
                probe = PoolUntypedAsync(probeGate);
                Require(ReferenceEquals(State(probe), runner), "Untyped runner was not pooled at consumption.");
            }
            finally
            {
                release.Set();
                Require(worker.Wait(k_workerTimeoutMilliseconds), "Untyped worker did not unwind after release.");
            }

            yield return null;
            Require(!probe.IsCompleted, "The unwinding worker completed the new untyped rental.");
            probeGate.Complete();
            probe.GetAwaiter().GetResult();
            CriticalGate lateGate = new CriticalGate();
            OnityTask late = PoolUntypedAsync(lateGate);
            Require(ReferenceEquals(State(late), runner), "Held untyped runner was not pooled after unwind.");
            lateGate.Complete();
            late.GetAwaiter().GetResult();
            yield return null;
            yield return null;
        }

        private static IEnumerator WorkerUnwindRecycle()
        {
            // A worker completes the method and unwinds while the main thread consumes and re-rents the
            // same runner; every rental must keep its own result.
            const int iterations = 1000;
            for (int i = 0; i < iterations; i++)
            {
                CriticalGate gate = new CriticalGate();
                OnityTask<int> task = PoolTypedAsync(gate, i);
                Task worker = Task.Run(() => gate.Complete());
                Require(worker.Wait(k_workerTimeoutMilliseconds), "Recycle worker timed out.");
                Require(task.GetAwaiter().GetResult() == i, "Recycled rental lost its result at " + i);
                CriticalGate nextGate = new CriticalGate();
                OnityTask<int> next = PoolTypedAsync(nextGate, -i);
                nextGate.Complete();
                Require(next.GetAwaiter().GetResult() == -i, "Re-rented runner result corrupted at " + i);
                if ((i & 127) == 0)
                {
                    yield return null;
                }
            }
        }

        private static IEnumerator StatelessDefaultWaits()
        {
            OnityTask frame = OnityTask.NextFrame();
            Require(ReferenceEquals(State(frame.Preserve()), State(frame)), "Preserve allocated for a stateless wait.");
            int consumers = 0;
            frame.GetAwaiter().UnsafeOnCompleted(() => consumers++);
            frame.GetAwaiter().UnsafeOnCompleted(() => consumers++);
            Task firstBridge = frame.AsTask();
            frame.Forget();
            int workerResumed = 0;
            int main = Thread.CurrentThread.ManagedThreadId;
            Task worker = Task.Run(() =>
            {
                OnityTask workerWait = OnityTask.NextFrame();
                workerWait.GetAwaiter().UnsafeOnCompleted(() => workerResumed = Thread.CurrentThread.ManagedThreadId);
            });
            IEnumerator wait = WaitForThreadPool(() => consumers == 2 && firstBridge.IsCompleted && workerResumed != 0);
            while (wait.MoveNext())
            {
                yield return null;
            }

            Require(worker.Wait(k_workerTimeoutMilliseconds) && !worker.IsFaulted, "Worker frame wait failed.");
            Require(workerResumed == main, "A worker-registered frame wait did not resume on the main thread.");
            frame.GetAwaiter().GetResult();
            frame.GetAwaiter().GetResult();
            Require(frame.AsTask().IsCompleted, "A completed stateless wait gave a pending bridge.");
        }

        private static IEnumerator YieldAwaitableSmoke()
        {
            int created = Time.frameCount;
            OnityTask<int> resumed = ResumeFrameAfterYieldAsync();
            IEnumerator wait = WaitForThreadPool(() => resumed.IsCompleted);
            while (wait.MoveNext())
            {
                yield return null;
            }

            int frame = resumed.GetAwaiter().GetResult();
            Require(frame - created <= 1, "Yield did not resume at the next Update drain.");
            OnityTask converted = OnityTask.Yield();
            wait = WaitForThreadPool(() => converted.IsCompleted);
            while (wait.MoveNext())
            {
                yield return null;
            }

            converted.GetAwaiter().GetResult();
            converted.GetAwaiter().GetResult();
        }

        private static IEnumerator TaskShape()
        {
            Require(typeof(OnityTask).TypeInitializer == null, "OnityTask gained a static constructor.");
            Require(typeof(OnityTask<int>).TypeInitializer == null, "OnityTask<int> gained a static constructor.");
            yield return null;
        }

        private static async OnityTask<int> ResumeFrameAfterYieldAsync()
        {
            await OnityTask.Yield();
            return Time.frameCount;
        }

        private static IEnumerator AppendedTimingsSmoke()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            int resumed = 0;
            int wrongThread = 0;
            for (int timing = 3; timing <= (int)OnityPlayerLoopTiming.LastTimeUpdate; timing++)
            {
                OnityTask yieldTask = OnityTask.Yield((OnityPlayerLoopTiming)timing);
                Require(OnityTaskPlayerLoop.IsInjected((OnityPlayerLoopTiming)timing), "Timing node not installed on use.");
                yieldTask.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    wrongThread += Thread.CurrentThread.ManagedThreadId == main ? 0 : 1;
                    resumed++;
                });
            }

            int itemCalls = 0;
            int posted = 0;
            OnityTaskPlayerLoop.AddAction(OnityPlayerLoopTiming.LastPreUpdate, new CountdownItem(() => ++itemCalls < 3));
            OnityTask.Post(() => posted++, OnityPlayerLoopTiming.PostLateUpdate);
            Task worker = Task.Run(() => OnityTask.Post(() => posted++, OnityPlayerLoopTiming.LastEarlyUpdate));
            Require(worker.Wait(k_workerTimeoutMilliseconds) && !worker.IsFaulted, "Worker post failed.");
            IEnumerator wait = WaitForThreadPool(() => resumed == 16 && itemCalls == 3 && posted == 2);
            while (wait.MoveNext())
            {
                yield return null;
            }

            Require(wrongThread == 0, "An appended timing resumed off the main thread.");
            yield return null;
            Require(itemCalls == 3 && posted == 2, "A PlayerLoop item or posted action ran too often.");
        }

        private static IEnumerator CancelImmediatelySmoke()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            for (int round = 0; round < 32; round++)
            {
                using (var cancellation = new CancellationTokenSource())
                using (var nextCancellation = new CancellationTokenSource())
                {
                    OnityTask wait = round % 2 == 0
                        ? OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellation.Token, true)
                        : OnityTask.DelayFrame(1000, OnityPlayerLoopTiming.PreUpdate, cancellation.Token, true);
                    int thread = 0;
                    bool canceled = false;
                    wait.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        thread = Thread.CurrentThread.ManagedThreadId;
                        try
                        {
                            wait.GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException exception)
                        {
                            canceled = exception.CancellationToken == cancellation.Token;
                        }
                    });
                    Task worker = Task.Run(() => cancellation.Cancel());
                    Require(worker.Wait(k_workerTimeoutMilliseconds), "Immediate cancellation worker timed out.");
                    Require(canceled && thread != main, "Immediate cancellation did not publish on the worker.");

                    // The released source is rented again while the canceled cycle's entry is still queued
                    // (in PreUpdate on odd rounds): that stale entry must not publish the new Update wait.
                    OnityTask next = OnityTask.Yield(OnityPlayerLoopTiming.Update, nextCancellation.Token);
                    int updateBeginFrame = -1;
                    bool nextDone = false;
                    bool nextInOrder = false;
                    Exception nextError = null;
                    OnityTask.Post(() => updateBeginFrame = Time.frameCount, OnityPlayerLoopTiming.UpdateBegin);
                    next.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        try
                        {
                            next.GetAwaiter().GetResult();
                            nextInOrder = updateBeginFrame == Time.frameCount;
                        }
                        catch (Exception exception)
                        {
                            nextError = exception;
                        }

                        nextDone = true;
                    });
                    IEnumerator settle = WaitForThreadPool(() => nextDone);
                    while (settle.MoveNext())
                    {
                        yield return null;
                    }

                    Require(nextError == null && nextInOrder, "A stale queue entry published the next rental early.");
                }
            }
        }

        private static IEnumerator TimedWaitsSmoke()
        {
            float started = Time.realtimeSinceStartup;
            OnityTask realtime = OnityTask.Delay(TimeSpan.FromMilliseconds(50), OnityDelayType.Realtime,
                OnityPlayerLoopTiming.PreLateUpdate);
            OnityTask unscaled = OnityTask.WaitForSeconds(0.05f, true, OnityPlayerLoopTiming.LastUpdate);
            var gate = new StrongBox<bool>();
            OnityTask predicate = OnityTask.WaitUntil(gate, box => box.Value, OnityPlayerLoopTiming.EarlyUpdate);
            int fired = 0;
            OnityPlayerLoopTimer timer = OnityPlayerLoopTimer.StartNew(TimeSpan.FromMilliseconds(10), true,
                OnityDelayType.Realtime, OnityPlayerLoopTiming.PostLateUpdate, CancellationToken.None,
                state => fired++, null);
            var never = new OnityTaskCompletionSource<int>();
            OnityTask<(bool isTimeout, int result)> timeout = never.Task.TimeoutWithoutException(
                TimeSpan.FromMilliseconds(20), OnityDelayType.Realtime, OnityPlayerLoopTiming.Update);
            int changedFrom = Time.frameCount;
            OnityTask<int> changed = OnityTask.WaitUntilValueChanged(gate, box => box.Value ? 1 : 0);
            IEnumerator wait = WaitForThreadPool(() => realtime.IsCompleted && unscaled.IsCompleted && fired >= 2
                && timeout.IsCompleted);
            while (wait.MoveNext())
            {
                yield return null;
            }

            Require(Time.realtimeSinceStartup - started >= 0.045f, "A timed delay completed early.");
            realtime.GetAwaiter().GetResult();
            unscaled.GetAwaiter().GetResult();
            Require(timeout.GetAwaiter().GetResult().isTimeout, "The timed timeout did not win.");
            Require(!predicate.IsCompleted && !changed.IsCompleted, "A polled wait completed before its change.");
            gate.Value = true;
            wait = WaitForThreadPool(() => predicate.IsCompleted && changed.IsCompleted);
            while (wait.MoveNext())
            {
                yield return null;
            }

            predicate.GetAwaiter().GetResult();
            Require(changed.GetAwaiter().GetResult() == 1 && Time.frameCount > changedFrom, "Value change result mismatch.");
            timer.Dispose();
        }

        private sealed class CountdownItem : IOnityPlayerLoopItem
        {
            private readonly Func<bool> m_step;

            internal CountdownItem(Func<bool> step)
            {
                m_step = step;
            }

            public bool MoveNext()
            {
                return m_step();
            }
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
                OnityTask.FlowExecutionContext = false;
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
                OnityTask.FlowExecutionContext = false;
            }
        }

        private static IEnumerator TypedArrayWhenAny()
        {
            CriticalGate gate = new CriticalGate();
            OnityTask<int> native = ArrayLoserFault(gate);
            bool gateCompleted = false;
            OnityTaskCompletionSource<int> winner = new OnityTaskCompletionSource<int>();
            TaskCompletionSource<int> taskLoser = new TaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> race = OnityTask.WhenAny(new[]
            {
                native, winner.Task.Preserve(), OnityTask<int>.FromTask(taskLoser.Task)
            });
            try
            {
                winner.TrySetResult(42);
                Require(race.GetAwaiter().GetResult() == (1, 42), "Typed array winner mismatch.");
                OnityTask<(int winnerIndex, int result)> fresh = OnityTask.WhenAny(new[]
                {
                    OnityTask<int>.FromResult(91), OnityTask<int>.FromResult(92)
                });
                Require(fresh.GetAwaiter().GetResult() == (0, 91), "Fresh typed race was corrupted.");
                taskLoser.TrySetCanceled();
                gateCompleted = true;
                gate.Complete();
                Require(taskLoser.Task.IsCanceled, "Typed Task loser was canceled incorrectly.");
                Exception consumed = null;
                try
                {
                    native.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    consumed = exception;
                }
                Require(consumed is InvalidOperationException
                    && consumed.Message != "observed typed native array loser", "Typed native loser was not consumed.");
                yield return null;
            }
            finally
            {
                winner.TrySetResult(0);
                taskLoser.TrySetResult(0);
                if (!gateCompleted)
                {
                    gate.Complete();
                }
            }
        }

        private static async OnityTask<int> ArrayLoserFault(CriticalGate gate)
        {
            await gate;
            throw new InvalidOperationException("observed typed native array loser");
        }

        private static IEnumerator UntypedArrayWhenAny()
        {
            CriticalGate gate = new CriticalGate();
            OnityTask native = PoolUntypedAsync(gate);
            OnityTaskCompletionSource loser = new OnityTaskCompletionSource();
            TaskCompletionSource<bool> taskLoser = new TaskCompletionSource<bool>();
            OnityTask<int> race = OnityTask.WhenAny(new[]
            {
                loser.Task.Preserve(), native, OnityTask.FromTask(taskLoser.Task)
            });
            int thread = 0;
            int index = -1;
            Exception error = null;
            int completed = 0;
            race.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    index = race.GetAwaiter().GetResult();
                    thread = Thread.CurrentThread.ManagedThreadId;
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    Volatile.Write(ref completed, 1);
                }
            });
            int main = Thread.CurrentThread.ManagedThreadId;
            Task worker = Task.Run(() => gate.Complete());
            try
            {
                IEnumerator wait = WaitForThreadPool(() => worker.IsCompleted && Volatile.Read(ref completed) != 0);
                while (wait.MoveNext())
                {
                    yield return null;
                }
                worker.GetAwaiter().GetResult();
                Require(error == null && index == 1 && thread != main, "Untyped native worker winner mismatch.");
                Require(OnityTask.WhenAny(new[] { OnityTask.Completed, OnityTask.Completed })
                    .GetAwaiter().GetResult() == 0, "Fresh untyped race was corrupted.");
                loser.TrySetException(new InvalidOperationException("observed array loser"));
                taskLoser.TrySetCanceled();
                Require(taskLoser.Task.IsCanceled, "Untyped Task loser status mismatch.");
                yield return null;
            }
            finally
            {
                loser.TrySetResult();
                taskLoser.TrySetResult(false);
                Require(worker.Wait(TimeSpan.FromSeconds(5)), "Array smoke worker timed out.");
            }
        }

        private static IEnumerator ExternalCancellation()
        {
            using (CancellationTokenSource external = new CancellationTokenSource())
            using (CancellationTokenSource producerCancellation = new CancellationTokenSource())
            {
                producerCancellation.Cancel();
                OnityTaskCompletionSource<int> typed = new OnityTaskCompletionSource<int>();
                OnityTaskCompletionSource plain = new OnityTaskCompletionSource();
                OnityTask<int> typedOutput = typed.Task.AttachExternalCancellation(external.Token);
                OnityTask plainOutput = plain.Task.AttachExternalCancellation(external.Token);
                Exception typedError = null;
                Exception plainError = null;
                int callbackThread = 0;
                int completed = 0;
                typedOutput.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        typedOutput.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        typedError = exception;
                    }
                    finally
                    {
                        callbackThread = Thread.CurrentThread.ManagedThreadId;
                        Volatile.Write(ref completed, 1);
                    }
                });
                Task<int> worker = Task.Run(() =>
                {
                    external.Cancel();
                    return Thread.CurrentThread.ManagedThreadId;
                });
                try
                {
                    IEnumerator wait = WaitForThreadPool(() => worker.IsCompleted && Volatile.Read(ref completed) != 0);
                    while (wait.MoveNext())
                    {
                        yield return null;
                    }
                    Require(callbackThread == worker.GetAwaiter().GetResult(), "External cancellation publication thread mismatch.");
                    try
                    {
                        plainOutput.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        plainError = exception;
                    }
                    Require(typedError is OperationCanceledException typedCanceled
                        && typedCanceled.CancellationToken == external.Token, "Typed external token identity mismatch.");
                    Require(plainError is OperationCanceledException plainCanceled
                        && plainCanceled.CancellationToken == external.Token, "Untyped external token identity mismatch.");
                    typed.TrySetException(new InvalidOperationException("late externally canceled producer"));
                    plain.TrySetCanceled(producerCancellation.Token);
                    Task<int> lateBridge = typed.Task.AsTask();
                    Require(lateBridge.IsFaulted, "Late producer fault lost its status.");
                    OnityTaskCompletionSource<int> completedSource = new OnityTaskCompletionSource<int>();
                    completedSource.TrySetResult(63);
                    Require(completedSource.Task.AttachExternalCancellation(external.Token).GetAwaiter().GetResult() == 63,
                        "Completed producer did not win against precanceled external token.");
                    yield return null;
                }
                finally
                {
                    typed.TrySetResult(0);
                    plain.TrySetResult();
                    Require(worker.Wait(TimeSpan.FromSeconds(5)), "Cancellation smoke worker timed out.");
                }
            }
        }

        private static IEnumerator CancellationSuppression()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                OnityTaskCompletionSource<int> typed = new OnityTaskCompletionSource<int>();
                OnityTaskCompletionSource plain = new OnityTaskCompletionSource();
                OnityTask<(bool isCanceled, int result)> typedOutput = typed.Task.SuppressCancellationThrow();
                OnityTask<bool> plainOutput = plain.Task.SuppressCancellationThrow();
                try
                {
                    typed.TrySetCanceled(cancellation.Token);
                    plain.TrySetCanceled(cancellation.Token);
                    Require(typedOutput.IsCompletedSuccessfully && typedOutput.GetAwaiter().GetResult() == (true, 0),
                        "Typed suppression cancellation result mismatch.");
                    Require(plainOutput.IsCompletedSuccessfully && plainOutput.GetAwaiter().GetResult(),
                        "Untyped suppression cancellation result mismatch.");
                    Require(OnityTask<int>.FromResult(0).SuppressCancellationThrow().GetAwaiter().GetResult() == (false, 0),
                        "Typed default success was confused with cancellation.");
                    Require(!OnityTask.Completed.SuppressCancellationThrow().GetAwaiter().GetResult(),
                        "Untyped success was confused with cancellation.");
                    OperationCanceledException fault = new OperationCanceledException("Player faulted OCE", cancellation.Token);
                    OnityTask<(bool isCanceled, int result)> typedFault = OnityTask<int>.FromTask(
                        Task.FromException<int>(fault)).SuppressCancellationThrow();
                    OnityTask<bool> plainFault = OnityTask.FromTask(Task.FromException(fault)).SuppressCancellationThrow();
                    Require(typedFault.IsFaulted && plainFault.IsFaulted, "Suppression converted a faulted OCE to cancellation.");
                    Exception typedError = null;
                    Exception plainError = null;
                    try
                    {
                        typedFault.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        typedError = exception;
                    }
                    try
                    {
                        plainFault.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        plainError = exception;
                    }
                    Require(ReferenceEquals(typedError, fault) && ReferenceEquals(plainError, fault),
                        "Suppression changed faulted OCE identity.");
                    yield return null;
                }
                finally
                {
                    typed.TrySetResult(0);
                    plain.TrySetResult();
                }
            }
        }

        private static IEnumerator PlayerLoopFrameDistances()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                OnityTaskPlayerLoop.Initialize();
                OnityTaskPlayerLoop.Initialize();
                int registered = Time.frameCount;
                int[] nextFrames = { -1, -1, -1 };
                int[] delayedFrames = { -1, -1, -1 };
                Exception error = null;
                try
                {
                    Require(OnityTask.DelayFrames(0, OnityPlayerLoopTiming.Update, default).IsCompletedSuccessfully,
                        "Zero-frame timing wait did not complete inline.");
                    for (int i = 0; i < 3; i++)
                    {
                        int index = i;
                        OnityTask next = OnityTask.NextFrame((OnityPlayerLoopTiming)i, cancellation.Token);
                        OnityTask delayed = OnityTask.DelayFrames(2, (OnityPlayerLoopTiming)i, cancellation.Token);
                        ObserveTimingSmoke(next, () => nextFrames[index] = Time.frameCount, exception => error = exception);
                        ObserveTimingSmoke(delayed, () => delayedFrames[index] = Time.frameCount, exception => error = exception);
                    }
                    IEnumerator wait = WaitForThreadPool(() => error != null
                        || (delayedFrames[0] >= 0 && delayedFrames[1] >= 0 && delayedFrames[2] >= 0));
                    while (wait.MoveNext())
                    {
                        yield return null;
                    }
                    Require(error == null, "Timing publication failed: " + error);
                    for (int i = 0; i < 3; i++)
                    {
                        Require(nextFrames[i] - registered >= 1, "NextFrame did not cross a rendered frame.");
                        Require(delayedFrames[i] - registered >= 2, "DelayFrames was shortened by phase ticks.");
                    }
                }
                finally
                {
                    cancellation.Cancel();
                }
            }
        }

        private static IEnumerator PlayerLoopPausedCancellation()
        {
            float previousScale = Time.timeScale;
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (CancellationTokenSource nestedCancellation = new CancellationTokenSource())
            {
                Task worker = null;
                int main = Thread.CurrentThread.ManagedThreadId;
                int callbackThread = 0;
                int outerFrame = -1;
                int nestedFrame = -1;
                Exception error = null;
                try
                {
                    Time.timeScale = 0f;
                    OnityTask wait = OnityTask.DelayFrames(1000, OnityPlayerLoopTiming.FixedUpdate, cancellation.Token);
                    wait.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        try
                        {
                            ReadTimingSmokeCancellation(wait, cancellation.Token);
                            callbackThread = Thread.CurrentThread.ManagedThreadId;
                            outerFrame = Time.frameCount;
                            OnityTask nested = OnityTask.Yield(OnityPlayerLoopTiming.Update, nestedCancellation.Token);
                            nested.GetAwaiter().UnsafeOnCompleted(() =>
                            {
                                try
                                {
                                    ReadTimingSmokeCancellation(nested, nestedCancellation.Token);
                                    nestedFrame = Time.frameCount;
                                }
                                catch (Exception exception)
                                {
                                    error = exception;
                                }
                            });
                            nestedCancellation.Cancel();
                        }
                        catch (Exception exception)
                        {
                            error = exception;
                        }
                    });
                    worker = Task.Run(() => cancellation.Cancel());
                    Require(worker.Wait(TimeSpan.FromSeconds(5)), "Timing cancellation worker timed out.");
                    Require(!wait.IsCompleted, "Timing cancellation published from the worker.");
                    IEnumerator poll = WaitForThreadPool(() => error != null || nestedFrame >= 0);
                    while (poll.MoveNext())
                    {
                        yield return null;
                    }
                    Require(error == null, "Paused timing cancellation failed: " + error);
                    Require(callbackThread == main, "Paused fixed cancellation did not publish on main.");
                    Require(nestedFrame > outerFrame, "Reentrant canceled Update wait completed in the same pass.");
                    ReadTimingSmokeCancellation(OnityTask.DelayFrames(0, OnityPlayerLoopTiming.Update, cancellation.Token),
                        cancellation.Token);
                }
                finally
                {
                    cancellation.Cancel();
                    nestedCancellation.Cancel();
                    if (worker != null)
                    {
                        Require(worker.Wait(TimeSpan.FromSeconds(5)), "Timing smoke cleanup worker timed out.");
                    }
                    Time.timeScale = previousScale;
                }
            }
        }

        private static IEnumerator EveryUpdateAdapterSmoke()
        {
            using (var caller = new CancellationTokenSource())
            {
                var iterator = OnityAsyncEnumerable.EveryUpdate().GetAsyncEnumerator(caller.Token);
                int firstFrame = -1;
                int secondFrame = -1;
                Exception error = null;
                Task worker = null;
                OnityTask disposal = default;
                try
                {
                    yield return null;
                    yield return null;
                    Require(ReadTimeoutSmokeException(() => { var ignored = iterator.Current; }) is InvalidOperationException,
                        "Idle EveryUpdate acquired a Current item.");
                    var first = iterator.MoveNextAsync();
                    first.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        try
                        {
                            Require(first.GetAwaiter().GetResult(), "EveryUpdate ended without a Unit item.");
                            firstFrame = Time.frameCount;
                            var second = iterator.MoveNextAsync();
                            second.GetAwaiter().UnsafeOnCompleted(() =>
                            {
                                try
                                {
                                    Require(second.GetAwaiter().GetResult(), "Reentrant EveryUpdate ended.");
                                    secondFrame = Time.frameCount;
                                }
                                catch (Exception exception)
                                {
                                    error = exception;
                                }
                            });
                        }
                        catch (Exception exception)
                        {
                            error = exception;
                        }
                    });
                    IEnumerator wait = WaitForThreadPool(() => error != null || secondFrame >= 0);
                    while (wait.MoveNext())
                    {
                        yield return null;
                    }
                    Require(error == null && secondFrame > firstFrame, "EveryUpdate reentrant request used the same pass.");
                    Require(iterator.Current.Equals(Onity.Core.Unit.Default), "EveryUpdate returned a non-Unit value.");
                    yield return null;
                    yield return null;
                    var pending = iterator.MoveNextAsync();
                    bool falseOnWorker = false;
                    bool cleanupPendingOnWorker = false;
                    worker = Task.Run(() =>
                    {
                        disposal = iterator.DisposeAsync();
                        falseOnWorker = pending.IsCompletedSuccessfully && !pending.GetAwaiter().GetResult();
                        cleanupPendingOnWorker = !disposal.IsCompleted;
                    });
                    Require(worker.Wait(k_workerTimeoutMilliseconds), "EveryUpdate disposal worker timed out.");
                    worker.GetAwaiter().GetResult();
                    Require(falseOnWorker && cleanupPendingOnWorker, "EveryUpdate disposal lost its false/drain boundary.");
                    Require(!caller.IsCancellationRequested, "EveryUpdate canceled its caller CTS.");
                    wait = WaitForThreadPool(() => disposal.IsCompleted);
                    while (wait.MoveNext())
                    {
                        yield return null;
                    }
                    disposal.GetAwaiter().GetResult();
                    Require(!iterator.MoveNextAsync().GetAwaiter().GetResult(), "Disposed EveryUpdate resumed.");
                }
                finally
                {
                    caller.Cancel();
                    if (worker != null)
                    {
                        Require(worker.Wait(k_workerTimeoutMilliseconds), "EveryUpdate cleanup worker timed out.");
                    }
                    iterator.DisposeAsync();
                }
            }
        }

        private static IEnumerator BclAdapterSmoke()
        {
            foreach (bool throwBody in new[] { false, true })
            {
                var native = new FiniteSmokeSource();
                var fault = new InvalidOperationException("BCL foreach body");
                Task<int> body = ConsumeBclSmoke(native.AsAsyncEnumerable(), throwBody, fault);
                IEnumerator wait = WaitForThreadPool(() => body.IsCompleted);
                while (wait.MoveNext())
                {
                    yield return null;
                }
                if (throwBody)
                {
                    Require(ReferenceEquals(ReadTimeoutSmokeException(() => body.GetAwaiter().GetResult()), fault),
                        "Exported BCL foreach changed body fault identity.");
                }
                else
                {
                    Require(body.GetAwaiter().GetResult() == 19, "Exported BCL foreach break result changed.");
                }
                Require(native.Disposals == 1, "Exported BCL foreach cleanup repeated.");
            }

            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int finalizations = 0;
            var real = AdapterCompilerIterator(release.Task, () => finalizations++).AsOnityAsyncEnumerable()
                .GetAsyncEnumerator();
            var move = real.MoveNextAsync();
            var dispose = real.DisposeAsync();
            try
            {
                Require(move.IsCompletedSuccessfully && !move.GetAwaiter().GetResult(),
                    "Imported compiler iterator did not honor explicit disposal false.");
                Require(!dispose.IsCompleted && finalizations == 0,
                    "Imported compiler iterator disposed while its Move ValueTask was unconsumed.");
                release.TrySetResult(true);
                IEnumerator wait = WaitForThreadPool(() => dispose.IsCompleted);
                while (wait.MoveNext())
                {
                    yield return null;
                }
                dispose.GetAwaiter().GetResult();
                Require(finalizations == 1, "Compiler iterator finally did not run exactly once.");
            }
            finally
            {
                release.TrySetResult(true);
                real.DisposeAsync();
            }

            var reusable = new AdapterSmokeValueSource();
            var bcl = new AdapterSmokeBclSource { Move = () => reusable.Task };
            var imported = bcl.AsOnityAsyncEnumerable().GetAsyncEnumerator();
            try
            {
                for (int version = 0; version < 2; version++)
                {
                    if (version != 0)
                    {
                        reusable.Reset();
                    }
                    var next = imported.MoveNextAsync();
                    reusable.Complete(ValueTaskSourceStatus.Succeeded, null);
                    Require(next.GetAwaiter().GetResult() && imported.Current == 47,
                        "Reusable ValueTask version did not yield an item.");
                    Require(reusable.Results == version + 1 && reusable.Registrations == version + 1,
                        "Reusable ValueTask was consumed or registered more than once.");
                    Require(reusable.Flags == ValueTaskSourceOnCompletedFlags.None,
                        "Import observer captured scheduling/execution context flags.");
                }
            }
            finally
            {
                reusable.Complete(ValueTaskSourceStatus.Succeeded, null);
                imported.DisposeAsync().GetAwaiter().GetResult();
            }
            Require(bcl.Disposals == 1, "Reusable source cleanup repeated.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                foreach (bool canceled in new[] { false, true })
                {
                    var outcome = new AdapterSmokeValueSource();
                    var source = new AdapterSmokeBclSource { Move = () => outcome.Task };
                    var first = source.AsOnityAsyncEnumerable().FirstAsync();
                    var fault = new OperationCanceledException("BCL actual outcome", cancellation.Token);
                    outcome.Complete(canceled ? ValueTaskSourceStatus.Canceled : ValueTaskSourceStatus.Faulted, fault);
                    Require(first.IsCanceled == canceled && first.IsFaulted != canceled,
                        "Imported actual ValueTask outcome status changed.");
                    Exception error = ReadTimeoutSmokeException(() => first.GetAwaiter().GetResult());
                    if (canceled)
                    {
                        Require(error is OperationCanceledException
                            && ((OperationCanceledException)error).CancellationToken == cancellation.Token,
                            "Imported cancellation token changed.");
                    }
                    else
                    {
                        Require(ReferenceEquals(error, fault), "Imported faulted OCE identity changed.");
                    }
                    Require(outcome.Results == 1 && source.Disposals == 1,
                        "Fault/cancellation observation or cleanup repeated.");
                }
            }
        }

        private static async Task<int> ConsumeBclSmoke(IAsyncEnumerable<int> source, bool throwBody, Exception fault)
        {
            await foreach (int value in source.ConfigureAwait(false))
            {
                if (throwBody)
                {
                    throw fault;
                }
                return value;
            }
            return -1;
        }

        private static async IAsyncEnumerable<int> AdapterCompilerIterator(Task release, Action cleanup,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            try
            {
                await release.ConfigureAwait(false);
                yield return 47;
            }
            finally
            {
                cleanup();
            }
        }

        private sealed class AdapterSmokeBclSource : IAsyncEnumerable<int>, IAsyncEnumerator<int>
        {
            internal Func<ValueTask<bool>> Move;
            internal int Disposals;
            public int Current => 47;
            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
            public ValueTask<bool> MoveNextAsync() => Move();
            public ValueTask DisposeAsync()
            {
                Disposals++;
                return default;
            }
        }

        private sealed class AdapterSmokeValueSource : IValueTaskSource<bool>
        {
            private short m_version = 1;
            private ValueTaskSourceStatus m_status = ValueTaskSourceStatus.Pending;
            private Action<object> m_callback;
            private object m_state;
            private Exception m_error;
            internal int Results;
            internal int Registrations;
            internal ValueTaskSourceOnCompletedFlags Flags;
            internal ValueTask<bool> Task => new ValueTask<bool>(this, m_version);
            public ValueTaskSourceStatus GetStatus(short token)
            {
                Require(token == m_version, "Reusable ValueTask status read after GetResult.");
                return m_status;
            }
            public bool GetResult(short token)
            {
                Require(token == m_version && m_status != ValueTaskSourceStatus.Pending,
                    "Reusable ValueTask consumed twice or while pending.");
                Results++;
                m_version++;
                if (m_error != null)
                {
                    throw m_error;
                }
                return true;
            }
            public void OnCompleted(Action<object> callback, object state, short token,
                ValueTaskSourceOnCompletedFlags flags)
            {
                Require(token == m_version, "Reusable ValueTask registration token changed.");
                Registrations++;
                Flags = flags;
                m_callback = callback;
                m_state = state;
            }
            internal void Complete(ValueTaskSourceStatus status, Exception error)
            {
                if (m_status != ValueTaskSourceStatus.Pending)
                {
                    return;
                }
                m_status = status;
                m_error = error;
                Action<object> callback = m_callback;
                m_callback = null;
                callback?.Invoke(m_state);
            }
            internal void Reset()
            {
                m_status = ValueTaskSourceStatus.Pending;
                m_error = null;
            }
        }

        private static IEnumerator AwaitOperatorsSmoke()
        {
            int selected = 0;
            int filtered = 0;
            var array = OnityAsyncEnumerable.Range(1, 6)
                .SelectAwait((value, token) => { selected++; return OnityTask<int>.FromResult(value * 3); })
                .WhereAwait((value, token) => { filtered++; return OnityTask<bool>.FromResult(value % 2 == 0); })
                .ToArrayAsync().GetAwaiter().GetResult();
            Require(selected == 6 && filtered == 6 && array.Length == 3
                && array[0] == 6 && array[1] == 12 && array[2] == 18, "Await operator pipeline lost sequential FIFO values.");
            int rejected = 0;
            var empty = OnityAsyncEnumerable.Range(0, 100000).WhereAwait((value, token) =>
            {
                rejected++;
                return OnityTask<bool>.FromResult(false);
            }).ToArrayAsync().GetAwaiter().GetResult();
            Require(empty.Length == 0 && rejected == 100000, "Synchronous WhereAwait rejection failed to stay iterative.");
            int sum = 0;
            var terminal = OnityAsyncEnumerable.Range(1, 4).ForEachAsync((value, token) =>
            {
                sum += value;
                return default;
            });
            terminal.GetAwaiter().GetResult();
            Require(sum == 10, "ForEach action order/count changed.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var canceled = new OnityTaskCompletionSource();
                canceled.TrySetCanceled(cancellation.Token);
                var result = OnityAsyncEnumerable.Return(1).ForEachAsync((value, token) => canceled.Task);
                var error = ReadTimeoutSmokeException(() => result.GetAwaiter().GetResult()) as OperationCanceledException;
                Require(result.IsCanceled && error != null && error.CancellationToken == cancellation.Token,
                    "ForEach actual cancellation changed status/token.");
                var fault = new OperationCanceledException("actual await delegate fault", cancellation.Token);
                var faulted = OnityAsyncEnumerable.Return(1)
                    .SelectAwait((value, token) => OnityTask<int>.FromTask(Task.FromException<int>(fault))).FirstAsync();
                Require(faulted.IsFaulted && !faulted.IsCanceled
                    && ReferenceEquals(ReadTimeoutSmokeException(() => faulted.GetAwaiter().GetResult()), fault),
                    "SelectAwait faulted OCE was normalized to cancellation.");
                var predicate = OnityAsyncEnumerable.Return(1)
                    .WhereAwait((value, token) => throw fault).ToArrayAsync();
                Require(predicate.IsFaulted && ReferenceEquals(ReadTimeoutSmokeException(() => predicate.GetAwaiter().GetResult()), fault),
                    "WhereAwait synchronous thrown OCE lost fault identity.");
            }
            yield break;
        }

        private static IEnumerator AwaitOperatorDisposalSmoke()
        {
            foreach (bool cleanupFirst in new[] { false, true })
            {
                var gate = new CriticalGate();
                var pending = AwaitSmokeNative(gate);
                bool gateCompleted = false;
                Action completeDelegate = () =>
                {
                    if (!gateCompleted)
                    {
                        gateCompleted = true;
                        gate.Complete();
                    }
                };
                var cleanup = new OnityTaskCompletionSource();
                var source = new FiniteSmokeSource { Cleanup = () => cleanup.Task };
                var iterator = source.SelectAwait((value, token) => pending).GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                var disposal = iterator.DisposeAsync();
                try
                {
                    Require(move.IsCompletedSuccessfully && !move.GetAwaiter().GetResult(),
                        "Explicit await operator disposal did not publish false.");
                    Require(source.Moves == 1 && source.Disposals == 1 && !disposal.IsCompleted
                        && disposal.Equals(iterator.DisposeAsync()),
                        "Pending delegate prevented upstream disposal or shared cleanup.");
                    if (cleanupFirst)
                    {
                        cleanup.TrySetResult();
                    }
                    else
                    {
                        completeDelegate();
                    }
                    Require(!disposal.IsCompleted, "Await operator cleanup did not wait both accepted operations.");
                    completeDelegate();
                    cleanup.TrySetResult();
                    IEnumerator wait = WaitForThreadPool(() => disposal.IsCompleted);
                    while (wait.MoveNext())
                    {
                        yield return null;
                    }
                    disposal.GetAwaiter().GetResult();
                    Require(!iterator.MoveNextAsync().GetAwaiter().GetResult(), "Disposed await operator resumed.");
                }
                finally
                {
                    completeDelegate();
                    cleanup.TrySetResult();
                    iterator.DisposeAsync().GetAwaiter().GetResult();
                }
            }
            var upstream = new OnityTaskCompletionSource<bool>();
            var native = new FiniteSmokeSource
            {
                Input = upstream.Task,
                Cleanup = () => { upstream.TrySetResult(false); return default; }
            };
            var filtered = native.WhereAwait((value, token) => OnityTask<bool>.FromResult(true)).GetAsyncEnumerator();
            var pendingMove = filtered.MoveNextAsync();
            try
            {
                filtered.DisposeAsync().GetAwaiter().GetResult();
                Require(pendingMove.IsCompletedSuccessfully && !pendingMove.GetAwaiter().GetResult()
                    && native.Disposals == 1, "Native upstream move/disposal formed a cleanup deadlock.");
            }
            finally
            {
                upstream.TrySetResult(false);
                filtered.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        private static async OnityTask<int> AwaitSmokeNative(CriticalGate gate)
        {
            await gate;
            return 37;
        }

        private static IEnumerator ChannelBoundedSmoke()
        {
            var channel = OnityChannel.CreateBounded<int>(2);
            using (var cancellation = new CancellationTokenSource())
            {
                OnityTask canceled = default;
                OnityTask first = default;
                OnityTask second = default;
                try
                {
                    Require(channel.Writer.TryWrite(10) && channel.Writer.TryWrite(11),
                        "Bounded channel failed its initial fill.");
                    canceled = channel.Writer.WriteAsync(12, cancellation.Token);
                    first = channel.Writer.WriteAsync(13);
                    second = channel.Writer.WriteAsync(14);
                    Require(!canceled.IsCompleted && !first.IsCompleted && !second.IsCompleted
                        && !channel.Writer.TryWrite(15), "Queued writers lost backpressure or were bypassed.");
                    cancellation.Cancel();
                    var canceledError = ReadTimeoutSmokeException(() => canceled.GetAwaiter().GetResult())
                        as OperationCanceledException;
                    Require(canceled.IsCanceled && canceledError != null
                        && canceledError.CancellationToken == cancellation.Token,
                        "Queued writer cancellation lost its actual token.");
                    Require(channel.Reader.TryRead(out int value) && value == 10
                        && first.IsCompletedSuccessfully && !second.IsCompleted,
                        "The oldest accepted writer was not promoted first.");
                    first.GetAwaiter().GetResult();
                    Require(!channel.Writer.TryWrite(15), "TryWrite bypassed the remaining queued writer.");
                    Require(channel.Reader.ReadAsync().GetAwaiter().GetResult() == 11
                        && second.IsCompletedSuccessfully, "Second promotion changed FIFO order.");
                    second.GetAwaiter().GetResult();
                    var closure = new OperationCanceledException("actual channel closure fault", cancellation.Token);
                    Require(channel.Writer.TryComplete(closure) && !channel.Writer.TryComplete(),
                        "Channel completion was not first-wins.");
                    Require(channel.Reader.ReadAsync().GetAwaiter().GetResult() == 13
                        && channel.Reader.ReadAsync().GetAwaiter().GetResult() == 14,
                        "Closure failed to drain accepted values or retained a canceled writer item.");
                    var closedRead = channel.Reader.ReadAsync();
                    Require(closedRead.IsFaulted && !closedRead.IsCanceled
                        && ReferenceEquals(ReadTimeoutSmokeException(() => closedRead.GetAwaiter().GetResult()), closure),
                        "Channel closure converted the original faulted OCE into cancellation.");
                    var closedWrite = channel.Writer.WriteAsync(16);
                    var closedError = ReadTimeoutSmokeException(() => closedWrite.GetAwaiter().GetResult())
                        as OnityChannelClosedException;
                    Require(closedError != null && ReferenceEquals(closedError.InnerException, closure),
                        "Rejected writer did not preserve the channel closure cause.");
                }
                finally
                {
                    channel.Writer.TryComplete();
                    ReadTimeoutSmokeException(() => canceled.GetAwaiter().GetResult());
                    ReadTimeoutSmokeException(() => first.GetAwaiter().GetResult());
                    ReadTimeoutSmokeException(() => second.GetAwaiter().GetResult());
                }
            }
            yield break;
        }

        private static IEnumerator ChannelReadAllSmoke()
        {
            for (int winner = 0; winner < 2; winner++)
            {
                var channel = OnityChannel.CreateUnbounded<int>();
                using (var captured = new CancellationTokenSource())
                using (var enumeration = new CancellationTokenSource())
                {
                    var iterator = channel.Reader.ReadAllAsync(captured.Token)
                        .GetAsyncEnumerator(enumeration.Token);
                    var move = iterator.MoveNextAsync();
                    try
                    {
                        Require(!move.IsCompleted && !channel.Reader.TryRead(out _),
                            "Pending ReadAll failed to retain its single consumer lease.");
                        var chosen = winner == 0 ? captured : enumeration;
                        chosen.Cancel();
                        var error = ReadTimeoutSmokeException(() => move.GetAwaiter().GetResult())
                            as OperationCanceledException;
                        Require(move.IsCanceled && error != null && error.CancellationToken == chosen.Token,
                            "ReadAll token-pair cancellation changed the winning original token.");
                        iterator.DisposeAsync().GetAwaiter().GetResult();
                        Require(channel.Writer.TryWrite(21)
                            && channel.Reader.ReadAsync().GetAwaiter().GetResult() == 21,
                            "Canceled ReadAll retained its lease or consumed a replacement item.");
                    }
                    finally
                    {
                        iterator.DisposeAsync().GetAwaiter().GetResult();
                        channel.Writer.TryComplete();
                    }
                }
            }
            for (int escape = 0; escape < 3; escape++)
            {
                var channel = OnityChannel.CreateUnbounded<int>();
                var iterator = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                var preserved = escape == 1 ? move.Preserve() : default;
                Task<bool> bridge = escape == 2 ? move.AsTask() : null;
                try
                {
                    var disposal = iterator.DisposeAsync();
                    Require(disposal.IsCompleted && move.IsCompleted,
                        "Completed ReadAll disposal left its owned move pending.");
                    disposal.GetAwaiter().GetResult();
                    bool value = escape == 2 ? bridge.GetAwaiter().GetResult()
                        : (escape == 1 ? preserved : move).GetAwaiter().GetResult();
                    Require(!value && !iterator.MoveNextAsync().GetAwaiter().GetResult(),
                        "Explicit ReadAll disposal did not produce sticky false.");
                    Require(channel.Writer.TryWrite(23)
                        && channel.Reader.ReadAsync().GetAwaiter().GetResult() == 23,
                        "Disposed ReadAll retained the lease or closed the shared channel.");
                }
                finally
                {
                    iterator.DisposeAsync().GetAwaiter().GetResult();
                    channel.Writer.TryComplete();
                }
            }
            foreach (bool throwBody in new[] { false, true })
            {
                var channel = OnityChannel.CreateUnbounded<int>();
                try
                {
                    channel.Writer.TryWrite(31);
                    channel.Writer.TryWrite(32);
                    var fault = new InvalidOperationException("channel foreach body");
                    var consumption = ConsumeChannelSmoke(channel.Reader.ReadAllAsync(), throwBody, fault);
                    Require(consumption.IsCompleted, "Buffered channel foreach failed to stay inline.");
                    if (throwBody)
                    {
                        Require(ReferenceEquals(ReadTimeoutSmokeException(() => consumption.GetAwaiter().GetResult()), fault),
                            "Channel foreach changed the body exception.");
                    }
                    else
                    {
                        Require(consumption.GetAwaiter().GetResult() == 31, "Channel foreach break changed Current.");
                    }
                    Require(channel.Reader.ReadAsync().GetAwaiter().GetResult() == 32
                        && channel.Writer.TryWrite(33), "Foreach cleanup consumed the next item or closed the channel.");
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }
            yield break;
        }

        private static async OnityTask<int> ConsumeChannelSmoke(IOnityAsyncEnumerable<int> source,
            bool throwBody, Exception fault)
        {
            await foreach (int value in source)
            {
                if (throwBody)
                {
                    throw fault;
                }
                return value;
            }
            throw new InvalidOperationException("Channel foreach ended before its buffered item.");
        }

        private static IEnumerator FiniteInlineStreams()
        {
            int[] values = OnityAsyncEnumerable.Range(1, 10).Where(value => value % 2 == 0)
                .Select(value => value * 3).Take(3).ToArrayAsync().GetAwaiter().GetResult();
            Require(values.Length == 3 && values[0] == 6 && values[1] == 12 && values[2] == 18,
                "Finite pipeline order/Take limit changed.");
            IOnityAsyncEnumerable<object> covariance = OnityAsyncEnumerable.Return("finite covariance");
            Require((string)covariance.FirstAsync().GetAwaiter().GetResult() == "finite covariance",
                "Native covariance failed.");
            foreach (bool throwBody in new[] { false, true })
            {
                var source = new FiniteSmokeSource();
                var fault = new InvalidOperationException("finite foreach body");
                var consumption = ConsumeFiniteSmoke(source, throwBody, fault);
                Require(consumption.IsCompleted, "Finite foreach did not stay inline.");
                if (throwBody)
                {
                    Require(ReferenceEquals(ReadTimeoutSmokeException(() => consumption.GetAwaiter().GetResult()), fault),
                        "Finite foreach body exception changed.");
                }
                else
                {
                    Require(consumption.GetAwaiter().GetResult() == 19, "Finite foreach break result changed.");
                }
                Require(source.Acquires == 1 && source.Moves == 1 && source.Disposals == 1,
                    "Finite foreach did not clean upstream exactly once.");
            }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var empty = new FiniteSmokeSource();
                Require(empty.Take(0).WithCancellation(cancellation.Token).ToArrayAsync(cancellation.Token)
                    .GetAwaiter().GetResult().Length == 0 && empty.Acquires == 0,
                    "Take zero acquired upstream or canceled the empty stream.");
                var cleanupFault = new OperationCanceledException("actual cleanup fault", cancellation.Token);
                var failure = new FiniteSmokeSource
                {
                    Cleanup = () => OnityTask.FromTask(Task.FromException(cleanupFault))
                };
                var first = failure.FirstAsync();
                Require(first.IsFaulted && !first.IsCanceled, "Cleanup faulted OCE became cancellation.");
                Require(ReferenceEquals(ReadTimeoutSmokeException(() => first.GetAwaiter().GetResult()), cleanupFault),
                    "Cleanup failure did not replace the proposed first item.");
                Require(failure.Disposals == 1, "Faulting terminal cleanup repeated.");
            }
            yield break;
        }

        private static IEnumerator FinitePendingDisposal()
        {
            for (int kind = 0; kind < 3; kind++)
            {
                var gate = kind == 2 ? null : new CriticalGate();
                var task = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                OnityTask<bool> input = kind == 2 ? OnityTask<bool>.FromTask(task.Task) : FiniteNativeMove(gate);
                if (kind == 1)
                {
                    input = input.Preserve();
                }
                bool gateCompleted = false;
                Action completeMove = () =>
                {
                    if (gate != null && !gateCompleted)
                    {
                        gateCompleted = true;
                        gate.Complete();
                    }
                    task.TrySetResult(true);
                };
                var cleanup = new OnityTaskCompletionSource();
                var source = new FiniteSmokeSource
                {
                    Input = input,
                    Cleanup = () => { completeMove(); return cleanup.Task; }
                };
                using (var caller = new CancellationTokenSource())
                {
                    var iterator = source.Select(value => value).GetAsyncEnumerator(caller.Token);
                    var move = iterator.MoveNextAsync();
                    caller.Cancel();
                    Require(!move.IsCompleted, "The test source must ignore caller cancellation.");
                    var disposal = iterator.DisposeAsync();
                    var repeat = iterator.DisposeAsync();
                    try
                    {
                        Require(move.IsCompletedSuccessfully && !move.GetAwaiter().GetResult(),
                            "Explicit disposal did not publish false before delayed cleanup.");
                        Require(source.Disposals == 1 && !disposal.IsCompleted && repeat.Equals(disposal),
                            "Disposal failed to start/share cleanup while the native move was pending.");
                        cleanup.TrySetResult();
                        IEnumerator wait = WaitForThreadPool(() => disposal.IsCompleted);
                        while (wait.MoveNext())
                        {
                            yield return null;
                        }
                        disposal.GetAwaiter().GetResult();
                        repeat.GetAwaiter().GetResult();
                        Require(!iterator.MoveNextAsync().GetAwaiter().GetResult(), "Disposed stream resumed.");
                    }
                    finally
                    {
                        completeMove();
                        cleanup.TrySetResult();
                        iterator.DisposeAsync();
                    }
                }
            }
        }

        private static async OnityTask<int> ConsumeFiniteSmoke(FiniteSmokeSource source, bool throwBody, Exception fault)
        {
            int result = 0;
            await foreach (int value in source.Where(value => true))
            {
                if (throwBody)
                {
                    throw fault;
                }
                result = value;
                break;
            }
            return result;
        }

        private static async OnityTask<bool> FiniteNativeMove(CriticalGate gate)
        {
            await gate;
            return true;
        }

        private sealed class FiniteSmokeSource : IOnityAsyncEnumerable<int>, IOnityAsyncEnumerator<int>
        {
            internal OnityTask<bool> Input = OnityTask<bool>.FromResult(true);
            internal Func<OnityTask> Cleanup = () => default;
            internal int Acquires;
            internal int Moves;
            internal int Disposals;
            public int Current => 19;

            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Acquires++;
                return this;
            }

            public OnityTask<bool> MoveNextAsync()
            {
                Moves++;
                return Input;
            }

            public OnityTask DisposeAsync()
            {
                Disposals++;
                return Cleanup();
            }
        }

        private static IEnumerator EndOfFrameHeadlessGuard()
        {
            Require(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null,
                "This EOF smoke proves Null-graphics rejection and must use the headless smoke launch.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Require(ReadTimeoutSmokeException(() => OnityTask.WaitForEndOfFrame()) is PlatformNotSupportedException,
                    "Null-graphics EOF request was accepted.");
                Require(ReadTimeoutSmokeException(() => OnityTask.WaitForEndOfFrame(cancellation.Token)) is PlatformNotSupportedException,
                    "Precancellation bypassed the rendering guard.");
            }
            yield return null;
        }

        private static IEnumerator TimeoutOutcomes()
        {
            var typed = new OnityTaskCompletionSource<int>();
            var plain = new OnityTaskCompletionSource();
            Exception lateFault = new InvalidOperationException("Late Player timeout loser.");
            try
            {
                OnityTask<int> typedTimeout = typed.Task.Timeout(0f);
                OnityTask plainTimeout = plain.Task.Timeout(0f);
                var typedFlag = typed.Task.TimeoutWithoutException(0f);
                var plainFlag = plain.Task.TimeoutWithoutException(0f);
                Require(ReadTimeoutSmokeException(() => typedTimeout.GetAwaiter().GetResult()) is TimeoutException,
                    "Typed pending zero did not time out.");
                Require(ReadTimeoutSmokeException(() => plainTimeout.GetAwaiter().GetResult()) is TimeoutException,
                    "Untyped pending zero did not time out.");
                Require(typedFlag.GetAwaiter().GetResult() == (true, 0), "Typed own-timeout flag mismatch.");
                Require(plainFlag.GetAwaiter().GetResult(), "Untyped own-timeout flag mismatch.");
                Require(!typed.Task.IsCompleted && !plain.Task.IsCompleted, "Timeout canceled the producer.");
                typed.TrySetException(lateFault);
                plain.TrySetException(lateFault);
                Require(ReferenceEquals(typed.Task.AsTask().Exception.InnerException, lateFault)
                    && ReferenceEquals(plain.Task.AsTask().Exception.InnerException, lateFault),
                    "Late producer fault identity changed.");
                Require(OnityTask<int>.FromResult(0).TimeoutWithoutException(0f).GetAwaiter().GetResult() == (false, 0),
                    "Default producer success was confused with timeout.");
                Require(!OnityTask.Completed.TimeoutWithoutException(0f).GetAwaiter().GetResult(),
                    "Completed untyped input lost entry precedence.");
            }
            finally
            {
                typed.TrySetResult(0);
                plain.TrySetResult();
            }
            using (var cancellation = new CancellationTokenSource())
            {
                var canceledTyped = new OnityTaskCompletionSource<int>();
                var canceledPlain = new OnityTaskCompletionSource();
                OnityTask<int> typedOutput = canceledTyped.Task.Timeout(1000f);
                OnityTask plainOutput = canceledPlain.Task.Timeout(1000f);
                var typedFlag = canceledTyped.Task.TimeoutWithoutException(1000f);
                var plainFlag = canceledPlain.Task.TimeoutWithoutException(1000f);
                try
                {
                    canceledTyped.TrySetCanceled(cancellation.Token);
                    canceledPlain.TrySetCanceled(cancellation.Token);
                    Require(typedOutput.IsCanceled && plainOutput.IsCanceled && typedFlag.IsCanceled && plainFlag.IsCanceled,
                        "Producer cancellation became timeout or success.");
                    foreach (Action consume in new Action[]
                    {
                        () => typedOutput.GetAwaiter().GetResult(), () => plainOutput.GetAwaiter().GetResult(),
                        () => typedFlag.GetAwaiter().GetResult(), () => plainFlag.GetAwaiter().GetResult()
                    })
                    {
                        Exception error = ReadTimeoutSmokeException(consume);
                        Require(error is OperationCanceledException canceled && canceled.CancellationToken == cancellation.Token,
                            "Timeout adapter changed producer cancellation token.");
                    }
                    Require(!cancellation.IsCancellationRequested, "Timeout adapter canceled the token owner.");
                    foreach (Exception fault in new Exception[]
                    {
                        new OperationCanceledException("Faulted producer OCE.", cancellation.Token),
                        new TimeoutException("Producer-owned timeout exception.")
                    })
                    {
                        OnityTask<int> faultedTyped = OnityTask<int>.FromTask(Task.FromException<int>(fault));
                        OnityTask faultedPlain = OnityTask.FromTask(Task.FromException(fault));
                        OnityTask<int> typedFault = faultedTyped.Timeout(0f);
                        OnityTask plainFault = faultedPlain.Timeout(0f);
                        var typedFaultFlag = faultedTyped.TimeoutWithoutException(0f);
                        var plainFaultFlag = faultedPlain.TimeoutWithoutException(0f);
                        Require(typedFault.IsFaulted && plainFault.IsFaulted && typedFaultFlag.IsFaulted && plainFaultFlag.IsFaulted,
                            "Producer fault was converted to cancellation or own timeout.");
                        Require(ReferenceEquals(ReadTimeoutSmokeException(() => typedFault.GetAwaiter().GetResult()), fault)
                            && ReferenceEquals(ReadTimeoutSmokeException(() => plainFault.GetAwaiter().GetResult()), fault)
                            && ReferenceEquals(ReadTimeoutSmokeException(() => typedFaultFlag.GetAwaiter().GetResult()), fault)
                            && ReferenceEquals(ReadTimeoutSmokeException(() => plainFaultFlag.GetAwaiter().GetResult()), fault),
                            "Timeout adapter changed original fault identity.");
                    }
                    yield return null;
                }
                finally
                {
                    canceledTyped.TrySetResult(0);
                    canceledPlain.TrySetResult();
                }
            }
        }

        private static IEnumerator TimeoutClocksAndPublication()
        {
            float previousScale = Time.timeScale;
            var scaledProducer = new OnityTaskCompletionSource<int>();
            var unscaledProducer = new OnityTaskCompletionSource();
            var childProducer = new OnityTaskCompletionSource<int>();
            var workerGate = new CriticalGate();
            bool workerGateRegistered = false;
            int workerGateClaimed = 0;
            Task<int> worker = null;
            try
            {
                Time.timeScale = 0f;
                yield return null;
                double paused = Time.timeAsDouble;
                var scaled = scaledProducer.Task.TimeoutWithoutException(float.Epsilon, false);
                var unscaled = unscaledProducer.Task.TimeoutWithoutException(0.02f);
                IEnumerator poll = WaitForThreadPool(() => unscaled.IsCompleted);
                while (poll.MoveNext())
                {
                    yield return null;
                }
                Require(unscaled.GetAwaiter().GetResult(), "Unscaled timeout did not expire while paused.");
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                    Require(Time.timeAsDouble == paused && !scaled.IsCompleted,
                        "Positive epsilon scaled timeout expired without clock advancement.");
                }
                Time.timeScale = previousScale > 0f ? previousScale : 1f;
                poll = WaitForThreadPool(() => scaled.IsCompleted);
                while (poll.MoveNext())
                {
                    yield return null;
                }
                Require(scaled.GetAwaiter().GetResult() == (true, 0), "Scaled timeout did not expire after time resumed.");

                var native = PoolTypedAsync(workerGate, 61).Preserve().TimeoutWithoutException(1000f);
                workerGateRegistered = true;
                int completionThread = 0;
                Exception error = null;
                native.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        if (native.GetAwaiter().GetResult() != (false, 61))
                        {
                            throw new InvalidOperationException("Worker producer result changed.");
                        }
                        completionThread = Thread.CurrentThread.ManagedThreadId;
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                });
                worker = Task.Run(() =>
                {
                    if (Interlocked.Exchange(ref workerGateClaimed, 1) == 0)
                    {
                        workerGate.Complete();
                    }
                    return Thread.CurrentThread.ManagedThreadId;
                });
                Require(worker.Wait(TimeSpan.FromSeconds(5)), "Timeout producer worker did not finish.");
                Require(error == null && completionThread == worker.Result,
                    "Timeout producer victory did not publish on the producer worker.");
                yield return null;

                int registeredFrame = -1;
                int completedFrame = -1;
                var signal = OnityTask.Yield(OnityPlayerLoopTiming.Update);
                signal.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        signal.GetAwaiter().GetResult();
                        registeredFrame = Time.frameCount;
                        var child = childProducer.Task.TimeoutWithoutException(float.Epsilon);
                        child.GetAwaiter().UnsafeOnCompleted(() =>
                        {
                            try
                            {
                                if (child.GetAwaiter().GetResult() != (true, 0))
                                {
                                    throw new InvalidOperationException("Reentrant own-timeout result changed.");
                                }
                                completedFrame = Time.frameCount;
                            }
                            catch (Exception exception)
                            {
                                error = exception;
                            }
                        });
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                });
                poll = WaitForThreadPool(() => error != null || completedFrame >= 0);
                while (poll.MoveNext())
                {
                    yield return null;
                }
                Require(error == null, "Reentrant timeout failed: " + error);
                Require(completedFrame > registeredFrame, "Callback-created timeout expired in its registration pass.");
            }
            finally
            {
                Time.timeScale = previousScale;
                scaledProducer.TrySetResult(42);
                unscaledProducer.TrySetResult();
                childProducer.TrySetResult(42);
                if (workerGateRegistered && Interlocked.Exchange(ref workerGateClaimed, 1) == 0)
                {
                    workerGate.Complete();
                }
                if (worker != null)
                {
                    Require(worker.Wait(TimeSpan.FromSeconds(5)), "Timeout smoke cleanup worker timed out.");
                }
            }
        }

        private static Exception ReadTimeoutSmokeException(Action consume)
        {
            try
            {
                consume();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static IEnumerator LegacyRunnerRetirement()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                // Default frame waits run on the PlayerLoop scheduler in Play and are retired by session
                // exit, not by legacy runner destruction (PERF-7).
                OnityTask[] waits =
                {
                    OnityTask.DelayUnscaled(1000f, cancellation.Token), OnityTask.WaitUntil(() => false, cancellation.Token),
                    OnityTask.WaitWhile(() => true, cancellation.Token)
                };
                AsyncOperation operation = Resources.UnloadUnusedAssets();
                Require(!operation.isDone, "Legacy smoke needs a pending real operation.");
                OnityTask<AsyncOperation> observation = operation.AsOnityTask(cancellationToken: cancellation.Token);
                GameObject owner = CurrentLegacySmokeRunner();
                try
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                    foreach (OnityTask wait in waits)
                    {
                        Require(wait.IsCanceled, "Legacy wait was orphaned after runner destruction.");
                        ReadTimingSmokeCancellation(wait, cancellation.Token);
                    }
                    Require(observation.IsCanceled, "Native operation observer was orphaned.");
                    try
                    {
                        observation.GetAwaiter().GetResult();
                        throw new InvalidOperationException("Native operation observer unexpectedly succeeded.");
                    }
                    catch (OperationCanceledException exception)
                    {
                        Require(exception.CancellationToken == cancellation.Token, "Typed retirement token changed.");
                    }
                    Require(!cancellation.IsCancellationRequested, "Retirement canceled the token owner.");
                    IEnumerator poll = WaitForThreadPool(() => operation.isDone);
                    while (poll.MoveNext())
                    {
                        yield return null;
                    }
                    // The underlying operation finished independently of the canceled observation.
                }
                finally
                {
                    DestroyLegacySmokeRunner();
                }
            }
        }

        private static IEnumerator LegacyActiveRetirement()
        {
            int calls = 0;
            bool complete = false;
            bool inside = false;
            bool completedInside = false;
            bool replacementComplete = false;
            Exception error = null;
            GameObject owner = null;
            OnityTask wait = OnityTask.WaitUntil(() =>
            {
                if (++calls == 1)
                {
                    return false;
                }
                inside = true;
                UnityEngine.Object.DestroyImmediate(owner);
                completedInside |= complete;
                OnityTask replacement = OnityTask.NextFrame();
                ObserveTimingSmoke(replacement, () => replacementComplete = true, exception => error = exception);
                inside = false;
                return true;
            });
            owner = CurrentLegacySmokeRunner();
            wait.GetAwaiter().UnsafeOnCompleted(() =>
            {
                completedInside |= inside;
                try
                {
                    wait.GetAwaiter().GetResult();
                    error = new InvalidOperationException("Retired active predicate succeeded.");
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                complete = true;
            });
            try
            {
                IEnumerator poll = WaitForThreadPool(() => error != null || (complete && replacementComplete));
                while (poll.MoveNext())
                {
                    yield return null;
                }
                Require(error == null, "Active retirement failed: " + error);
                Require(!completedInside, "Active source published before its predicate returned.");
                Require(complete && replacementComplete, "Replacement legacy runner did not remain live.");
            }
            finally
            {
                DestroyLegacySmokeRunner();
            }
        }

        private static GameObject CurrentLegacySmokeRunner()
        {
            Type type = typeof(OnityTask).Assembly.GetType("Onity.Unity.Async.OnityTaskRunner", true);
            Component runner = (Component)type.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);
            return runner != null ? runner.gameObject : null;
        }

        private static void DestroyLegacySmokeRunner()
        {
            GameObject owner = CurrentLegacySmokeRunner();
            if (owner != null)
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static void ObserveTimingSmoke(OnityTask task, Action complete, Action<Exception> fail)
        {
            task.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    task.GetAwaiter().GetResult();
                    complete();
                }
                catch (Exception exception)
                {
                    fail(exception);
                }
            });
        }

        private static void ReadTimingSmokeCancellation(OnityTask task, CancellationToken token)
        {
            try
            {
                task.GetAwaiter().GetResult();
                throw new InvalidOperationException("Expected timing cancellation.");
            }
            catch (OperationCanceledException exception)
            {
                if (exception.CancellationToken != token)
                {
                    throw new InvalidOperationException("Timing cancellation token identity mismatch.");
                }
            }
        }

        private static IEnumerator ThreadPoolSwitch()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            Require(!OnityTask.SwitchToThreadPool().GetAwaiter().IsCompleted, "Pool switch completed inline.");
            Task<int> switched = ThreadPoolRoundTripAsync(mainThread).AsTask();
            IEnumerator wait = WaitForThreadPool(() => switched.IsCompleted);
            while (wait.MoveNext())
            {
                yield return null;
            }
            Require(switched.GetAwaiter().GetResult() == mainThread, "Pool switch did not return to main.");
        }

        private static IEnumerator ThreadPoolOutcomes()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                int calls = 0;
                OnityTask plain = OnityTask.RunOnThreadPool(() => { calls++; }, true, cancellation.Token);
                OnityTask<int> typed = OnityTask.RunOnThreadPool(() => ++calls, false, cancellation.Token);
                Require(plain.IsCanceled && typed.IsCanceled && calls == 0, "Pre-canceled delegates ran.");
                ThreadPoolObservation first = new ThreadPoolObservation();
                ThreadPoolObservation second = new ThreadPoolObservation();
                first.Complete(() => { plain.GetAwaiter().GetResult(); return 0; });
                second.Complete(() => typed.GetAwaiter().GetResult());
                Require(first.error is OperationCanceledException firstCanceled
                    && firstCanceled.CancellationToken == cancellation.Token
                    && second.error is OperationCanceledException secondCanceled
                    && secondCanceled.CancellationToken == cancellation.Token, "Pre-cancel tokens changed.");
            }
            for (int typed = 0; typed < 2; typed++)
            {
                for (int main = 0; main < 2; main++)
                {
                    for (int outcome = 0; outcome < (main == 0 ? 3 : 4); outcome++)
                    {
                        IEnumerator routine = ThreadPoolOutcome(typed != 0, main != 0, outcome);
                        try
                        {
                            while (routine.MoveNext())
                            {
                                yield return null;
                            }
                        }
                        finally
                        {
                            (routine as IDisposable)?.Dispose();
                        }
                    }
                }
            }
        }

        private static IEnumerator ThreadPoolOutcome(bool typed, bool returnMain, int outcome)
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            Exception sentinel = new InvalidOperationException("Thread-pool Player fault sentinel.");
            ThreadPoolObservation observation = new ThreadPoolObservation();
            int workerThread = 0;
            int finished = 0;
            Func<int> work = () =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                entered.Set();
                if (!release.Wait(k_workerTimeoutMilliseconds))
                {
                    throw new TimeoutException("Thread-pool Player delegate gate timed out.");
                }
                Interlocked.Increment(ref finished);
                if (outcome == 2)
                {
                    throw sentinel;
                }
                return 42;
            };
            try
            {
                if (outcome == 3)
                {
                    DrainThreadPoolReturns();
                    Require(PendingThreadPoolReturns() == 0, "Return queue baseline was not empty.");
                }
                if (typed)
                {
                    var awaiter = OnityTask.RunOnThreadPool(work, returnMain, cancellation.Token).GetAwaiter();
                    awaiter.UnsafeOnCompleted(() => observation.Complete(() => awaiter.GetResult()));
                }
                else
                {
                    var awaiter = OnityTask.RunOnThreadPool((Action)(() => { work(); }), returnMain,
                        cancellation.Token).GetAwaiter();
                    awaiter.UnsafeOnCompleted(() => observation.Complete(() => { awaiter.GetResult(); return 0; }));
                }
                IEnumerator wait = WaitForThreadPool(() => entered.IsSet);
                while (wait.MoveNext())
                {
                    yield return null;
                }
                if (outcome == 1 || outcome == 2)
                {
                    cancellation.Cancel();
                    Require(Volatile.Read(ref observation.completed) == 0, "Cancel interrupted executing work.");
                }
                if (outcome == 3)
                {
                    DrainThreadPoolReturns();
                    Require(PendingThreadPoolReturns() == 0, "Held-worker queue baseline was not empty.");
                }
                release.Set();
                if (outcome == 3)
                {
                    Require(SpinWait.SpinUntil(() => PendingThreadPoolReturns() > 0,
                        k_workerTimeoutMilliseconds), "Main return was not queued.");
                    cancellation.Cancel();
                    DrainThreadPoolReturns();
                }
                wait = WaitForThreadPool(() => Volatile.Read(ref observation.completed) != 0);
                while (wait.MoveNext())
                {
                    yield return null;
                }
                Require(workerThread != mainThread && finished == 1, "Delegate did not finish once on worker.");
                Require((observation.threadId == mainThread) == returnMain, "Completion thread mismatch.");
                if (outcome == 0)
                {
                    Require(observation.error == null && observation.value == (typed ? 42 : 0),
                        "Thread-pool success mismatch: " + observation.error);
                }
                else if (outcome == 2)
                {
                    Require(ReferenceEquals(observation.error, sentinel), "Fault lost identity or cancellation won.");
                }
                else
                {
                    Require(observation.error is OperationCanceledException canceled
                        && canceled.CancellationToken == cancellation.Token, "Cancellation token mismatch.");
                }
            }
            finally
            {
                release.Set();
            }
            // Let the deferred IL2CPP return unwind before the next queue-baseline check.
            yield return null;
            yield return null;
        }

        private static IEnumerator ThreadPoolContext()
        {
            AsyncLocal<string> local = new AsyncLocal<string>();
            AsyncLocal<string> runLocal = new AsyncLocal<string>();
            try
            {
                for (int mode = 0; mode < 3; mode++)
                {
                    OnityTask.FlowExecutionContext = mode != 1;
                    local.Value = "caller";
                    runLocal.Value = "caller";
                    Task<string> switched;
                    Task<string> run;
                    if (mode == 2)
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            switched = ReadThreadPoolContextAsync(local).AsTask();
                            run = OnityTask.RunOnThreadPool(() => runLocal.Value, false).AsTask();
                        }
                    }
                    else
                    {
                        switched = ReadThreadPoolContextAsync(local).AsTask();
                        run = OnityTask.RunOnThreadPool(() => runLocal.Value, false).AsTask();
                    }
                    IEnumerator wait = WaitForThreadPool(() => switched.IsCompleted && run.IsCompleted);
                    while (wait.MoveNext())
                    {
                        yield return null;
                    }
                    string expected = mode == 0 ? "caller" : null;
                    Require(switched.GetAwaiter().GetResult() == expected
                        && run.GetAwaiter().GetResult() == expected, "Pool context flow mismatch for mode " + mode);
                    Require(local.Value == "caller" && runLocal.Value == "caller",
                        "Pool context writes escaped to caller.");
                    local.Value = null;
                    runLocal.Value = null;
                }
            }
            finally
            {
                local.Value = null;
                runLocal.Value = null;
                OnityTask.FlowExecutionContext = false;
            }
        }

        private static async OnityTask<int> ThreadPoolRoundTripAsync(int mainThread)
        {
            await OnityTask.SwitchToThreadPool();
            Require(Thread.CurrentThread.IsThreadPoolThread && Thread.CurrentThread.ManagedThreadId != mainThread,
                "Switch did not reach worker.");
            Require(!OnityTask.SwitchToThreadPool().GetAwaiter().IsCompleted, "Worker switch completed inline.");
            await OnityTask.SwitchToThreadPool();
            await OnityTask.SwitchToMainThread();
            return Thread.CurrentThread.ManagedThreadId;
        }

        private static async OnityTask<string> ReadThreadPoolContextAsync(AsyncLocal<string> local)
        {
            await OnityTask.SwitchToThreadPool();
            string value = local.Value;
            try
            {
                local.Value = "worker";
                return value;
            }
            finally
            {
                local.Value = value;
            }
        }

        private static IEnumerator WaitForThreadPool(Func<bool> predicate)
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            while (!predicate() && timer.ElapsedMilliseconds < k_workerTimeoutMilliseconds)
            {
                yield return null;
            }
            Require(predicate(), "Thread-pool Player operation timed out.");
        }

        private static Type MainThreadDispatcher => typeof(OnityTask).Assembly.GetType(
            "Onity.Unity.Async.OnityTaskMainThreadDispatcher", true);

        private static int PendingThreadPoolReturns()
        {
            return (int)MainThreadDispatcher.GetProperty("PendingCount", BindingFlags.Static | BindingFlags.Public)
                .GetValue(null);
        }

        private static void DrainThreadPoolReturns()
        {
            MainThreadDispatcher.GetMethod("Drain", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        }

        private sealed class ThreadPoolObservation
        {
            internal int threadId;
            internal int value;
            internal Exception error;
            internal int completed;

            internal void Complete(Func<int> getResult)
            {
                threadId = Thread.CurrentThread.ManagedThreadId;
                try
                {
                    value = getResult();
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    Volatile.Write(ref completed, 1);
                }
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
