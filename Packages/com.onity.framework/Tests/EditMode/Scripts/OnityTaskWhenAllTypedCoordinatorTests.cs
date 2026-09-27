using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityTaskWhenAllTypedCoordinatorTests
    {
        private bool m_flow;
        private bool m_tracking;
        private bool m_stackTrace;

        [SetUp]
        public void SaveSettings()
        {
            m_flow = OnityTask.FlowExecutionContext;
            m_tracking = OnityTaskTracker.IsEnabled;
            m_stackTrace = OnityTaskTracker.EnableStackTrace;
            OnityTaskTracker.IsEnabled = false;
        }

        [TearDown]
        public void RestoreSettings()
        {
            OnityTask.FlowExecutionContext = m_flow;
            OnityTaskTracker.IsEnabled = m_tracking;
            OnityTaskTracker.EnableStackTrace = m_stackTrace;
            OnityTaskTracker.ClearAll();
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(8)]
        [TestCase(16)]
        public void EligibleInputs_PreserveOrderWithoutInputBridges(int count)
        {
            OnityTaskCompletionSource<int>[] sources = CreateSources(count);
            OnityTask<int>[] inputs = Inputs(sources);
            OnityTask<int[]> output = OnityTask.WhenAll(inputs);
            Task<int[]> retained = output.AsTask();
            Assert.That(output.AsTask(), Is.SameAs(retained));
            for (int i = count - 1; i >= 0; i--)
            {
                Assert.That(Bridge(sources[i]), Is.Null);
                sources[i].TrySetResult(i + 10);
                if (i != 0)
                {
                    Assert.That(output.IsCompleted, Is.False);
                }
            }
            int[] result = output.GetAwaiter().GetResult();
            for (int i = 0; i < count; i++)
            {
                Assert.That(result[i], Is.EqualTo(i + 10));
                Assert.That(Bridge(sources[i]), Is.Null);
                Assert.That(sources[i].Task.GetAwaiter().GetResult(), Is.EqualTo(i + 10));
            }
            Assert.That(output.GetAwaiter().GetResult(), Is.SameAs(result));
            Assert.That(retained.GetAwaiter().GetResult(), Is.SameAs(result));
        }

        [Test]
        public void MixedAndMutatedCallerArray_UsesTheOriginalSnapshot()
        {
            OnityTaskCompletionSource<int> pending = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> completed = new OnityTaskCompletionSource<int>();
            completed.TrySetResult(23);
            OnityTask<int>[] inputs = { default, pending.Task, OnityTask.FromResult(17), completed.Task };
            OnityTask<int[]> output = OnityTask.WhenAll(inputs);
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = OnityTask.FromResult(-1);
            }
            pending.TrySetResult(11);
            Assert.That(output.GetAwaiter().GetResult(), Is.EqualTo(new[] { 0, 11, 17, 23 }));
            Assert.That(Bridge(pending), Is.Null);
            Assert.That(Bridge(completed), Is.Null);
        }

        [Test]
        public void RetainedOutput_SurvivesCoordinatorReuseAndConcurrentReaders()
        {
            OnityTaskCompletionSource<int>[] sources = CreateSources(2);
            OnityTask<int[]> original = OnityTask.WhenAll(Inputs(sources));
            sources[0].TrySetResult(11);
            sources[1].TrySetResult(22);
            int[] retained = original.GetAwaiter().GetResult();
            for (int i = 0; i < 32; i++)
            {
                OnityTaskCompletionSource<int> next = new OnityTaskCompletionSource<int>();
                OnityTask<int[]> output = OnityTask.WhenAll(next.Task);
                next.TrySetResult(i);
                Assert.That(output.GetAwaiter().GetResult(), Is.EqualTo(new[] { i }));
            }
            Task[] readers = new Task[8];
            for (int i = 0; i < readers.Length; i++)
            {
                readers[i] = Task.Run(() =>
                {
                    if (!ReferenceEquals(original.GetAwaiter().GetResult(), retained))
                    {
                        throw new InvalidOperationException("Retained output changed after reuse.");
                    }
                });
            }
            Join(readers);
            Assert.That(retained, Is.EqualTo(new[] { 11, 22 }));
        }

        [TestCase("duplicate")]
        [TestCase("prebridged")]
        [TestCase("task")]
        [TestCase("derived")]
        [TestCase("pooled")]
        [TestCase("preserved")]
        [TestCase("seventeen")]
        public void IneligibleInput_UsesExistingBridgeFallback(string kind)
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
            OnityTask<int>[] inputs = { first.Task, second.Task };
            TaskCompletionSource<int> taskSource = null;
            CriticalGate gate = null;
            OnityTask<int> pooled = default;
            DerivedSource derived = null;
            if (kind == "duplicate")
            {
                inputs[1] = first.Task;
            }
            else if (kind == "prebridged")
            {
                first.Task.AsTask();
            }
            else if (kind == "task")
            {
                taskSource = new TaskCompletionSource<int>();
                inputs[1] = OnityTask<int>.FromTask(taskSource.Task);
            }
            else if (kind == "derived")
            {
                derived = new DerivedSource();
                inputs[1] = derived.Task;
            }
            else if (kind == "pooled" || kind == "preserved")
            {
                gate = new CriticalGate();
                pooled = ReturnAfter(gate);
                inputs[1] = kind == "preserved" ? pooled.Preserve() : pooled;
            }
            else
            {
                inputs = new OnityTask<int>[17];
                inputs[0] = first.Task;
                for (int i = 1; i < inputs.Length; i++)
                {
                    inputs[i] = OnityTask.FromResult(i);
                }
            }
            OnityTask<int[]> output = OnityTask.WhenAll(inputs);
            Assert.That(Bridge(first), Is.Not.Null, "Fallback must materialize the completion-source input.");
            if (kind == "duplicate")
            {
                Assert.That(first.Task.AsTask(), Is.SameAs(inputs[1].AsTask()));
            }
            if (kind == "pooled")
            {
                Assert.Throws<InvalidOperationException>(() => pooled.GetAwaiter().OnCompleted(() => { }));
            }
            first.TrySetResult(11);
            second.TrySetResult(22);
            taskSource?.TrySetResult(22);
            derived?.TrySetResult(22);
            gate?.Complete();
            WaitOutput(output);
            int[] result = output.GetAwaiter().GetResult();
            Assert.That(result[0], Is.EqualTo(11));
            Assert.That(result[1], Is.EqualTo(kind == "duplicate" ? 11 : kind == "seventeen" ? 1 : 22));
            Assert.That(result.Length, Is.EqualTo(kind == "seventeen" ? 17 : 2));
        }

        [Test]
        public void Faults_PreserveInputOrderIdentityNestedAggregatesAndOrigin()
        {
            OnityTaskCompletionSource<int>[] sources = CreateSources(3);
            OnityTask<int[]> output = OnityTask.WhenAll(Inputs(sources));
            Exception first = OriginFault();
            AggregateException nested = new AggregateException(new ArgumentException("nested"));
            sources[2].TrySetException(nested);
            sources[1].TrySetResult(17);
            sources[0].TrySetException(first);
            Assert.That(output.IsFaulted, Is.True);
            var faults = output.AsTask().Exception.InnerExceptions;
            Assert.That(faults.Count, Is.EqualTo(2));
            Assert.That(faults[0], Is.SameAs(first));
            Assert.That(faults[1], Is.SameAs(nested));
            Exception observed = Assert.Throws<InvalidOperationException>(() => output.GetAwaiter().GetResult());
            Assert.That(observed, Is.SameAs(first));
            Assert.That(observed.StackTrace, Does.Contain(nameof(OriginFault)));
        }

        [Test]
        public void FaultedOperationCanceledException_WinsOverCancellationWithoutChangingStatus()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int>[] sources = CreateSources(2);
            OnityTask<int[]> output = OnityTask.WhenAll(Inputs(sources));
            OperationCanceledException fault = new OperationCanceledException("fault sentinel", cancellation.Token);
            typeof(OnityTaskCompletionSource<int>).GetMethod("TrySetFault", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sources[1], new object[] { fault });
            sources[0].TrySetCanceled(cancellation.Token);
            Assert.That(output.IsFaulted, Is.True);
            Assert.That(output.IsCanceled, Is.False);
            Assert.That(output.AsTask().Exception.InnerException, Is.SameAs(fault));
            Assert.That(Assert.Catch<OperationCanceledException>(() => output.GetAwaiter().GetResult()), Is.SameAs(fault));
        }

        [Test]
        public void Cancellation_UsesFirstInputTokenInReverseCompletionOrder()
        {
            using CancellationTokenSource first = new CancellationTokenSource();
            using CancellationTokenSource last = new CancellationTokenSource();
            first.Cancel();
            last.Cancel();
            OnityTaskCompletionSource<int>[] sources = CreateSources(3);
            OnityTask<int[]> output = OnityTask.WhenAll(Inputs(sources));
            sources[2].TrySetCanceled(last.Token);
            sources[1].TrySetResult(17);
            Assert.That(output.IsCompleted, Is.False);
            sources[0].TrySetCanceled(first.Token);
            Assert.That(output.IsCanceled, Is.True);
            Assert.That(Assert.Catch<OperationCanceledException>(() => output.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(first.Token));
            Assert.That(Assert.Catch<OperationCanceledException>(() => output.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(first.Token));
        }

        [TestCase("before")]
        [TestCase("during")]
        [TestCase("after")]
        public void InputBridgeMaterialization_ObservesFaultAndKeepsOneBridge(string timing)
        {
            for (int iteration = 0; iteration < 16; iteration++)
            {
                OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
                OnityTaskCompletionSource<int> other = new OnityTaskCompletionSource<int>();
                Task<int> inputBridge = timing == "before" ? source.Task.AsTask() : null;
                Task creator = null;
                using ManualResetEventSlim start = new ManualResetEventSlim();
                if (timing == "during")
                {
                    creator = Task.Run(() =>
                    {
                        if (!start.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Bridge race start timed out.");
                        }
                        inputBridge = source.Task.AsTask();
                    });
                    start.Set();
                }
                OnityTask<int[]> output = OnityTask.WhenAll(source.Task, other.Task);
                Exception fault = new InvalidOperationException("materialized typed fault");
                try
                {
                    source.TrySetException(fault);
                    other.TrySetResult(17);
                }
                finally
                {
                    start.Set();
                    if (creator != null)
                    {
                        Join(creator);
                    }
                }
                if (timing == "after")
                {
                    inputBridge = source.Task.AsTask();
                }
                WaitOutput(output);
                Assert.That(inputBridge, Is.SameAs(source.Task.AsTask()));
                Assert.That(FaultObserved(inputBridge), Is.True);
                Assert.That(Assert.Throws<InvalidOperationException>(() => output.GetAwaiter().GetResult()),
                    Is.SameAs(fault));
            }
        }

        [Test]
        public void RegistrationCompletionRace_AndConcurrentProducers_SettleEveryInputOnce()
        {
            for (int iteration = 0; iteration < 32; iteration++)
            {
                OnityTaskCompletionSource<int>[] sources = CreateSources(16);
                using ManualResetEventSlim start = new ManualResetEventSlim();
                Task producer = Task.Run(() =>
                {
                    if (!start.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("Completion race start timed out.");
                    }
                    for (int i = 0; i < sources.Length; i++)
                    {
                        sources[i].TrySetResult(i);
                    }
                });
                OnityTask<int[]> output;
                try
                {
                    start.Set();
                    output = OnityTask.WhenAll(Inputs(sources));
                }
                finally
                {
                    start.Set();
                    Join(producer);
                }
                WaitOutput(output);
                int[] result = output.GetAwaiter().GetResult();
                for (int i = 0; i < result.Length; i++)
                {
                    Assert.That(result[i], Is.EqualTo(i));
                }
            }
            OnityTaskCompletionSource<int>[] concurrent = CreateSources(16);
            OnityTask<int[]> combined = OnityTask.WhenAll(Inputs(concurrent));
            Task[] producers = new Task[16];
            for (int i = 0; i < producers.Length; i++)
            {
                int index = i;
                producers[i] = Task.Run(() => concurrent[index].TrySetResult(index));
            }
            Join(producers);
            Assert.That(combined.GetAwaiter().GetResult().Length, Is.EqualTo(16));
        }

        [Test]
        public void CompletionCallback_CanCreateNestedCoordinatorWithoutCorruptingRetainedOutput()
        {
            OnityTaskCompletionSource<int>[] first = CreateSources(2);
            OnityTask<int[]> output = OnityTask.WhenAll(Inputs(first));
            TaskCompletionSource<int[]> observed = new TaskCompletionSource<int[]>();
            SynchronizationContext previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                output.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        OnityTaskCompletionSource<int> next = new OnityTaskCompletionSource<int>();
                        OnityTask<int[]> nested = OnityTask.WhenAll(next.Task, OnityTask.FromResult(99));
                        next.TrySetResult(73);
                        observed.TrySetResult(nested.GetAwaiter().GetResult());
                    }
                    catch (Exception exception)
                    {
                        observed.TrySetException(exception);
                    }
                });
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
            first[0].TrySetResult(11);
            first[1].TrySetResult(22);
            Join(observed.Task);
            Assert.That(observed.Task.Result, Is.EqualTo(new[] { 73, 99 }));
            Assert.That(output.GetAwaiter().GetResult(), Is.EqualTo(new[] { 11, 22 }));
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void OutputAwaiterContext_MatchesTaskWhenAllAndRestoresSuppression(bool flow, bool suppressed)
        {
            OnityTask.FlowExecutionContext = flow;
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            OnityTask<int[]> output;
            if (suppressed)
            {
                using (ExecutionContext.SuppressFlow())
                {
                    output = OnityTask.WhenAll(source.Task, OnityTask.FromResult(17));
                    Assert.That(ExecutionContext.IsFlowSuppressed(), Is.True);
                }
            }
            else
            {
                output = OnityTask.WhenAll(source.Task, OnityTask.FromResult(17));
            }
            Assert.That(ExecutionContext.IsFlowSuppressed(), Is.False);
            TaskCompletionSource<int> reference = new TaskCompletionSource<int>();
            string actual = ObserveContext(output.AsTask().GetAwaiter(), () => source.TrySetResult(11), suppressed);
            string expected = ObserveContext(Task.WhenAll(reference.Task, Task.FromResult(17)).GetAwaiter(),
                () => reference.TrySetResult(11), suppressed);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Tracker_RetainsFactoryContextForFaultAndTracksOneOutput()
        {
            AsyncLocal<string> local = new AsyncLocal<string>();
            try
            {
                OnityTaskTracker.IsEnabled = true;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTaskTracker.ClearAll();
                local.Value = "registration";
                OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
                OnityTask<int[]> output = OnityTask.WhenAll(source.Task, OnityTask.FromResult(17));
                local.Value = "completion";
                source.TrySetException(new ContextException(local));
                List<OnityTrackedTaskInfo> rows = new List<OnityTrackedTaskInfo>();
                Assert.That(SpinWait.SpinUntil(() =>
                {
                    OnityTaskTracker.GetSnapshot(rows);
                    return rows.Count == 1 && rows[0].IsCompleted;
                }, TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(rows[0].Source, Is.EqualTo("OnityAsync.WhenAll<T>"));
                Assert.That(rows[0].ErrorMessage, Is.EqualTo("registration"));
                Assert.That(local.Value, Is.EqualTo("completion"));
                Assert.That(output.IsFaulted, Is.True);
                Assert.Throws<ContextException>(() => output.GetAwaiter().GetResult());
            }
            finally
            {
                local.Value = null;
            }
        }

        private static OnityTaskCompletionSource<int>[] CreateSources(int count)
        {
            OnityTaskCompletionSource<int>[] sources = new OnityTaskCompletionSource<int>[count];
            for (int i = 0; i < count; i++)
            {
                sources[i] = new OnityTaskCompletionSource<int>();
            }
            return sources;
        }

        private static OnityTask<int>[] Inputs(OnityTaskCompletionSource<int>[] sources)
        {
            OnityTask<int>[] inputs = new OnityTask<int>[sources.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = sources[i].Task;
            }
            return inputs;
        }

        private static object Bridge(OnityTaskCompletionSource<int> source)
        {
            return typeof(OnityTaskCompletionSource<int>).GetField("m_taskBridge",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
        }

        private static bool FaultObserved(Task task)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object contingent = typeof(Task).GetField("m_contingentProperties", flags).GetValue(task);
            object holder = contingent.GetType().GetField("m_exceptionsHolder", flags).GetValue(contingent);
            return (bool)holder.GetType().GetField("m_isHandled", flags).GetValue(holder);
        }

        private static void Join(params Task[] tasks)
        {
            Assert.That(Task.WaitAll(tasks, TimeSpan.FromSeconds(5)), Is.True, "Owned worker timed out.");
        }

        private static void WaitOutput(OnityTask<int[]> output)
        {
            Assert.That(SpinWait.SpinUntil(() => output.IsCompleted, TimeSpan.FromSeconds(5)),
                Is.True, "Typed WhenAll output timed out.");
        }

        private static string ObserveContext<TAwaiter>(TAwaiter awaiter, Action complete, bool suppressed)
            where TAwaiter : ICriticalNotifyCompletion
        {
            AsyncLocal<string> local = new AsyncLocal<string>();
            TaskCompletionSource<string> observed = new TaskCompletionSource<string>();
            SynchronizationContext previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                local.Value = "registration";
                if (suppressed)
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        awaiter.OnCompleted(() => observed.TrySetResult(local.Value));
                    }
                }
                else
                {
                    awaiter.OnCompleted(() => observed.TrySetResult(local.Value));
                }
            }
            finally
            {
                local.Value = null;
                SynchronizationContext.SetSynchronizationContext(previous);
            }
            TaskCompletionSource<bool> worker = new TaskCompletionSource<bool>();
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                try
                {
                    complete();
                    worker.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    worker.TrySetException(exception);
                    observed.TrySetException(exception);
                }
            }, null);
            Join(worker.Task, observed.Task);
            return observed.Task.GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception OriginFault()
        {
            try
            {
                throw new InvalidOperationException("typed origin fault");
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private sealed class DerivedSource : OnityTaskCompletionSource<int>
        {
        }

        private sealed class ContextException : Exception
        {
            private readonly AsyncLocal<string> m_local;
            public ContextException(AsyncLocal<string> local) => m_local = local;
            public override string Message => m_local.Value;
        }

        private static async OnityTask<int> ReturnAfter(CriticalGate gate)
        {
            await gate;
            return 22;
        }

        private sealed class CriticalGate : ICriticalNotifyCompletion
        {
            private Action m_continuation;
            public bool IsCompleted => false;
            public CriticalGate GetAwaiter() => this;
            public void GetResult()
            {
            }
            public void OnCompleted(Action continuation) => m_continuation = continuation;
            public void UnsafeOnCompleted(Action continuation) => m_continuation = continuation;
            public void Complete() => Interlocked.Exchange(ref m_continuation, null)();
        }
    }
}
