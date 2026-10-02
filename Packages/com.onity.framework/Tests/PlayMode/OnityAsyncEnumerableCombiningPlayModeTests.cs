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
    public sealed class OnityAsyncEnumerableCombiningPlayModeTests
    {
        private static IOnityAsyncEnumerable<int> Frames() =>
            OnityAsyncEnumerable.EveryUpdate().Select(_ => Time.frameCount);

        [UnityTest]
        public IEnumerator Merge_TwoFrameStreams_RelaysEveryItemOfBoth()
        {
            Task<int[]> items = Frames().Select(_ => 1).Take(3)
                .Merge(Frames().Select(_ => 2).Take(2))
                .ToArrayAsync().AsTask();
            yield return Wait(() => items.IsCompleted);
            int[] result = items.Result;
            Assert.That(result.Length, Is.EqualTo(5));
            Assert.That(Array.FindAll(result, value => value == 1).Length, Is.EqualTo(3));
            Assert.That(Array.FindAll(result, value => value == 2).Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator TakeUntil_FrameDelay_EndsTheFrameStream()
        {
            int start = Time.frameCount;
            Task<int[]> items = Frames().TakeUntil(OnityTask.DelayFrames(3)).ToArrayAsync().AsTask();
            yield return Wait(() => items.IsCompleted);
            int[] frames = items.Result;
            Assert.That(frames.Length, Is.InRange(1, 4));
            for (int i = 1; i < frames.Length; i++)
            {
                Assert.That(frames[i], Is.GreaterThan(frames[i - 1]));
            }
            Assert.That(Time.frameCount - start, Is.LessThan(30));
        }

        [UnityTest]
        public IEnumerator SkipUntil_FrameDelay_StartsTheFrameStreamLater()
        {
            int start = Time.frameCount;
            Task<int[]> items = Frames().SkipUntil(OnityTask.DelayFrames(2)).Take(2).ToArrayAsync().AsTask();
            yield return Wait(() => items.IsCompleted);
            int[] frames = items.Result;
            Assert.That(frames.Length, Is.EqualTo(2));
            Assert.That(frames[0], Is.GreaterThanOrEqualTo(start + 2));
            Assert.That(frames[1], Is.GreaterThan(frames[0]));
        }

        [UnityTest]
        public IEnumerator Zip_FrameStreams_PairsEachFrameOnce()
        {
            Task<(int First, int Second)[]> pairs = Frames().Zip(Frames()).Take(3).ToArrayAsync().AsTask();
            yield return Wait(() => pairs.IsCompleted);
            (int First, int Second)[] result = pairs.Result;
            Assert.That(result.Length, Is.EqualTo(3));
            for (int i = 0; i < result.Length; i++)
            {
                Assert.That(result[i].Second, Is.GreaterThanOrEqualTo(result[i].First));
                if (i > 0)
                {
                    Assert.That(result[i].First, Is.GreaterThan(result[i - 1].First));
                }
            }
        }

        [UnityTest]
        public IEnumerator TakeUntilCanceled_FrameStream_EndsNormallyOnCancel()
        {
            using var cts = new CancellationTokenSource();
            Task<int[]> items = Frames().TakeUntilCanceled(cts.Token).ToArrayAsync().AsTask();
            yield return null;
            yield return null;
            cts.Cancel();
            yield return Wait(() => items.IsCompleted);
            Assert.That(items.IsCanceled || items.IsFaulted, Is.False);
            Assert.That(items.Result.Length, Is.GreaterThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator CombineLatest_ReactivePropertiesSetAcrossFrames_YieldOneResultPerChange()
        {
            var health = new OnityAsyncReactiveProperty<int>(10);
            var mana = new OnityAsyncReactiveProperty<int>(5);
            var results = new List<int>();
            using var cts = new CancellationTokenSource();
            Task consumption = health.CombineLatest(mana, (h, m) => h * 100 + m)
                .ForEachAsync(value => results.Add(value), cts.Token).AsTask();
            yield return null;
            health.Value = 20;
            yield return null;
            mana.Value = 6;
            yield return null;
            health.Value = 30;
            mana.Value = 7;
            yield return null;
            cts.Cancel();
            yield return Wait(() => consumption.IsCompleted);
            Assert.That(consumption.IsCanceled, Is.True);
            Assert.That(results, Is.EqualTo(new[] { 1005, 2005, 2006, 3006, 3007 }));
            health.Dispose();
            mana.Dispose();
        }

        [UnityTest]
        public IEnumerator CombineLatest_FrameStreamAndProperty_UsesTheLatestFrame()
        {
            var level = new OnityAsyncReactiveProperty<int>(1);
            int start = Time.frameCount;
            Task<int[]> items = Frames().CombineLatest(level.WithoutCurrent(), (frame, value) => frame * 10 + value)
                .Take(1).ToArrayAsync().AsTask();
            yield return null;
            yield return null;
            yield return null;
            level.Value = 2;
            yield return Wait(() => items.IsCompleted);
            int result = items.Result[0];
            Assert.That(result % 10, Is.EqualTo(2));
            Assert.That(result / 10, Is.GreaterThanOrEqualTo(start + 2), "the frame stream kept its latest frame");
            level.Dispose();
        }

        private static IEnumerator Wait(Func<bool> predicate)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!predicate())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "The stream timed out.");
                yield return null;
            }
        }
    }
}
