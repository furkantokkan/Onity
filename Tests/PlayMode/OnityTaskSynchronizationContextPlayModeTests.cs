using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Covers the SynchronizationContext switch and its return scopes against Unity's real main
    /// context, and the async-delegate <c>RunOnThreadPool</c> returning to the main thread.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskSynchronizationContextPlayModeTests
    {
        private const int k_timeoutSeconds = 10;

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed.TotalSeconds < k_timeoutSeconds)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, "The operation timed out.");
        }

        private static async OnityTask<(int worker, int resumed)> ResumeOnContextAsync(SynchronizationContext context)
        {
            await OnityTask.SwitchToThreadPool();
            int worker = Thread.CurrentThread.ManagedThreadId;
            await OnityTask.SwitchToSynchronizationContext(context);
            return (worker, Thread.CurrentThread.ManagedThreadId);
        }

        private static async OnityTask<(int inside, int after)> ReturnScopeAsync()
        {
            int inside;
            await using (OnityTask.ReturnToCurrentSynchronizationContext())
            {
                await OnityTask.SwitchToThreadPool();
                inside = Thread.CurrentThread.ManagedThreadId;
            }

            return (inside, Thread.CurrentThread.ManagedThreadId);
        }

        [UnityTest]
        public IEnumerator SwitchToSynchronizationContext_UnityContext_ResumesOnTheMainThread()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext mainContext = SynchronizationContext.Current;
            Assert.That(mainContext, Is.Not.Null);

            OnityTask<(int worker, int resumed)> run = ResumeOnContextAsync(mainContext);
            yield return WaitFor(() => run.IsCompleted);
            (int worker, int resumed) result = run.GetAwaiter().GetResult();

            Assert.That(result.worker, Is.Not.EqualTo(mainThread));
            Assert.That(result.resumed, Is.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator ReturnToCurrentSynchronizationContext_ScopeResumesOnTheMainThread()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;

            OnityTask<(int inside, int after)> run = ReturnScopeAsync();
            yield return WaitFor(() => run.IsCompleted);
            (int inside, int after) result = run.GetAwaiter().GetResult();

            Assert.That(result.inside, Is.Not.EqualTo(mainThread));
            Assert.That(result.after, Is.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator RunOnThreadPool_AsyncDelegate_PublishesOnTheMainThread()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int workerThread = 0;
            int completionThread = 0;

            OnityTask<string> task = OnityTask.RunOnThreadPool(
                state =>
                {
                    workerThread = Thread.CurrentThread.ManagedThreadId;
                    return OnityTask.FromResult("done " + state);
                },
                "state");
            task.GetAwaiter().UnsafeOnCompleted(() => completionThread = Thread.CurrentThread.ManagedThreadId);
            yield return WaitFor(() => task.IsCompleted && Volatile.Read(ref completionThread) != 0);

            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
            Assert.That(completionThread, Is.EqualTo(mainThread));
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo("done state"));
        }

        [UnityTest]
        public IEnumerator SwitchToTaskPool_LeavesTheMainThreadAndReturnsWithSwitchToMainThread()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;

            OnityTask<(bool pooled, int returned)> run = TaskPoolRoundTripAsync();
            yield return WaitFor(() => run.IsCompleted);
            (bool pooled, int returned) result = run.GetAwaiter().GetResult();

            Assert.That(result.pooled, Is.True);
            Assert.That(result.returned, Is.EqualTo(mainThread));
        }

        private static async OnityTask<(bool pooled, int returned)> TaskPoolRoundTripAsync()
        {
            await OnityTask.SwitchToTaskPool();
            bool pooled = Thread.CurrentThread.IsThreadPoolThread;
            await OnityTask.SwitchToMainThread();
            return (pooled, Thread.CurrentThread.ManagedThreadId);
        }
    }
}
