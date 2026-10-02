using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>Frame-driven checks of the composition overloads on Unity's PlayerLoop.</summary>
    public sealed class OnityTaskCompositionPlayModeTests
    {
        private const int k_timeoutFrames = 240;

        [UnityTest]
        public IEnumerator TupleWhenAll_FrameDelayedInputs_CompleteOnTheMainThreadAfterTheSlowestInput()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int startFrame = Time.frameCount;
            Task<((int, string) result, int thread, int frame)> observed = Observe(
                OnityTask.WhenAll(ValueAfterFrames(1, 1), ValueAfterFrames("late", 3)));

            yield return Wait(() => observed.IsCompleted);

            Assert.That(observed.IsCompletedSuccessfully, Is.True);
            var outcome = observed.Result;
            Assert.That(outcome.result, Is.EqualTo((1, "late")));
            Assert.That(outcome.thread, Is.EqualTo(mainThread));
            Assert.That(outcome.frame - startFrame, Is.GreaterThanOrEqualTo(3));
        }

        [UnityTest]
        public IEnumerator TupleAwaiters_TypedAndUntypedFrameWaits_ResumeAfterEveryInput()
        {
            int startFrame = Time.frameCount;
            Task<(int, int, int)> typed = AwaitTypedTuple();
            Task untyped = AwaitUntypedTuple();

            yield return Wait(() => typed.IsCompleted && untyped.IsCompleted);

            Assert.That(typed.IsCompletedSuccessfully, Is.True);
            Assert.That(untyped.IsCompletedSuccessfully, Is.True);
            Assert.That(typed.Result, Is.EqualTo((1, 2, 3)));
            Assert.That(Time.frameCount - startFrame, Is.GreaterThanOrEqualTo(3));
        }

        [UnityTest]
        public IEnumerator MixedWhenAny_FastestFrameWaitWins_AndTheSlowerLoserIsConsumedLater()
        {
            int startFrame = Time.frameCount;
            OnityTask<int> slow = ValueAfterFrames(1, 4);
            var race = OnityTask.WhenAny(slow, ValueAfterFrames("fast", 1));
            Task<(int winArgumentIndex, int result1, string result2)> observed = race.AsTask();

            yield return Wait(() => observed.IsCompleted);

            Assert.That(observed.IsCompletedSuccessfully, Is.True);
            Assert.That(observed.Result, Is.EqualTo((1, 0, "fast")));
            Assert.That(Time.frameCount - startFrame, Is.LessThan(4));
            Assert.Throws<InvalidOperationException>(() => slow.GetAwaiter().OnCompleted(() => { }),
                "The race still observes the slower input.");

            yield return Wait(() => Time.frameCount - startFrame >= 6);
            Assert.Throws<InvalidOperationException>(() => _ = slow.IsCompleted,
                "The slower input was consumed when it completed.");
        }

        [UnityTest]
        public IEnumerator WhenEach_FrameDelayedInputs_ArriveInCompletionOrder()
        {
            List<int> order = new List<int>();
            Task consumer = CollectInOrder(
                OnityTask.WhenEach(ValueAfterFrames(3, 3), ValueAfterFrames(1, 1), ValueAfterFrames(2, 2)),
                order);

            yield return Wait(() => consumer.IsCompleted);

            Assert.That(consumer.IsCompletedSuccessfully, Is.True);
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        private static async Task CollectInOrder(
            IOnityAsyncEnumerable<OnityWhenEachResult<int>> source,
            List<int> order)
        {
            await foreach (OnityWhenEachResult<int> item in source)
            {
                order.Add(item.GetResult());
            }
        }

        private static async Task<((int, string) result, int thread, int frame)> Observe(
            OnityTask<(int, string)> task)
        {
            (int, string) result = await task;
            return (result, Thread.CurrentThread.ManagedThreadId, Time.frameCount);
        }

        private static async Task<(int, int, int)> AwaitTypedTuple()
        {
            return await (ValueAfterFrames(1, 3), ValueAfterFrames(2, 1), ValueAfterFrames(3, 2));
        }

        private static async Task AwaitUntypedTuple()
        {
            await (OnityTask.DelayFrames(2), OnityTask.NextFrame(), OnityTask.DelayFrames(3));
        }

        private static async OnityTask<T> ValueAfterFrames<T>(T value, int frames)
        {
            await OnityTask.DelayFrames(frames);
            return value;
        }

        private static IEnumerator Wait(Func<bool> completed)
        {
            for (int frame = 0; frame < k_timeoutFrames && !completed(); frame++)
            {
                yield return null;
            }

            Assert.That(completed(), Is.True, "Composition PlayMode operation timed out.");
        }
    }
}
