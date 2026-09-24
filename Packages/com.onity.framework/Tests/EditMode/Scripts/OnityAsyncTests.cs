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
        public void OnityTask_WhenAll_PendingNativeInputs_DoNotCreateInputTaskBridges()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            FieldInfo bridgeField = typeof(OnityTaskCompletionSource<bool>).GetField(
                "m_taskBridge", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(bridgeField, Is.Not.Null);

            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            Assert.That(bridgeField.GetValue(first), Is.Null);
            Assert.That(bridgeField.GetValue(second), Is.Null);
            first.TrySetResult();
            second.TrySetResult();
            Assert.That(combined.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void OnityTask_WhenAll_PendingShareableDuplicate_ObservesBothInputs()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(source.Task, source.Task);

            source.TrySetResult();

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.DoesNotThrow(() => combined.GetAwaiter().GetResult());
        }

        [Test]
        public void OnityTask_WhenAll_ReentrantCompletion_ReusesCoordinatorSafely()
        {
            for (int i = 0; i < 64; i++)
            {
                OnityTaskCompletionSource first = new OnityTaskCompletionSource();
                OnityTaskCompletionSource second = new OnityTaskCompletionSource();
                first.Task.GetAwaiter().OnCompleted(() => second.TrySetResult());
                OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

                first.TrySetResult();

                Assert.That(combined.IsCompletedSuccessfully, Is.True);
            }
        }

        [Test]
        public void OnityTask_WhenAll_CompletionContinuation_CanStartNestedPair()
        {
            for (int i = 0; i < 64; i++)
            {
                OnityTaskCompletionSource first = new OnityTaskCompletionSource();
                OnityTaskCompletionSource second = new OnityTaskCompletionSource();
                OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);
                Task continuation = combined.AsTask().ContinueWith(completed =>
                {
                    completed.GetAwaiter().GetResult();
                    OnityTaskCompletionSource nestedFirst = new OnityTaskCompletionSource();
                    OnityTaskCompletionSource nestedSecond = new OnityTaskCompletionSource();
                    OnityTask nested = OnityTask.WhenAll(nestedFirst.Task, nestedSecond.Task);
                    nestedFirst.TrySetResult();
                    nestedSecond.TrySetResult();
                    nested.GetAwaiter().GetResult();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                first.TrySetResult();
                second.TrySetResult();

                Assert.That(continuation.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(continuation.IsCompletedSuccessfully, Is.True);
            }
        }

        [Test]
        public void OnityTask_WhenAll_ConcurrentNativeCompletions_CompleteOnce()
        {
            for (int i = 0; i < 32; i++)
            {
                OnityTaskCompletionSource first = new OnityTaskCompletionSource();
                OnityTaskCompletionSource second = new OnityTaskCompletionSource();
                OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);
                Task firstCompletion = Task.Run(() => first.TrySetResult());
                Task secondCompletion = Task.Run(() => second.TrySetResult());

                Assert.That(
                    Task.WaitAll(new[] { firstCompletion, secondCompletion },
                        TimeSpan.FromSeconds(5)),
                    Is.True);
                Assert.That(combined.AsTask().Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(combined.IsCompletedSuccessfully, Is.True);
            }
        }

        [Test]
        public void OnityTask_WhenAll_CompletionDuringRegistration_ResolvesEveryOutput()
        {
            for (int i = 0; i < 128; i++)
            {
                using ManualResetEventSlim start = new ManualResetEventSlim(false);
                OnityTaskCompletionSource first = new OnityTaskCompletionSource();
                OnityTaskCompletionSource second = new OnityTaskCompletionSource();
                Task producer = Task.Run(() =>
                {
                    start.Wait();
                    first.TrySetResult();
                    second.TrySetResult();
                });

                start.Set();
                OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

                Assert.That(producer.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(combined.AsTask().Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(combined.IsCompletedSuccessfully, Is.True);
            }
        }

        [Test]
        public void OnityTask_WhenAll_OutputRemainsShareableAfterCoordinatorReuse()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);
            Task output = combined.AsTask();

            first.TrySetResult();
            second.TrySetResult();
            for (int i = 0; i < 64; i++)
            {
                OnityTaskCompletionSource next = new OnityTaskCompletionSource();
                OnityTask another = OnityTask.WhenAll(next.Task, OnityTask.Completed);
                next.TrySetResult();
                Assert.That(another.IsCompletedSuccessfully, Is.True);
            }

            Assert.That(combined.AsTask(), Is.SameAs(output));
            Assert.DoesNotThrow(() => combined.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => combined.GetAwaiter().GetResult());
        }

        [Test]
        public void OnityTask_WhenAll_AbovePoolLimit_KeepsOutstandingOutputsSeparate()
        {
            const int k_operationCount = 300;
            OnityTaskCompletionSource[] first = new OnityTaskCompletionSource[k_operationCount];
            OnityTaskCompletionSource[] second = new OnityTaskCompletionSource[k_operationCount];
            OnityTask[] outputs = new OnityTask[k_operationCount];

            for (int i = 0; i < k_operationCount; i++)
            {
                first[i] = new OnityTaskCompletionSource();
                second[i] = new OnityTaskCompletionSource();
                outputs[i] = OnityTask.WhenAll(first[i].Task, second[i].Task);
            }

            for (int i = k_operationCount - 1; i >= 0; i--)
            {
                first[i].TrySetResult();
                second[i].TrySetResult();
                Assert.That(outputs[i].IsCompletedSuccessfully, Is.True);
            }

            for (int i = 0; i < k_operationCount; i++)
            {
                Assert.DoesNotThrow(() => outputs[i].GetAwaiter().GetResult());
            }
        }

        [Test]
        public void OnityTask_WhenAll_FaultedOperationCanceledException_RemainsFaulted()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OperationCanceledException failure =
                new OperationCanceledException("faulted cancellation exception");
            MethodInfo setFault = typeof(OnityTaskCompletionSource<bool>).GetMethod(
                "TrySetFault", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setFault, Is.Not.Null);
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            setFault.Invoke(first, new object[] { failure });
            second.TrySetResult();

            Assert.That(first.Task.IsFaulted, Is.True);
            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(combined.AsTask().Exception.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void OnityTask_WhenAll_PendingNativeFaults_AggregateInInputOrder()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            Exception firstFailure = new InvalidOperationException("first failure");
            Exception secondFailure = new ArgumentException("second failure");
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            second.TrySetException(secondFailure);
            first.TrySetException(firstFailure);

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(combined.AsTask().Exception.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(combined.AsTask().Exception.InnerExceptions[0], Is.SameAs(firstFailure));
            Assert.That(combined.AsTask().Exception.InnerExceptions[1], Is.SameAs(secondFailure));
        }

        [Test]
        public void OnityTask_WhenAll_PendingNativeFault_PreservesOriginAndObservesInput()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(source.Task, OnityTask.Completed);
            Exception failure = CreateWhenAllFaultAtOrigin();

            source.TrySetException(failure);

            Exception observed = Assert.Throws<InvalidOperationException>(
                () => combined.GetAwaiter().GetResult());
            Assert.That(observed, Is.SameAs(failure));
            Assert.That(observed.StackTrace, Does.Contain(nameof(CreateWhenAllFaultAtOrigin)));
            FieldInfo faultField = typeof(OnityTaskCompletionSource<bool>).GetField(
                "m_unobservedFault", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(faultField, Is.Not.Null);
            object unobservedFault = faultField.GetValue(source);
            Assert.That(unobservedFault, Is.Not.Null);
            FieldInfo observedField = unobservedFault.GetType().GetField(
                "m_observed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(observedField, Is.Not.Null);
            Assert.That(observedField.GetValue(unobservedFault), Is.EqualTo(1));
        }

        [Test]
        public void OnityTask_WhenAll_MaterializedInputFault_ObservesTaskBridgeLikeTaskWhenAll()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            Task bridge = source.Task.AsTask();
            OnityTask combined = OnityTask.WhenAll(source.Task, OnityTask.Completed);
            TaskCompletionSource<bool> referenceSource = new TaskCompletionSource<bool>();
            Task reference = Task.WhenAll(referenceSource.Task, Task.CompletedTask);

            source.TrySetException(new InvalidOperationException("Onity bridge fault"));
            referenceSource.TrySetException(new InvalidOperationException("reference fault"));

            Assert.That(SpinWait.SpinUntil(() => combined.IsCompleted,
                TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(SpinWait.SpinUntil(() => reference.IsCompleted,
                TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(reference.IsFaulted, Is.True);
            bool bridgeObserved = IsTaskFaultObserved(bridge);
            bool referenceObserved = IsTaskFaultObserved(referenceSource.Task);
            TestContext.WriteLine(
                "Materialized input fault observed: Onity={0}, Task.WhenAll={1}",
                bridgeObserved, referenceObserved);
            Assert.That(bridgeObserved, Is.EqualTo(referenceObserved));
        }

        [Test]
        public void OnityTask_WhenAll_ConcurrentMaterialization_ObservesSharedTaskBridge()
        {
            const int k_readerCount = 16;
            using ManualResetEventSlim start = new ManualResetEventSlim(false);
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(source.Task, OnityTask.Completed);
            Task[] bridges = new Task[k_readerCount];
            Task[] readers = new Task[k_readerCount];

            for (int i = 0; i < k_readerCount; i++)
            {
                int index = i;
                readers[i] = Task.Run(() =>
                {
                    start.Wait();
                    bridges[index] = source.Task.AsTask();
                });
            }

            start.Set();
            Assert.That(Task.WaitAll(readers, TimeSpan.FromSeconds(5)), Is.True);
            for (int i = 1; i < bridges.Length; i++)
            {
                Assert.That(bridges[i], Is.SameAs(bridges[0]));
            }

            source.TrySetException(new InvalidOperationException("shared bridge fault"));

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(IsTaskFaultObserved(bridges[0]), Is.True);
        }

        [Test]
        public void OnityTask_WhenAll_LateMaterializedFault_ObservesTaskBridge()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(source.Task, OnityTask.Completed);
            source.TrySetException(new InvalidOperationException("late bridge fault"));
            Assert.That(combined.IsFaulted, Is.True);

            Task bridge = source.Task.AsTask();
            Assert.That(bridge.IsFaulted, Is.True);
            Assert.That(IsTaskFaultObserved(bridge), Is.True);
            Assert.Throws<InvalidOperationException>(() => combined.GetAwaiter().GetResult());

            OnityTaskCompletionSource bareSource = new OnityTaskCompletionSource();
            bareSource.TrySetException(new InvalidOperationException("bare bridge fault"));
            Task bareBridge = bareSource.Task.AsTask();
            Assert.That(bareBridge.IsFaulted, Is.True);
            Assert.That(IsTaskFaultObserved(bareBridge), Is.False,
                "An AsTask bridge without WhenAll still belongs to its own consumer.");
            Assert.Throws<InvalidOperationException>(() => bareSource.Task.GetAwaiter().GetResult());
            _ = bareBridge.Exception;
        }

        [Test]
        public void OnityTask_WhenAll_PendingNestedAggregateFaults_KeepInputOrder()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            AggregateException firstFailure = new AggregateException(
                new InvalidOperationException("inner first"));
            AggregateException secondFailure = new AggregateException(
                new ArgumentException("inner second"));
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            second.TrySetException(secondFailure);
            first.TrySetException(firstFailure);

            AggregateException output = combined.AsTask().Exception;
            Assert.That(output.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(output.InnerExceptions[0], Is.SameAs(firstFailure));
            Assert.That(output.InnerExceptions[1], Is.SameAs(secondFailure));
        }

        [Test]
        public void OnityTask_WhenAll_PendingNativeCancellation_PreservesFirstInputToken()
        {
            using CancellationTokenSource firstCancellation = new CancellationTokenSource();
            using CancellationTokenSource secondCancellation = new CancellationTokenSource();
            firstCancellation.Cancel();
            secondCancellation.Cancel();
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

            second.TrySetCanceled(secondCancellation.Token);
            first.TrySetCanceled(firstCancellation.Token);

            Assert.That(combined.IsCanceled, Is.True);
            OperationCanceledException observed = Assert.Catch<OperationCanceledException>(
                () => combined.GetAwaiter().GetResult());
            TaskCompletionSource<bool> firstReference =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> secondReference =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task reference = Task.WhenAll(firstReference.Task, secondReference.Task);
            secondReference.TrySetCanceled(secondCancellation.Token);
            firstReference.TrySetCanceled(firstCancellation.Token);
            OperationCanceledException referenceObserved =
                Assert.Catch<OperationCanceledException>(
                    () => reference.GetAwaiter().GetResult());

            Assert.That(observed.GetType(), Is.EqualTo(referenceObserved.GetType()));
            Assert.That(observed.CancellationToken, Is.EqualTo(referenceObserved.CancellationToken));
            Assert.That(observed.CancellationToken, Is.EqualTo(firstCancellation.Token));
            TestContext.WriteLine(
                "Cancellation type: Onity={0}, Task.WhenAll={1}",
                observed.GetType().Name,
                referenceObserved.GetType().Name);
        }

        [Test]
        public void OnityTask_WhenAll_TaskBackedInput_KeepsExistingBridgeFallback()
        {
            OnityTaskCompletionSource native = new OnityTaskCompletionSource();
            TaskCompletionSource<bool> dotNet = new TaskCompletionSource<bool>();
            FieldInfo bridgeField = typeof(OnityTaskCompletionSource<bool>).GetField(
                "m_taskBridge", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(bridgeField, Is.Not.Null);

            OnityTask combined = OnityTask.WhenAll(native.Task, OnityTask.FromTask(dotNet.Task));

            Assert.That(bridgeField.GetValue(native), Is.Not.Null);
            native.TrySetResult();
            dotNet.TrySetResult(true);
            Assert.That(combined.AsTask().Wait(TimeSpan.FromSeconds(5)), Is.True);
        }

        [TestCase(PrebridgedInputs.First, PairOutcome.Success)]
        [TestCase(PrebridgedInputs.Second, PairOutcome.Success)]
        [TestCase(PrebridgedInputs.Both, PairOutcome.Success)]
        [TestCase(PrebridgedInputs.First, PairOutcome.Fault)]
        [TestCase(PrebridgedInputs.Second, PairOutcome.Fault)]
        [TestCase(PrebridgedInputs.Both, PairOutcome.Fault)]
        [TestCase(PrebridgedInputs.First, PairOutcome.Cancellation)]
        [TestCase(PrebridgedInputs.Second, PairOutcome.Cancellation)]
        [TestCase(PrebridgedInputs.Both, PairOutcome.Cancellation)]
        public void OnityTask_WhenAll_PrebridgedInputs_UseTaskFallback(
            PrebridgedInputs prebridged, PairOutcome outcome)
        {
            using CancellationTokenSource firstCancellation = new CancellationTokenSource();
            using CancellationTokenSource secondCancellation = new CancellationTokenSource();
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            Task firstPrebridge = (prebridged & PrebridgedInputs.First) != 0
                ? first.Task.AsTask()
                : null;
            Task secondPrebridge = (prebridged & PrebridgedInputs.Second) != 0
                ? second.Task.AsTask()
                : null;

            OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);
            Task output = combined.AsTask();
            Task firstBridge = first.Task.AsTask();
            Task secondBridge = second.Task.AsTask();
            Assert.That(output, Is.Not.InstanceOf<Task<bool>>(),
                "A preexisting input bridge must select the Task.WhenAll fallback.");
            Assert.That(output.IsCompleted, Is.False);
            if (firstPrebridge != null)
            {
                Assert.That(firstBridge, Is.SameAs(firstPrebridge));
            }

            if (secondPrebridge != null)
            {
                Assert.That(secondBridge, Is.SameAs(secondPrebridge));
            }

            if (outcome == PairOutcome.Success)
            {
                second.TrySetResult();
                Assert.That(output.IsCompleted, Is.False);
                first.TrySetResult();
                Assert.That(SpinWait.SpinUntil(() => output.IsCompleted,
                    TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(output.IsCompletedSuccessfully, Is.True);
                Assert.DoesNotThrow(() => combined.GetAwaiter().GetResult());
                Assert.DoesNotThrow(() => combined.GetAwaiter().GetResult());
            }
            else if (outcome == PairOutcome.Fault)
            {
                Exception firstFailure = new InvalidOperationException("first bridge fault");
                Exception secondFailure = new ArgumentException("second bridge fault");
                second.TrySetException(secondFailure);
                first.TrySetException(firstFailure);

                Assert.That(SpinWait.SpinUntil(() => output.IsCompleted,
                    TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(output.IsFaulted, Is.True);
                Assert.That(IsTaskFaultObserved(firstBridge), Is.True);
                Assert.That(IsTaskFaultObserved(secondBridge), Is.True);
                Assert.That(output.Exception.InnerExceptions.Count, Is.EqualTo(2));
                Assert.That(output.Exception.InnerExceptions[0], Is.SameAs(firstFailure));
                Assert.That(output.Exception.InnerExceptions[1], Is.SameAs(secondFailure));
                Assert.That(Assert.Throws<InvalidOperationException>(
                    () => combined.GetAwaiter().GetResult()), Is.SameAs(firstFailure));
                Assert.That(Assert.Throws<InvalidOperationException>(
                    () => combined.GetAwaiter().GetResult()), Is.SameAs(firstFailure));
            }
            else
            {
                firstCancellation.Cancel();
                secondCancellation.Cancel();
                second.TrySetCanceled(secondCancellation.Token);
                first.TrySetCanceled(firstCancellation.Token);

                Assert.That(SpinWait.SpinUntil(() => output.IsCompleted,
                    TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(output.IsCanceled, Is.True);
                OperationCanceledException observed = Assert.Catch<OperationCanceledException>(
                    () => combined.GetAwaiter().GetResult());
                Assert.That(observed.CancellationToken, Is.EqualTo(firstCancellation.Token));
                Assert.That(Assert.Catch<OperationCanceledException>(
                    () => combined.GetAwaiter().GetResult()).CancellationToken,
                    Is.EqualTo(firstCancellation.Token));
            }

            Assert.That(combined.AsTask(), Is.SameAs(output));
        }

        [Test]
        public void OnityTask_WhenAll_BridgeCreationDuringRouteSelection_PreservesFaultObservation()
        {
            for (int i = 0; i < 64; i++)
            {
                using ManualResetEventSlim start = new ManualResetEventSlim(false);
                OnityTaskCompletionSource source = new OnityTaskCompletionSource();
                Task bridge = null;
                Task creator = Task.Run(() =>
                {
                    start.Wait();
                    bridge = source.Task.AsTask();
                });

                start.Set();
                OnityTask combined = OnityTask.WhenAll(source.Task, OnityTask.Completed);
                Assert.That(creator.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Exception failure = new InvalidOperationException("route selection fault");
                source.TrySetException(failure);

                Assert.That(SpinWait.SpinUntil(() => combined.IsCompleted,
                    TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(combined.IsFaulted, Is.True);
                Assert.That(IsTaskFaultObserved(bridge), Is.True);
                Assert.That(Assert.Throws<InvalidOperationException>(
                    () => combined.GetAwaiter().GetResult()), Is.SameAs(failure));
            }
        }

        [Test]
        public void OnityTask_WhenAll_PendingNativeOutput_PreservesAwaiterExecutionContext()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTask combined = OnityTask.WhenAll(source.Task, OnityTask.Completed);
            TaskCompletionSource<bool> referenceSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task reference = Task.WhenAll(referenceSource.Task, Task.CompletedTask);

            string onityObserved = ObserveExecutionContext(
                combined.AsTask().GetAwaiter(), () => source.TrySetResult(), true);
            string referenceObserved = ObserveExecutionContext(
                reference.GetAwaiter(), () => referenceSource.TrySetResult(true), true);

            Assert.That(onityObserved, Is.EqualTo(referenceObserved));
            TestContext.WriteLine(
                "Awaiter context: Onity={0}, Task.WhenAll={1}",
                onityObserved ?? "<null>",
                referenceObserved ?? "<null>");
        }

        [Test]
        public void OnityTask_WhenAll_PendingFault_TrackerPreservesRegistrationContext()
        {
            bool previousTrackingEnabled = OnityTaskTracker.IsEnabled;
            bool previousStackTraceEnabled = OnityTaskTracker.EnableStackTrace;
            AsyncLocal<string> context = new AsyncLocal<string>();

            try
            {
                OnityTaskTracker.IsEnabled = true;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTaskTracker.ClearAll();
                context.Value = "registration";

                OnityTaskCompletionSource first = new OnityTaskCompletionSource();
                OnityTaskCompletionSource second = new OnityTaskCompletionSource();
                OnityTask combined = OnityTask.WhenAll(first.Task, second.Task);

                context.Value = "completion";
                first.TrySetException(new ContextMessageException(context));
                second.TrySetResult();

                List<OnityTrackedTaskInfo> rows = new List<OnityTrackedTaskInfo>(1);
                Assert.That(SpinWait.SpinUntil(() =>
                {
                    OnityTaskTracker.GetSnapshot(rows);
                    return rows.Count == 1 && rows[0].IsCompleted;
                }, TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(combined.IsFaulted, Is.True);
                Assert.That(rows[0].Source, Is.EqualTo("OnityAsync.WhenAll"));
                Assert.That(rows[0].ErrorMessage, Is.EqualTo("registration"));
                Assert.That(context.Value, Is.EqualTo("completion"));
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
        public void OnityTask_WhenAll_AlreadySuppressedFlow_RemainsSuppressedDuringCreation()
        {
            Assert.That(ExecutionContext.IsFlowSuppressed(), Is.False);
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask combined;
            AsyncFlowControl flowControl = ExecutionContext.SuppressFlow();
            try
            {
                combined = OnityTask.WhenAll(first.Task, second.Task);
                Assert.That(ExecutionContext.IsFlowSuppressed(), Is.True);
            }
            finally
            {
                flowControl.Undo();
            }

            Assert.That(ExecutionContext.IsFlowSuppressed(), Is.False);
            first.TrySetResult();
            second.TrySetResult();
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

        [TestCase(0, 11)]
        [TestCase(1, 22)]
        public void OnityTask_WhenAnyTyped_PendingWinner_ReturnsMatchingIndexAndValue(
            int winnerIndex, int expectedValue)
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> race =
                OnityTask.WhenAny(first.Task, second.Task);

            Assert.That(race.IsCompleted, Is.False);
            if (winnerIndex == 0)
            {
                first.TrySetResult(expectedValue);
            }
            else
            {
                second.TrySetResult(expectedValue);
            }

            Assert.That(race.IsCompletedSuccessfully, Is.True);
            (int index, int value) result = race.GetAwaiter().GetResult();
            Assert.That(result.index, Is.EqualTo(winnerIndex));
            Assert.That(result.value, Is.EqualTo(expectedValue));

            if (winnerIndex == 0)
            {
                second.TrySetResult(22);
            }
            else
            {
                first.TrySetResult(11);
            }
        }

        [Test]
        public void OnityTask_WhenAnyTyped_AlreadyCompletedInputs_FirstWinsTie()
        {
            OnityTask<(int winnerIndex, int result)> race = OnityTask.WhenAny(
                OnityTask<int>.FromResult(11),
                OnityTask<int>.FromResult(22));

            Assert.That(race.IsCompletedSuccessfully, Is.True);
            (int index, int value) result = race.GetAwaiter().GetResult();
            Assert.That(result.index, Is.Zero);
            Assert.That(result.value, Is.EqualTo(11));
        }

        [Test]
        public void OnityTask_WhenAnyTyped_DefaultAndReferenceResults_PreserveWinnerValue()
        {
            OnityTask<(int winnerIndex, int result)> defaultRace = OnityTask.WhenAny(
                OnityTask<int>.FromResult(0),
                OnityTask<int>.FromResult(22));
            object expected = new object();
            OnityTask<(int winnerIndex, object result)> referenceRace = OnityTask.WhenAny(
                OnityTask<object>.FromResult(null),
                OnityTask<object>.FromResult(expected));

            (int defaultIndex, int defaultValue) = defaultRace.GetAwaiter().GetResult();
            (int referenceIndex, object referenceValue) = referenceRace.GetAwaiter().GetResult();
            Assert.That(defaultIndex, Is.Zero);
            Assert.That(defaultValue, Is.Zero);
            Assert.That(referenceIndex, Is.Zero);
            Assert.That(referenceValue, Is.Null);

            OnityTaskCompletionSource<object> pending = new OnityTaskCompletionSource<object>();
            OnityTask<(int winnerIndex, object result)> pendingReference = OnityTask.WhenAny(
                pending.Task,
                OnityTask<object>.FromResult(expected));
            (int pendingIndex, object pendingValue) = pendingReference.GetAwaiter().GetResult();
            Assert.That(pendingIndex, Is.EqualTo(1));
            Assert.That(pendingValue, Is.SameAs(expected));
            pending.TrySetResult(null);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void OnityTask_WhenAnyTyped_WinnerFault_PreservesException(int winnerIndex)
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
            InvalidOperationException failure = new InvalidOperationException("winner failed");
            OnityTask<(int winnerIndex, int result)> race =
                OnityTask.WhenAny(first.Task, second.Task);

            if (winnerIndex == 0)
            {
                first.TrySetException(failure);
            }
            else
            {
                second.TrySetException(failure);
            }

            Assert.That(race.IsFaulted, Is.True);
            Assert.That(race.IsCanceled, Is.False);
            Assert.That(Assert.Throws<InvalidOperationException>(
                () => race.GetAwaiter().GetResult()), Is.SameAs(failure));
            (winnerIndex == 0 ? second : first).TrySetResult(99);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void OnityTask_WhenAnyTyped_WinnerCancellation_PreservesToken(int winnerIndex)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> race =
                OnityTask.WhenAny(first.Task, second.Task);

            (winnerIndex == 0 ? first : second).TrySetCanceled(cancellation.Token);

            Assert.That(race.IsCanceled, Is.True);
            Assert.That(race.IsFaulted, Is.False);
            OperationCanceledException caught = Assert.Catch<OperationCanceledException>(
                () => race.GetAwaiter().GetResult());
            Assert.That(caught.CancellationToken, Is.EqualTo(cancellation.Token));
            (winnerIndex == 0 ? second : first).TrySetResult(99);
        }

        [Test]
        public void OnityTask_WhenAnyTyped_FaultedOperationCanceledException_RemainsFaulted()
        {
            OperationCanceledException failure =
                new OperationCanceledException("faulted input");
            OnityTask<(int winnerIndex, int result)> race = OnityTask.WhenAny(
                OnityTask<int>.FromException(failure),
                OnityTask<int>.FromResult(22));

            Assert.That(race.IsFaulted, Is.True);
            Assert.That(race.IsCanceled, Is.False);
            Task<(int winnerIndex, int result)> bridge = race.AsTask();
            Assert.That(bridge.IsFaulted, Is.True);
            Assert.That(bridge.IsCanceled, Is.False);
            Assert.That(bridge.Exception.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void OnityTask_WhenAnyTyped_LosingFaultAndCancellation_DoNotChangeWinner()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int> faultingLoser = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> faultRace = OnityTask.WhenAny(
                OnityTask<int>.FromResult(11), faultingLoser.Task);
            Task<(int winnerIndex, int result)> faultBridge = faultRace.AsTask();
            Assert.That(faultBridge.GetAwaiter().GetResult(), Is.EqualTo((0, 11)));
            faultingLoser.TrySetException(new InvalidOperationException("loser failed"));
            Assert.That(faultBridge.IsCompletedSuccessfully, Is.True);
            Assert.That(faultBridge.Result, Is.EqualTo((0, 11)));

            OnityTaskCompletionSource<int> cancelingLoser = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> cancelRace = OnityTask.WhenAny(
                OnityTask<int>.FromResult(33), cancelingLoser.Task);
            Task<(int winnerIndex, int result)> cancelBridge = cancelRace.AsTask();
            Assert.That(cancelBridge.GetAwaiter().GetResult(), Is.EqualTo((0, 33)));
            cancelingLoser.TrySetCanceled(cancellation.Token);
            Assert.That(cancelBridge.IsCompletedSuccessfully, Is.True);
            Assert.That(cancelBridge.Result, Is.EqualTo((0, 33)));
        }

        [Test]
        public void OnityTask_WhenAnyTyped_LosingFault_AfterNativeResultOrPreserve_IsHarmless()
        {
            OnityTaskCompletionSource<int> nativeLoser = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> nativeRace = OnityTask.WhenAny(
                OnityTask<int>.FromResult(11), nativeLoser.Task);
            Assert.That(nativeRace.GetAwaiter().GetResult(), Is.EqualTo((0, 11)));
            Assert.That(nativeLoser.TrySetException(
                new InvalidOperationException("late native loser")), Is.True);

            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> preservedLoser = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> shared =
                OnityTask.WhenAny(first.Task, preservedLoser.Task).Preserve();
            first.TrySetResult(33);
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo((0, 33)));
            Assert.That(preservedLoser.TrySetException(
                new InvalidOperationException("late preserved loser")), Is.True);
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo((0, 33)));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void OnityTask_WhenAnyTyped_LateSingleConsumerLoser_IsConsumedOnce(
            bool materializeWinner, bool loserFaults)
        {
            OnityTaskCompletionSource innerFirst = new OnityTaskCompletionSource();
            OnityTaskCompletionSource innerSecond = new OnityTaskCompletionSource();
            OnityTask<int> nativeLoser = OnityTask.WhenAny(
                innerFirst.Task, innerSecond.Task);
            OnityTaskCompletionSource<int> winner = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> outer =
                OnityTask.WhenAny(winner.Task, nativeLoser);
            Task<(int winnerIndex, int result)> bridge =
                materializeWinner ? outer.AsTask() : null;

            Assert.That(winner.TrySetResult(42), Is.True);
            if (materializeWinner)
            {
                Assert.That(bridge.GetAwaiter().GetResult(), Is.EqualTo((0, 42)));
            }
            else
            {
                Assert.That(outer.GetAwaiter().GetResult(), Is.EqualTo((0, 42)));
            }

            if (loserFaults)
            {
                Assert.That(innerFirst.TrySetException(
                    new InvalidOperationException("late native loser")), Is.True);
            }
            else
            {
                Assert.That(innerFirst.TrySetResult(), Is.True);
            }

            Assert.That(nativeLoser.IsCompleted, Is.True);
            Assert.Throws<InvalidOperationException>(
                () => nativeLoser.GetAwaiter().GetResult());
            Assert.That(outer.IsCompletedSuccessfully, Is.True);
            if (materializeWinner)
            {
                Assert.That(bridge.GetAwaiter().GetResult(), Is.EqualTo((0, 42)));
            }
            else
            {
                Assert.Throws<InvalidOperationException>(
                    () => outer.GetAwaiter().GetResult());
            }

            Assert.That(innerSecond.TrySetResult(), Is.True);
        }

        [Test]
        public void OnityTask_WhenAnyTyped_DuplicateNativeInput_IsRejectedWithoutConsumingIt()
        {
            OnityTask<int> native =
                OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);

            Assert.Throws<ArgumentException>(() => OnityTask.WhenAny(native, native));
            Assert.That(native.GetAwaiter().GetResult(), Is.Zero);
        }

        [Test]
        public void OnityTask_WhenAnyTyped_DuplicateShareableInput_FirstWinsTie()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> race =
                OnityTask.WhenAny(source.Task, source.Task);

            source.TrySetResult(17);

            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((0, 17)));
        }

        [Test]
        public async Task OnityTask_WhenAnyTyped_Preserve_SupportsPendingAndLateConsumers()
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> original =
                OnityTask.WhenAny(first.Task, second.Task);
            OnityTask<(int winnerIndex, int result)> shared = original.Preserve();
            Task<(int winnerIndex, int result)> firstObserver = ObserveTypedRace(shared);
            Task<(int winnerIndex, int result)> secondObserver = ObserveTypedRace(shared);
            Task<(int winnerIndex, int result)> bridge = shared.AsTask();

            Assert.That(firstObserver.IsCompleted, Is.False);
            Assert.That(secondObserver.IsCompleted, Is.False);
            Assert.That(shared.AsTask(), Is.SameAs(bridge));
            Assert.Throws<InvalidOperationException>(() => original.AsTask());
            second.TrySetResult(22);

            (int winnerIndex, int result)[] observed = await Task.WhenAll(
                firstObserver, secondObserver, bridge);
            Assert.That(observed, Is.All.EqualTo((1, 22)));
            Assert.That(await ObserveTypedRace(shared), Is.EqualTo((1, 22)));
            Assert.That(shared.AsTask(), Is.SameAs(bridge));
            first.TrySetResult(11);
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo((1, 22)));
        }

        [Test]
        public void OnityTask_WhenAnyTyped_AsTaskBridgeSurvivesLateLoser()
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
            OnityTask<(int winnerIndex, int result)> race =
                OnityTask.WhenAny(first.Task, second.Task);
            Task<(int winnerIndex, int result)> bridge = race.AsTask();

            Assert.That(race.AsTask(), Is.SameAs(bridge));
            second.TrySetResult(22);
            Assert.That(bridge.GetAwaiter().GetResult(), Is.EqualTo((1, 22)));
            first.TrySetResult(11);
            Assert.That(bridge.GetAwaiter().GetResult(), Is.EqualTo((1, 22)));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void OnityTask_WhenAnyTyped_StaleInput_FaultsDuringRegistration(
            int staleIndex)
        {
            OnityTask<int> stale =
                OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);
            FieldInfo stateField = typeof(OnityTask<int>).GetField(
                "m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(stateField, Is.Not.Null);
            object source = stateField.GetValue(stale);
            MethodInfo reset = source.GetType().BaseType.GetMethod(
                "Reset", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(reset, Is.Not.Null);
            reset.Invoke(source, new object[] { CancellationToken.None });
            OnityTaskCompletionSource otherFirst = new OnityTaskCompletionSource();
            OnityTaskCompletionSource otherSecond = new OnityTaskCompletionSource();
            OnityTask<int> other = OnityTask.WhenAny(otherFirst.Task, otherSecond.Task);
            OnityTask<(int winnerIndex, int result)> race = staleIndex == 0
                ? OnityTask.WhenAny(stale, other)
                : OnityTask.WhenAny(other, stale);

            Assert.That(race.IsFaulted, Is.True);
            Assert.Throws<InvalidOperationException>(() => race.GetAwaiter().GetResult());
            Assert.That(otherFirst.TrySetResult(), Is.True);
            Assert.Throws<InvalidOperationException>(() => other.GetAwaiter().GetResult());
            Assert.That(otherSecond.TrySetResult(), Is.True);
        }

        [Test]
        public async Task OnityTask_WhenAnyTyped_ConcurrentCompletions_KeepIndexAndValueTogether()
        {
            for (int i = 0; i < 32; i++)
            {
                TaskCompletionSource<int> first =
                    new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<int> second =
                    new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                OnityTask<(int winnerIndex, int result)> race = OnityTask.WhenAny(
                    OnityTask<int>.FromTask(first.Task),
                    OnityTask<int>.FromTask(second.Task));
                Task<(int winnerIndex, int result)> bridge = race.AsTask();

                await Task.WhenAll(
                    Task.Run(() => first.SetResult(11)),
                    Task.Run(() => second.SetResult(22)));
                Assert.That(await Task.WhenAny(bridge, Task.Delay(5000)), Is.SameAs(bridge));
                (int winnerIndex, int result) = await bridge;
                Assert.That(winnerIndex, Is.InRange(0, 1));
                Assert.That(result, Is.EqualTo(winnerIndex == 0 ? 11 : 22));
            }
        }

        [Test]
        public async Task OnityTask_WhenAnyTyped_CompletionDuringRegistration_ResolvesWinner()
        {
            for (int i = 0; i < 32; i++)
            {
                TaskCompletionSource<int> first =
                    new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                using ManualResetEventSlim start = new ManualResetEventSlim(false);
                Task producer = Task.Run(() =>
                {
                    start.Wait();
                    first.SetResult(11);
                });
                start.Set();
                OnityTask<(int winnerIndex, int result)> race = OnityTask.WhenAny(
                    OnityTask<int>.FromTask(first.Task),
                    OnityTask<int>.FromResult(22));
                Task<(int winnerIndex, int result)> bridge = race.AsTask();

                Assert.That(await Task.WhenAny(bridge, Task.Delay(5000)), Is.SameAs(bridge));
                (int winnerIndex, int result) = await bridge;
                Assert.That(result, Is.EqualTo(winnerIndex == 0 ? 11 : 22));
                await producer;
            }
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

        private static async Task<(int winnerIndex, int result)> ObserveTypedRace(
            OnityTask<(int winnerIndex, int result)> task)
        {
            return await task;
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateWhenAllFaultAtOrigin()
        {
            try
            {
                throw new InvalidOperationException("original fault");
            }
            catch (InvalidOperationException exception)
            {
                return exception;
            }
        }

        private static bool IsTaskFaultObserved(Task task)
        {
            const BindingFlags k_privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo contingentField = typeof(Task).GetField(
                "m_contingentProperties", k_privateInstance);
            Assert.That(contingentField, Is.Not.Null);
            object contingent = contingentField.GetValue(task);
            Assert.That(contingent, Is.Not.Null);
            FieldInfo holderField = contingent.GetType().GetField(
                "m_exceptionsHolder", k_privateInstance);
            Assert.That(holderField, Is.Not.Null);
            object holder = holderField.GetValue(contingent);
            Assert.That(holder, Is.Not.Null);
            FieldInfo handledField = holder.GetType().GetField(
                "m_isHandled", k_privateInstance);
            Assert.That(handledField, Is.Not.Null);
            return (bool)handledField.GetValue(holder);
        }

        public enum PrebridgedInputs
        {
            First = 1,
            Second = 2,
            Both = First | Second
        }

        public enum PairOutcome
        {
            Success,
            Fault,
            Cancellation
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
