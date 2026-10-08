using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Phase T: the appended UniTask timings, lazy node installation, PlayerLoop items and posted
    /// continuations, immediate cancellation, timed main-thread switches and the stateless explicit waits.
    /// </summary>
    public sealed class OnityTaskTimingPlayModeTests
    {
        private const BindingFlags k_staticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator AppendedTimings_InstallOnFirstUse_AndResumeOnTheMainThread()
        {
            RestartSession();
            int main = Thread.CurrentThread.ManagedThreadId;
            var resumed = new Dictionary<OnityPlayerLoopTiming, int>();
            Exception error = null;
            foreach (OnityPlayerLoopTiming timing in Enum.GetValues(typeof(OnityPlayerLoopTiming)))
            {
                if ((int)timing < 3)
                {
                    continue;
                }

                Assert.That(OnityTaskPlayerLoop.IsInjected(timing), Is.False, timing + " was installed before use.");
                OnityTask wait = OnityTask.Yield(timing);
                Assert.That(OnityTaskPlayerLoop.IsInjected(timing), Is.True, timing + " was not installed on use.");
                OnityPlayerLoopTiming captured = timing;
                Observe(wait, () => resumed[captured] = Thread.CurrentThread.ManagedThreadId, exception => error = exception);
            }

            yield return Wait(() => error != null || resumed.Count == 16);
            Assert.That(error, Is.Null);
            foreach (KeyValuePair<OnityPlayerLoopTiming, int> entry in resumed)
            {
                Assert.That(entry.Value, Is.EqualTo(main), entry.Key.ToString());
            }

            StringAssert.Contains("Onity.LastPostLateUpdateMarker", OnityTaskPlayerLoop.DumpCurrentPlayerLoop());
        }

        [UnityTest]
        public IEnumerator AppendedTimings_RunInPlayerLoopOrder()
        {
            OnityTaskPlayerLoop.InitializeAll();
            yield return null;
            OnityPlayerLoopTiming[] sameFrame =
            {
                OnityPlayerLoopTiming.LastInitialization, OnityPlayerLoopTiming.EarlyUpdate,
                OnityPlayerLoopTiming.LastEarlyUpdate, OnityPlayerLoopTiming.PreUpdate,
                OnityPlayerLoopTiming.LastPreUpdate, OnityPlayerLoopTiming.UpdateBegin, OnityPlayerLoopTiming.Update,
                OnityPlayerLoopTiming.LastUpdate, OnityPlayerLoopTiming.PreLateUpdate, OnityPlayerLoopTiming.LateUpdate,
                OnityPlayerLoopTiming.LastPreLateUpdate, OnityPlayerLoopTiming.PostLateUpdate,
                OnityPlayerLoopTiming.LastPostLateUpdate
            };
            var order = new List<OnityPlayerLoopTiming>();
            int registeredFrame = -1;
            // Registered from the Initialization drain, every other timing of that frame follows it.
            OnityTaskPlayerLoop.AddContinuation(OnityPlayerLoopTiming.Initialization, () =>
            {
                registeredFrame = Time.frameCount;
                foreach (OnityPlayerLoopTiming timing in sameFrame)
                {
                    OnityPlayerLoopTiming captured = timing;
                    OnityTaskPlayerLoop.AddContinuation(timing, () =>
                    {
                        if (Time.frameCount == registeredFrame)
                        {
                            order.Add(captured);
                        }
                    });
                }
            });

            yield return Wait(() => registeredFrame >= 0 && Time.frameCount > registeredFrame + 1);
            Assert.That(order, Is.EqualTo(sameFrame));
        }

        [UnityTest]
        public IEnumerator FirstUseInsideADrain_InstallsAfterTheDrain()
        {
            RestartSession();
            bool injectedInsideDrain = true;
            bool completed = false;
            Exception error = null;
            OnityTaskPlayerLoop.AddContinuation(OnityPlayerLoopTiming.Update, () =>
            {
                try
                {
                    OnityTask wait = OnityTask.Yield(OnityPlayerLoopTiming.LastPostLateUpdate);
                    injectedInsideDrain = OnityTaskPlayerLoop.IsInjected(OnityPlayerLoopTiming.LastPostLateUpdate);
                    Observe(wait, () => completed = true, exception => error = exception);
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });

            yield return Wait(() => completed || error != null);
            Assert.That(error, Is.Null);
            Assert.That(injectedInsideDrain, Is.False, "The loop was replaced while Onity iterated its queue.");
            Assert.That(OnityTaskPlayerLoop.IsInjected(OnityPlayerLoopTiming.LastPostLateUpdate), Is.True);
        }

        [UnityTest]
        public IEnumerator WorkerFirstUse_InstallsAtTheNextUpdateDrain()
        {
            RestartSession();
            int main = Thread.CurrentThread.ManagedThreadId;
            int resumedThread = 0;
            Task worker = Task.Run(() =>
            {
                OnityTaskPlayerLoop.AddContinuation(
                    OnityPlayerLoopTiming.PreUpdate, () => resumedThread = Thread.CurrentThread.ManagedThreadId);
            });
            Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(worker.IsFaulted, Is.False);
            yield return Wait(() => resumedThread != 0);
            Assert.That(resumedThread, Is.EqualTo(main));
            Assert.That(OnityTaskPlayerLoop.IsInjected(OnityPlayerLoopTiming.PreUpdate), Is.True);
        }

        [UnityTest]
        public IEnumerator AddAction_RunsEachDrainUntilFalse_AndDropsAThrowingItem()
        {
            var counted = new CountingItem(3);
            var throwing = new ThrowingItem();
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Phase T item failure"));
            OnityTaskPlayerLoop.AddAction(OnityPlayerLoopTiming.PreLateUpdate, counted);
            OnityTaskPlayerLoop.AddAction(OnityPlayerLoopTiming.PreLateUpdate, throwing);
            yield return Wait(() => counted.Calls >= 3);
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            Assert.That(counted.Calls, Is.EqualTo(3));
            Assert.That(throwing.Calls, Is.EqualTo(1));
            Assert.That(counted.Frames[1], Is.GreaterThan(counted.Frames[0]));
        }

        [UnityTest]
        public IEnumerator PostedContinuations_RunOnce_AndASessionCloseDropsThem()
        {
            int posted = 0;
            OnityTask.Post(() => posted++, OnityPlayerLoopTiming.LateUpdate);
            yield return Wait(() => posted != 0);
            yield return null;
            Assert.That(posted, Is.EqualTo(1));

            int dropped = 0;
            OnityTask.Post(() => dropped++, OnityPlayerLoopTiming.PostLateUpdate);
            OnityTaskPlayerLoop.AddContinuation(OnityPlayerLoopTiming.Update, () => dropped++);
            RestartSession();
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            Assert.That(dropped, Is.Zero, "A session close ran a posted continuation.");
        }

        [UnityTest]
        public IEnumerator CancelImmediately_PublishesOnTheCancelingThread()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            using (var cancellation = new CancellationTokenSource())
            {
                OnityTask[] waits =
                {
                    OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellation.Token, true),
                    OnityTask.NextFrame(OnityPlayerLoopTiming.FixedUpdate, cancellation.Token, true),
                    OnityTask.DelayFrame(1000, OnityPlayerLoopTiming.PreUpdate, cancellation.Token, true),
                    OnityTask.NextFrame(cancellation.Token, true),
                    OnityTask.WaitForFixedUpdate(cancellation.Token, true)
                };
                var threads = new int[waits.Length];
                var canceled = new bool[waits.Length];
                for (int i = 0; i < waits.Length; i++)
                {
                    int index = i;
                    OnityTask wait = waits[i];
                    wait.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        threads[index] = Thread.CurrentThread.ManagedThreadId;
                        try
                        {
                            wait.GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException exception)
                        {
                            canceled[index] = exception.CancellationToken == cancellation.Token;
                        }
                    });
                }

                Task worker = Task.Run(() => cancellation.Cancel());
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                for (int i = 0; i < waits.Length; i++)
                {
                    Assert.That(canceled[i], Is.True, "Wait " + i + " was not canceled with its token.");
                    Assert.That(threads[i], Is.Not.EqualTo(main), "Wait " + i + " waited for the main thread.");
                }
            }

            // The queue entries left behind are skipped by their drains.
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TimedSwitchAndReturnScope_ResumeOnTheMainThread()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            OnityTask<int> switched = SwitchBackAsync(OnityPlayerLoopTiming.PreUpdate);
            OnityTask<int> returned = ReturnScopeAsync(OnityPlayerLoopTiming.LastUpdate);
            OnityTask<int> untimed = ReturnScopeUntimedAsync();
            yield return Wait(() => switched.IsCompleted && returned.IsCompleted && untimed.IsCompleted);
            Assert.That(switched.GetAwaiter().GetResult(), Is.EqualTo(main));
            Assert.That(returned.GetAwaiter().GetResult(), Is.EqualTo(main));
            Assert.That(untimed.GetAwaiter().GetResult(), Is.EqualTo(main));
            Assert.That(OnityTask.SwitchToMainThread(OnityPlayerLoopTiming.PreUpdate).GetAwaiter().IsCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator WaitForFixedUpdate_ResumesAtTheEndOfTheFixedStep()
        {
            var order = new List<string>();
            Exception error = null;
            // Registered together, both resume in the next fixed step: Onity's FixedUpdate node right after
            // the fixed scripts, LastFixedUpdate at the end of the phase, after the physics simulation.
            Observe(OnityTask.WaitForFixedUpdate(), () => order.Add("last"), exception => error = exception);
            Observe(OnityTask.NextFixedFrame(), () => order.Add("scripts"), exception => error = exception);
            yield return Wait(() => order.Count == 2 || error != null);
            Assert.That(error, Is.Null);
            Assert.That(order, Is.EqualTo(new[] { "scripts", "last" }));
        }

        [UnityTest]
        public IEnumerator DelayFrame_ZeroYieldsOnce_AndPositiveCountsRenderedFrames()
        {
            OnityTask zero = OnityTask.DelayFrame(0, OnityPlayerLoopTiming.PreLateUpdate);
            Assert.That(zero.IsCompleted, Is.False);
            int registered = Time.frameCount;
            int completedFrame = -1;
            Exception error = null;
            Observe(OnityTask.DelayFrame(2, OnityPlayerLoopTiming.PreUpdate), () => completedFrame = Time.frameCount,
                exception => error = exception);
            yield return Wait(() => zero.IsCompleted && (completedFrame >= 0 || error != null));
            Assert.That(error, Is.Null);
            Assert.That(completedFrame - registered, Is.GreaterThanOrEqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.DelayFrame(-1));
        }

        [UnityTest]
        public IEnumerator ExplicitTokenlessWaits_AreStatelessAndShareable()
        {
            OnityTask frame = OnityTask.NextFrame(OnityPlayerLoopTiming.PostLateUpdate);
            OnityTask delayed = OnityTask.DelayFrames(2, OnityPlayerLoopTiming.EarlyUpdate, default);
            Assert.That(State(frame.Preserve()), Is.SameAs(State(frame)));
            int consumers = 0;
            frame.GetAwaiter().UnsafeOnCompleted(() => consumers++);
            frame.GetAwaiter().UnsafeOnCompleted(() => consumers++);
            Task bridge = delayed.AsTask();
            Assert.That(delayed.AsTask(), Is.SameAs(bridge));
            yield return Wait(() => consumers == 2 && bridge.IsCompleted);
            frame.GetAwaiter().GetResult();
            frame.GetAwaiter().GetResult();
            delayed.GetAwaiter().GetResult();
        }

        [Test]
        public void MainThreadSurface_ReportsTheUnityThreadAndContext()
        {
            Assert.That(OnityTaskPlayerLoop.IsMainThread, Is.True);
            Assert.That(OnityTaskPlayerLoop.MainThreadId, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
            Assert.That(OnityTaskPlayerLoop.UnitySynchronizationContext, Is.Not.Null);
            Task<bool> worker = Task.Run(() => OnityTaskPlayerLoop.IsMainThread);
            Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(worker.Result, Is.False);
        }

        [UnityTest]
        public IEnumerator WaitForEndOfFrameWithBehaviour_ValidatesAndCompletes()
        {
            Assert.Throws<ArgumentNullException>(() => OnityTask.WaitForEndOfFrame((MonoBehaviour)null));
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("End-of-frame waits need a graphics device.");
            }

            var host = new GameObject("Onity Timing Behaviour").AddComponent<OnityTimingHostBehaviour>();
            try
            {
                bool completed = false;
                Exception error = null;
                Observe(OnityTask.WaitForEndOfFrame(host), () => completed = true, exception => error = exception);
                yield return Wait(() => completed || error != null);
                Assert.That(error, Is.Null);
            }
            finally
            {
                UnityEngine.Object.Destroy(host.gameObject);
            }
        }

        private static async OnityTask<int> SwitchBackAsync(OnityPlayerLoopTiming timing)
        {
            await OnityTask.SwitchToThreadPool();
            await OnityTask.SwitchToMainThread(timing);
            return Thread.CurrentThread.ManagedThreadId;
        }

        private static async OnityTask<int> ReturnScopeAsync(OnityPlayerLoopTiming timing)
        {
            await using (OnityTask.ReturnToMainThread(timing))
            {
                await OnityTask.SwitchToThreadPool();
            }

            return Thread.CurrentThread.ManagedThreadId;
        }

        private static async OnityTask<int> ReturnScopeUntimedAsync()
        {
            await using (OnityTask.ReturnToMainThread())
            {
                await OnityTask.SwitchToThreadPool();
            }

            return Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>Closes and reopens the session, so only the eager nodes are installed.</summary>
        private static void RestartSession()
        {
            typeof(OnityTaskPlayerLoop).GetMethod("CloseSession", k_staticPrivate).Invoke(null, null);
            typeof(OnityTaskPlayerLoop).GetMethod("BeginSession", k_staticPrivate).Invoke(null, new object[] { true });
            OnityTaskPlayerLoop.Initialize();
        }

        private static object State(OnityTask task)
        {
            return typeof(OnityTask).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(task);
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
            // Bounded by real time: fixed-step timings need fixedDeltaTime of game time, and a batch-mode
            // frame can take about 0.15 ms.
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!completed() && timer.Elapsed.TotalSeconds < 10d)
            {
                yield return null;
            }

            Assert.That(completed(), Is.True, "Timing operation timed out.");
        }

        private sealed class CountingItem : IOnityPlayerLoopItem
        {
            private readonly int m_limit;

            internal readonly List<int> Frames = new List<int>();
            internal int Calls;

            internal CountingItem(int limit)
            {
                m_limit = limit;
            }

            public bool MoveNext()
            {
                Calls++;
                Frames.Add(Time.frameCount);
                return Calls < m_limit;
            }
        }

        private sealed class ThrowingItem : IOnityPlayerLoopItem
        {
            internal int Calls;

            public bool MoveNext()
            {
                Calls++;
                throw new InvalidOperationException("Phase T item failure");
            }
        }
    }

    /// <summary>Behaviour argument of the UniTask-shaped end-of-frame overloads.</summary>
    public sealed class OnityTimingHostBehaviour : MonoBehaviour
    {
    }
}
