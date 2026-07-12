using System;
using System.Collections;
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

        private static async OnityTask AwaitNativeThenMaterialized(
            Func<bool> firstPredicate,
            Func<bool> secondPredicate)
        {
            await OnityTask.WaitUntil(firstPredicate);
            await OnityTask.WaitUntil(secondPredicate).AsTask();
        }
    }
}
