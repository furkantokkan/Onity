using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    // Frame-driven checks of the single-source stream operators, sources and consumers.
    public sealed class OnityAsyncEnumerableOperatorsPlayModeTests
    {
        [UnityTest]
        public IEnumerator Create_ProducerAwaitingFrames_DeliversItemsAcrossFrames_AndUnwindsOnEarlyExit()
        {
            int unwound = 0;
            int startFrame = Time.frameCount;
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                try
                {
                    for (int i = 0; ; i++)
                    {
                        await OnityTask.NextFrame(token);
                        await writer.YieldAsync(i);
                    }
                }
                finally
                {
                    unwound++;
                }
            });
            var task = stream.Take(3).ToArrayAsync();
            Assert.That(task.IsCompleted, Is.False);
            yield return Wait(() => task.IsCompleted);
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(unwound, Is.EqualTo(1));
            Assert.That(Time.frameCount, Is.GreaterThan(startFrame + 2));
        }

        [UnityTest]
        public IEnumerator Subscribe_OnEveryUpdate_RunsOncePerFrame_AndDisposeStopsTheStream()
        {
            int frames = 0;
            IDisposable subscription = OnityAsyncEnumerable.EveryUpdate().Subscribe(unit => frames++);
            yield return Wait(() => frames >= 3);
            subscription.Dispose();
            int atDispose = frames;
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }
            Assert.That(frames, Is.LessThanOrEqualTo(atDispose + 1));
        }

        [UnityTest]
        public IEnumerator ProjectionOperators_ComposeOverFrameDrivenStreams()
        {
            var buffered = OnityAsyncEnumerable.EveryUpdate().Take(5).Select((unit, index) => index).Buffer(2)
                .ToArrayAsync();
            var paired = OnityAsyncEnumerable.EveryUpdate().Take(3).Select((unit, index) => index).Pairwise()
                .ToArrayAsync();
            var reversed = OnityAsyncEnumerable.EveryUpdate().Take(3).Select((unit, index) => index).Reverse()
                .ToArrayAsync();
            var events = new List<string>();
            var counted = OnityAsyncEnumerable.EveryUpdate().Take(2)
                .Do(unit => events.Add("next"), () => events.Add("completed")).CountAsync();
            yield return Wait(() => buffered.IsCompleted && paired.IsCompleted && reversed.IsCompleted
                && counted.IsCompleted);

            Assert.That(buffered.GetAwaiter().GetResult().Select(list => list.ToArray()).ToArray(),
                Is.EqualTo(new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 } }));
            Assert.That(paired.GetAwaiter().GetResult(), Is.EqualTo(new[] { (0, 1), (1, 2) }));
            Assert.That(reversed.GetAwaiter().GetResult(), Is.EqualTo(new[] { 2, 1, 0 }));
            Assert.That(counted.GetAwaiter().GetResult(), Is.EqualTo(2));
            // Do follows Take(2): the stream ends after the second item, and the count can only complete once
            // the end reached Do, so its onCompleted has already run.
            Assert.That(events, Is.EqualTo(new[] { "next", "next", "completed" }));
        }

        [UnityTest]
        public IEnumerator TaskConversionAndNever_FollowFramesAndCancellation()
        {
            int frame = Time.frameCount;
            var frames = OnityTask.NextFrame().ToOnityAsyncEnumerable().ToArrayAsync();
            yield return Wait(() => frames.IsCompleted);
            Assert.That(frames.GetAwaiter().GetResult().Length, Is.EqualTo(1));
            Assert.That(Time.frameCount, Is.GreaterThan(frame));

            using (var cts = new CancellationTokenSource())
            {
                var never = OnityAsyncEnumerable.Never<int>().ToArrayAsync(cts.Token);
                yield return null;
                Assert.That(never.IsCompleted, Is.False);
                cts.Cancel();
                yield return Wait(() => never.IsCompleted);
                Assert.That(never.IsCanceled, Is.True);
            }
        }

        private static IEnumerator Wait(Func<bool> done)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!done())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Frame-driven stream timed out.");
                yield return null;
            }
        }
    }
}
