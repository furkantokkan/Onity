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
        public async Task OnityTask_WhenAll_TwoNativeInputs_DoNotCreateInputTaskBridges()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(new[] { first.Task, second.Task });

            Assert.That(GetCompletionSourceBridge(first), Is.Null);
            Assert.That(GetCompletionSourceBridge(second), Is.Null);
            Assert.That(combined.IsCompleted, Is.False);

            second.TrySetResult();
            Assert.That(combined.IsCompleted, Is.False);
            first.TrySetResult();
            await combined;

            Assert.That(GetCompletionSourceBridge(first), Is.Null);
            Assert.That(GetCompletionSourceBridge(second), Is.Null);
            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.That(combined.AsTask().IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void OnityTask_WhenAll_FaultWaitsForSecondInput()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            InvalidOperationException failure = new InvalidOperationException("first failed");
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            first.TrySetException(failure);
            Assert.That(combined.IsCompleted, Is.False);
            second.TrySetResult();

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(Assert.Throws<InvalidOperationException>(
                () => combined.GetAwaiter().GetResult()), Is.SameAs(failure));
        }

        [Test]
        public void OnityTask_WhenAll_FaultTakesPriorityOverEarlierCancellation()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OperationCanceledException failure = new OperationCanceledException("faulted second input");
            OnityTask combined = OnityTask.WhenAll(first.Task, OnityTask.FromException(failure));

            Assert.That(combined.IsCompleted, Is.False);
            first.TrySetCanceled(cancellation.Token);

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(combined.IsCanceled, Is.False);
            Assert.That(Assert.Catch<OperationCanceledException>(
                () => combined.GetAwaiter().GetResult()), Is.SameAs(failure));
        }

        [Test]
        public void OnityTask_WhenAll_CancellationPreservesFirstInputToken()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            first.TrySetCanceled(cancellation.Token);
            second.TrySetResult();

            Assert.That(combined.IsCanceled, Is.True);
            Assert.That(Assert.Catch<OperationCanceledException>(
                () => combined.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(combined.AsTask().IsCanceled, Is.True);
        }

        [Test]
        public async Task OnityTask_WhenAll_BridgeRetainsAllNativeAndTaskFaults()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            TaskCompletionSource<bool> second =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception firstFailure = new InvalidOperationException("native failure");
            Exception secondFailure = new ArgumentException("task failure one");
            Exception thirdFailure = new FormatException("task failure two");
            OnityTask combined = OnityTask.WhenAll(first.Task, OnityTask.FromTask(second.Task));
            Task bridge = combined.AsTask();

            second.SetException(new[] { secondFailure, thirdFailure });
            first.TrySetException(firstFailure);

            InvalidOperationException observedFailure = null;
            try
            {
                await bridge;
            }
            catch (InvalidOperationException exception)
            {
                observedFailure = exception;
            }

            Assert.That(observedFailure, Is.SameAs(firstFailure));
            Assert.That(Assert.Throws<InvalidOperationException>(
                () => combined.GetAwaiter().GetResult()), Is.SameAs(firstFailure));
            Assert.That(bridge.IsFaulted, Is.True);
            Assert.That(bridge.Exception.InnerExceptions,
                Is.EqualTo(new[] { firstFailure, secondFailure, thirdFailure }));
        }

        [Test]
        public void OnityTask_WhenAll_TwoNativeFaults_KeepInputOrder()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            Exception firstFailure = new InvalidOperationException("first failure");
            Exception secondFailure = new ArgumentException("second failure");
            Task bridge = OnityTask.WhenAll(first.Task, second.Task).AsTask();

            second.TrySetException(secondFailure);
            first.TrySetException(firstFailure);

            Assert.That(bridge.IsFaulted, Is.True);
            Assert.That(bridge.Exception.InnerExceptions,
                Is.EqualTo(new[] { firstFailure, secondFailure }));
        }

        [Test]
        public void OnityTask_WhenAll_NativeFaultedCancellationException_RemainsFaulted()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OperationCanceledException failure = new OperationCanceledException("faulted input");
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            MethodInfo setFault = typeof(OnityTaskCompletionSource<bool>).GetMethod(
                "TrySetFault", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setFault, Is.Not.Null);
            Assert.That(setFault.Invoke(first, new object[] { failure }), Is.EqualTo(true));
            second.TrySetResult();

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(combined.IsCanceled, Is.False);
            Assert.That(combined.AsTask().Exception.InnerException, Is.SameAs(failure));
        }

        [Test]
        public async Task OnityTask_WhenAll_TaskBackedInput_DoesNotRequireRegistrationContextPump()
        {
            TaskCompletionSource<bool> first =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            SynchronizationContext previousContext = SynchronizationContext.Current;
            NoPumpSynchronizationContext registrationContext = new NoPumpSynchronizationContext();
            OnityTask combined;

            try
            {
                SynchronizationContext.SetSynchronizationContext(registrationContext);
                combined = OnityTask.WhenAll(OnityTask.FromTask(first.Task), second.Task);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            await Task.Run(() =>
            {
                first.SetResult(true);
                second.TrySetResult();
            });

            Task completion = combined.AsTask();
            Task winner = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(completion));
            Assert.That(registrationContext.PostCount, Is.Zero);
        }

        [Test]
        public async Task OnityTask_WhenAll_TwoNativeInputs_AwaitResumesOnUnityContext()
        {
            SynchronizationContext unityContext = SynchronizationContext.Current;
            int unityThread = Thread.CurrentThread.ManagedThreadId;
            Assert.That(unityContext, Is.Not.Null);

            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);
            Task<int> observing = ObserveCompletionThread(combined);

            await Task.Run(() =>
            {
                first.TrySetResult();
                second.TrySetResult();
            });

            Assert.That(await observing, Is.EqualTo(unityThread));
        }

        [Test]
        public async Task OnityTask_WhenAll_DuplicateNativeInput_UsesOneTaskBridge()
        {
            (OnityTask task, object source, MethodInfo tick) = NewStandaloneFrameTask();
            OnityTask combined = OnityTask.WhenAll(task, task);

            Assert.That(combined.IsCompleted, Is.False);
            tick.Invoke(source, new object[] { 0f, 0f });
            await combined;

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void OnityTask_WhenAll_DuplicateShareableInput_UsesNativeRegistrations()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTask shared = source.Task;
            OnityTask combined = OnityTask.WhenAll(shared, shared);

            Assert.That(GetCompletionSourceBridge(source), Is.Null);
            source.TrySetResult();

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.That(GetCompletionSourceBridge(source), Is.Null);
            Assert.DoesNotThrow(() => combined.GetAwaiter().GetResult());
        }

        [Test]
        public void OnityTask_WhenAll_PooledInputCompletesLateAndIsConsumed()
        {
            (OnityTask task, object source, MethodInfo tick) = NewStandaloneFrameTask();
            OnityTask combined = OnityTask.WhenAll(OnityTask.Completed, task);

            Assert.That(combined.IsCompleted, Is.False);
            tick.Invoke(source, new object[] { 0f, 0f });

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.DoesNotThrow(() => combined.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void OnityTask_WhenAll_DisabledTrackerUsesTaskBackedOutputWithoutInputBridges()
        {
            bool previousTracking = OnityTaskTracker.IsEnabled;
            try
            {
                OnityTaskTracker.IsEnabled = false;
                OnityTaskCompletionSource first = new OnityTaskCompletionSource();
                OnityTaskCompletionSource second = new OnityTaskCompletionSource();
                OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);
                FieldInfo stateField = typeof(OnityTask).GetField(
                    "m_state", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(stateField, Is.Not.Null);
                object state = stateField.GetValue(combined);

                Assert.That(state, Is.InstanceOf<Task>());
                Assert.That(combined.AsTask(), Is.SameAs(state));
                Assert.That(GetCompletionSourceBridge(first), Is.Null);
                Assert.That(GetCompletionSourceBridge(second), Is.Null);
                first.TrySetResult();
                second.TrySetResult();
                Assert.DoesNotThrow(() => combined.GetAwaiter().GetResult());
                Assert.That(GetCompletionSourceBridge(first), Is.Null);
                Assert.That(GetCompletionSourceBridge(second), Is.Null);
            }
            finally
            {
                OnityTaskTracker.IsEnabled = previousTracking;
            }
        }

        [Test]
        public void OnityTask_WhenAll_TrackerKeepsOutputVisibleAndNativeAwaitWorks()
        {
            bool previousTracking = OnityTaskTracker.IsEnabled;
            try
            {
                OnityTaskTracker.IsEnabled = true;
                OnityTaskTracker.ClearAll();
                OnityTaskCompletionSource first = new OnityTaskCompletionSource();
                OnityTaskCompletionSource second = new OnityTaskCompletionSource();
                OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);
                List<OnityTrackedTaskInfo> rows = new List<OnityTrackedTaskInfo>();

                OnityTaskTracker.GetSnapshot(rows, includeCompleted: false);
                Assert.That(rows.Exists(row => row.Source == "OnityAsync.WhenAll"), Is.True);
                Assert.That(GetCompletionSourceBridge(first), Is.Null);
                Assert.That(GetCompletionSourceBridge(second), Is.Null);

                first.TrySetResult();
                second.TrySetResult();
                Assert.DoesNotThrow(() => combined.GetAwaiter().GetResult());
                Assert.That(combined.AsTask().IsCompletedSuccessfully, Is.True);
            }
            finally
            {
                OnityTaskTracker.ClearAll();
                OnityTaskTracker.IsEnabled = previousTracking;
            }
        }

        [Test]
        public async Task OnityTask_WhenAll_ConcurrentCompletionsDuringRegistration_CompleteOnce()
        {
            bool previousTracking = OnityTaskTracker.IsEnabled;
            try
            {
                OnityTaskTracker.IsEnabled = false;
                for (int i = 0; i < 64; i++)
                {
                    OnityTaskCompletionSource first = new OnityTaskCompletionSource();
                    OnityTaskCompletionSource second = new OnityTaskCompletionSource();
                    Task completingFirst = Task.Run(() => first.TrySetResult());
                    Task completingSecond = Task.Run(() => second.TrySetResult());
                    OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

                    await Task.WhenAll(completingFirst, completingSecond);
                    await combined;
                    Assert.That(combined.IsCompletedSuccessfully, Is.True);
                }
            }
            finally
            {
                OnityTaskTracker.IsEnabled = previousTracking;
            }
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

        private static object GetCompletionSourceBridge(OnityTaskCompletionSource source)
        {
            FieldInfo bridgeField = typeof(OnityTaskCompletionSource<bool>).GetField(
                "m_taskBridge", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(bridgeField, Is.Not.Null);
            return bridgeField.GetValue(source);
        }

        private static async Task<int> ObserveCompletionThread(OnityTask task)
        {
            await task;
            return Thread.CurrentThread.ManagedThreadId;
        }

        private static (OnityTask task, object source, MethodInfo tick) NewStandaloneFrameTask()
        {
            Type sourceType = typeof(OnityTask).Assembly.GetType(
                "Onity.Unity.Async.OnityFrameTaskSource", true);
            object source = Activator.CreateInstance(sourceType, true);
            MethodInfo reset = sourceType.BaseType.GetMethod(
                "Reset", BindingFlags.Instance | BindingFlags.NonPublic);
            reset.Invoke(source, new object[] { CancellationToken.None });

            ConstructorInfo taskConstructor = null;
            ConstructorInfo[] constructors = typeof(OnityTask).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(sourceType))
                {
                    taskConstructor = constructors[i];
                    break;
                }
            }

            Assert.That(taskConstructor, Is.Not.Null);
            MethodInfo tick = sourceType.GetMethod("Tick", BindingFlags.Instance | BindingFlags.Public);
            return ((OnityTask)taskConstructor.Invoke(new[] { source }), source, tick);
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

        private sealed class NoPumpSynchronizationContext : SynchronizationContext
        {
            private int m_postCount;

            public int PostCount => Volatile.Read(ref m_postCount);

            public override void Post(SendOrPostCallback callback, object state)
            {
                Interlocked.Increment(ref m_postCount);
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
