using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the lambda-friendly <c>Create</c> factories, the lazily started <c>Defer</c> tasks,
    /// <c>Never</c>, the <c>Lazy</c> factories and the generic <c>FromException</c> and
    /// <c>FromCanceled</c> helpers of <see cref="OnityTask"/>.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskFactoriesEditModeTests
    {
        private static void AwaitCompleted(OnityTask task)
        {
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void Create_RunsTheFactoryImmediatelyAndReturnsItsTask()
        {
            int calls = 0;
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            OnityTask task = OnityTask.Create(() =>
            {
                calls++;
                return source.Task;
            });

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            source.TrySetResult();
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void Create_WithToken_PassesTheTokenToTheFactory()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            CancellationToken received = default;

            OnityTask task = OnityTask.Create(token =>
            {
                received = token;
                return OnityTask.CompletedTask;
            }, cancellation.Token);

            Assert.That(received, Is.EqualTo(cancellation.Token));
            AwaitCompleted(task);
        }

        [Test]
        public void Create_WithState_PassesTheStateToTheFactory()
        {
            string received = null;

            OnityTask task = OnityTask.Create("state", value =>
            {
                received = value;
                return OnityTask.CompletedTask;
            });

            Assert.That(received, Is.EqualTo("state"));
            AwaitCompleted(task);
        }

        [Test]
        public void Create_Typed_ReturnsTheFactoryResult()
        {
            OnityTask<int> task = OnityTask.Create(() => OnityTask.FromResult(7));

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(7));
        }

        [Test]
        public async Task Create_AsyncLambda_RunsAsAnOnityTask()
        {
            int steps = 0;

            OnityTask untyped = OnityTask.Create(async () =>
            {
                await OnityTask.CompletedTask;
                steps++;
            });
            OnityTask<int> typed = OnityTask.Create(async () =>
            {
                await OnityTask.CompletedTask;
                steps++;
                return 3;
            });

            await untyped;
            Assert.That(await typed, Is.EqualTo(3));
            Assert.That(steps, Is.EqualTo(2));
        }

        [Test]
        public void Create_NullFactory_ThrowsSynchronously()
        {
            Assert.Throws<ArgumentNullException>(() => OnityTask.Create((Func<OnityTask>)null));
            Assert.Throws<ArgumentNullException>(
                () => OnityTask.Create((Func<CancellationToken, OnityTask>)null, CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => OnityTask.Create(1, (Func<int, OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.Create((Func<OnityTask<int>>)null));
        }

        [Test]
        public void Create_FactoryThrows_PropagatesToTheCaller()
        {
            Assert.Throws<InvalidOperationException>(
                () => OnityTask.Create((Func<OnityTask>)(() => throw new InvalidOperationException("boom"))));
        }

        [Test]
        public void Defer_DoesNotRunTheFactoryUntilTheTaskIsFirstUsed()
        {
            int calls = 0;
            OnityTask task = OnityTask.Defer(() =>
            {
                calls++;
                return OnityTask.CompletedTask;
            });

            Assert.That(calls, Is.Zero, "Creating the deferred task must not start the work.");
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Defer_RunsTheFactoryOnce_ForRepeatedReadsAndAwaits()
        {
            int calls = 0;
            OnityTask<int> task = OnityTask.Defer(() =>
            {
                calls++;
                return OnityTask.FromResult(5);
            });

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(5));
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(5), "A deferred task is shareable.");
            Assert.That(task.AsTask().Result, Is.EqualTo(5));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Defer_WithState_PassesTheStateToTheFactory()
        {
            string receivedUntyped = null;
            string receivedTyped = null;

            OnityTask untyped = OnityTask.Defer("a", value =>
            {
                receivedUntyped = value;
                return OnityTask.CompletedTask;
            });
            OnityTask<int> typed = OnityTask.Defer("bb", value =>
            {
                receivedTyped = value;
                return OnityTask.FromResult(value.Length);
            });

            AwaitCompleted(untyped);
            Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo(2));
            Assert.That(receivedUntyped, Is.EqualTo("a"));
            Assert.That(receivedTyped, Is.EqualTo("bb"));
        }

        [Test]
        public void Defer_SharesOneOutcomeWithEveryAwaiter()
        {
            OnityTaskCompletionSource<int> inner = new OnityTaskCompletionSource<int>();
            OnityTask<int> deferred = OnityTask.Defer(() => inner.Task);
            int first = 0;
            int second = 0;

            deferred.GetAwaiter().UnsafeOnCompleted(() => first++);
            deferred.GetAwaiter().UnsafeOnCompleted(() => second++);
            Assert.That(deferred.Status, Is.EqualTo(OnityTaskStatus.Pending));
            inner.TrySetResult(9);

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(1));
            Assert.That(deferred.GetAwaiter().GetResult(), Is.EqualTo(9));
            Assert.That(deferred.GetAwaiter().GetResult(), Is.EqualTo(9));
        }

        [Test]
        public void Defer_ConsumesASingleConsumerTaskOnlyOnce()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> deferred = OnityTask.Defer(() => source.Task);

            source.TrySetResult(3);

            Assert.That(deferred.GetAwaiter().GetResult(), Is.EqualTo(3));
            Assert.That(deferred.GetAwaiter().GetResult(), Is.EqualTo(3),
                "The deferred task preserves the pooled inner task, so it can be read again.");
        }

        [Test]
        public void Defer_FactoryThrows_FaultsTheTaskInsteadOfThrowingFromTheFirstRead()
        {
            InvalidOperationException failure = new InvalidOperationException("factory failed");
            OnityTask task = OnityTask.Defer((Func<OnityTask>)(() => throw failure));

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void Defer_FactoryThrowsCancellation_CancelsTheTaskWithItsToken()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTask<int> task = OnityTask.Defer<int>(
                () => throw new OperationCanceledException(cancellation.Token));

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            OperationCanceledException thrown =
                Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.That(thrown.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void Defer_FactoryThatUsesItsOwnTask_FaultsInsteadOfRecursing()
        {
            OnityTask self = default;
            self = OnityTask.Defer(() =>
            {
                bool completed = self.IsCompleted;
                return OnityTask.CompletedTask;
            });

            Assert.That(self.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.Throws<InvalidOperationException>(() => self.GetAwaiter().GetResult());
        }

        [Test]
        public void Defer_NullFactory_ThrowsSynchronously()
        {
            Assert.Throws<ArgumentNullException>(() => OnityTask.Defer((Func<OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.Defer((Func<OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.Defer(1, (Func<int, OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.Defer(1, (Func<int, OnityTask<int>>)null));
        }

        [Test]
        public void Defer_FirstUseFromManyThreads_RunsTheFactoryOnce()
        {
            const int k_threads = 8;
            int calls = 0;
            OnityTask<int> task = OnityTask.Defer(() =>
            {
                Interlocked.Increment(ref calls);
                Thread.Sleep(20);
                return OnityTask.FromResult(1);
            });
            using ManualResetEventSlim start = new ManualResetEventSlim();
            List<Task<OnityTaskStatus>> readers = new List<Task<OnityTaskStatus>>();
            for (int i = 0; i < k_threads; i++)
            {
                readers.Add(Task.Run(() =>
                {
                    start.Wait();
                    return task.Status;
                }));
            }

            start.Set();
            Task.WaitAll(readers.ToArray());

            Assert.That(calls, Is.EqualTo(1));
            for (int i = 0; i < k_threads; i++)
            {
                Assert.That(readers[i].Result, Is.EqualTo(OnityTaskStatus.Succeeded));
            }
        }

        [Test]
        public void Never_StaysPendingUntilTheTokenIsCanceled_ThenCancelsWithThatToken()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTask untyped = OnityTask.Never(cancellation.Token);
            OnityTask<string> typed = OnityTask.Never<string>(cancellation.Token);

            Assert.That(untyped.Status, Is.EqualTo(OnityTaskStatus.Pending));
            Assert.That(typed.Status, Is.EqualTo(OnityTaskStatus.Pending));
            cancellation.Cancel();

            Assert.That(untyped.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(typed.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => untyped.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => typed.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void Never_WithAnAlreadyCanceledToken_IsCanceledImmediately()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(OnityTask.Never(cancellation.Token).Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(OnityTask.Never<int>(cancellation.Token).Status, Is.EqualTo(OnityTaskStatus.Canceled));
        }

        [Test]
        public void Never_WithATokenThatCannotCancel_StaysPending()
        {
            Assert.That(OnityTask.Never(CancellationToken.None).Status, Is.EqualTo(OnityTaskStatus.Pending));
            Assert.That(OnityTask.Never<int>(default).Status, Is.EqualTo(OnityTaskStatus.Pending));
        }

        [Test]
        public void Never_IsShareable()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTask never = OnityTask.Never(cancellation.Token);
            int resumed = 0;

            never.GetAwaiter().UnsafeOnCompleted(() => resumed++);
            never.GetAwaiter().UnsafeOnCompleted(() => resumed++);
            cancellation.Cancel();

            Assert.That(resumed, Is.EqualTo(2));
        }

        [Test]
        public void FromException_Generic_CreatesAFaultedTypedTask()
        {
            InvalidOperationException failure = new InvalidOperationException("failed");

            OnityTask<int> task = OnityTask.FromException<int>(failure);

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void FromCanceled_Generic_CreatesACanceledTypedTask()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            OnityTask<int> task = OnityTask.FromCanceled<int>(cancellation.Token);

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void Lazy_Factories_CreateLazyTasksThatDoNotStartEarly()
        {
            int untypedCalls = 0;
            int typedCalls = 0;

            OnityAsyncLazy untyped = OnityTask.Lazy(() =>
            {
                untypedCalls++;
                return OnityTask.CompletedTask;
            });
            OnityAsyncLazy<int> typed = OnityTask.Lazy(() =>
            {
                typedCalls++;
                return OnityTask.FromResult(4);
            });

            Assert.That(untypedCalls + typedCalls, Is.Zero);
            AwaitCompleted(untyped.Task);
            Assert.That(typed.Task.GetAwaiter().GetResult(), Is.EqualTo(4));
            Assert.That(untypedCalls, Is.EqualTo(1));
            Assert.That(typedCalls, Is.EqualTo(1));
        }
    }
}
