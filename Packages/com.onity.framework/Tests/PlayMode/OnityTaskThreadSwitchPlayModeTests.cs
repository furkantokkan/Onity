using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    [TestFixture]
    public sealed class OnityTaskThreadSwitchPlayModeTests
    {
        private const int k_timeoutFrames = 300;
        private const int k_concurrentTimeoutFrames = 1800;

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

            Assert.That(awaiter.IsCompleted, Is.True,
                "A canceled switch still completes synchronously on the main thread.");
            OperationCanceledException exception =
                Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_FromWorker_ResumesOnMainThreadDuringUpdate()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            GameObject probeObject = new GameObject("ThreadSwitchPhaseProbe");
            LoopPhaseProbe probe = probeObject.AddComponent<LoopPhaseProbe>();

            try
            {
                yield return null;
                yield return null;

                Task<SwitchObservation> awaited = Task.Run(() => SwitchAndObserve(probe));
                yield return WaitForFlag(() => awaited.IsCompleted, k_timeoutFrames);

                Assert.That(awaited.IsCompletedSuccessfully, Is.True);
                SwitchObservation observation = awaited.Result;
                Assert.That(observation.WorkerThreadId, Is.Not.EqualTo(mainThreadId),
                    "The switch was requested on the main thread, so the queue was not exercised.");
                Assert.That(observation.ThreadId, Is.EqualTo(mainThreadId));
                Assert.That(observation.InFixedTimeStep, Is.False);
                Assert.That(observation.UpdateFrame, Is.EqualTo(observation.Frame),
                    "The continuation ran before this frame's Update phase.");
                Assert.That(observation.LateUpdateFrame, Is.Not.EqualTo(observation.Frame),
                    "The continuation ran after this frame's LateUpdate phase.");
            }
            finally
            {
                UnityEngine.Object.Destroy(probeObject);
            }
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_CanceledWhileQueued_ThrowsOnMainThread()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using ManualResetEventSlim queued = new ManualResetEventSlim(false);
            OnityTaskThreadSwitchAwaiter awaiter =
                OnityTask.SwitchToMainThread(cancellation.Token).GetAwaiter();
            bool workerObservedCompleted = true;
            int continuationThreadId = 0;
            Exception completionException = null;
            bool completed = false;

            Task producer = Task.Run(() =>
            {
                workerObservedCompleted = awaiter.IsCompleted;
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
                queued.Set();
            });

            // The runner cannot drain while this coroutine step blocks the main thread, so the
            // continuation is still queued when the token is canceled.
            Assert.That(queued.Wait(TimeSpan.FromSeconds(5d)), Is.True);
            Assert.That(workerObservedCompleted, Is.False);
            Assert.That(completed, Is.False, "Enqueue must never run the continuation inline.");
            cancellation.Cancel();

            yield return WaitForFlag(() => completed, k_timeoutFrames);

            Assert.That(producer.IsCompletedSuccessfully, Is.True);
            Assert.That(continuationThreadId, Is.EqualTo(mainThreadId));
            Assert.That(completionException, Is.TypeOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)completionException).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_ConcurrentProducers_AllResumeOnMainThread()
        {
            const int producerCount = 8;
            const int switchesPerProducer = 64;
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
            yield return WaitForFlag(() => all.IsCompleted, k_concurrentTimeoutFrames);

            Assert.That(all.IsCompletedSuccessfully, Is.True);
            Assert.That(completedSwitches, Is.EqualTo(producerCount * switchesPerProducer));
            Assert.That(offThreadResumes, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_ReentrantRegistrationDuringDrain_ResumesInLaterFrame()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int firstFrame = -1;
            int secondFrame = -1;
            int secondThreadId = 0;

            Task chain = Task.Run(async () =>
            {
                await OnityTask.SwitchToMainThread();
                firstFrame = Time.frameCount;

                // This registration happens inside the running drain. The awaiter is complete on
                // the main thread, so the registration is manual; it must wait for a later drain.
                OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread().GetAwaiter();
                TaskCompletionSource<bool> second = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                awaiter.UnsafeOnCompleted(() =>
                {
                    secondThreadId = Thread.CurrentThread.ManagedThreadId;
                    secondFrame = Time.frameCount;
                    second.TrySetResult(true);
                });

                await second.Task;
            });

            yield return WaitForFlag(() => chain.IsCompleted, k_timeoutFrames);

            Assert.That(chain.IsCompletedSuccessfully, Is.True);
            Assert.That(secondThreadId, Is.EqualTo(mainThreadId));
            Assert.That(firstFrame, Is.GreaterThanOrEqualTo(0));
            Assert.That(secondFrame, Is.GreaterThan(firstFrame),
                "A continuation registered during a drain ran in the same drain.");
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_AsyncLocal_FlowsThroughAsyncTaskAndAsyncOnityTask()
        {
            AsyncLocal<string> local = new AsyncLocal<string>();
            SynchronizationContext unityContext = SynchronizationContext.Current;
            Assert.That(unityContext, Is.Not.Null);

            Task<(string value, SynchronizationContext context)> viaTask =
                Task.Run(() => FlowThroughTaskAsync(local));
            Task<(string value, SynchronizationContext context)> viaOnityTask =
                Task.Run(() => FlowThroughOnityTaskAsync(local).AsTask());

            yield return WaitForFlag(() => viaTask.IsCompleted && viaOnityTask.IsCompleted, k_timeoutFrames);

            Assert.That(viaTask.IsCompletedSuccessfully, Is.True);
            Assert.That(viaOnityTask.IsCompletedSuccessfully, Is.True);
            Assert.That(viaTask.Result.value, Is.EqualTo("task"));
            Assert.That(viaTask.Result.context, Is.SameAs(unityContext));
            Assert.That(viaOnityTask.Result.value, Is.EqualTo("onity"));
            Assert.That(viaOnityTask.Result.context, Is.SameAs(unityContext));
            Assert.That(local.Value, Is.Null, "The flowed value leaked into the main thread's context.");
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_SuppressedFlow_DoesNotFlowAsyncLocal()
        {
            AsyncLocal<string> local = new AsyncLocal<string>();
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;

            Task<(string value, int threadId)> viaTask =
                Task.Run(() => SuppressedFlowThroughTaskAsync(local));
            Task<(string value, int threadId)> viaOnityTask =
                Task.Run(() => SuppressedFlowThroughOnityTaskAsync(local));

            yield return WaitForFlag(() => viaTask.IsCompleted && viaOnityTask.IsCompleted, k_timeoutFrames);

            Assert.That(viaTask.IsCompletedSuccessfully, Is.True);
            Assert.That(viaOnityTask.IsCompletedSuccessfully, Is.True);
            Assert.That(viaTask.Result.threadId, Is.EqualTo(mainThreadId));
            Assert.That(viaOnityTask.Result.threadId, Is.EqualTo(mainThreadId));
            Assert.That(viaTask.Result.value, Is.Null);
            Assert.That(viaOnityTask.Result.value, Is.Null);
        }

        [UnityTest]
        public IEnumerator SwitchToMainThread_AfterRunnerDestroyed_RecreatesPumpAndResumes()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            OnityTask warmupTask = OnityTask.NextFrame();
            yield return WaitForFlag(() => warmupTask.IsCompleted, k_timeoutFrames);
            warmupTask.GetAwaiter().GetResult();

            GameObject runner = FindRunner();
            Assert.That(runner, Is.Not.Null);
            UnityEngine.Object.Destroy(runner);
            yield return null;
            Assert.That(FindRunner(), Is.Null);

            Task<int> resumed = Task.Run(() => SwitchAndGetThreadId());
            yield return WaitForFlag(() => resumed.IsCompleted, k_timeoutFrames);

            Assert.That(resumed.IsCompletedSuccessfully, Is.True);
            Assert.That(resumed.Result, Is.EqualTo(mainThreadId));
            Assert.That(FindRunner(), Is.Not.Null, "The worker switch did not recreate the runner.");
        }

        private static GameObject FindRunner()
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i].name == "OnityTaskRunner")
                {
                    return objects[i];
                }
            }

            return null;
        }

        private static IEnumerator WaitForFlag(Func<bool> predicate, int timeoutFrames)
        {
            int remainingFrames = timeoutFrames;
            while (predicate() == false && remainingFrames > 0)
            {
                remainingFrames--;
                yield return null;
            }

            Assert.That(predicate(), Is.True, "Operation did not complete before the test timeout.");
        }

        private static async Task<int> SwitchAndGetThreadId()
        {
            await OnityTask.SwitchToMainThread();
            return Thread.CurrentThread.ManagedThreadId;
        }

        private static async Task<SwitchObservation> SwitchAndObserve(LoopPhaseProbe probe)
        {
            int workerThreadId = Thread.CurrentThread.ManagedThreadId;
            await OnityTask.SwitchToMainThread();
            return new SwitchObservation(
                workerThreadId,
                Thread.CurrentThread.ManagedThreadId,
                Time.frameCount,
                Time.inFixedTimeStep,
                probe.UpdateFrame,
                probe.LateUpdateFrame);
        }

        private static async Task<(string value, SynchronizationContext context)> FlowThroughTaskAsync(
            AsyncLocal<string> local)
        {
            local.Value = "task";
            await OnityTask.SwitchToMainThread();
            return (local.Value, SynchronizationContext.Current);
        }

        private static async OnityTask<(string value, SynchronizationContext context)> FlowThroughOnityTaskAsync(
            AsyncLocal<string> local)
        {
            local.Value = "onity";
            await OnityTask.SwitchToMainThread();
            return (local.Value, SynchronizationContext.Current);
        }

        private static Task<(string value, int threadId)> SuppressedFlowThroughTaskAsync(AsyncLocal<string> local)
        {
            local.Value = "task";
            using (ExecutionContext.SuppressFlow())
            {
                // The async method runs synchronously up to the switch, so its continuation is
                // registered while flow is suppressed and the control is undone on this thread.
                return ReadAfterSwitchAsync(local);
            }
        }

        private static Task<(string value, int threadId)> SuppressedFlowThroughOnityTaskAsync(
            AsyncLocal<string> local)
        {
            local.Value = "onity";
            using (ExecutionContext.SuppressFlow())
            {
                return ReadAfterSwitchOnityAsync(local).AsTask();
            }
        }

        private static async Task<(string value, int threadId)> ReadAfterSwitchAsync(AsyncLocal<string> local)
        {
            await OnityTask.SwitchToMainThread();
            return (local.Value, Thread.CurrentThread.ManagedThreadId);
        }

        private static async OnityTask<(string value, int threadId)> ReadAfterSwitchOnityAsync(
            AsyncLocal<string> local)
        {
            await OnityTask.SwitchToMainThread();
            return (local.Value, Thread.CurrentThread.ManagedThreadId);
        }

        private readonly struct SwitchObservation
        {
            public readonly int WorkerThreadId;
            public readonly int ThreadId;
            public readonly int Frame;
            public readonly bool InFixedTimeStep;
            public readonly int UpdateFrame;
            public readonly int LateUpdateFrame;

            public SwitchObservation(
                int workerThreadId,
                int threadId,
                int frame,
                bool inFixedTimeStep,
                int updateFrame,
                int lateUpdateFrame)
            {
                WorkerThreadId = workerThreadId;
                ThreadId = threadId;
                Frame = frame;
                InFixedTimeStep = inFixedTimeStep;
                UpdateFrame = updateFrame;
                LateUpdateFrame = lateUpdateFrame;
            }
        }

        /// <summary>
        /// Moves the awaiting method onto a thread-pool thread so the next switch exercises the queue.
        /// </summary>
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

        /// <summary>
        /// Records the frame of its last Update and LateUpdate ahead of default-order scripts.
        /// </summary>
        [DefaultExecutionOrder(-32000)]
        private sealed class LoopPhaseProbe : MonoBehaviour
        {
            public int UpdateFrame { get; private set; } = -1;

            public int LateUpdateFrame { get; private set; } = -1;

            private void Update()
            {
                UpdateFrame = Time.frameCount;
            }

            private void LateUpdate()
            {
                LateUpdateFrame = Time.frameCount;
            }
        }
    }
}
