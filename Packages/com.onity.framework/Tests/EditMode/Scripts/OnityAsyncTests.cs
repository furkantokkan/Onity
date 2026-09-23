using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Messaging;
using Onity.Reactive;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

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
        public async Task NextLateFrameAsync_CanceledToken_ThrowsOperationCanceledException()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Task task = OnityAsync.NextLateFrameAsync(cancellationTokenSource.Token);

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
        public async Task OnityTask_DelayZero_CompletesSuccessfully()
        {
            OnityTask task = OnityTask.Delay(0f);

            await task;

            Assert.That(task.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void OnityTask_DelayFramesNegative_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.DelayFrames(-1));
        }

        [Test]
        public void OnityTask_DelayFramesZero_CompletesImmediately()
        {
            OnityTask task = OnityTask.DelayFrames(0);

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        }

        [TestCase(0)]
        [TestCase(2)]
        public void OnityTask_DelayFramesPreCanceled_ThrowsOperationCanceledException(int frameCount)
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            OnityTask task = OnityTask.DelayFrames(frameCount, cancellationTokenSource.Token);

            Assert.That(task.IsCanceled, Is.True);
            Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public async Task OnityTask_WaitUntilTrue_CompletesSuccessfully()
        {
            OnityTask task = OnityTask.WaitUntil(() => true);

            await task;

            Assert.That(task.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task OnityTask_WaitWhileFalse_CompletesSuccessfully()
        {
            OnityTask task = OnityTask.WaitWhile(() => false);

            await task;

            Assert.That(task.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void OnityTask_DelayCanceledToken_ThrowsOperationCanceledException()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            OnityTask task = OnityTask.Delay(1f, cancellationTokenSource.Token);

            Assert.CatchAsync<OperationCanceledException>(
                async () => await task.AsTask());
        }

        [Test]
        public async Task OnityTask_FromResult_ReturnsValue()
        {
            OnityTask<int> task = OnityTask<int>.FromResult(42);
            int value = await task;

            Assert.That(value, Is.EqualTo(42));
            Assert.That(task.IsCompletedSuccessfully, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OnityTask_TaskBackedAwaiter_ExecutionContextMatchesNativeAwaiter(bool useSafeRegistration)
        {
            TaskCompletionSource<int> source =
                new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<int> nativeSource =
                new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            OnityTaskAwaiter awaiter = OnityTask.FromTask(source.Task).GetAwaiter();

            string nativeContext = ObserveExecutionContext(
                ((Task)nativeSource.Task).GetAwaiter(), () => nativeSource.SetResult(42), useSafeRegistration);
            string adaptedContext = ObserveExecutionContext(
                awaiter, () => source.SetResult(42), useSafeRegistration);

            Assert.That(adaptedContext, Is.EqualTo(nativeContext),
                "The adapter must preserve the native Task awaiter's registration semantics.");
            Assert.DoesNotThrow(() => awaiter.GetResult());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OnityTask_TypedTaskBackedAwaiter_ExecutionContextMatchesNativeAwaiter(bool useSafeRegistration)
        {
            TaskCompletionSource<int> source =
                new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<int> nativeSource =
                new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            OnityTaskAwaiter<int> awaiter = OnityTask<int>.FromTask(source.Task).GetAwaiter();

            string nativeContext = ObserveExecutionContext(
                nativeSource.Task.GetAwaiter(), () => nativeSource.SetResult(42), useSafeRegistration);
            string adaptedContext = ObserveExecutionContext(
                awaiter, () => source.SetResult(42), useSafeRegistration);

            Assert.That(adaptedContext, Is.EqualTo(nativeContext),
                "The adapter must preserve the native Task<T> awaiter's registration semantics.");
            Assert.That(awaiter.GetResult(), Is.EqualTo(42));
        }

        [TestCase(0)]
        [TestCase(42)]
        public void OnityTask_FromResultAsTask_DoesNotInvokeResultEquality(int value)
        {
            OnityTask<ThrowingEquatableResult> task =
                OnityTask<ThrowingEquatableResult>.FromResult(new ThrowingEquatableResult(value));

            Task<ThrowingEquatableResult> converted = task.AsTask();

            Assert.That(converted.IsCompletedSuccessfully, Is.True);
            Assert.That(converted.GetAwaiter().GetResult().Value, Is.EqualTo(value));
        }

        [Test]
        public async Task OnityTaskMethodBuilder_AsyncMethodCompletesSuccessfully()
        {
            OnityTask task = CompleteWithOnityTaskAsync();

            await task;

            Assert.That(task.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task OnityTaskMethodBuilder_AsyncTypedMethodReturnsValue()
        {
            OnityTask<int> task = ReturnWithOnityTaskAsync();
            int value = await task;

            Assert.That(value, Is.EqualTo(13));
            Assert.That(task.IsCompletedSuccessfully, Is.True);
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
        public void OnityTask_LoadSceneEmpty_ThrowsArgumentException()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await OnityTask.LoadScene(string.Empty).AsTask());
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
        public void DeferredLoad_CanceledBeforeStart_DoesNotStartSceneLoad()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Task<AsyncOperation> task = OnitySceneLoader.LoadAsync(
                "AnyScene",
                activateOnLoad: false,
                cancellationToken: cancellationTokenSource.Token);

            Assert.That(task.IsCanceled, Is.True);
        }

        [Test]
        public void DeferredLoad_ProgressCallbackFault_IsLoggedOnce()
        {
            MethodInfo reportMethod = typeof(OnitySceneLoader).GetMethod(
                "ReportLoadProgress",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(reportMethod, Is.Not.Null);

            int callCount = 0;
            Action<float> onProgress = _ =>
            {
                callCount++;
                throw new InvalidOperationException("deferred callback failed");
            };
            object[] arguments = { onProgress, 0f, false };
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deferred callback failed"));

            reportMethod.Invoke(null, arguments);
            reportMethod.Invoke(null, arguments);

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(arguments[0], Is.Null);
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
        public async Task OnityTask_WhenAll_AllTasksComplete_CompletesSuccessfully()
        {
            OnityTask first = OnityTask.Completed;
            OnityTask second = OnityTask.Delay(0f);
            OnityTask whenAllTask = OnityTask.WhenAll(first, second);

            await whenAllTask;

            Assert.That(whenAllTask.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void OnityTask_WhenAll_CompletedNativeInputs_ReturnsCompletedTask()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            first.TrySetResult();
            second.TrySetResult();

            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.That(combined.AsTask(), Is.SameAs(Task.CompletedTask));
        }

        [Test]
        public async Task OnityTask_WhenAll_PendingInputs_WaitsForBoth()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            first.TrySetResult();
            Assert.That(combined.IsCompleted, Is.False);
            second.TrySetResult();
            await combined;

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task OnityTask_WhenAll_FaultedInputWinsOverCancellation()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            Exception failure = new InvalidOperationException("Expected failure.");
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            first.TrySetCanceled();
            second.TrySetException(failure);

            Exception observed = null;
            try
            {
                await combined;
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            Assert.That(observed, Is.SameAs(failure));
            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(combined.AsTask().Exception.InnerException, Is.SameAs(failure));
        }

        [Test]
        public async Task OnityTask_WhenAllTyped_ReturnsResultsInInputOrder()
        {
            TaskCompletionSource<int> firstCompletionSource =
                new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<int> secondCompletionSource =
                new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            OnityTask<int[]> whenAllTask = OnityTask.WhenAll(
                OnityTask<int>.FromTask(firstCompletionSource.Task),
                OnityTask<int>.FromTask(secondCompletionSource.Task));

            secondCompletionSource.SetResult(22);
            Assert.That(whenAllTask.IsCompleted, Is.False);
            firstCompletionSource.SetResult(11);

            int[] results = await whenAllTask;

            Assert.That(results, Is.EqualTo(new[] { 11, 22 }));
        }

        [Test]
        public void OnityTask_WhenAllTyped_CompletedInputs_DoNotCreateTrackerEntry()
        {
            bool previousTrackingEnabled = OnityTaskTracker.IsEnabled;
            bool previousStackTraceEnabled = OnityTaskTracker.EnableStackTrace;

            try
            {
                OnityTaskTracker.IsEnabled = true;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTaskTracker.ClearAll();

                OnityTask<int[]> combined = OnityTask.WhenAll(
                    OnityTask<int>.FromResult(10),
                    OnityTask<int>.FromResult(20),
                    OnityTask<int>.FromResult(30));

                Assert.That(combined.IsCompletedSuccessfully, Is.True);
                Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo(new[] { 10, 20, 30 }));
                List<OnityTrackedTaskInfo> rows = new List<OnityTrackedTaskInfo>();
                OnityTaskTracker.GetSnapshot(rows);
                Assert.That(rows, Is.Empty);
            }
            finally
            {
                OnityTaskTracker.ClearAll();
                OnityTaskTracker.IsEnabled = previousTrackingEnabled;
                OnityTaskTracker.EnableStackTrace = previousStackTraceEnabled;
            }
        }

        [Test]
        public void OnityTask_WhenAllTyped_NoInputs_ReturnsEmptyResults()
        {
            OnityTask<int[]> combined = OnityTask.WhenAll<int>();

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.That(combined.GetAwaiter().GetResult(), Is.Empty);
        }

        [Test]
        public void OnityTask_WhenAllTyped_NullInputs_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => OnityTask.WhenAll<int>((OnityTask<int>[])null));
        }

        [Test]
        public void OnityTask_WhenAllTyped_OneCompletedInput_ReturnsItsResult()
        {
            OnityTask<int[]> combined = OnityTask.WhenAll(OnityTask<int>.FromResult(42));

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void OnityTask_WhenAllTyped_CompletedNativeInput_IsConsumedOnce()
        {
            OnityTask<int> native = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);

            OnityTask<int[]> combined = OnityTask.WhenAll(native);

            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo(new[] { 0 }));
            Assert.Throws<InvalidOperationException>(() => native.GetAwaiter().GetResult());
        }

        [Test]
        public void OnityTask_WhenAllTyped_CompletedNativeDuplicate_KeepsFailureBehavior()
        {
            OnityTask<int> native = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);

            Assert.Throws<InvalidOperationException>(() => OnityTask.WhenAll(native, native));
        }

        [Test]
        public async Task OnityTask_WhenAllTyped_PendingNativeDuplicate_SharesBridge()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask<int> native = OnityTask.WhenAny(first.Task, second.Task);
            OnityTask<int[]> combined = OnityTask.WhenAll(native, native);

            first.TrySetResult();
            second.TrySetResult();
            int[] results = await combined;

            Assert.That(results, Is.EqualTo(new[] { 0, 0 }));
        }

        [Test]
        public void OnityTask_WhenAllTyped_CompletedShareableDuplicate_ReturnsBothResults()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            source.TrySetResult(17);

            OnityTask<int[]> combined = OnityTask.WhenAll(source.Task, source.Task);

            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo(new[] { 17, 17 }));
        }

        [Test]
        public void OnityTask_WhenAllTyped_CompletedTaskBackedDuplicate_ReturnsBothResults()
        {
            Task<int> task = Task.FromResult(29);
            OnityTask<int> input = OnityTask<int>.FromTask(task);

            OnityTask<int[]> combined = OnityTask.WhenAll(input, input);

            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo(new[] { 29, 29 }));
        }

        [Test]
        public void OnityTask_WhenAllTyped_ManyCompletedInputs_PreserveOrder()
        {
            OnityTask<int>[] inputs = new OnityTask<int>[64];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = OnityTask<int>.FromResult(i);
            }

            int[] results = OnityTask.WhenAll(inputs).GetAwaiter().GetResult();

            Assert.That(results.Length, Is.EqualTo(inputs.Length));
            for (int i = 0; i < results.Length; i++)
            {
                Assert.That(results[i], Is.EqualTo(i));
            }
        }

        [Test]
        public void OnityTask_WhenAllTyped_ManyInputsWithNativeSource_PreserveOrder()
        {
            OnityTask<int>[] inputs = new OnityTask<int>[17];
            inputs[0] = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);
            for (int i = 1; i < inputs.Length; i++)
            {
                inputs[i] = OnityTask<int>.FromResult(i);
            }

            int[] results = OnityTask.WhenAll(inputs).GetAwaiter().GetResult();

            Assert.That(results.Length, Is.EqualTo(inputs.Length));
            for (int i = 0; i < results.Length; i++)
            {
                Assert.That(results[i], Is.EqualTo(i));
            }
        }

        [Test]
        public void OnityTask_WhenAllTyped_FaultedInputPropagatesException()
        {
            OnityTask<int[]> whenAllTask = OnityTask.WhenAll(
                OnityTask<int>.FromResult(1),
                OnityTask<int>.FromException(new InvalidOperationException("Expected failure.")));

            Assert.That(whenAllTask.IsFaulted, Is.True);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await whenAllTask.AsTask());
        }

        [Test]
        public void OnityTask_WhenAllTyped_CanceledInputPropagatesCancellation()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();
            OnityTask<int[]> whenAllTask = OnityTask.WhenAll(
                OnityTask<int>.FromResult(1),
                OnityTask<int>.FromCanceled(cancellationTokenSource.Token));

            Assert.That(whenAllTask.IsCanceled, Is.True);
            Assert.CatchAsync<OperationCanceledException>(async () => await whenAllTask.AsTask());
        }

        [Test]
        public async Task PublishOnityTask_DeliversToOnityTaskSubscriber()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            int receivedValue = 0;

            using IDisposable subscription =
                channel.SubscribeOnityTask(
                    async (value, cancellationToken) =>
                    {
                        await OnityTask.Delay(0f, cancellationToken);
                        receivedValue = value;
                    });

            await channel.PublishOnityTask(17);

            Assert.That(receivedValue, Is.EqualTo(17));
        }

        [Test]
        public void PublishOnityTask_CanceledToken_ThrowsOperationCanceledException()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            OnityTask task = channel.PublishOnityTask(17, cancellationTokenSource.Token);

            Assert.CatchAsync<OperationCanceledException>(
                async () => await task.AsTask());
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
        public void TaskTracker_PendingTasks_RecordSuccessFaultAndCancellation()
        {
            bool previousTrackingEnabled = OnityTaskTracker.IsEnabled;
            bool previousStackTraceEnabled = OnityTaskTracker.EnableStackTrace;

            try
            {
                OnityTaskTracker.IsEnabled = true;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTaskTracker.ClearAll();

                TaskCompletionSource<bool> success = new TaskCompletionSource<bool>();
                TaskCompletionSource<bool> fault = new TaskCompletionSource<bool>();
                TaskCompletionSource<bool> cancellation = new TaskCompletionSource<bool>();
                OnityTaskTracker.Track(success.Task, "success");
                OnityTaskTracker.Track(fault.Task, "fault");
                OnityTaskTracker.Track(cancellation.Task, "cancellation");

                success.SetResult(true);
                fault.SetException(new InvalidOperationException("tracked failure"));
                cancellation.SetCanceled();

                List<OnityTrackedTaskInfo> rows = new List<OnityTrackedTaskInfo>(3);
                OnityTaskTracker.GetSnapshot(rows);
                Dictionary<int, OnityTrackedTaskInfo> byId =
                    new Dictionary<int, OnityTrackedTaskInfo>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    byId[rows[i].TaskId] = rows[i];
                }

                Assert.That(byId[success.Task.Id].Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(byId[fault.Task.Id].Status, Is.EqualTo(TaskStatus.Faulted));
                Assert.That(byId[fault.Task.Id].ErrorMessage, Is.EqualTo("tracked failure"));
                Assert.That(byId[cancellation.Task.Id].Status, Is.EqualTo(TaskStatus.Canceled));
                Assert.That(byId[cancellation.Task.Id].ErrorMessage, Is.EqualTo("Canceled"));
                Assert.That(byId[success.Task.Id].IsCompleted, Is.True);
                Assert.That(byId[fault.Task.Id].IsCompleted, Is.True);
                Assert.That(byId[cancellation.Task.Id].IsCompleted, Is.True);
            }
            finally
            {
                OnityTaskTracker.ClearAll();
                OnityTaskTracker.IsEnabled = previousTrackingEnabled;
                OnityTaskTracker.EnableStackTrace = previousStackTraceEnabled;
            }
        }

        [Test]
        public void TaskTracker_PendingFault_PreservesRegistrationContextForMessage()
        {
            bool previousTrackingEnabled = OnityTaskTracker.IsEnabled;
            bool previousStackTraceEnabled = OnityTaskTracker.EnableStackTrace;
            AsyncLocal<string> context = new AsyncLocal<string>();
            SynchronizationContext registrationContext = SynchronizationContext.Current;

            try
            {
                OnityTaskTracker.IsEnabled = true;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTaskTracker.ClearAll();
                context.Value = "registration";

                TaskCompletionSource<bool> source = new TaskCompletionSource<bool>();
                OnityTaskTracker.Track(source.Task, "context");
                Assert.That(context.Value, Is.EqualTo("registration"));
                Assert.That(SynchronizationContext.Current, Is.SameAs(registrationContext));
                context.Value = "completion";
                source.SetException(new ContextMessageException(context));

                List<OnityTrackedTaskInfo> rows = new List<OnityTrackedTaskInfo>(1);
                OnityTaskTracker.GetSnapshot(rows);
                Assert.That(rows.Count, Is.EqualTo(1));
                Assert.That(rows[0].TaskId, Is.EqualTo(source.Task.Id));
                Assert.That(rows[0].ErrorMessage, Is.EqualTo("registration"));
                Assert.That(context.Value, Is.EqualTo("completion"));
                Assert.That(SynchronizationContext.Current, Is.SameAs(registrationContext));
            }
            finally
            {
                context.Value = null;
                OnityTaskTracker.ClearAll();
                OnityTaskTracker.IsEnabled = previousTrackingEnabled;
                OnityTaskTracker.EnableStackTrace = previousStackTraceEnabled;
            }
        }

        [Test]
        public void TaskTracker_ReentrantErrorMessage_DoesNotRestoreClearedEntry()
        {
            bool previousTrackingEnabled = OnityTaskTracker.IsEnabled;
            bool previousStackTraceEnabled = OnityTaskTracker.EnableStackTrace;

            try
            {
                OnityTaskTracker.IsEnabled = true;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTaskTracker.ClearAll();

                TaskCompletionSource<bool> source = new TaskCompletionSource<bool>();
                OnityTaskTracker.Track(source.Task, "before clear");
                source.SetException(new ClearingTrackerException());
                OnityTaskTracker.Track(source.Task, "after clear");

                List<OnityTrackedTaskInfo> rows = new List<OnityTrackedTaskInfo>(1);
                OnityTaskTracker.GetSnapshot(rows);
                Assert.That(rows.Count, Is.EqualTo(1));
                Assert.That(rows[0].Source, Is.EqualTo("after clear"));
                Assert.That(rows[0].IsCompleted, Is.True);
            }
            finally
            {
                OnityTaskTracker.ClearAll();
                OnityTaskTracker.IsEnabled = previousTrackingEnabled;
                OnityTaskTracker.EnableStackTrace = previousStackTraceEnabled;
            }
        }

        [Test]
        public void TaskTracker_ReentrantRetrack_PreservesReplacementCompletionTime()
        {
            bool previousTrackingEnabled = OnityTaskTracker.IsEnabled;
            bool previousStackTraceEnabled = OnityTaskTracker.EnableStackTrace;

            try
            {
                OnityTaskTracker.IsEnabled = true;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTaskTracker.ClearAll();

                TaskCompletionSource<bool> source = new TaskCompletionSource<bool>();
                OnityTaskTracker.Track(source.Task, "before clear");
                source.SetException(new ClearingTrackerException(
                    () => OnityTaskTracker.Track(source.Task, "during clear")));

                List<OnityTrackedTaskInfo> rows = new List<OnityTrackedTaskInfo>(1);
                OnityTaskTracker.GetSnapshot(rows);
                Assert.That(rows.Count, Is.EqualTo(1));
                Assert.That(rows[0].Source, Is.EqualTo("during clear"));
                Assert.That(rows[0].IsCompleted, Is.True);
                Assert.That(rows[0].ElapsedMilliseconds, Is.GreaterThanOrEqualTo(0));
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
        public async Task AsyncOperationAsOnityTask_MissingResource_CompletesWithOriginalOperation()
        {
            ResourceRequest request = Resources.LoadAsync<TextAsset>("OnityAsyncTests_OnityTaskResource");
            OnityTask<ResourceRequest> task = request.AsOnityTask();
            ResourceRequest completedOperation = await task;

            Assert.That(completedOperation, Is.SameAs(request));
            Assert.That(task.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void AsyncOperationAsOnityTask_NullOperation_ThrowsArgumentNullException()
        {
            Assert.That(
                () => OnityTaskBridgeExtensions.AsOnityTask<ResourceRequest>(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AsyncOperationAsOnityTask_CanceledToken_ThrowsOperationCanceledException()
        {
            ResourceRequest request = Resources.LoadAsync<TextAsset>("OnityAsyncTests_OnityTaskCanceledResource");
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            OnityTask<ResourceRequest> task = request.AsOnityTask(cancellationToken: cancellationTokenSource.Token);

            Assert.CatchAsync<OperationCanceledException>(
                async () => await task.AsTask());
        }

        [Test]
        public void OnityTask_GetJsonEmptyUrl_ThrowsArgumentException()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await OnityTask.GetJson<DummyWebResponse>(string.Empty).AsTask());
        }

        [Test]
        public async Task ObservableFirstOnityTask_ReturnsFirstValue()
        {
            using Subject<int> subject = new Subject<int>();
            OnityTask<int> task = subject.FirstOnityTask();

            subject.OnNext(7);

            int value = await task;
            Assert.That(value, Is.EqualTo(7));
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

        private static string ObserveExecutionContext<TAwaiter>(
            TAwaiter awaiter, Action complete, bool useSafeRegistration)
            where TAwaiter : ICriticalNotifyCompletion
        {
            AsyncLocal<string> context = new AsyncLocal<string>();
            TaskCompletionSource<string> observed =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            SynchronizationContext previousSynchronizationContext = SynchronizationContext.Current;
            string previousValue = context.Value;

            try
            {
                // Exclude Unity's SynchronizationContext so only ExecutionContext capture is tested.
                SynchronizationContext.SetSynchronizationContext(null);
                context.Value = "registration";
                Action continuation = () => observed.TrySetResult(context.Value);
                if (useSafeRegistration)
                {
                    awaiter.OnCompleted(continuation);
                }
                else
                {
                    awaiter.UnsafeOnCompleted(continuation);
                }
            }
            finally
            {
                context.Value = previousValue;
                SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
            }

            // The completion thread must not inherit the registration thread's ExecutionContext.
            ThreadPool.UnsafeQueueUserWorkItem(_ => complete(), null);
            Assert.That(observed.Task.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "The registered continuation did not run within five seconds.");
            // Native Unity Mono and other .NET runtimes may differ; compare the adapter to
            // its corresponding native awaiter instead of assuming a particular runtime's flow.
            return observed.Task.GetAwaiter().GetResult();
        }

        private readonly struct ThrowingEquatableResult : IEquatable<ThrowingEquatableResult>
        {
            public int Value { get; }

            public ThrowingEquatableResult(int value)
            {
                Value = value;
            }

            public bool Equals(ThrowingEquatableResult other)
            {
                throw new InvalidOperationException("Result equality must not run during AsTask conversion.");
            }
        }

        private sealed class ContextMessageException : Exception
        {
            private readonly AsyncLocal<string> m_context;

            public override string Message => m_context.Value;

            public ContextMessageException(AsyncLocal<string> context)
            {
                m_context = context;
            }
        }

        private sealed class ClearingTrackerException : Exception
        {
            private readonly Action m_afterClear;
            private int m_hasCleared;

            public ClearingTrackerException(Action afterClear = null)
            {
                m_afterClear = afterClear;
            }

            public override string Message
            {
                get
                {
                    if (Interlocked.Exchange(ref m_hasCleared, 1) == 0)
                    {
                        OnityTaskTracker.ClearAll();
                        m_afterClear?.Invoke();
                    }

                    return "tracker cleared during error message";
                }
            }
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

        [Serializable]
        private sealed class DummyWebResponse
        {
            public string Value;
        }

        private static async OnityTask CompleteWithOnityTaskAsync()
        {
            await OnityTask.Delay(0f);
        }

        private static async OnityTask<int> ReturnWithOnityTaskAsync()
        {
            await OnityTask.Delay(0f);
            return 13;
        }
    }
}
