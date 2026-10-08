using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Covers the stateless default frame waits and the Yield awaitable of the PERF-7 scheduler:
    /// token-less waits keep their frame semantics, can be awaited and read repeatedly, preserve
    /// themselves, accept worker registrations (also after the wait became due) that resume on Unity's
    /// main thread, and leave no pending scheduler work behind; <c>OnityTask.Yield()</c> holds no
    /// reference and resumes at the next Update drain.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskStatelessWaitPlayModeTests
    {
        private const int k_timeoutFrames = 120;
        private const double k_timeoutSeconds = 5d;
        private const BindingFlags k_instance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator NextFrame_CrossesARenderedFrame_AndCanBeReadRepeatedly()
        {
            int created = Time.frameCount;
            OnityTask task = OnityTask.NextFrame();
            Assert.That(task.IsCompleted, Is.False, "a frame wait never completes in its creating frame");
            int completedFrame = -1;
            task.GetAwaiter().UnsafeOnCompleted(() => completedFrame = Time.frameCount);
            yield return Poll(() => completedFrame >= 0);

            Assert.That(completedFrame - created, Is.GreaterThanOrEqualTo(1));
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult(), "a stateless wait has no single consumer");
            Assert.That(task.IsCompletedSuccessfully, Is.True);
        }

        [UnityTest]
        public IEnumerator DelayFrames_TokenLess_CountsRenderedFrames()
        {
            int created = Time.frameCount;
            OnityTask task = OnityTask.DelayFrames(3);
            int completedFrame = -1;
            task.GetAwaiter().UnsafeOnCompleted(() => completedFrame = Time.frameCount);
            yield return Poll(() => completedFrame >= 0);

            Assert.That(completedFrame - created, Is.GreaterThanOrEqualTo(3));
        }

        [UnityTest]
        public IEnumerator StatelessWait_PreserveReturnsTheSameTask_AndServesSeveralConsumers()
        {
            OnityTask task = OnityTask.NextFrame();
            OnityTask preserved = task.Preserve();
            Assert.That(ReadState(preserved), Is.SameAs(ReadState(task)), "Preserve allocates nothing for a stateless wait");

            int consumers = 0;
            task.GetAwaiter().UnsafeOnCompleted(() => consumers++);
            task.GetAwaiter().UnsafeOnCompleted(() => consumers++);
            Task bridge = task.AsTask();
            yield return Poll(() => consumers == 2 && bridge.IsCompleted);

            Assert.That(bridge.IsCompletedSuccessfully, Is.True);
            Assert.DoesNotThrow(() => preserved.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator NextFixedAndLateFrame_TokenLess_CompleteAtTheirNextDrain()
        {
            bool fixedDone = false;
            bool lateDone = false;
            OnityTask fixedWait = OnityTask.NextFixedFrame();
            OnityTask lateWait = OnityTask.NextLateFrame();
            fixedWait.GetAwaiter().UnsafeOnCompleted(() => fixedDone = true);
            lateWait.GetAwaiter().UnsafeOnCompleted(() => lateDone = true);
            yield return Poll(() => fixedDone && lateDone);

            Assert.DoesNotThrow(() => fixedWait.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => lateWait.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator WorkerRegistration_ResumesOnTheMainThread()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            int resumedThread = 0;
            Task worker = Task.Run(() =>
            {
                OnityTask task = OnityTask.NextFrame();
                task.GetAwaiter().UnsafeOnCompleted(() => resumedThread = Thread.CurrentThread.ManagedThreadId);
            });
            yield return Poll(() => worker.IsCompleted && resumedThread != 0);

            Assert.That(worker.IsFaulted, Is.False, worker.Exception?.ToString());
            Assert.That(resumedThread, Is.EqualTo(main));
        }

        [UnityTest]
        public IEnumerator WorkerRegistration_OnADueFrameWait_ResumesOnTheMainThread()
        {
            yield return RegisterOnAWorkerOnceDue(() => OnityTask.NextFrame());
        }

        [UnityTest]
        public IEnumerator WorkerRegistration_OnADueYieldWait_ResumesOnTheMainThread()
        {
            yield return RegisterOnAWorkerOnceDue(() => OnityTask.Yield());
        }

        [UnityTest]
        public IEnumerator YieldAwaitable_HoldsNoReference_AndResumesAtTheNextDrain()
        {
            foreach (FieldInfo field in typeof(OnityYieldAwaiter).GetFields(k_instance | BindingFlags.Public))
            {
                Assert.That(field.FieldType.IsValueType, Is.True, "awaiter field " + field.Name);
            }

            int created = Time.frameCount;
            OnityTask<int> resumed = ResumeFrameAfterYieldAsync();
            yield return Poll(() => resumed.IsCompleted);

            int resumedFrame = resumed.GetAwaiter().GetResult();
            Assert.That(resumedFrame - created, Is.LessThanOrEqualTo(1), "a yield resumes at the next Update drain");
        }

        [UnityTest]
        public IEnumerator YieldAwaitable_ConvertsToAStatelessTask()
        {
            OnityTask task = OnityTask.Yield();
            yield return Poll(() => task.IsCompleted);

            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator SchedulerWork_ReturnsToIdle()
        {
            OnityTask frame = OnityTask.NextFrame();
            OnityTask delayed = OnityTask.DelayFrames(2);
            OnityTask yielded = OnityTask.Yield();
            yield return Poll(() => frame.IsCompleted && delayed.IsCompleted && yielded.IsCompleted);
            yield return null;

            PropertyInfo pending = typeof(OnityTaskPlayerLoop).GetProperty(
                "PendingWorkCount", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(pending, Is.Not.Null);
            Assert.That((int)pending.GetValue(null), Is.Zero);
        }

        private static async OnityTask<int> ResumeFrameAfterYieldAsync()
        {
            await OnityTask.Yield();
            return Time.frameCount;
        }

        /// <summary>
        /// Creates a stateless wait on a worker, lets the main thread drain until that wait is due, and
        /// only then lets the worker register a continuation, which must resume on the main thread. The
        /// registration therefore always finds the wait due. Before this fix a due wait ran its
        /// continuation inline on the registering thread, so these tests failed by construction, while
        /// <see cref="WorkerRegistration_ResumesOnTheMainThread"/> failed only when a drain happened to fall
        /// between the worker's creation and its registration.
        /// </summary>
        private static IEnumerator RegisterOnAWorkerOnceDue(Func<OnityTask> create)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(k_timeoutSeconds);
            int main = Thread.CurrentThread.ManagedThreadId;
            int workerThread = 0;
            int resumedThread = 0;
            bool dueAtRegistration = false;
            OnityTask task = default;
            // Not disposed: an assertion can end the test while the worker is still leaving Wait.
            ManualResetEventSlim created = new ManualResetEventSlim(false);
            ManualResetEventSlim release = new ManualResetEventSlim(false);
            Task worker = Task.Run(() =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                task = create();
                created.Set();
                if (!release.Wait(timeout))
                {
                    throw new TimeoutException("The main thread did not release the registration.");
                }

                dueAtRegistration = task.IsCompleted;
                task.GetAwaiter().UnsafeOnCompleted(() => resumedThread = Thread.CurrentThread.ManagedThreadId);
            });

            // Nothing drains while this step blocks the main thread; frames run while it yields below.
            bool wasCreated = created.Wait(timeout);
            double deadline = Time.realtimeSinceStartupAsDouble + k_timeoutSeconds;
            while (wasCreated && !task.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            bool dueBeforeRelease = wasCreated && task.IsCompleted;
            // Released before any assertion can end the test, so the worker never stays parked.
            release.Set();
            Assert.That(wasCreated, Is.True, "The worker did not create its wait.");
            Assert.That(workerThread, Is.Not.EqualTo(main), "The wait was not created on a worker.");
            Assert.That(ReadState(task), Is.SameAs(ReadState(create())), "The worker did not get the stateless wait.");
            Assert.That(dueBeforeRelease, Is.True, "The worker's wait did not become due.");

            deadline = Time.realtimeSinceStartupAsDouble + k_timeoutSeconds;
            while (!worker.IsFaulted && (!worker.IsCompleted || resumedThread == 0)
                && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            Assert.That(worker.IsCompleted, Is.True, "The worker did not finish its registration.");
            Assert.That(worker.IsFaulted, Is.False, worker.Exception?.ToString());
            Assert.That(dueAtRegistration, Is.True, "The worker registered before its wait was due.");
            Assert.That(resumedThread, Is.Not.Zero, "The continuation did not run.");
            Assert.That(resumedThread, Is.EqualTo(main),
                "A worker registration on a due wait resumed off the main thread.");
        }

        private static object ReadState(OnityTask task)
        {
            return typeof(OnityTask).GetField("m_state", k_instance).GetValue(task);
        }

        private static IEnumerator Poll(Func<bool> condition)
        {
            for (int i = 0; i < k_timeoutFrames && !condition(); i++)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, "condition not reached within " + k_timeoutFrames + " frames");
        }
    }
}
