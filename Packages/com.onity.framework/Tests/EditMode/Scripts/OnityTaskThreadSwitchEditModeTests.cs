using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityTaskThreadSwitchEditModeTests
    {
        private const int k_timeoutIterations = 2000;

        [Test]
        public void SwitchToMainThread_OnMainThread_CompletesSynchronously()
        {
            OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread().GetAwaiter();

            Assert.That(awaiter.IsCompleted, Is.True);
            Assert.DoesNotThrow(() => awaiter.GetResult());
        }

        [Test]
        public void SwitchToMainThread_OnMainThreadWithCanceledToken_ThrowsAtGetResult()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            OnityTaskThreadSwitchAwaiter awaiter =
                OnityTask.SwitchToMainThread(cancellation.Token).GetAwaiter();

            Assert.That(awaiter.IsCompleted, Is.True);
            OperationCanceledException exception =
                Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void SwitchToMainThread_AwaitedTwiceOnMainThread_CompletesEachTime()
        {
            OnityTaskThreadSwitch switchAwaitable = OnityTask.SwitchToMainThread();

            Assert.That(switchAwaitable.GetAwaiter().IsCompleted, Is.True);
            Assert.DoesNotThrow(() => switchAwaitable.GetAwaiter().GetResult());
            Assert.That(switchAwaitable.GetAwaiter().IsCompleted, Is.True);
            Assert.DoesNotThrow(() => switchAwaitable.GetAwaiter().GetResult());
        }

        [Test]
        public void SwitchToMainThread_NullContinuation_ThrowsArgumentNullException()
        {
            OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread().GetAwaiter();

            Assert.Throws<ArgumentNullException>(() => awaiter.OnCompleted(null));
            Assert.Throws<ArgumentNullException>(() => awaiter.UnsafeOnCompleted(null));
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_FromWorker_ResumesOnEditorMainThread()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;

            Task<(int workerThreadId, int resumedThreadId)> awaited = Task.Run(() => SwitchAndGetThreadIds());
            yield return WaitForFlag(() => awaited.IsCompleted);

            Assert.That(awaited.IsCompletedSuccessfully, Is.True);
            Assert.That(awaited.Result.workerThreadId, Is.Not.EqualTo(mainThreadId));
            Assert.That(awaited.Result.resumedThreadId, Is.EqualTo(mainThreadId));
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_ManualRegistrationOnMainThread_RunsOnLaterEditorUpdate()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread().GetAwaiter();
            int continuationThreadId = 0;
            bool ran = false;

            awaiter.UnsafeOnCompleted(() =>
            {
                continuationThreadId = Thread.CurrentThread.ManagedThreadId;
                ran = true;
            });

            Assert.That(ran, Is.False, "Enqueue must never run the continuation inline.");
            yield return WaitForFlag(() => ran);

            Assert.That(continuationThreadId, Is.EqualTo(mainThreadId));
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_CanceledWhileQueued_ThrowsOnEditorMainThread()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTaskThreadSwitchAwaiter awaiter =
                OnityTask.SwitchToMainThread(cancellation.Token).GetAwaiter();
            int continuationThreadId = 0;
            Exception completionException = null;
            bool completed = false;

            awaiter.UnsafeOnCompleted(() =>
            {
                continuationThreadId = Thread.CurrentThread.ManagedThreadId;
                try
                {
                    awaiter.GetResult();
                }
                catch (Exception exception)
                {
                    completionException = exception;
                }

                completed = true;
            });
            cancellation.Cancel();

            yield return WaitForFlag(() => completed);

            Assert.That(continuationThreadId, Is.EqualTo(mainThreadId));
            Assert.That(completionException, Is.TypeOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)completionException).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_ConcurrentProducers_AllResumeOnEditorMainThread()
        {
            const int producerCount = 4;
            const int switchesPerProducer = 16;
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int offThreadResumes = 0;
            int completedSwitches = 0;
            Task[] producers = new Task[producerCount];

            for (int i = 0; i < producerCount; i++)
            {
                producers[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < switchesPerProducer; j++)
                    {
                        await new WorkerThreadSwitch();
                        await OnityTask.SwitchToMainThread();

                        if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
                        {
                            Interlocked.Increment(ref offThreadResumes);
                        }

                        Interlocked.Increment(ref completedSwitches);
                    }
                });
            }

            Task all = Task.WhenAll(producers);
            yield return WaitForFlag(() => all.IsCompleted);

            Assert.That(all.IsCompletedSuccessfully, Is.True);
            Assert.That(completedSwitches, Is.EqualTo(producerCount * switchesPerProducer));
            Assert.That(offThreadResumes, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_ThrowingContinuation_DoesNotStopTheRestOfTheBatch()
        {
            OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread().GetAwaiter();
            bool secondRan = false;

            LogAssert.Expect(LogType.Exception,
                new Regex("InvalidOperationException: Thread switch continuation failure\\."));
            awaiter.UnsafeOnCompleted(() => throw new InvalidOperationException("Thread switch continuation failure."));
            awaiter.UnsafeOnCompleted(() => secondRan = true);

            yield return WaitForFlag(() => secondRan);
        }

        [Test]
        public void SwitchToMainThread_SynchronousDrain_RunsRestOfBatchOnceAfterThrowingContinuation()
        {
            Type dispatcherType = typeof(OnityTask).Assembly.GetType(
                "Onity.Unity.Async.OnityTaskMainThreadDispatcher", true);
            MethodInfo drain = dispatcherType.GetMethod("Drain", BindingFlags.Static | BindingFlags.Public);
            PropertyInfo pendingCount = dispatcherType.GetProperty("PendingCount", BindingFlags.Static | BindingFlags.Public);
            Assert.That(drain, Is.Not.Null);
            Assert.That(pendingCount, Is.Not.Null);

            // Consume anything another test left queued so the counts below belong to this test.
            drain.Invoke(null, null);

            OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread().GetAwaiter();
            int throwCount = 0;
            bool secondRan = false;
            bool thirdRan = false;

            LogAssert.Expect(LogType.Exception,
                new Regex("InvalidOperationException: Synchronous drain failure\\."));
            awaiter.UnsafeOnCompleted(() =>
            {
                throwCount++;
                throw new InvalidOperationException("Synchronous drain failure.");
            });
            awaiter.UnsafeOnCompleted(() => secondRan = true);
            awaiter.UnsafeOnCompleted(() => thirdRan = true);
            Assert.That((int)pendingCount.GetValue(null), Is.EqualTo(3));

            drain.Invoke(null, null);

            Assert.That(throwCount, Is.EqualTo(1));
            Assert.That(secondRan, Is.True, "A throwing continuation stopped the rest of its batch.");
            Assert.That(thirdRan, Is.True, "A throwing continuation stopped the rest of its batch.");
            Assert.That((int)pendingCount.GetValue(null), Is.EqualTo(0));

            drain.Invoke(null, null);

            Assert.That(throwCount, Is.EqualTo(1), "A drained continuation ran twice.");
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_MoreContinuationsThanInitialCapacity_RunInRegistrationOrder()
        {
            const int continuationCount = 200;
            OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread().GetAwaiter();
            List<int> observed = new List<int>(continuationCount);

            for (int i = 0; i < continuationCount; i++)
            {
                int index = i;
                awaiter.UnsafeOnCompleted(() => observed.Add(index));
            }

            yield return WaitForFlag(() => observed.Count == continuationCount);

            for (int i = 0; i < continuationCount; i++)
            {
                Assert.That(observed[i], Is.EqualTo(i), "Queued continuations ran out of registration order.");
            }
        }

        private static IEnumerator WaitForFlag(Func<bool> predicate)
        {
            int remainingIterations = k_timeoutIterations;
            while (predicate() == false && remainingIterations > 0)
            {
                remainingIterations--;
                yield return null;
            }

            Assert.That(predicate(), Is.True, "Operation did not complete before the test timeout.");
        }

        private static async Task<(int workerThreadId, int resumedThreadId)> SwitchAndGetThreadIds()
        {
            int workerThreadId = Thread.CurrentThread.ManagedThreadId;
            await OnityTask.SwitchToMainThread();
            return (workerThreadId, Thread.CurrentThread.ManagedThreadId);
        }

        private readonly struct WorkerThreadSwitch : ICriticalNotifyCompletion
        {
            private static readonly WaitCallback s_invoke = state => ((Action)state)();

            public bool IsCompleted => false;

            public WorkerThreadSwitch GetAwaiter()
            {
                return this;
            }

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                ThreadPool.QueueUserWorkItem(s_invoke, continuation);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                ThreadPool.UnsafeQueueUserWorkItem(s_invoke, continuation);
            }
        }
    }
}
