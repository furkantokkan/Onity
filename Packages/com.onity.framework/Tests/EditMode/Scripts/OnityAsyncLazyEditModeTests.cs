using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="OnityAsyncLazy"/> and <see cref="OnityAsyncLazy{T}"/>: lazy start, one
    /// shared outcome for every consumer, failure and cancellation propagation, and wrapping a
    /// task that is already running.
    /// </summary>
    [TestFixture]
    public sealed class OnityAsyncLazyEditModeTests
    {
        [Test]
        public void Constructor_NullFactory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new OnityAsyncLazy(null));
            Assert.Throws<ArgumentNullException>(() => new OnityAsyncLazy<int>(null));
        }

        [Test]
        public void Task_StartsTheFactoryOnFirstAccessOnly()
        {
            int calls = 0;
            OnityAsyncLazy lazy = new OnityAsyncLazy(() =>
            {
                calls++;
                return OnityTask.CompletedTask;
            });

            Assert.That(calls, Is.Zero);
            Assert.That(lazy.Task.IsCompleted, Is.True);
            Assert.That(lazy.Task.IsCompleted, Is.True);
            lazy.GetAwaiter().GetResult();

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Task_PendingFactoryTask_CompletesEveryConsumerWithTheSameResult()
        {
            OnityTaskCompletionSource<int> inner = new OnityTaskCompletionSource<int>();
            int calls = 0;
            OnityAsyncLazy<int> lazy = new OnityAsyncLazy<int>(() =>
            {
                calls++;
                return inner.Task;
            });
            int resumedFirst = 0;
            int resumedSecond = 0;

            lazy.GetAwaiter().UnsafeOnCompleted(() => resumedFirst++);
            lazy.Task.GetAwaiter().UnsafeOnCompleted(() => resumedSecond++);
            Assert.That(lazy.Task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            inner.TrySetResult(42);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(resumedFirst, Is.EqualTo(1));
            Assert.That(resumedSecond, Is.EqualTo(1));
            Assert.That(lazy.GetAwaiter().GetResult(), Is.EqualTo(42));
            Assert.That(lazy.Task.GetAwaiter().GetResult(), Is.EqualTo(42), "The shared task can be read again.");
        }

        [Test]
        public void Task_FactoryThrows_CompletesTheSharedTaskAsFaulted()
        {
            InvalidOperationException failure = new InvalidOperationException("factory failed");
            OnityAsyncLazy lazy = new OnityAsyncLazy(() => throw failure);

            Assert.That(lazy.Task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => lazy.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(lazy.Task.Status, Is.EqualTo(OnityTaskStatus.Faulted), "The factory must not run again.");
        }

        [Test]
        public void Task_FactoryTaskFaults_ReportsTheSameFaultToEveryConsumer()
        {
            InvalidOperationException failure = new InvalidOperationException("inner failed");
            OnityTaskCompletionSource<int> inner = new OnityTaskCompletionSource<int>();
            OnityAsyncLazy<int> lazy = new OnityAsyncLazy<int>(() => inner.Task);

            Assert.That(lazy.Task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            inner.TrySetException(failure);

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => lazy.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => lazy.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void Task_FactoryTaskCanceled_CompletesTheSharedTaskAsCanceled()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTaskCompletionSource inner = new OnityTaskCompletionSource();
            OnityAsyncLazy lazy = new OnityAsyncLazy(() => inner.Task);

            Assert.That(lazy.Task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            cancellation.Cancel();
            inner.TrySetCanceled(cancellation.Token);

            Assert.That(lazy.Task.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => lazy.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void Task_FactoryUsesItsOwnLazyValue_FaultsInsteadOfDeadlocking()
        {
            OnityAsyncLazy lazy = null;
            lazy = new OnityAsyncLazy(() =>
            {
                OnityTask reentered = lazy.Task;
                return OnityTask.CompletedTask;
            });

            Assert.That(lazy.Task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.Throws<InvalidOperationException>(() => lazy.GetAwaiter().GetResult());
        }

        [Test]
        public void Task_FirstAccessFromManyThreads_RunsTheFactoryOnce()
        {
            const int k_threads = 8;
            int calls = 0;
            OnityAsyncLazy<int> lazy = new OnityAsyncLazy<int>(() =>
            {
                Interlocked.Increment(ref calls);
                Thread.Sleep(20);
                return OnityTask.FromResult(11);
            });
            using ManualResetEventSlim start = new ManualResetEventSlim();
            List<Task<int>> readers = new List<Task<int>>();
            for (int i = 0; i < k_threads; i++)
            {
                readers.Add(Task.Run(() =>
                {
                    start.Wait();
                    return lazy.GetAwaiter().GetResult();
                }));
            }

            start.Set();
            Task.WaitAll(readers.ToArray());

            Assert.That(calls, Is.EqualTo(1));
            for (int i = 0; i < k_threads; i++)
            {
                Assert.That(readers[i].Result, Is.EqualTo(11));
            }
        }

        [Test]
        public void ToAsyncLazy_SharesARunningSingleConsumerTask()
        {
            OnityAutoResetTaskCompletionSource<string> source = OnityAutoResetTaskCompletionSource<string>.Create();
            OnityAsyncLazy<string> lazy = source.Task.ToAsyncLazy();
            int resumed = 0;

            lazy.GetAwaiter().UnsafeOnCompleted(() => resumed++);
            lazy.GetAwaiter().UnsafeOnCompleted(() => resumed++);
            Assert.That(lazy.Task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            source.TrySetResult("done");

            Assert.That(resumed, Is.EqualTo(2));
            Assert.That(lazy.GetAwaiter().GetResult(), Is.EqualTo("done"));
            Assert.That(lazy.GetAwaiter().GetResult(), Is.EqualTo("done"));
        }

        [Test]
        public void ToAsyncLazy_AlreadyCompletedTask_CompletesImmediately()
        {
            OnityAsyncLazy untyped = OnityTask.CompletedTask.ToAsyncLazy();
            OnityAsyncLazy<int> typed = OnityTask.FromResult(8).ToAsyncLazy();

            Assert.That(untyped.Task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(typed.Task.GetAwaiter().GetResult(), Is.EqualTo(8));
        }

        [Test]
        public void ToAsyncLazy_FaultedTask_SharesTheFault()
        {
            InvalidOperationException failure = new InvalidOperationException("failed");
            OnityAsyncLazy<int> lazy = OnityTask.FromException<int>(failure).ToAsyncLazy();

            Assert.That(lazy.Task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => lazy.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public async Task GetAwaiter_AllowsAwaitingTheLazyValueDirectly()
        {
            OnityAsyncLazy<int> lazy = new OnityAsyncLazy<int>(() => OnityTask.FromResult(3));

            int first = await lazy;
            int second = await lazy;

            Assert.That(first, Is.EqualTo(3));
            Assert.That(second, Is.EqualTo(3));
        }
    }
}
