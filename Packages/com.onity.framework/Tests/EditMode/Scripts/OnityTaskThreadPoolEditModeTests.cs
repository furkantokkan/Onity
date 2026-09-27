using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityTaskThreadPoolEditModeTests
    {
        private const int k_timeoutSeconds = 10;
        private bool m_previousFlow;

        [SetUp]
        public void SaveSettings()
        {
            m_previousFlow = OnityTask.FlowExecutionContext;
        }

        [TearDown]
        public void RestoreSettings()
        {
            OnityTask.FlowExecutionContext = m_previousFlow;
        }

        [Test]
        public void Factories_NullDelegates_ThrowSynchronously()
        {
            Assert.Throws<ArgumentNullException>(() => OnityTask.RunOnThreadPool((Action)null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.RunOnThreadPool((Func<int>)null));
            var awaiter = OnityTask.SwitchToThreadPool().GetAwaiter();
            Assert.Throws<ArgumentNullException>(() => awaiter.OnCompleted(null));
            Assert.Throws<ArgumentNullException>(() => awaiter.UnsafeOnCompleted(null));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Run_PreCanceled_DoesNotInvokeEitherDelegate(bool returnToMainThread)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int calls = 0;
            OnityTask plain = OnityTask.RunOnThreadPool(() => { calls++; }, returnToMainThread, cancellation.Token);
            OnityTask<int> typed = OnityTask.RunOnThreadPool(() => ++calls, returnToMainThread, cancellation.Token);
            Assert.That(plain.IsCanceled, Is.True);
            Assert.That(typed.IsCanceled, Is.True);
            Assert.That(Assert.Catch<OperationCanceledException>(() => plain.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(Assert.Catch<OperationCanceledException>(() => typed.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(calls, Is.Zero);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void Switch_RawRegistrations_DoNotCaptureContext_AndCancelOnWorker(bool safe, bool preCancel)
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            AsyncLocal<string> local = new AsyncLocal<string>();
            local.Value = "caller";
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            ManualResetEventSlim entered = new ManualResetEventSlim();
            ManualResetEventSlim release = new ManualResetEventSlim();
            int completed = 0;
            if (preCancel)
            {
                cancellation.Cancel();
            }

            var awaiter = OnityTask.SwitchToThreadPool(cancellation.Token).GetAwaiter();
            Assert.That(awaiter.IsCompleted, Is.False);
            Exception observed = null;
            string observedLocal = "unset";
            int workerThread = 0;
            Action continuation = () =>
            {
                try
                {
                    workerThread = Thread.CurrentThread.ManagedThreadId;
                    observedLocal = local.Value;
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)))
                    {
                        throw new TimeoutException("Worker gate was not released.");
                    }
                    awaiter.GetResult();
                }
                catch (Exception exception)
                {
                    observed = exception;
                }
                finally
                {
                    // A primitive flag cannot throw if the test already timed out.
                    Volatile.Write(ref completed, 1);
                }
            };
            try
            {
                if (safe)
                {
                    awaiter.OnCompleted(continuation);
                }
                else
                {
                    awaiter.UnsafeOnCompleted(continuation);
                }
                Assert.That(entered.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)), Is.True);
                Assert.That(local.Value, Is.EqualTo("caller"));
                cancellation.Cancel();
            }
            finally
            {
                release.Set();
                bool exited = SpinWait.SpinUntil(() => Volatile.Read(ref completed) != 0,
                    TimeSpan.FromSeconds(k_timeoutSeconds));
                local.Value = null;
                if (exited)
                {
                    entered.Dispose();
                    release.Dispose();
                }
                // On failure retain gates reachable by a delayed callback; never dispose under it.
                Assert.That(exited, Is.True);
            }
            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
            Assert.That(observedLocal, Is.Null);
            Assert.That(observed, Is.TypeOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)observed).CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(local.Value, Is.Null);
        }

        [UnityTest]
        public IEnumerator Switch_FromMainAndWorker_AlwaysQueuesAndSupportsReuse()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            OnityTaskThreadPoolSwitch reusable = OnityTask.SwitchToThreadPool();
            Task<int> first = ReadWorkerThread(reusable).AsTask();
            yield return WaitFor(() => first.IsCompleted);
            Assert.That(first.GetAwaiter().GetResult(), Is.Not.EqualTo(mainThread));
            Task<(bool completed, bool worker)> second = Task.Run(async () =>
            {
                bool completed = reusable.GetAwaiter().IsCompleted;
                await reusable;
                return (completed, Thread.CurrentThread.IsThreadPoolThread);
            });
            yield return WaitFor(() => second.IsCompleted);
            Assert.That(second.GetAwaiter().GetResult().completed, Is.False);
            Assert.That(second.Result.worker, Is.True);
        }

        [UnityTest]
        public IEnumerator Run_BothOverloads_AllOutcomes_PublishOnRequestedThread()
        {
            for (int typed = 0; typed < 2; typed++)
            {
                for (int main = 0; main < 2; main++)
                {
                    for (int outcome = 0; outcome < 3; outcome++)
                    {
                        yield return VerifyOutcome(typed != 0, main != 0, outcome);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator Run_CanceledBeforeWorkerInvocation_DoesNotInvokeDelegate()
        {
            OnityTask.FlowExecutionContext = true;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            for (int typed = 0; typed < 2; typed++)
            {
                for (int main = 0; main < 2; main++)
                {
                    using CancellationTokenSource cancellation = new CancellationTokenSource();
                    ManualResetEventSlim entered = new ManualResetEventSlim();
                    ManualResetEventSlim release = new ManualResetEventSlim();
                    int calls = 0;
                    int gateTimedOut = 0;
                    int gateEntries = 0;
                    int callbackExited = 0;
                    object marker = new object();
                    Observation observation = new Observation();
                    AsyncLocal<object> local = new AsyncLocal<object>(change =>
                    {
                        if (change.ThreadContextChanged && ReferenceEquals(change.CurrentValue, marker)
                            && Thread.CurrentThread.IsThreadPoolThread
                            && Interlocked.CompareExchange(ref gateEntries, 1, 0) == 0)
                        {
                            try
                            {
                                entered.Set();
                                if (!release.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)))
                                {
                                    Interlocked.Exchange(ref gateTimedOut, 1);
                                }
                            }
                            catch (Exception)
                            {
                                // Nothing may escape a context callback, including event failures.
                                Interlocked.Exchange(ref gateTimedOut, 1);
                            }
                            finally
                            {
                                Volatile.Write(ref callbackExited, 1);
                            }
                        }
                    });
                    local.Value = marker;
                    try
                    {
                        Register(typed != 0, () => ++calls, main != 0, cancellation.Token, observation);
                        yield return WaitFor(() => entered.IsSet);
                        cancellation.Cancel();
                        release.Set();
                        yield return WaitFor(() => Volatile.Read(ref observation.Completed) != 0);
                        Assert.That(gateTimedOut, Is.Zero);
                        Assert.That(calls, Is.Zero, "Canceled queued work invoked its delegate.");
                        Assert.That(observation.ThreadId == mainThread, Is.EqualTo(main != 0));
                        Assert.That(observation.Error, Is.TypeOf<OperationCanceledException>());
                        Assert.That(((OperationCanceledException)observation.Error).CancellationToken,
                            Is.EqualTo(cancellation.Token));
                    }
                    finally
                    {
                        release.Set();
                        local.Value = null;
                        if (Volatile.Read(ref callbackExited) != 0)
                        {
                            entered.Dispose();
                            release.Dispose();
                        }
                        // A callback that has not exited still owns these gates after a timeout.
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator Run_CancellationAfterMainReturnIsQueued_ThrowsWithOriginalToken()
        {
            for (int typed = 0; typed < 2; typed++)
            {
                Drain();
                Assert.That(PendingCount(), Is.Zero);
                int mainThread = Thread.CurrentThread.ManagedThreadId;
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                using ManualResetEventSlim release = new ManualResetEventSlim();
                Observation observation = new Observation();
                try
                {
                    Register(typed != 0, () =>
                    {
                        if (!release.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)))
                        {
                            throw new TimeoutException("Delegate gate was not released.");
                        }
                        return 42;
                    }, true, cancellation.Token, observation);
                    release.Set();
                    Assert.That(SpinWait.SpinUntil(() => PendingCount() > 0,
                        TimeSpan.FromSeconds(k_timeoutSeconds)), Is.True);
                    Assert.That(Volatile.Read(ref observation.Completed), Is.Zero);
                    cancellation.Cancel();
                    Drain();
                    Assert.That(Volatile.Read(ref observation.Completed), Is.EqualTo(1));
                    Assert.That(observation.ThreadId, Is.EqualTo(mainThread));
                    Assert.That(observation.Error, Is.TypeOf<OperationCanceledException>());
                    Assert.That(((OperationCanceledException)observation.Error).CancellationToken,
                        Is.EqualTo(cancellation.Token));
                }
                finally
                {
                    release.Set();
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator SwitchAndRun_ContextFlowOnOffAndSuppressed_IsolateCaller()
        {
            for (int mode = 0; mode < 3; mode++)
            {
                OnityTask.FlowExecutionContext = mode != 1;
                AsyncLocal<string> local = new AsyncLocal<string>();
                AsyncLocal<string> runLocal = new AsyncLocal<string>();
                local.Value = "caller";
                runLocal.Value = "caller";
                Task<string> switched;
                Task<string> run;
                try
                {
                    if (mode == 2)
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            switched = ReadLocalAfterSwitch(local).AsTask();
                            run = OnityTask.RunOnThreadPool(() => runLocal.Value, false).AsTask();
                        }
                    }
                    else
                    {
                        switched = ReadLocalAfterSwitch(local).AsTask();
                        run = OnityTask.RunOnThreadPool(() => runLocal.Value, false).AsTask();
                    }
                    yield return WaitFor(() => switched.IsCompleted && run.IsCompleted);
                    Assert.That(switched.GetAwaiter().GetResult(), Is.EqualTo(mode == 0 ? "caller" : null));
                    Assert.That(run.GetAwaiter().GetResult(), Is.EqualTo(mode == 0 ? "caller" : null));
                    Assert.That(local.Value, Is.EqualTo("caller"));
                    Assert.That(runLocal.Value, Is.EqualTo("caller"));
                }
                finally
                {
                    local.Value = null;
                    runLocal.Value = null;
                }
            }
        }

        [UnityTest]
        public IEnumerator Run_ConcurrentProducers_KeepResultsAndCompletionThreads()
        {
            const int count = 32;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            Task<(int value, int thread)>[] consumers = new Task<(int, int)>[count];
            using ManualResetEventSlim release = new ManualResetEventSlim();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    int value = i;
                    consumers[i] = ObserveRun(value, (i & 1) == 0, release).AsTask();
                }
                release.Set();
                Task all = Task.WhenAll(consumers);
                yield return WaitFor(() => all.IsCompleted);
                Assert.That(all.IsCompletedSuccessfully, Is.True, all.Exception?.ToString());
                for (int i = 0; i < count; i++)
                {
                    Assert.That(consumers[i].Result.value, Is.EqualTo(i));
                    Assert.That(consumers[i].Result.thread == mainThread, Is.EqualTo((i & 1) == 0));
                }
            }
            finally
            {
                release.Set();
            }
        }

        [UnityTest]
        public IEnumerator Run_OriginatingSessionEndsWhileWorking_DiscardsReturnButKeepsBackgroundWork()
        {
            Drain();
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            Observation stale = new Observation();
            int finished = 0;
            try
            {
                Register(true, () =>
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)))
                    {
                        throw new TimeoutException("Delegate gate was not released.");
                    }
                    Interlocked.Increment(ref finished);
                    return 42;
                }, true, default, stale);
                yield return WaitFor(() => entered.IsSet);
                DispatcherMethod("BeginSession", BindingFlags.NonPublic).Invoke(null, null);
                release.Set();
                Assert.That(SpinWait.SpinUntil(() => PendingCount() > 0,
                    TimeSpan.FromSeconds(k_timeoutSeconds)), Is.True);
                Drain();
                Assert.That(Volatile.Read(ref finished), Is.EqualTo(1));
                Assert.That(Volatile.Read(ref stale.Completed), Is.Zero,
                    "Old work published completion into a new session.");
                Task<int> fresh = OnityTask.RunOnThreadPool(() => 73).AsTask();
                yield return WaitFor(() => fresh.IsCompleted);
                Assert.That(fresh.GetAwaiter().GetResult(), Is.EqualTo(73));
                Task<int> background = OnityTask.RunOnThreadPool(() => 91, false).AsTask();
                yield return WaitFor(() => background.IsCompleted);
                Assert.That(background.GetAwaiter().GetResult(), Is.EqualTo(91));
            }
            finally
            {
                release.Set();
            }
        }

        private static IEnumerator VerifyOutcome(bool typed, bool returnMain, int outcome)
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            Observation observation = new Observation();
            Exception sentinel = new InvalidOperationException("Worker fault sentinel.");
            int workerThread = 0;
            int finished = 0;
            try
            {
                Register(typed, () =>
                {
                    workerThread = Thread.CurrentThread.ManagedThreadId;
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)))
                    {
                        throw new TimeoutException("Delegate gate was not released.");
                    }
                    Interlocked.Increment(ref finished);
                    if (outcome == 2)
                    {
                        throw sentinel;
                    }
                    return 42;
                }, returnMain, cancellation.Token, observation);
                yield return WaitFor(() => entered.IsSet);
                if (outcome != 0)
                {
                    cancellation.Cancel();
                    Assert.That(Volatile.Read(ref observation.Completed), Is.Zero,
                        "Cancellation interrupted an executing delegate.");
                }
                release.Set();
                yield return WaitFor(() => Volatile.Read(ref observation.Completed) != 0);
                Assert.That(workerThread, Is.Not.EqualTo(mainThread));
                Assert.That(finished, Is.EqualTo(1));
                Assert.That(observation.ThreadId == mainThread, Is.EqualTo(returnMain));
                if (outcome == 0)
                {
                    Assert.That(observation.Error, Is.Null);
                    Assert.That(observation.Value, Is.EqualTo(typed ? 42 : 0));
                }
                else if (outcome == 1)
                {
                    Assert.That(observation.Error, Is.TypeOf<OperationCanceledException>());
                    Assert.That(((OperationCanceledException)observation.Error).CancellationToken,
                        Is.EqualTo(cancellation.Token));
                }
                else
                {
                    Assert.That(observation.Error, Is.SameAs(sentinel), "Delegate fault lost to cancellation.");
                }
            }
            finally
            {
                release.Set();
            }
        }

        private static void Register(bool typed, Func<int> work, bool returnMain,
            CancellationToken token, Observation observation)
        {
            if (typed)
            {
                var awaiter = OnityTask.RunOnThreadPool(work, returnMain, token).GetAwaiter();
                awaiter.UnsafeOnCompleted(() => Complete(observation, () => awaiter.GetResult()));
            }
            else
            {
                var awaiter = OnityTask.RunOnThreadPool((Action)(() => { work(); }), returnMain, token).GetAwaiter();
                awaiter.UnsafeOnCompleted(() => Complete(observation, () => { awaiter.GetResult(); return 0; }));
            }
        }

        private static void Complete(Observation observation, Func<int> getResult)
        {
            observation.ThreadId = Thread.CurrentThread.ManagedThreadId;
            try
            {
                observation.Value = getResult();
            }
            catch (Exception exception)
            {
                observation.Error = exception;
            }
            finally
            {
                Volatile.Write(ref observation.Completed, 1);
            }
        }

        private static async OnityTask<int> ReadWorkerThread(OnityTaskThreadPoolSwitch reusable)
        {
            await reusable;
            return Thread.CurrentThread.ManagedThreadId;
        }

        private static async OnityTask<string> ReadLocalAfterSwitch(AsyncLocal<string> local)
        {
            await OnityTask.SwitchToThreadPool();
            string observed = local.Value;
            try
            {
                local.Value = "worker";
                return observed;
            }
            finally
            {
                local.Value = observed;
            }
        }

        private static async OnityTask<(int value, int thread)> ObserveRun(int value, bool returnMain,
            ManualResetEventSlim release)
        {
            int result = await OnityTask.RunOnThreadPool(() =>
            {
                if (!release.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)))
                {
                    throw new TimeoutException("Concurrent gate was not released.");
                }
                return value;
            }, returnMain);
            return (result, Thread.CurrentThread.ManagedThreadId);
        }

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed.TotalSeconds < k_timeoutSeconds)
            {
                yield return null;
            }
            Assert.That(predicate(), Is.True, "Thread-pool operation timed out.");
        }

        private static Type Dispatcher => typeof(OnityTask).Assembly.GetType(
            "Onity.Unity.Async.OnityTaskMainThreadDispatcher", true);

        private static MethodInfo DispatcherMethod(string name, BindingFlags visibility)
        {
            return Dispatcher.GetMethod(name, BindingFlags.Static | visibility);
        }

        private static int PendingCount()
        {
            return (int)Dispatcher.GetProperty("PendingCount", BindingFlags.Static | BindingFlags.Public)
                .GetValue(null);
        }

        private static void Drain()
        {
            DispatcherMethod("Drain", BindingFlags.Public).Invoke(null, null);
        }

        private sealed class Observation
        {
            public int ThreadId;
            public int Value;
            public Exception Error;
            public int Completed;
        }
    }
}
