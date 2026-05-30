using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Reactive;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityAsyncTests
    {
        [Test]
        public async Task DelayAsync_ZeroDelay_CompletesImmediately()
        {
            Task delayTask = OnityAsync.DelayAsync(0f);
            await delayTask;
            Assert.That(delayTask.IsCompleted, Is.True);
        }

        [Test]
        public async Task WaitUntilAsync_TruePredicate_CompletesImmediately()
        {
            Task waitTask = OnityAsync.WaitUntilAsync(() => true);
            await waitTask;
            Assert.That(waitTask.IsCompleted, Is.True);
        }

        [Test]
        public async Task WaitWhileAsync_FalsePredicate_CompletesImmediately()
        {
            Task waitTask = OnityAsync.WaitWhileAsync(() => false);
            await waitTask;
            Assert.That(waitTask.IsCompleted, Is.True);
        }

        [Test]
        public async Task NextFrameAsync_CanceledToken_ThrowsOperationCanceledException()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Task task = OnityAsync.NextFrameAsync(cancellationTokenSource.Token);

            try
            {
                await task;
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public async Task WaitUntilAsync_CanceledToken_ThrowsOperationCanceledException()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Task task = OnityAsync.WaitUntilAsync(() => false, cancellationTokenSource.Token);

            try
            {
                await task;
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public void LoadSingleAsync_EmptyScene_ThrowsArgumentException()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await OnitySceneLoader.LoadSingleAsync(string.Empty));
        }

        [Test]
        public void UnloadAsync_EmptyScene_ThrowsArgumentException()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await OnitySceneLoader.UnloadAsync(string.Empty));
        }

        [Test]
        public async Task LoadSingleAsync_CanceledToken_ThrowsOperationCanceledException()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Task task = OnitySceneLoader.LoadSingleAsync(
                "AnyScene",
                cancellationToken: cancellationTokenSource.Token);

            try
            {
                await task;
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public void ActivateAsync_NullOperation_ThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await OnitySceneLoader.ActivateAsync(null));
        }

        [Test]
        public async Task DelayAsync_TimeSpan_ZeroDelay_CompletesImmediately()
        {
            Task delayTask = OnityAsync.DelayAsync(TimeSpan.Zero);
            await delayTask;
            Assert.That(delayTask.IsCompleted, Is.True);
        }

        [Test]
        public async Task WhenAll_AllTasksComplete_CompletesSuccessfully()
        {
            Task first = Task.CompletedTask;
            Task second = Task.CompletedTask;
            Task whenAllTask = OnityAsync.WhenAll(first, second);

            await whenAllTask;

            Assert.That(whenAllTask.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task WhenAny_ReturnsWinningTask()
        {
            TaskCompletionSource<bool> firstCompletionSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> secondCompletionSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<Task<bool>> whenAnyTask = OnityAsync.WhenAny(firstCompletionSource.Task, secondCompletionSource.Task);
            secondCompletionSource.SetResult(true);

            Task<bool> winner = await whenAnyTask;

            Assert.That(ReferenceEquals(winner, secondCompletionSource.Task), Is.True);
        }

        [Test]
        public async Task CancelAfterSlim_TimeoutReached_CancelsTokenSource()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            ManualTimeProvider timeProvider = new ManualTimeProvider();
            IDisposable timerHandle =
                cancellationTokenSource.CancelAfterSlim(TimeSpan.FromSeconds(1d), timeProvider);

            timeProvider.AdvanceOne();
            await Task.Yield();

            Assert.That(cancellationTokenSource.IsCancellationRequested, Is.True);

            timerHandle.Dispose();
        }

        [Test]
        public async Task TimeoutController_TimeoutAndReset_BehavesAsExpected()
        {
            ManualTimeProvider timeProvider = new ManualTimeProvider();
            using OnityTimeoutController timeoutController = new OnityTimeoutController(
                timeProvider: timeProvider);

            CancellationToken timeoutToken = timeoutController.Timeout(TimeSpan.FromSeconds(1d));

            Assert.That(timeoutController.IsTimeout(), Is.False);
            Assert.That(timeoutToken.IsCancellationRequested, Is.False);

            timeProvider.AdvanceOne();
            await Task.Yield();

            Assert.That(timeoutController.IsTimeout(), Is.True);
            Assert.That(timeoutToken.IsCancellationRequested, Is.True);

            timeoutController.Reset();
            Assert.That(timeoutController.IsTimeout(), Is.False);
        }

        [Test]
        public void TaskTracker_EnableStackTrace_CapturesTraceInSnapshot()
        {
            bool previousTrackingEnabled = OnityTaskTracker.IsEnabled;
            bool previousStackTraceEnabled = OnityTaskTracker.EnableStackTrace;

            try
            {
                OnityTaskTracker.IsEnabled = true;
                OnityTaskTracker.EnableStackTrace = true;
                OnityTaskTracker.ClearAll();

                Task trackedTask = OnityTaskTracker.Track(Task.CompletedTask, "OnityAsyncTests.TaskTracker");
                Assert.That(trackedTask.IsCompleted, Is.True);

                List<OnityTrackedTaskInfo> rows = new List<OnityTrackedTaskInfo>(8);
                OnityTaskTracker.GetSnapshot(rows, includeCompleted: true);

                Assert.That(rows.Count, Is.GreaterThan(0));
                Assert.That(rows[0].StackTrace, Is.Not.Empty);
            }
            finally
            {
                OnityTaskTracker.ClearAll();
                OnityTaskTracker.IsEnabled = previousTrackingEnabled;
                OnityTaskTracker.EnableStackTrace = previousStackTraceEnabled;
            }
        }

        [Test]
        public void AsyncOperationAsTask_NullOperation_ThrowsArgumentNullException()
        {
            Assert.That(
                () => OnityAsyncOperationExtensions.AsTask<ResourceRequest>(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task AsyncOperationAsTask_MissingResource_CompletesWithOriginalOperation()
        {
            ResourceRequest request = Resources.LoadAsync<TextAsset>("OnityAsyncTests_MissingResource");
            Task<ResourceRequest> task = request.AsTask();
            ResourceRequest completedOperation = await task;

            Assert.That(completedOperation, Is.SameAs(request));
            Assert.That(task.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task AsyncOperationWithCancellation_CanceledToken_ThrowsOperationCanceledException()
        {
            ResourceRequest request = Resources.LoadAsync<TextAsset>("OnityAsyncTests_CanceledResource");
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Task<ResourceRequest> task = request.WithCancellation(cancellationTokenSource.Token);

            try
            {
                await task;
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public async Task AsyncOperationGetAwaiter_MissingResource_CompletesWithOriginalOperation()
        {
            ResourceRequest request = Resources.LoadAsync<TextAsset>("OnityAsyncTests_AwaiterResource");
            ResourceRequest completedOperation = await request;

            Assert.That(completedOperation, Is.SameAs(request));
        }

        private sealed class ManualTimeProvider : OnityTimeProvider
        {
            private readonly Queue<TaskCompletionSource<bool>> m_pending =
                new Queue<TaskCompletionSource<bool>>();

            public override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
            {
                if (delay <= TimeSpan.Zero)
                {
                    return Task.CompletedTask;
                }

                TaskCompletionSource<bool> completionSource =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
                }

                m_pending.Enqueue(completionSource);
                return completionSource.Task;
            }

            public void AdvanceOne()
            {
                Assert.That(m_pending.Count, Is.GreaterThan(0));
                TaskCompletionSource<bool> completionSource = m_pending.Dequeue();
                completionSource.TrySetResult(true);
            }
        }
    }
}
