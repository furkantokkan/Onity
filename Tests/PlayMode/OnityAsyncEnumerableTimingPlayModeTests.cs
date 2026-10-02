using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>A6: PlayerLoop timing streams (EveryUpdate(timing), Timer, Interval, TimerFrame, IntervalFrame, EveryValueChanged).</summary>
    public sealed class OnityAsyncEnumerableTimingPlayModeTests
    {
        [UnityTest]
        public IEnumerator EveryUpdateTiming_YieldsOncePerDrainOnLaterFrames()
        {
            OnityTask<List<int>> frames = CollectFramesAsync(OnityAsyncEnumerable.EveryUpdate(OnityPlayerLoopTiming.PreUpdate), 3);
            yield return Wait(() => frames.IsCompleted);
            List<int> result = frames.GetAwaiter().GetResult();
            Assert.That(result[1], Is.GreaterThan(result[0]));
            Assert.That(result[2], Is.GreaterThan(result[1]));
        }

        [UnityTest]
        public IEnumerator Timers_YieldAfterTheirDelays_AndZeroIntervalsNeverSpinInline()
        {
            OnityTask<List<int>> once = CollectFramesAsync(
                OnityAsyncEnumerable.Timer(TimeSpan.FromMilliseconds(30), OnityPlayerLoopTiming.Update, true), 5);
            OnityTask<List<int>> zero = CollectFramesAsync(OnityAsyncEnumerable.Interval(TimeSpan.Zero), 3);
            OnityTask<List<int>> frames = CollectFramesAsync(OnityAsyncEnumerable.IntervalFrame(2, OnityPlayerLoopTiming.LastUpdate), 2);
            OnityTask<List<int>> timerFrame = CollectFramesAsync(OnityAsyncEnumerable.TimerFrame(1, 3), 3);
            int start = Time.frameCount;
            yield return Wait(() => once.IsCompleted && zero.IsCompleted && frames.IsCompleted && timerFrame.IsCompleted);
            Assert.That(once.GetAwaiter().GetResult().Count, Is.EqualTo(1), "A one-shot timer yielded more than once.");
            List<int> zeroFrames = zero.GetAwaiter().GetResult();
            Assert.That(zeroFrames[1], Is.GreaterThan(zeroFrames[0]), "A zero interval yielded twice in one frame.");
            List<int> intervalFrames = frames.GetAwaiter().GetResult();
            Assert.That(intervalFrames[0] - start, Is.GreaterThanOrEqualTo(2));
            Assert.That(intervalFrames[1] - intervalFrames[0], Is.GreaterThanOrEqualTo(2));
            List<int> timed = timerFrame.GetAwaiter().GetResult();
            Assert.That(timed[1] - timed[0], Is.GreaterThanOrEqualTo(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityAsyncEnumerable.TimerFrame(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityAsyncEnumerable.Interval(TimeSpan.FromTicks(-1)));
        }

        [UnityTest]
        public IEnumerator EveryValueChanged_YieldsTheCurrentValueThenChanges_AndEndsWhenTheTargetIsDestroyed()
        {
            var host = new GameObject("Onity Value Stream");
            var values = new List<string>();
            OnityTask collect = CollectNamesAsync(host, values);
            yield return null;
            Assert.That(values, Is.EqualTo(new[] { "Onity Value Stream" }));
            host.name = "renamed";
            yield return Wait(() => values.Count == 2);
            Assert.That(values[1], Is.EqualTo("renamed"));
            UnityEngine.Object.Destroy(host);
            yield return Wait(() => collect.IsCompleted);
            collect.GetAwaiter().GetResult();
            Assert.Throws<ArgumentNullException>(() => OnityAsyncEnumerable.EveryValueChanged<GameObject, string>(null, g => g.name));
        }

        [UnityTest]
        public IEnumerator CancelImmediately_EndsAPendingMoveWithoutAFrame()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                IOnityAsyncEnumerator<Unit> enumerator = OnityAsyncEnumerable
                    .TimerFrame(1000, OnityPlayerLoopTiming.Update, true)
                    .GetAsyncEnumerator(cancellation.Token);
                OnityTask<bool> move = enumerator.MoveNextAsync();
                var worker = System.Threading.Tasks.Task.Run(() => cancellation.Cancel());
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(move.IsCompleted, Is.True, "The cancellation waited for the main thread.");
                Assert.That(move.IsCanceled, Is.True);
                OnityTask dispose = enumerator.DisposeAsync();
                yield return Wait(() => dispose.IsCompleted);
            }
        }

        private static async OnityTask<List<int>> CollectFramesAsync(IOnityAsyncEnumerable<Unit> source, int count)
        {
            var frames = new List<int>();
            await foreach (Unit item in source)
            {
                frames.Add(Time.frameCount);
                if (frames.Count == count)
                {
                    break;
                }
            }

            return frames;
        }

        private static async OnityTask CollectNamesAsync(GameObject host, List<string> values)
        {
            await foreach (string name in OnityAsyncEnumerable.EveryValueChanged(host, target => target.name))
            {
                values.Add(name);
            }
        }

        private static IEnumerator Wait(Func<bool> completed)
        {
            // Bounded by real time: the timers measure time, and a batch-mode frame can take about 0.15 ms.
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!completed() && timer.Elapsed.TotalSeconds < 10d)
            {
                yield return null;
            }

            Assert.That(completed(), Is.True, "Timing stream timed out.");
        }
    }
}
