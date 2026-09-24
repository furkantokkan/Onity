using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    [TestFixture]
    public sealed class OnityTaskSafetyPlayModeTests
    {
        private const int k_timeoutFrames = 120;

        [UnityTest]
        public IEnumerator WhenAll_NativePendingOutput_ResumesOnMainThread()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);
            int continuationThreadId = 0;
            bool continuationCompleted = false;

            combined.GetAwaiter().OnCompleted(() =>
            {
                continuationThreadId = Thread.CurrentThread.ManagedThreadId;
                continuationCompleted = true;
            });

            yield return null;
            first.TrySetResult();
            Assert.That(combined.IsCompleted, Is.False);
            second.TrySetResult();
            yield return WaitForFlag(() => continuationCompleted);

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.That(continuationThreadId, Is.EqualTo(mainThreadId));
        }

        [UnityTest]
        public IEnumerator WhenAll_WorkerCompletedInputs_AwaitResumesOnMainThread()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);
            Task<int> awaited = AwaitAndGetThreadId(combined);

            yield return null;
            Task<int> producer = Task.Run(() =>
            {
                int workerThreadId = Thread.CurrentThread.ManagedThreadId;
                first.TrySetResult();
                second.TrySetResult();
                return workerThreadId;
            });
            yield return WaitForFlag(() => producer.IsCompleted && awaited.IsCompleted);

            Assert.That(producer.IsCompletedSuccessfully, Is.True);
            Assert.That(awaited.IsCompletedSuccessfully, Is.True);
            Assert.That(producer.Result, Is.Not.EqualTo(mainThreadId));
            Assert.That(awaited.Result, Is.EqualTo(mainThreadId));
            Assert.That(combined.IsCompletedSuccessfully, Is.True);
        }

        [UnityTest]
        public IEnumerator PooledTask_StaleCopyThrowsAfterSourceReuse()
        {
            bool completeFirst = false;
            OnityTask firstTask = OnityTask.WaitUntil(() => completeFirst);

            yield return null;
            completeFirst = true;
            yield return WaitForCompletion(firstTask);
            firstTask.GetAwaiter().GetResult();

            bool completeSecond = false;
            OnityTask secondTask = OnityTask.WaitUntil(() => completeSecond);

            Assert.Throws<InvalidOperationException>(() => _ = firstTask.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => firstTask.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => firstTask.AsTask());
            Assert.Throws<InvalidOperationException>(
                () => firstTask.GetAwaiter().OnCompleted(() => { }));

            completeSecond = true;
            yield return WaitForCompletion(secondTask);
            Assert.DoesNotThrow(() => secondTask.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator PooledTask_SecondNativeConsumptionThrows()
        {
            bool complete = false;
            OnityTask task = OnityTask.WaitUntil(() => complete);

            yield return null;
            complete = true;
            yield return WaitForCompletion(task);

            task.GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => task.AsTask());
        }

        [UnityTest]
        public IEnumerator AsTaskCompletion_ReturnsSourceToPoolWithoutChangingTaskResult()
        {
            bool completeFirst = false;
            OnityTask firstTask = OnityTask.WaitUntil(() => completeFirst);
            Task materializedTask = firstTask.AsTask();

            completeFirst = true;
            yield return WaitForFlag(() => materializedTask.IsCompleted);

            bool completeSecond = false;
            OnityTask secondTask = OnityTask.WaitUntil(() => completeSecond);

            Assert.That(materializedTask.IsCompletedSuccessfully, Is.True);
            Assert.Throws<InvalidOperationException>(() => _ = firstTask.IsCompleted);

            completeSecond = true;
            yield return WaitForCompletion(secondTask);
            secondTask.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator CompilerAwaitChain_ReusesSourceWithoutLosingNextOperation()
        {
            bool completeFirst = false;
            bool completeSecond = false;
            OnityTask chain = AwaitPredicatesInSequence(
                () => completeFirst,
                () => completeSecond);

            yield return null;
            completeFirst = true;
            yield return null;

            Assert.That(chain.IsCompleted, Is.False);

            completeSecond = true;
            yield return WaitForCompletion(chain);
            chain.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator NativeAwaitThenAsTask_DoesNotReleaseNewGenerationFromOldCompletion()
        {
            bool completeFirst = false;
            bool completeSecond = false;
            bool completeThird = false;
            OnityTask chain = AwaitNativeThenMaterialized(
                () => completeFirst,
                () => completeSecond);

            yield return null;
            completeFirst = true;
            yield return null;

            OnityTask thirdTask = OnityTask.WaitUntil(() => completeThird);
            completeThird = true;
            yield return WaitForCompletion(thirdTask);
            thirdTask.GetAwaiter().GetResult();

            Assert.That(chain.IsCompleted, Is.False);

            completeSecond = true;
            yield return WaitForCompletion(chain);
            chain.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator BackgroundCancellation_ContinuationRunsOnMainThread()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            OnityTask task = OnityTask.WaitUntil(() => false, cancellationTokenSource.Token);
            int continuationThreadId = 0;
            Exception completionException = null;
            bool isCompleted = false;

            task.GetAwaiter().OnCompleted(
                () =>
                {
                    continuationThreadId = Thread.CurrentThread.ManagedThreadId;

                    try
                    {
                        task.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        completionException = exception;
                    }

                    isCompleted = true;
                });

            Task cancellationTask = Task.Run(() => cancellationTokenSource.Cancel());
            yield return WaitForFlag(() => isCompleted);

            Assert.That(cancellationTask.IsCompletedSuccessfully, Is.True);
            Assert.That(completionException, Is.TypeOf<OperationCanceledException>());
            Assert.That(continuationThreadId, Is.EqualTo(mainThreadId));
        }

        [UnityTest]
        public IEnumerator Preserve_NextFrame_CompletesTwoPendingAndLateConsumers()
        {
            OnityTask shared = OnityTask.NextFrame().Preserve();
            Task first = ObservePreservedTask(shared);
            Task second = ObservePreservedTask(shared);

            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);

            yield return WaitForFlag(() => first.IsCompleted && second.IsCompleted);

            Assert.That(first.IsCompletedSuccessfully, Is.True);
            Assert.That(second.IsCompletedSuccessfully, Is.True);
            Task late = ObservePreservedTask(shared);
            Assert.That(late.IsCompletedSuccessfully, Is.True);
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => shared.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator Preserve_BackgroundCancellation_CompletesOnMainThreadForBothConsumers()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTask shared = OnityTask.DelayFrames(1000, cancellation.Token).Preserve();
            int firstThreadId = 0;
            int secondThreadId = 0;
            Exception firstException = null;
            Exception secondException = null;
            bool firstCompleted = false;
            bool secondCompleted = false;

            shared.GetAwaiter().OnCompleted(() =>
            {
                firstThreadId = Thread.CurrentThread.ManagedThreadId;
                try
                {
                    shared.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    firstException = exception;
                }

                firstCompleted = true;
            });
            shared.GetAwaiter().OnCompleted(() =>
            {
                secondThreadId = Thread.CurrentThread.ManagedThreadId;
                try
                {
                    shared.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    secondException = exception;
                }

                secondCompleted = true;
            });

            Task cancellationTask = Task.Run(() => cancellation.Cancel());
            yield return WaitForFlag(() => firstCompleted && secondCompleted);

            Assert.That(cancellationTask.IsCompletedSuccessfully, Is.True);
            Assert.That(firstThreadId, Is.EqualTo(mainThreadId));
            Assert.That(secondThreadId, Is.EqualTo(mainThreadId));
            Assert.That(firstException, Is.TypeOf<OperationCanceledException>());
            Assert.That(secondException, Is.TypeOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)firstException).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(((OperationCanceledException)secondException).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(shared.IsCanceled, Is.True);
        }

        [UnityTest]
        public IEnumerator ReusedSource_OldCancellationTokenCannotCancelNewGeneration()
        {
            using CancellationTokenSource oldCancellationTokenSource = new CancellationTokenSource();
            using CancellationTokenSource newCancellationTokenSource = new CancellationTokenSource();

            bool completeFirst = false;
            OnityTask firstTask = OnityTask.WaitUntil(
                () => completeFirst,
                oldCancellationTokenSource.Token);

            yield return null;
            completeFirst = true;
            yield return WaitForCompletion(firstTask);
            firstTask.GetAwaiter().GetResult();

            OnityTask secondTask = OnityTask.WaitUntil(
                () => false,
                newCancellationTokenSource.Token);

            oldCancellationTokenSource.Cancel();
            yield return null;

            Assert.That(secondTask.IsCompleted, Is.False);

            newCancellationTokenSource.Cancel();
            yield return WaitForCompletion(secondTask);
            Assert.Catch<OperationCanceledException>(
                () => secondTask.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator ThrowingNativeContinuation_DoesNotFaultReusedSourceOrStopOtherWait()
        {
            bool completeFirst = false;
            bool completeReplacement = false;
            bool completeOther = false;
            bool replacementStarted = false;

            using CancellationTokenSource replacementCancellation = new CancellationTokenSource();
            using CancellationTokenSource otherCancellation = new CancellationTokenSource();

            OnityTask otherTask = OnityTask.WaitUntil(
                () => completeOther, otherCancellation.Token);
            OnityTask firstTask = OnityTask.WaitUntil(() => completeFirst);
            OnityTask replacementTask = default;

            firstTask.GetAwaiter().OnCompleted(
                () =>
                {
                    firstTask.GetAwaiter().GetResult();
                    replacementTask = OnityTask.WaitUntil(
                        () => completeReplacement, replacementCancellation.Token);
                    replacementStarted = true;
                    throw new InvalidOperationException("Continuation failure after source reuse.");
                });

            try
            {
                yield return null;
                LogAssert.Expect(LogType.Exception,
                    new Regex("InvalidOperationException: Continuation failure after source reuse\\."));
                completeFirst = true;
                yield return WaitForFlag(() => replacementStarted);

                Assert.That(replacementTask.IsCompleted, Is.False,
                    "A throwing continuation faulted the replacement task.");
                Assert.That(otherTask.IsCompleted, Is.False);

                completeOther = true;
                yield return WaitForCompletion(otherTask);
                Assert.DoesNotThrow(() => otherTask.GetAwaiter().GetResult());

                completeReplacement = true;
                yield return WaitForCompletion(replacementTask);
                Assert.DoesNotThrow(() => replacementTask.GetAwaiter().GetResult());
            }
            finally
            {
                otherCancellation.Cancel();
                replacementCancellation.Cancel();
            }
        }

        [UnityTest]
        public IEnumerator FixedFrameCancellation_CompletesWhenTimeScaleIsZero()
        {
            float previousTimeScale = Time.timeScale;
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;

            try
            {
                Time.timeScale = 0f;
                using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

                OnityTask task = OnityTask.NextFixedFrame(cancellationTokenSource.Token);
                int continuationThreadId = 0;
                Exception completionException = null;
                bool isCompleted = false;

                task.GetAwaiter().OnCompleted(
                    () =>
                    {
                        continuationThreadId = Thread.CurrentThread.ManagedThreadId;

                        try
                        {
                            task.GetAwaiter().GetResult();
                        }
                        catch (Exception exception)
                        {
                            completionException = exception;
                        }

                        isCompleted = true;
                    });

                Task cancellationTask = Task.Run(() => cancellationTokenSource.Cancel());
                yield return WaitForFlag(() => isCompleted);

                Assert.That(cancellationTask.IsCompletedSuccessfully, Is.True);
                Assert.That(completionException, Is.TypeOf<OperationCanceledException>());
                Assert.That(continuationThreadId, Is.EqualTo(mainThreadId));
            }
            finally
            {
                Time.timeScale = previousTimeScale;
            }
        }

        [UnityTest]
        public IEnumerator NextFrame_ScheduledBeforeRunnerUpdate_CompletesOnFollowingFrame()
        {
            OnityTask warmupTask = OnityTask.NextFrame();
            yield return WaitForCompletion(warmupTask);
            warmupTask.GetAwaiter().GetResult();

            GameObject probeObject = new GameObject("NextFrameUpdateProbe");
            probeObject.SetActive(false);
            NextFrameUpdateProbe probe = probeObject.AddComponent<NextFrameUpdateProbe>();
            probeObject.SetActive(true);

            try
            {
                yield return WaitForFlag(() => probe.CompletedFrame >= 0);

                Assert.That(probe.ObservedStartFrame, Is.True);
                Assert.That(probe.CompletedFrame, Is.EqualTo(probe.StartedFrame + 1),
                    "NextFrame completed in the Update that scheduled it.");

                probe.ConsumeTask();
            }
            finally
            {
                UnityEngine.Object.Destroy(probeObject);
            }
        }

        [UnityTest]
        public IEnumerator NextFrame_RecreatesRunnerAfterDestroy()
        {
            OnityTask warmupTask = OnityTask.NextFrame();
            yield return WaitForCompletion(warmupTask);
            warmupTask.GetAwaiter().GetResult();

            GameObject runner = null;
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i].name == "OnityTaskRunner")
                {
                    runner = objects[i];
                    break;
                }
            }

            Assert.That(runner, Is.Not.Null);
            UnityEngine.Object.Destroy(runner);
            yield return null;

            OnityTask task = OnityTask.NextFrame();
            yield return WaitForCompletion(task);
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator DelayFrames_ScheduledBeforeRunnerUpdate_CompletesAfterRequestedFrames()
        {
            OnityTask warmupTask = OnityTask.NextFrame();
            yield return WaitForCompletion(warmupTask);
            warmupTask.GetAwaiter().GetResult();

            for (int frameCount = 1; frameCount <= 2; frameCount++)
            {
                GameObject probeObject = new GameObject("DelayFramesUpdateProbe");
                probeObject.SetActive(false);
                NextFrameUpdateProbe probe = probeObject.AddComponent<NextFrameUpdateProbe>();
                probe.FrameCount = frameCount;
                probeObject.SetActive(true);

                try
                {
                    yield return WaitForFlag(() => probe.CompletedFrame >= 0);

                    Assert.That(probe.ObservedStartFrame, Is.True);
                    Assert.That(probe.CompletedFrame, Is.EqualTo(probe.StartedFrame + frameCount),
                        "DelayFrames completed after the wrong number of rendered frames.");

                    probe.ConsumeTask();
                }
                finally
                {
                    UnityEngine.Object.Destroy(probeObject);
                }
            }
        }

        [UnityTest]
        public IEnumerator PositiveDelays_ScheduledBeforeRunnerUpdate_WaitBeyondSchedulingFrame()
        {
            OnityTask warmupTask = OnityTask.NextFrame();
            yield return WaitForCompletion(warmupTask);
            warmupTask.GetAwaiter().GetResult();

            for (int delayKind = 0; delayKind < 2; delayKind++)
            {
                GameObject probeObject = new GameObject("DelayUpdateProbe");
                probeObject.SetActive(false);
                NextFrameUpdateProbe probe = probeObject.AddComponent<NextFrameUpdateProbe>();
                probe.DelaySeconds = 0.0001f;
                probe.UseUnscaledTime = delayKind == 1;
                probeObject.SetActive(true);

                try
                {
                    yield return WaitForFlag(() => probe.CompletedFrame >= 0);

                    Assert.That(probe.ObservedStartFrame, Is.True);
                    Assert.That(probe.CompletedFrame, Is.GreaterThan(probe.StartedFrame),
                        probe.UseUnscaledTime
                            ? "DelayUnscaled completed in its scheduling frame."
                            : "Delay completed in its scheduling frame.");

                    probe.ConsumeTask();
                }
                finally
                {
                    UnityEngine.Object.Destroy(probeObject);
                }
            }
        }

        private static IEnumerator WaitForCompletion(OnityTask task)
        {
            int remainingFrames = k_timeoutFrames;
            while (task.IsCompleted == false && remainingFrames > 0)
            {
                remainingFrames--;
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "OnityTask did not complete before the test timeout.");
        }

        private static async Task ObservePreservedTask(OnityTask task)
        {
            await task;
        }

        private static IEnumerator WaitForFlag(Func<bool> predicate)
        {
            int remainingFrames = k_timeoutFrames;
            while (predicate() == false && remainingFrames > 0)
            {
                remainingFrames--;
                yield return null;
            }

            Assert.That(predicate(), Is.True, "Operation did not complete before the test timeout.");
        }

        private static async OnityTask AwaitPredicatesInSequence(
            Func<bool> firstPredicate,
            Func<bool> secondPredicate)
        {
            await OnityTask.WaitUntil(firstPredicate);
            await OnityTask.WaitUntil(secondPredicate);
        }

        private static async Task<int> AwaitAndGetThreadId(OnityTask task)
        {
            await task;
            return Thread.CurrentThread.ManagedThreadId;
        }

        private static async OnityTask AwaitNativeThenMaterialized(
            Func<bool> firstPredicate,
            Func<bool> secondPredicate)
        {
            await OnityTask.WaitUntil(firstPredicate);
            await OnityTask.WaitUntil(secondPredicate).AsTask();
        }

        [DefaultExecutionOrder(-32000)]
        private sealed class NextFrameUpdateProbe : MonoBehaviour
        {
            private OnityTask m_task;

            public int FrameCount { get; set; } = -1;

            public float DelaySeconds { get; set; }

            public bool UseUnscaledTime { get; set; }

            public int StartedFrame { get; private set; } = -1;

            public int CompletedFrame { get; private set; } = -1;

            public bool ObservedStartFrame { get; private set; }

            public void ConsumeTask()
            {
                m_task.GetAwaiter().GetResult();
            }

            private void Update()
            {
                if (StartedFrame >= 0)
                {
                    return;
                }

                StartedFrame = Time.frameCount;
                if (DelaySeconds > 0f)
                {
                    m_task = UseUnscaledTime
                        ? OnityTask.DelayUnscaled(DelaySeconds)
                        : OnityTask.Delay(DelaySeconds);
                }
                else
                {
                    m_task = FrameCount < 0
                        ? OnityTask.NextFrame()
                        : OnityTask.DelayFrames(FrameCount);
                }
            }

            private void LateUpdate()
            {
                if (StartedFrame < 0 || CompletedFrame >= 0)
                {
                    return;
                }

                if (Time.frameCount == StartedFrame)
                {
                    ObservedStartFrame = true;
                }

                if (m_task.IsCompleted)
                {
                    CompletedFrame = Time.frameCount;
                }
            }
        }
    }
}
