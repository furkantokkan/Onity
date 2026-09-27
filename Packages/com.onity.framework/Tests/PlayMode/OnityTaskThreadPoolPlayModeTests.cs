using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    [TestFixture]
    public sealed class OnityTaskThreadPoolPlayModeTests
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

        [UnityTest]
        public IEnumerator Switch_FromMainAndWorker_ResumesOnThreadPool()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            Assert.That(OnityTask.SwitchToThreadPool().GetAwaiter().IsCompleted, Is.False);
            Task<(int worker, int main)> returned = SwitchRoundTrip().AsTask();
            yield return WaitFor(() => returned.IsCompleted);
            Assert.That(returned.GetAwaiter().GetResult().worker, Is.Not.EqualTo(mainThread));
            Assert.That(returned.Result.main, Is.EqualTo(mainThread));
            Task<bool> fromWorker = Task.Run(async () =>
            {
                Assert.That(OnityTask.SwitchToThreadPool().GetAwaiter().IsCompleted, Is.False);
                await OnityTask.SwitchToThreadPool();
                return Thread.CurrentThread.IsThreadPoolThread;
            });
            yield return WaitFor(() => fromWorker.IsCompleted);
            Assert.That(fromWorker.GetAwaiter().GetResult(), Is.True);
        }

        [UnityTest]
        public IEnumerator Run_BothOverloads_SuccessCancellationAndFault_CompleteOnSelectedThread()
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
        public IEnumerator Run_PreCanceled_DoesNotInvokeDelegates()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int calls = 0;
            Task plain = OnityTask.RunOnThreadPool(() => { calls++; }, true, cancellation.Token).AsTask();
            Task<int> typed = OnityTask.RunOnThreadPool(() => ++calls, false, cancellation.Token).AsTask();
            yield return WaitFor(() => plain.IsCompleted && typed.IsCompleted);
            Assert.That(plain.IsCanceled, Is.True);
            Assert.That(typed.IsCanceled, Is.True);
            Assert.That(Assert.Catch<OperationCanceledException>(() => plain.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(Assert.Catch<OperationCanceledException>(() => typed.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(calls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SwitchAndRun_ContextFlowOnOffAndSuppressed_PreserveUnityReturnContext()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext mainContext = SynchronizationContext.Current;
            for (int mode = 0; mode < 3; mode++)
            {
                OnityTask.FlowExecutionContext = mode != 1;
                AsyncLocal<string> local = new AsyncLocal<string>();
                local.Value = "caller";
                Task<(string value, int thread, SynchronizationContext context)> observation;
                try
                {
                    if (mode == 2)
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            observation = ObserveContext(local).AsTask();
                        }
                    }
                    else
                    {
                        observation = ObserveContext(local).AsTask();
                    }
                    yield return WaitFor(() => observation.IsCompleted);
                    var result = observation.GetAwaiter().GetResult();
                    Assert.That(result.value, Is.EqualTo(mode == 0 ? "caller" : null));
                    Assert.That(result.thread, Is.EqualTo(mainThread));
                    Assert.That(result.context, Is.SameAs(mainContext));
                    Assert.That(local.Value, Is.EqualTo("caller"));
                }
                finally
                {
                    local.Value = null;
                }
            }
        }

        [UnityTest]
        public IEnumerator Run_ConcurrentConsumers_AllReturnCorrectResultsAndThreads()
        {
            const int count = 24;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            using ManualResetEventSlim release = new ManualResetEventSlim();
            Task<(int value, int thread)>[] consumers = new Task<(int, int)>[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    consumers[i] = ObserveRun(i, (i & 1) == 0, release).AsTask();
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

        private static IEnumerator VerifyOutcome(bool typed, bool returnMain, int outcome)
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            Exception sentinel = new InvalidOperationException("Play Mode worker fault sentinel.");
            Exception observed = null;
            int workerThread = 0;
            int completionThread = 0;
            int completed = 0;
            int finished = 0;
            int result = 0;
            Func<int> work = () =>
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
            };
            Action<Func<int>> consume = getResult =>
            {
                completionThread = Thread.CurrentThread.ManagedThreadId;
                try
                {
                    result = getResult();
                }
                catch (Exception exception)
                {
                    observed = exception;
                }
                finally
                {
                    Volatile.Write(ref completed, 1);
                }
            };
            try
            {
                if (typed)
                {
                    var awaiter = OnityTask.RunOnThreadPool(work, returnMain, cancellation.Token).GetAwaiter();
                    awaiter.UnsafeOnCompleted(() => consume(() => awaiter.GetResult()));
                }
                else
                {
                    var awaiter = OnityTask.RunOnThreadPool((Action)(() => { work(); }), returnMain,
                        cancellation.Token).GetAwaiter();
                    awaiter.UnsafeOnCompleted(() => consume(() => { awaiter.GetResult(); return 0; }));
                }
                yield return WaitFor(() => entered.IsSet);
                if (outcome != 0)
                {
                    cancellation.Cancel();
                    Assert.That(Volatile.Read(ref completed), Is.Zero,
                        "Cancellation interrupted an executing delegate.");
                }
                release.Set();
                yield return WaitFor(() => Volatile.Read(ref completed) != 0);
                Assert.That(workerThread, Is.Not.EqualTo(mainThread));
                Assert.That(finished, Is.EqualTo(1));
                Assert.That(completionThread == mainThread, Is.EqualTo(returnMain));
                if (outcome == 0)
                {
                    Assert.That(observed, Is.Null);
                    Assert.That(result, Is.EqualTo(typed ? 42 : 0));
                }
                else if (outcome == 1)
                {
                    Assert.That(observed, Is.TypeOf<OperationCanceledException>());
                    Assert.That(((OperationCanceledException)observed).CancellationToken,
                        Is.EqualTo(cancellation.Token));
                }
                else
                {
                    Assert.That(observed, Is.SameAs(sentinel));
                }
            }
            finally
            {
                release.Set();
            }
        }

        private static async OnityTask<(int worker, int main)> SwitchRoundTrip()
        {
            await OnityTask.SwitchToThreadPool();
            int worker = Thread.CurrentThread.ManagedThreadId;
            await OnityTask.SwitchToMainThread();
            return (worker, Thread.CurrentThread.ManagedThreadId);
        }

        private static async OnityTask<(string value, int thread, SynchronizationContext context)>
            ObserveContext(AsyncLocal<string> local)
        {
            await OnityTask.SwitchToThreadPool();
            string observed = local.Value;
            string runValue = await OnityTask.RunOnThreadPool(() => local.Value, false);
            if (observed != runValue)
            {
                throw new InvalidOperationException("Run lost the worker's execution context.");
            }
            await OnityTask.SwitchToMainThread();
            return (observed, Thread.CurrentThread.ManagedThreadId, SynchronizationContext.Current);
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
    }
}
