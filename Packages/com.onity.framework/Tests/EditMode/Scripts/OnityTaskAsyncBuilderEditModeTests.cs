using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the pooled native builder behind suspended async OnityTask methods: representation,
    /// single-consumer rules, exception and cancellation mapping, pool reuse, execution-context
    /// flow, Forget, and two-input WhenAll.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskAsyncBuilderEditModeTests
    {
        private bool m_previousFlow;
        private bool m_previousTracking;

        [SetUp]
        public void SaveSettings()
        {
            m_previousFlow = OnityTask.FlowExecutionContext;
            m_previousTracking = OnityTaskTracker.IsEnabled;
        }

        [TearDown]
        public void RestoreSettings()
        {
            OnityTask.FlowExecutionContext = m_previousFlow;
            OnityTaskTracker.IsEnabled = m_previousTracking;
        }

        [Test]
        public void SuspendedUntypedMethod_IsRunnerBacked_AndStaleAfterConsumption()
        {
            ManualAwaitable gate = new ManualAwaitable();
            OnityTask task = CompleteAfterAsync(gate);
            object state = GetState(task);

            Assert.That(state, Is.Not.Null);
            Assert.That(state, Is.Not.InstanceOf<Task>());
            Assert.That(state.GetType().Name, Does.StartWith("OnityAsyncStateMachineRunner"));
            Assert.That(task.IsCompleted, Is.False);

            gate.Complete();

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => _ = task.IsCompleted,
                "A consumed runner-backed task must reject its stale token.");
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void SuspendedTypedMethod_ReturnsResultThroughRunner()
        {
            ManualAwaitable gate = new ManualAwaitable();
            OnityTask<int> task = ReturnAfterAsync(gate, 17);

            Assert.That(GetState(task), Is.Not.InstanceOf<Task>());
            Assert.That(task.IsCompleted, Is.False);

            gate.Complete();

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(17));
            Assert.Throws<InvalidOperationException>(() => _ = task.IsCompleted);
        }

        [Test]
        public void SafeAwaiterSuspension_IsRunnerBacked()
        {
            SafeManualAwaitable gate = new SafeManualAwaitable();
            OnityTask<int> task = ReturnAfterSafeAsync(gate, 5);

            Assert.That(GetState(task), Is.Not.InstanceOf<Task>());

            gate.Complete();

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(5));
        }

        [Test]
        public void SynchronousCancellation_WithUncanceledToken_IsCanceled()
        {
            OnityTask<int> task = CancelSynchronouslyWithoutTokenAsync();

            Assert.That(task.IsCanceled, Is.True);
            Assert.That(task.IsFaulted, Is.False);
            Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void SuspendedException_IsFaulted_AndRethrowsTheSameInstance()
        {
            ManualAwaitable gate = new ManualAwaitable();
            InvalidOperationException failure = new InvalidOperationException("after suspension");
            OnityTask<int> task = ThrowAfterAsync(gate, failure);

            gate.Complete();

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(task.IsCanceled, Is.False);
            InvalidOperationException thrown =
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.That(thrown, Is.SameAs(failure));
        }

        [Test]
        public void SuspendedCancellation_IsCanceled_AndRethrowsTheSameInstance()
        {
            ManualAwaitable gate = new ManualAwaitable();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            CustomCanceledException canceled = new CustomCanceledException(cancellation.Token);
            OnityTask task = ThrowAfterAsync(gate, canceled);

            gate.Complete();

            Assert.That(task.IsCanceled, Is.True);
            Assert.That(task.IsFaulted, Is.False);
            OperationCanceledException thrown =
                Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.That(thrown, Is.SameAs(canceled));
            Assert.That(thrown.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void SuspendedMethod_SupportsOnlyOneNativeAwaiter()
        {
            ManualAwaitable gate = new ManualAwaitable();
            OnityTask task = CompleteAfterAsync(gate);
            OnityTaskAwaiter awaiter = task.GetAwaiter();

            awaiter.OnCompleted(() => { });

            Assert.Throws<InvalidOperationException>(() => awaiter.OnCompleted(() => { }));

            gate.Complete();
        }

        [Test]
        public async Task SuspendedMethod_AsTask_BridgesTheResult_AndBlocksNativeConsumption()
        {
            ManualAwaitable gate = new ManualAwaitable();
            OnityTask<int> task = ReturnAfterAsync(gate, 23);
            Task<int> bridge = task.AsTask();

            Assert.That(bridge.IsCompleted, Is.False);

            gate.Complete();

            Assert.That(await bridge, Is.EqualTo(23));
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void SuspendedMethod_Preserve_SharesTheResultWithSeveralConsumers()
        {
            ManualAwaitable gate = new ManualAwaitable();
            OnityTask<int> shared = ReturnAfterAsync(gate, 31).Preserve();
            int firstObserved = 0;
            int secondObserved = 0;

            shared.GetAwaiter().OnCompleted(() => firstObserved = shared.GetAwaiter().GetResult());
            shared.GetAwaiter().OnCompleted(() => secondObserved = shared.GetAwaiter().GetResult());

            gate.Complete();

            Assert.That(firstObserved, Is.EqualTo(31));
            Assert.That(secondObserved, Is.EqualTo(31));
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo(31), "A preserved result must stay readable.");
        }

        [Test]
        public void SuspendedMethod_RunnerIsReusedAfterConsumption()
        {
            ManualAwaitable firstGate = new ManualAwaitable();
            OnityTask<int> first = ReturnAfterAsync(firstGate, 1);
            object firstRunner = GetState(first);
            firstGate.Complete();
            first.GetAwaiter().GetResult();
            DrainDeferredPoolReturns();

            ManualAwaitable secondGate = new ManualAwaitable();
            OnityTask<int> second = ReturnAfterAsync(secondGate, 2);

            Assert.That(GetState(second), Is.SameAs(firstRunner), "The released runner was not rented again.");
            Assert.Throws<InvalidOperationException>(() => _ = first.IsCompleted,
                "The first task's token must stay stale after the runner is reused.");

            secondGate.Complete();
            Assert.That(second.GetAwaiter().GetResult(), Is.EqualTo(2));
        }

        [Test]
        public void SuspendedMethod_ConsumerStartingTheSameMethod_DuringCompletion_Works()
        {
            ManualAwaitable firstGate = new ManualAwaitable();
            ManualAwaitable secondGate = new ManualAwaitable();
            OnityTask<int> first = ReturnAfterAsync(firstGate, 1);
            OnityTask<int> second = default;
            int firstResult = 0;

            first.GetAwaiter().OnCompleted(() =>
            {
                // Consuming here releases the runner while the producer's MoveNext is still on
                // the stack; the next rent must not disturb the completing method.
                firstResult = first.GetAwaiter().GetResult();
                second = ReturnAfterAsync(secondGate, 2);
            });

            firstGate.Complete();

            Assert.That(firstResult, Is.EqualTo(1));
            Assert.That(second.IsCompleted, Is.False);
            secondGate.Complete();
            Assert.That(second.GetAwaiter().GetResult(), Is.EqualTo(2));
        }

        [Test]
        public void ResumedOnWorker_FlowsAsyncLocal_WhenFlowIsEnabled()
        {
            OnityTask.FlowExecutionContext = true;
            AsyncLocal<string> local = new AsyncLocal<string>();
            ManualAwaitable gate = new ManualAwaitable();
            local.Value = "main";
            OnityTask<ResumeObservation> task = ObserveAfterAsync(gate, local);
            local.Value = null;
            string workerAmbientAfter = "unset";
            SynchronizationContext workerContext = null;

            Task.Run(() =>
            {
                // Unity's Task constructor captures the main thread's context through the public
                // ExecutionContext.Capture, so a Task.Run body carries a copy of the Unity context.
                workerContext = SynchronizationContext.Current;
                gate.Complete();
                workerAmbientAfter = local.Value;
            }).GetAwaiter().GetResult();

            ResumeObservation observation = task.GetAwaiter().GetResult();
            Assert.That(observation.ThreadId, Is.Not.EqualTo(Thread.CurrentThread.ManagedThreadId));
            Assert.That(observation.LocalValue, Is.EqualTo("main"));
            Assert.That(observation.Context, Is.SameAs(workerContext),
                "The resumed method must observe the resuming thread's synchronization context.");
            Assert.That(workerAmbientAfter, Is.Null, "The flowed value leaked into the worker's ambient context.");
        }

        [Test]
        public void ResumedOnWorker_DoesNotFlowAsyncLocal_WhenFlowIsDisabled()
        {
            OnityTask.FlowExecutionContext = false;
            AsyncLocal<string> local = new AsyncLocal<string>();
            ManualAwaitable gate = new ManualAwaitable();
            local.Value = "main";
            OnityTask<ResumeObservation> task = ObserveAfterAsync(gate, local);
            local.Value = null;

            Task.Run(() => gate.Complete()).GetAwaiter().GetResult();

            Assert.That(task.GetAwaiter().GetResult().LocalValue, Is.Null);
        }

        [Test]
        public void ResumedOnWorker_DoesNotFlowAsyncLocal_WhenFlowIsSuppressed()
        {
            OnityTask.FlowExecutionContext = true;
            AsyncLocal<string> local = new AsyncLocal<string>();
            ManualAwaitable gate = new ManualAwaitable();
            local.Value = "main";
            OnityTask<ResumeObservation> task;
            using (ExecutionContext.SuppressFlow())
            {
                task = ObserveAfterAsync(gate, local);
            }

            local.Value = null;

            Task.Run(() => gate.Complete()).GetAwaiter().GetResult();

            Assert.That(task.GetAwaiter().GetResult().LocalValue, Is.Null);
        }

        [Test]
        public void ResumedOnMainThread_KeepsUnityContext_AndIsolatesAsyncLocalWrites()
        {
            OnityTask.FlowExecutionContext = true;
            SynchronizationContext unityContext = SynchronizationContext.Current;
            Assert.That(unityContext, Is.Not.Null);
            AsyncLocal<string> local = new AsyncLocal<string>();
            ManualAwaitable gate = new ManualAwaitable();
            local.Value = "before";
            OnityTask<ResumeObservation> task = WriteAfterAsync(gate, local, "inner");

            gate.Complete();

            ResumeObservation observation = task.GetAwaiter().GetResult();
            Assert.That(observation.ThreadId, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
            Assert.That(observation.LocalValue, Is.EqualTo("before"));
            Assert.That(observation.Context, Is.SameAs(unityContext),
                "The resumed method must observe the resuming thread's synchronization context.");
            Assert.That(SynchronizationContext.Current, Is.SameAs(unityContext));
            Assert.That(local.Value, Is.EqualTo("before"),
                "A write made after the await leaked into the resuming thread's context.");
        }

        [Test]
        public void ResumedOnMainThread_WithoutFlow_LetsAsyncLocalWritesReachTheThread()
        {
            OnityTask.FlowExecutionContext = false;
            AsyncLocal<string> local = new AsyncLocal<string>();
            ManualAwaitable gate = new ManualAwaitable();
            local.Value = "before";
            OnityTask<ResumeObservation> task = WriteAfterAsync(gate, local, "inner");

            try
            {
                gate.Complete();

                Assert.That(task.GetAwaiter().GetResult().LocalValue, Is.EqualTo("before"),
                    "Without flow the method resumes on the thread's ambient context, which still holds the value.");
                Assert.That(local.Value, Is.EqualTo("inner"),
                    "Without flow a write after the await stays on the resuming thread, as it does in UniTask.");
            }
            finally
            {
                local.Value = null;
            }
        }

        [Test]
        public void FlowSwitchFlippedWhileSuspended_KeepsEachMethodsCaptureDecision()
        {
            AsyncLocal<string> local = new AsyncLocal<string>();
            ManualAwaitable flowedGate = new ManualAwaitable();
            ManualAwaitable bareGate = new ManualAwaitable();

            try
            {
                OnityTask.FlowExecutionContext = true;
                local.Value = "captured";
                OnityTask<ResumeObservation> flowed = ObserveAfterAsync(flowedGate, local);

                OnityTask.FlowExecutionContext = false;
                OnityTask<ResumeObservation> bare = ObserveAfterAsync(bareGate, local);

                // The decision was taken at each suspension; flipping the switch and the ambient
                // value before resumption must not change what either method observes.
                local.Value = "ambient";
                OnityTask.FlowExecutionContext = true;
                flowedGate.Complete();
                OnityTask.FlowExecutionContext = false;
                bareGate.Complete();

                Assert.That(flowed.GetAwaiter().GetResult().LocalValue, Is.EqualTo("captured"));
                Assert.That(bare.GetAwaiter().GetResult().LocalValue, Is.EqualTo("ambient"));
                Assert.That(local.Value, Is.EqualTo("ambient"));
            }
            finally
            {
                local.Value = null;
            }
        }

        [Test]
        public void Forget_NativeTask_ReportsTheFailureToTheHandler()
        {
            OnityTaskTracker.IsEnabled = false;
            ManualAwaitable gate = new ManualAwaitable();
            InvalidOperationException failure = new InvalidOperationException("forgotten failure");
            Exception reported = null;

            ThrowAfterAsync(gate, failure).Forget(exception => reported = exception);
            gate.Complete();

            Assert.That(reported, Is.SameAs(failure));
        }

        [Test]
        public void Forget_NativeTask_LogsWithoutHandler()
        {
            OnityTaskTracker.IsEnabled = false;
            ManualAwaitable gate = new ManualAwaitable();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: logged failure"));

            ThrowAfterAsync(gate, new InvalidOperationException("logged failure")).Forget();
            gate.Complete();
        }

        [Test]
        public void Forget_WithTrackingEnabled_RegistersWithTheTracker()
        {
            OnityTaskTracker.IsEnabled = true;
            OnityTaskTracker.ClearAll();
            ManualAwaitable gate = new ManualAwaitable();

            CompleteAfterAsync(gate).Forget();

            List<OnityTrackedTaskInfo> snapshot = new List<OnityTrackedTaskInfo>();
            OnityTaskTracker.GetSnapshot(snapshot, true);
            Assert.That(snapshot.Count, Is.GreaterThan(0), "A forgotten task must stay visible while tracking is on.");

            gate.Complete();
            OnityTaskTracker.ClearAll();
        }

        [Test]
        public async Task WhenAll_TwoPendingSuspendedMethods_CompletesAfterBoth()
        {
            ManualAwaitable firstGate = new ManualAwaitable();
            ManualAwaitable secondGate = new ManualAwaitable();
            OnityTask first = CompleteAfterAsync(firstGate);
            OnityTask second = CompleteAfterAsync(secondGate);
            OnityTask combined = OnityTask.WhenAll(first, second);

            Assert.That(combined.IsCompleted, Is.False);
            firstGate.Complete();
            Assert.That(combined.IsCompleted, Is.False);
            secondGate.Complete();

            await combined;
            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.Throws<InvalidOperationException>(() => _ = first.IsCompleted,
                "WhenAll must consume its runner-backed inputs.");
        }

        [Test]
        public void WhenAll_FaultedSuspendedInput_PropagatesTheFault()
        {
            ManualAwaitable firstGate = new ManualAwaitable();
            ManualAwaitable secondGate = new ManualAwaitable();
            InvalidOperationException failure = new InvalidOperationException("first failed");
            OnityTask combined = OnityTask.WhenAll(
                ThrowUntypedAfterAsync(firstGate, failure), CompleteAfterAsync(secondGate));

            firstGate.Complete();
            secondGate.Complete();

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(Assert.Throws<InvalidOperationException>(() => combined.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void WhenAll_PendingInputWithTaskBridge_TakesTheTaskPath_AndCompletes()
        {
            ManualAwaitable firstGate = new ManualAwaitable();
            ManualAwaitable secondGate = new ManualAwaitable();
            OnityTask first = CompleteAfterAsync(firstGate);
            OnityTask second = CompleteAfterAsync(secondGate);
            Task bridge = second.AsTask();

            OnityTask combined = OnityTask.WhenAll(first, second);

            Assert.That(GetState(combined), Is.InstanceOf<Task>(),
                "An input that already has a .NET bridge must not enter the pooled coordinator.");
            firstGate.Complete();
            secondGate.Complete();

            Task combinedTask = combined.AsTask();
            Assert.That(combinedTask.Wait(TimeSpan.FromSeconds(5)), Is.True, "WhenAll did not complete.");
            Assert.That(combinedTask.IsCompletedSuccessfully, Is.True);
            Assert.That(bridge.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void WhenAll_InputWithNativeAwaiter_Throws_AndKeepsTheOtherInputAwaitable()
        {
            ManualAwaitable firstGate = new ManualAwaitable();
            ManualAwaitable secondGate = new ManualAwaitable();
            OnityTask first = CompleteAfterAsync(firstGate);
            OnityTask second = CompleteAfterAsync(secondGate);
            bool firstObserved = false;
            first.GetAwaiter().OnCompleted(() => firstObserved = true);

            Assert.Throws<InvalidOperationException>(() => OnityTask.WhenAll(first, second),
                "An input that already has a native awaiter cannot join a WhenAll.");

            // The rejected call must not have claimed the other input or stranded a coordinator.
            secondGate.Complete();
            Assert.That(second.IsCompletedSuccessfully, Is.True);
            Assert.DoesNotThrow(() => second.GetAwaiter().GetResult());
            firstGate.Complete();
            Assert.That(firstObserved, Is.True);
        }

        [Test]
        public void WhenAll_ConsumedInput_ThrowsInsteadOfHanging()
        {
            ManualAwaitable firstGate = new ManualAwaitable();
            ManualAwaitable secondGate = new ManualAwaitable();
            OnityTask first = CompleteAfterAsync(firstGate);
            OnityTask second = CompleteAfterAsync(secondGate);
            firstGate.Complete();
            first.GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => OnityTask.WhenAll(first, second));

            secondGate.Complete();
            Assert.DoesNotThrow(() => second.GetAwaiter().GetResult());
        }

        /// <summary>
        /// Under IL2CPP a released runner returns to its pool from the next dispatcher drain rather
        /// than synchronously; the Mono path returns immediately, so this is a no-op there.
        /// </summary>
        private static void DrainDeferredPoolReturns()
        {
#if ENABLE_IL2CPP
            Type dispatcherType = typeof(OnityTask).Assembly.GetType(
                "Onity.Unity.Async.OnityTaskMainThreadDispatcher", true);
            MethodInfo drain = dispatcherType.GetMethod("Drain", BindingFlags.Static | BindingFlags.Public);
            Assert.That(drain, Is.Not.Null);
            drain.Invoke(null, null);
#endif
        }

        private static object GetState(OnityTask task)
        {
            FieldInfo field = typeof(OnityTask).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
        }

        private static object GetState<T>(OnityTask<T> task)
        {
            FieldInfo field = typeof(OnityTask<T>).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
        }

        private static async OnityTask CompleteAfterAsync(ManualAwaitable gate)
        {
            await gate;
        }

        private static async OnityTask<int> ReturnAfterAsync(ManualAwaitable gate, int value)
        {
            await gate;
            return value;
        }

        private static async OnityTask<int> ReturnAfterSafeAsync(SafeManualAwaitable gate, int value)
        {
            await gate;
            return value;
        }

        private static async OnityTask<int> CancelSynchronouslyWithoutTokenAsync()
        {
            await OnityTask.Completed;
            throw new OperationCanceledException();
        }

        private static async OnityTask<int> ThrowAfterAsync(ManualAwaitable gate, Exception exception)
        {
            await gate;
            throw exception;
        }

        private static async OnityTask ThrowAfterAsync(ManualAwaitable gate, OperationCanceledException exception)
        {
            await gate;
            throw exception;
        }

        private static async OnityTask ThrowUntypedAfterAsync(ManualAwaitable gate, Exception exception)
        {
            await gate;
            throw exception;
        }

        private static async OnityTask<ResumeObservation> ObserveAfterAsync(ManualAwaitable gate, AsyncLocal<string> local)
        {
            await gate;
            return new ResumeObservation(Thread.CurrentThread.ManagedThreadId, local.Value, SynchronizationContext.Current);
        }

        private static async OnityTask<ResumeObservation> WriteAfterAsync(
            ManualAwaitable gate,
            AsyncLocal<string> local,
            string value)
        {
            await gate;
            ResumeObservation observation = new ResumeObservation(
                Thread.CurrentThread.ManagedThreadId, local.Value, SynchronizationContext.Current);
            local.Value = value;
            return observation;
        }

        private readonly struct ResumeObservation
        {
            public readonly int ThreadId;
            public readonly string LocalValue;
            public readonly SynchronizationContext Context;

            public ResumeObservation(int threadId, string localValue, SynchronizationContext context)
            {
                ThreadId = threadId;
                LocalValue = localValue;
                Context = context;
            }
        }

        private sealed class CustomCanceledException : OperationCanceledException
        {
            public CustomCanceledException(CancellationToken token)
                : base("custom cancellation", token)
            {
            }
        }

        /// <summary>
        /// Critical awaitable that stores its continuation and runs it on the completing thread.
        /// </summary>
        private sealed class ManualAwaitable : ICriticalNotifyCompletion
        {
            private Action m_continuation;

            public bool IsCompleted => false;

            public ManualAwaitable GetAwaiter()
            {
                return this;
            }

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void Complete()
            {
                Action continuation = Interlocked.Exchange(ref m_continuation, null);
                if (continuation == null)
                {
                    throw new InvalidOperationException("No continuation was registered.");
                }

                continuation();
            }
        }

        private sealed class SafeManualAwaitable : INotifyCompletion
        {
            private Action m_continuation;

            public bool IsCompleted => false;

            public SafeManualAwaitable GetAwaiter()
            {
                return this;
            }

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void Complete()
            {
                Action continuation = Interlocked.Exchange(ref m_continuation, null);
                if (continuation == null)
                {
                    throw new InvalidOperationException("No continuation was registered.");
                }

                continuation();
            }
        }
    }
}
