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
    /// themselves, accept worker registrations that resume on Unity's main thread, and leave no
    /// pending scheduler work behind; <c>OnityTask.Yield()</c> holds no reference and resumes at the
    /// next Update drain.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskStatelessWaitPlayModeTests
    {
        private const int k_timeoutFrames = 120;
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
