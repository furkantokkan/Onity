using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Phase T timed waits (A4): delays with a clock and a timing, polled predicates, WaitUntilCanceled,
    /// WaitUntilValueChanged, the PlayerLoop timer and its CancelAfterSlim, TimeoutController and Timeout
    /// overloads, SourceOnCompleted and JobHandle.WaitAsync.
    /// </summary>
    public sealed class OnityTaskTimedWaitPlayModeTests
    {
        [UnityTest]
        public IEnumerator Delay_EveryClockAndTiming_WaitsAtLeastItsDuration()
        {
            var cases = new[]
            {
                (OnityDelayType.DeltaTime, OnityPlayerLoopTiming.Update),
                (OnityDelayType.UnscaledDeltaTime, OnityPlayerLoopTiming.PreLateUpdate),
                (OnityDelayType.Realtime, OnityPlayerLoopTiming.LastPostLateUpdate)
            };
            var delay = TimeSpan.FromMilliseconds(100);
            float startTime = Time.realtimeSinceStartup;
            int startFrame = Time.frameCount;
            var completed = new float[cases.Length];
            Exception error = null;
            for (int i = 0; i < cases.Length; i++)
            {
                int index = i;
                Observe(OnityTask.Delay(delay, cases[i].Item1, cases[i].Item2),
                    () => completed[index] = Time.realtimeSinceStartup, exception => error = exception);
            }

            yield return Wait(() => error != null || (completed[0] > 0f && completed[1] > 0f && completed[2] > 0f));
            Assert.That(error, Is.Null);
            for (int i = 0; i < cases.Length; i++)
            {
                Assert.That(completed[i] - startTime, Is.GreaterThanOrEqualTo(0.09f), cases[i].ToString());
            }

            Assert.That(Time.frameCount, Is.GreaterThan(startFrame));
            Assert.That(OnityTask.Delay(TimeSpan.Zero, OnityDelayType.Realtime).IsCompletedSuccessfully, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.Delay(TimeSpan.FromTicks(-1), false));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.WaitForSeconds(float.NaN));
        }

        [UnityTest]
        public IEnumerator Delay_ScaledClockPausesWithTimeScale_WhileUnscaledRuns()
        {
            float previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                bool scaled = false;
                bool unscaled = false;
                Observe(OnityTask.Delay(TimeSpan.FromMilliseconds(50), OnityDelayType.DeltaTime),
                    () => scaled = true, exception => { });
                Observe(OnityTask.WaitForSeconds(0.05f, true), () => unscaled = true, exception => { });
                yield return Wait(() => unscaled);
                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                }

                Assert.That(scaled, Is.False, "A scaled delay advanced while time was paused.");
                Time.timeScale = 1f;
                yield return Wait(() => scaled);
            }
            finally
            {
                Time.timeScale = previousScale;
            }
        }

        [UnityTest]
        public IEnumerator PolledPredicates_RunAtTheirTiming_AndFaultOnPredicateException()
        {
            var flag = new Flag();
            OnityTask until = OnityTask.WaitUntil(flag, state => state.Value, OnityPlayerLoopTiming.PreLateUpdate);
            OnityTask whileTrue = OnityTask.WaitWhile(() => !flag.Value, OnityPlayerLoopTiming.EarlyUpdate);
            Assert.That(OnityTask.WaitUntil(() => true, OnityPlayerLoopTiming.PreUpdate).IsCompletedSuccessfully, Is.True);
            bool untilDone = false;
            bool whileDone = false;
            Observe(until, () => untilDone = true, exception => { });
            Observe(whileTrue, () => whileDone = true, exception => { });
            yield return null;
            Assert.That(untilDone || whileDone, Is.False);
            flag.Value = true;
            yield return Wait(() => untilDone && whileDone);

            int calls = 0;
            Exception fault = null;
            Observe(OnityTask.WaitUntil(() => ++calls > 1 ? throw new InvalidOperationException("predicate") : false,
                OnityPlayerLoopTiming.Update), () => { }, exception => fault = exception);
            yield return Wait(() => fault != null);
            Assert.That(fault, Is.TypeOf<InvalidOperationException>());
        }

        [UnityTest]
        public IEnumerator FlaggedCancellation_OfAPausedFixedWait_PublishesAtTheNextUpdate()
        {
            float previousScale = Time.timeScale;
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    Time.timeScale = 0f;
                    OnityTask wait = OnityTask.WaitUntil(
                        () => false, OnityPlayerLoopTiming.FixedUpdateBegin, cancellation.Token);
                    bool canceled = false;
                    wait.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        try
                        {
                            wait.GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException exception)
                        {
                            canceled = exception.CancellationToken == cancellation.Token;
                        }
                    });
                    Task worker = Task.Run(() => cancellation.Cancel());
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(canceled, Is.False, "A flag-only cancellation published off the main thread.");
                    yield return Wait(() => canceled);
                }
                finally
                {
                    Time.timeScale = previousScale;
                }
            }
        }

        [UnityTest]
        public IEnumerator WaitUntilCanceled_CompletesAfterCancellation_OrImmediatelyOnTheCancelingThread()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            using (var polled = new CancellationTokenSource())
            using (var immediate = new CancellationTokenSource())
            {
                bool polledDone = false;
                int immediateThread = 0;
                Observe(OnityTask.WaitUntilCanceled(polled.Token, OnityPlayerLoopTiming.LastUpdate),
                    () => polledDone = true, exception => { });
                OnityTask immediateWait = OnityTask.WaitUntilCanceled(immediate.Token, OnityPlayerLoopTiming.Update, true);
                immediateWait.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    immediateWait.GetAwaiter().GetResult();
                    immediateThread = Thread.CurrentThread.ManagedThreadId;
                });
                yield return null;
                Assert.That(polledDone, Is.False);
                polled.Cancel();
                Task worker = Task.Run(() => immediate.Cancel());
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(immediateThread, Is.Not.EqualTo(0).And.Not.EqualTo(main));
                yield return Wait(() => polledDone);
                Assert.That(OnityTask.WaitUntilCanceled(polled.Token).IsCompletedSuccessfully, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator WaitUntilValueChanged_ReturnsTheNewValue_AndCancelsForADestroyedTarget()
        {
            var counter = new Counter();
            OnityTask<int> changed = OnityTask.WaitUntilValueChanged(counter, target => target.Value / 2,
                OnityPlayerLoopTiming.PreUpdate);
            counter.Value = 1;
            yield return null;
            yield return null;
            Assert.That(changed.IsCompleted, Is.False, "The comparer saw a change of the monitored value.");
            counter.Value = 4;
            yield return Wait(() => changed.IsCompleted);
            Assert.That(changed.GetAwaiter().GetResult(), Is.EqualTo(2));

            var host = new GameObject("Onity Value Changed Target");
            OnityTask<string> destroyed = OnityTask.WaitUntilValueChanged(host, target => target.name);
            UnityEngine.Object.Destroy(host);
            yield return Wait(() => destroyed.IsCompleted);
            Assert.That(destroyed.IsCanceled, Is.True);
        }

        [UnityTest]
        public IEnumerator CancelImmediately_DelayPublishesOnTheCancelingThread()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            using (var cancellation = new CancellationTokenSource())
            {
                OnityTask wait = OnityTask.Delay(TimeSpan.FromSeconds(30), OnityDelayType.Realtime,
                    OnityPlayerLoopTiming.PostLateUpdate, cancellation.Token, true);
                int thread = 0;
                wait.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    thread = Thread.CurrentThread.ManagedThreadId;
                    Assert.Throws<OperationCanceledException>(() => wait.GetAwaiter().GetResult());
                });
                Task worker = Task.Run(() => cancellation.Cancel());
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(thread, Is.Not.EqualTo(0).And.Not.EqualTo(main));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerLoopTimer_OneShotPeriodicRestartStopAndSessionExit()
        {
            int oneShot = 0;
            OnityPlayerLoopTimer single = OnityPlayerLoopTimer.StartNew(TimeSpan.FromMilliseconds(20), false,
                OnityDelayType.Realtime, OnityPlayerLoopTiming.Update, CancellationToken.None, state => oneShot++, null);
            int periodic = 0;
            OnityPlayerLoopTimer repeating = OnityPlayerLoopTimer.StartNew(TimeSpan.Zero, true,
                OnityDelayType.UnscaledDeltaTime, OnityPlayerLoopTiming.LastPreUpdate, CancellationToken.None,
                state => periodic++, null);
            yield return Wait(() => oneShot == 1 && periodic >= 3);
            Assert.That(single.IsRunning, Is.False);
            repeating.Stop();
            int stoppedAt = periodic;
            yield return null;
            yield return null;
            Assert.That(periodic, Is.LessThanOrEqualTo(stoppedAt + 1));

            int restarted = 0;
            OnityPlayerLoopTimer selfRestart = null;
            selfRestart = OnityPlayerLoopTimer.StartNew(TimeSpan.Zero, false, OnityDelayType.UnscaledDeltaTime,
                OnityPlayerLoopTiming.Update, CancellationToken.None, state =>
                {
                    if (++restarted < 3)
                    {
                        selfRestart.Restart();
                    }
                }, null);
            yield return Wait(() => restarted == 3);
            Assert.That(selfRestart.IsRunning, Is.False);

            repeating.Restart();
            Assert.That(repeating.IsRunning, Is.True);
            RestartSession();
            Assert.That(repeating.IsRunning, Is.False, "A session exit kept the timer running.");
            repeating.Restart();
            yield return Wait(() => periodic > stoppedAt + 2);
            repeating.Dispose();
            Assert.Throws<ObjectDisposedException>(() => repeating.Restart());
        }

        [UnityTest]
        public IEnumerator CancelAfterSlimAndTimeoutController_UseThePlayerLoopClock()
        {
            using (var source = new CancellationTokenSource())
            using (var kept = new CancellationTokenSource())
            {
                source.CancelAfterSlim(TimeSpan.FromMilliseconds(30), OnityDelayType.Realtime, OnityPlayerLoopTiming.PreUpdate);
                IDisposable stopped = kept.CancelAfterSlim(30, OnityDelayType.UnscaledDeltaTime);
                stopped.Dispose();
                yield return Wait(() => source.IsCancellationRequested);
                yield return new WaitForSecondsRealtime(0.1f);
                Assert.That(kept.IsCancellationRequested, Is.False);
            }

            using (var controller = new OnityTimeoutController(OnityDelayType.Realtime, OnityPlayerLoopTiming.LastUpdate))
            {
                CancellationToken token = controller.Timeout(30);
                Assert.That(controller.IsTimeout(), Is.False);
                yield return Wait(() => token.IsCancellationRequested);
                Assert.That(controller.IsTimeout(), Is.True);
                CancellationToken reset = controller.Timeout(TimeSpan.FromSeconds(30));
                controller.Reset();
                yield return null;
                Assert.That(controller.IsTimeout(), Is.False);
                Assert.That(reset.IsCancellationRequested, Is.True, "Reset cancels the previous token.");
            }
        }

        [UnityTest]
        public IEnumerator TimedTimeout_FaultsAndCancelsTheTaskSource_OrKeepsTheProducerResult()
        {
            using (var producerCancellation = new CancellationTokenSource())
            {
                var never = new OnityTaskCompletionSource<int>();
                OnityTask<int> timed = never.Task.Timeout(TimeSpan.FromMilliseconds(20), OnityDelayType.Realtime,
                    OnityPlayerLoopTiming.PreLateUpdate, producerCancellation);
                yield return Wait(() => timed.IsCompleted);
                Assert.Throws<TimeoutException>(() => timed.GetAwaiter().GetResult());
                Assert.That(producerCancellation.IsCancellationRequested, Is.True);
            }

            var producer = new OnityTaskCompletionSource<int>();
            OnityTask<(bool isTimeout, int result)> flagged = producer.Task.TimeoutWithoutException(
                TimeSpan.FromSeconds(30), OnityDelayType.UnscaledDeltaTime, OnityPlayerLoopTiming.Update);
            producer.TrySetResult(7);
            yield return Wait(() => flagged.IsCompleted);
            Assert.That(flagged.GetAwaiter().GetResult(), Is.EqualTo((false, 7)));

            var silent = new OnityTaskCompletionSource();
            OnityTask<bool> expired = silent.Task.TimeoutWithoutException(TimeSpan.Zero, OnityDelayType.DeltaTime);
            Assert.That(expired.GetAwaiter().GetResult(), Is.True);
        }

        [UnityTest]
        public IEnumerator SourceOnCompleted_PassesItsState()
        {
            var state = new object();
            object received = null;
            OnityTask wait = OnityTask.Yield(OnityPlayerLoopTiming.Update, CancellationToken.None);
            wait.GetAwaiter().SourceOnCompleted(value => received = value, state);
            yield return Wait(() => received != null);
            Assert.That(received, Is.SameAs(state));
            Assert.Throws<ArgumentNullException>(() => wait.GetAwaiter().SourceOnCompleted(null, state));
        }

        [UnityTest]
        public IEnumerator JobHandleWaitAsync_CompletesTheHandleAtTheTiming()
        {
            JobHandle handle = new CountingJob().Schedule();
            OnityTask wait = handle.WaitAsync(OnityPlayerLoopTiming.PreLateUpdate);
            yield return Wait(() => wait.IsCompleted);
            wait.GetAwaiter().GetResult();
            Assert.That(handle.IsCompleted, Is.True);
        }

        private struct CountingJob : IJob
        {
            public void Execute()
            {
            }
        }

        private sealed class Flag
        {
            public volatile bool Value;
        }

        private sealed class Counter
        {
            public int Value;
        }

        private static void RestartSession()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            typeof(OnityTaskPlayerLoop).GetMethod("CloseSession", flags).Invoke(null, null);
            typeof(OnityTaskPlayerLoop).GetMethod("BeginSession", flags).Invoke(null, new object[] { true });
            OnityTaskPlayerLoop.Initialize();
        }

        private static void Observe(OnityTask task, Action complete, Action<Exception> fail)
        {
            task.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    task.GetAwaiter().GetResult();
                    complete();
                }
                catch (Exception exception)
                {
                    fail(exception);
                }
            });
        }

        private static IEnumerator Wait(Func<bool> completed)
        {
            // Bounded by real time: the waits measure time, and in Editor batch mode a frame can take about
            // 0.15 ms, so a frame budget (600 frames, about 90 ms) can expire before a 100 ms delay.
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!completed() && timer.Elapsed.TotalSeconds < 10d)
            {
                yield return null;
            }

            Assert.That(completed(), Is.True, "Timed wait timed out.");
        }
    }
}
