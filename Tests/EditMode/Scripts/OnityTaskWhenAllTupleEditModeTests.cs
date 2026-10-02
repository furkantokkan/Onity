using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using static Onity.Tests.EditMode.OnityTaskCompositionTestSupport;

namespace Onity.Tests.EditMode
{
    /// <summary>Pins the generated tuple <c>OnityTask.WhenAll&lt;T1..Tn&gt;</c> overloads.</summary>
    [TestFixture]
    public sealed class OnityTaskWhenAllTupleEditModeTests
    {
        [Test]
        public void CompletedInputs_ReturnTheTupleWithoutRentingASource()
        {
            OnityTask<(int, string)> combined = OnityTask.WhenAll(OnityTask.FromResult(1), OnityTask.FromResult("a"));

            Assert.That(GetState(combined), Is.Null, "Already successful inputs take the allocation-free path.");
            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo((1, "a")));
        }

        [Test]
        public void PendingInputs_WaitForEveryInput_AndKeepArgumentOrder()
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<string> second = new OnityTaskCompletionSource<string>();
            OnityTaskCompletionSource<double> third = new OnityTaskCompletionSource<double>();
            OnityTask<(int, string, double)> combined = OnityTask.WhenAll(first.Task, second.Task, third.Task);

            third.TrySetResult(3.5);
            first.TrySetResult(1);
            Assert.That(combined.IsCompleted, Is.False);
            second.TrySetResult("two");

            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo((1, "two", 3.5)));
        }

        [Test]
        public void EightInputs_MixCompletedAndPendingInputs_AcrossTheNestedTupleBoundary()
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(8);
            OnityTask<(int, int, int, int, int, int, int, int)> combined = OnityTask.WhenAll(
                OnityTask.FromResult(0), s[1].Task, OnityTask.FromResult(2), s[3].Task,
                OnityTask.FromResult(4), s[5].Task, OnityTask.FromResult(6), s[7].Task);

            s[7].TrySetResult(7);
            s[5].TrySetResult(5);
            s[3].TrySetResult(3);
            Assert.That(combined.IsCompleted, Is.False);
            s[1].TrySetResult(1);

            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo((0, 1, 2, 3, 4, 5, 6, 7)));
        }

        [Test]
        public void FifteenInputs_ReturnEveryResultInArgumentOrder()
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(15);
            var combined = OnityTask.WhenAll(
                s[0].Task, s[1].Task, s[2].Task, s[3].Task, s[4].Task, s[5].Task, s[6].Task, s[7].Task,
                s[8].Task, s[9].Task, s[10].Task, s[11].Task, s[12].Task, s[13].Task, s[14].Task);

            for (int i = s.Length - 1; i >= 0; i--)
            {
                Assert.That(combined.IsCompleted, Is.False);
                s[i].TrySetResult(i * 10);
            }

            Assert.That(combined.GetAwaiter().GetResult(),
                Is.EqualTo((0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140)));
        }

        [Test]
        public void Faults_PublishTheFirstFaultInArgumentOrder_AfterEveryInputSettles()
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(3);
            ArgumentException secondFailure = new ArgumentException("second");
            InvalidOperationException thirdFailure = new InvalidOperationException("third");
            OnityTask<(int, int, int)> combined = OnityTask.WhenAll(s[0].Task, s[1].Task, s[2].Task);

            s[2].TrySetException(thirdFailure);
            s[1].TrySetException(secondFailure);
            Assert.That(combined.IsCompleted, Is.False, "WhenAll waits for every input.");
            s[0].TrySetResult(1);

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(Assert.Throws<ArgumentException>(() => combined.GetAwaiter().GetResult()),
                Is.SameAs(secondFailure));
            Assert.That(FaultObserved(s[2].Task.AsTask()), Is.True, "A fault that is not published is observed.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Fault_TakesPrecedenceOverCancellation(bool cancellationFirst)
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
            InvalidOperationException failure = new InvalidOperationException("fault");
            OnityTask<(int, int)> combined = OnityTask.WhenAll(first.Task, second.Task);

            if (cancellationFirst)
            {
                first.TrySetCanceled();
                second.TrySetException(failure);
            }
            else
            {
                second.TrySetException(failure);
                first.TrySetCanceled();
            }

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(Assert.Throws<InvalidOperationException>(() => combined.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void Cancellation_PublishesTheFirstCanceledArgumentToken()
        {
            using CancellationTokenSource firstCancellation = new CancellationTokenSource();
            using CancellationTokenSource secondCancellation = new CancellationTokenSource();
            firstCancellation.Cancel();
            secondCancellation.Cancel();
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
            OnityTask<(int, int)> combined = OnityTask.WhenAll(first.Task, second.Task);
            Task<(int, int)> bridge = combined.AsTask();

            second.TrySetCanceled(secondCancellation.Token);
            first.TrySetCanceled(firstCancellation.Token);

            Join(bridge.ContinueWith(_ => { }));
            Assert.That(bridge.IsCanceled, Is.True);
            OperationCanceledException observed = Assert.Catch<OperationCanceledException>(
                () => bridge.GetAwaiter().GetResult());
            Assert.That(observed.CancellationToken, Is.EqualTo(firstCancellation.Token));
        }

        [Test]
        public void NativeCancellation_IsCanceledWithTheInputToken()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<string> second = new OnityTaskCompletionSource<string>();
            OnityTask<(int, string)> combined = OnityTask.WhenAll(first.Task, second.Task);

            second.TrySetResult("done");
            first.TrySetCanceled(cancellation.Token);

            Assert.That(combined.IsCanceled, Is.True);
            Assert.That(Assert.Catch<OperationCanceledException>(() => combined.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void FaultedOperationCanceledException_RemainsAFault()
        {
            OperationCanceledException failure = new OperationCanceledException("faulted input");
            OnityTaskCompletionSource<string> pending = new OnityTaskCompletionSource<string>();
            OnityTask<(int, string)> combined = OnityTask.WhenAll(OnityTask<int>.FromException(failure), pending.Task);

            pending.TrySetResult("done");

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(combined.IsCanceled, Is.False);
            Task<(int, string)> bridge = combined.AsTask();
            Assert.That(bridge.IsFaulted, Is.True);
            Assert.That(bridge.Exception.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void DuplicateSingleConsumerInput_IsRejectedBeforeAnyInputIsClaimed()
        {
            CompositionGate gate = new CompositionGate();
            OnityTask<int> native = Native(gate, 7);
            OnityTaskCompletionSource<string> other = new OnityTaskCompletionSource<string>();
            OnityTask<int> completedNative = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);

            ArgumentException pending = Assert.Throws<ArgumentException>(
                () => OnityTask.WhenAll(native, other.Task, native));
            ArgumentException completed = Assert.Throws<ArgumentException>(
                () => OnityTask.WhenAll(completedNative, completedNative));

            Assert.That(pending.ParamName, Is.EqualTo("task3"));
            Assert.That(completed.ParamName, Is.EqualTo("task2"));
            Assert.That(completedNative.GetAwaiter().GetResult(), Is.Zero);
            gate.Complete();
            Assert.That(native.GetAwaiter().GetResult(), Is.EqualTo(7));
            other.TrySetResult("unused");
        }

        [Test]
        public void DuplicateShareableInputs_AreAllowed()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            OnityTask<(int, int)> combined = OnityTask.WhenAll(source.Task, source.Task);

            source.TrySetResult(5);

            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo((5, 5)));
        }

        [Test]
        public void OneCompletedNativeInput_TakesTheFastPath_AndIsConsumedOnce()
        {
            OnityTask<int> native = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);

            OnityTask<(int, string)> combined = OnityTask.WhenAll(native, OnityTask.FromResult("x"));

            Assert.That(GetState(combined), Is.Null);
            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo((0, "x")));
            Assert.Throws<InvalidOperationException>(() => native.GetAwaiter().GetResult());
        }

        [Test]
        public void TwoCompletedNativeInputs_AreValidatedByTheSource_AndCompleteSynchronously()
        {
            OnityTask<int> first = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);
            OnityTask<int> second = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);

            OnityTask<(int, int)> combined = OnityTask.WhenAll(first, second);

            Assert.That(GetState(combined), Is.Not.Null);
            Assert.That(combined.IsCompletedSuccessfully, Is.True);
            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo((0, 0)));
            Assert.Throws<InvalidOperationException>(() => first.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => second.GetAwaiter().GetResult());
        }

        [Test]
        public void NativeInputs_AreClaimed_ThenConsumedOnceWhenTheyComplete()
        {
            CompositionGate firstGate = new CompositionGate();
            CompositionGate secondGate = new CompositionGate();
            OnityTask<int> first = Native(firstGate, 1);
            OnityTask<string> second = Native(secondGate, "two");

            OnityTask<(int, string)> combined = OnityTask.WhenAll(first, second);
            Assert.Throws<InvalidOperationException>(() => first.GetAwaiter().OnCompleted(() => { }),
                "WhenAll observes the input.");
            secondGate.Complete();
            Assert.That(combined.IsCompleted, Is.False);
            firstGate.Complete();

            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo((1, "two")));
            Assert.Throws<InvalidOperationException>(() => _ = first.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => _ = second.IsCompleted);
        }

        [Test]
        public void NativeFault_PropagatesTheSameInstance()
        {
            CompositionGate gate = new CompositionGate();
            InvalidOperationException failure = new InvalidOperationException("native failure");
            OnityTask<(int, int)> combined = OnityTask.WhenAll(NativeFault<int>(gate, failure), OnityTask.FromResult(2));

            gate.Complete();

            Assert.That(combined.IsFaulted, Is.True);
            Assert.That(Assert.Throws<InvalidOperationException>(() => combined.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void StaleInput_FaultsTheOutput_AndStillObservesTheOtherInputs()
        {
            OnityTask<int> stale = CreateStaleTask();
            OnityTaskCompletionSource<string> other = new OnityTaskCompletionSource<string>();

            OnityTask<(int, string)> combined = OnityTask.WhenAll(stale, other.Task);
            Assert.That(combined.IsCompleted, Is.False, "WhenAll still waits for the observable input.");
            other.TrySetException(new ArgumentException("observed despite a stale input"));

            Assert.That(combined.IsFaulted, Is.True);
            Assert.Throws<InvalidOperationException>(() => combined.GetAwaiter().GetResult(),
                "The stale input is the first fault in argument order.");
            Assert.That(FaultObserved(other.Task.AsTask()), Is.True);
        }

        [Test]
        public void PreclaimedInput_FaultsTheOutput_AndLeavesTheInputToItsAwaiter()
        {
            CompositionGate gate = new CompositionGate();
            OnityTask<int> claimed = Native(gate, 1);
            int observedResult = 0;
            claimed.GetAwaiter().UnsafeOnCompleted(() => observedResult = claimed.GetAwaiter().GetResult());

            OnityTask<(int, int)> combined = OnityTask.WhenAll(claimed, OnityTask.FromResult(2));

            Assert.That(combined.IsFaulted, Is.True);
            Assert.Throws<InvalidOperationException>(() => combined.GetAwaiter().GetResult());
            gate.Complete();
            Assert.That(observedResult, Is.EqualTo(1));
        }

        [Test]
        public void Output_NativeIsExclusive_BridgeIsCached_AndPreserveIsShareable()
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(6);
            OnityTask<(int, int)> native = OnityTask.WhenAll(s[0].Task, s[1].Task);
            s[0].TrySetResult(1);
            s[1].TrySetResult(2);
            Assert.That(native.GetAwaiter().GetResult(), Is.EqualTo((1, 2)));
            Assert.Throws<InvalidOperationException>(() => native.GetAwaiter().GetResult());

            OnityTask<(int, int)> bridged = OnityTask.WhenAll(s[2].Task, s[3].Task);
            Task<(int, int)> bridge = bridged.AsTask();
            Assert.That(bridged.AsTask(), Is.SameAs(bridge));
            s[2].TrySetResult(3);
            s[3].TrySetResult(4);
            Join(bridge);
            Assert.That(bridge.Result, Is.EqualTo((3, 4)));
            Assert.Throws<InvalidOperationException>(() => bridged.GetAwaiter().GetResult());

            OnityTask<(int, int)> shared = OnityTask.WhenAll(s[4].Task, s[5].Task).Preserve();
            s[4].TrySetResult(5);
            s[5].TrySetResult(6);
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo((5, 6)));
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo((5, 6)));
        }

        [Test]
        public void SettledSource_IsReusedWithoutRetainingInputsOrResults()
        {
            object previous = null;
            for (int i = 0; i < 3; i++)
            {
                OnityTaskCompletionSource<PoolProbe> first = new OnityTaskCompletionSource<PoolProbe>();
                OnityTaskCompletionSource<PoolProbe> second = new OnityTaskCompletionSource<PoolProbe>();
                OnityTask<(PoolProbe, PoolProbe)> combined = OnityTask.WhenAll(first.Task, second.Task);
                object state = GetState(combined);
                if (previous != null)
                {
                    Assert.That(state, Is.SameAs(previous), "A settled source returns to its pool.");
                }

                first.TrySetResult(new PoolProbe("first" + i));
                second.TrySetResult(new PoolProbe("second" + i));
                Assert.That(combined.GetAwaiter().GetResult(),
                    Is.EqualTo((new PoolProbe("first" + i), new PoolProbe("second" + i))));

                Assert.That(IsReleasedTaskValue(GetField(state, "m_task1")), Is.True);
                Assert.That(IsReleasedTaskValue(GetField(state, "m_task2")), Is.True);
                Assert.That(GetField(state, "m_result1"), Is.EqualTo(default(PoolProbe)));
                Assert.That(GetField(state, "m_result2"), Is.EqualTo(default(PoolProbe)));
                previous = state;
            }
        }

        [Test]
        public void RegistrationAndConcurrentCompletionRaces_SettleEveryInputOnce()
        {
            for (int repeat = 0; repeat < 32; repeat++)
            {
                OnityTaskCompletionSource<int>[] s = Sources<int>(4);
                Task[] workers = new Task[s.Length];
                for (int i = 0; i < workers.Length; i++)
                {
                    int index = i;
                    workers[i] = Task.Run(() => s[index].TrySetResult(index * 10));
                }

                OnityTask<(int, int, int, int)> combined =
                    OnityTask.WhenAll(s[0].Task, s[1].Task, s[2].Task, s[3].Task);
                Join(workers);
                Wait(() => combined.IsCompleted);

                Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo((0, 10, 20, 30)));
            }
        }

        [Test]
        public void TaskBackedInputs_PublishOnTheProducer_WithoutPostingToTheCreatorContext()
        {
            SynchronizationContext previous = SynchronizationContext.Current;
            CountingContext context = new CountingContext();
            TaskCompletionSource<int> first = new TaskCompletionSource<int>();
            TaskCompletionSource<string> second = new TaskCompletionSource<string>();
            OnityTask<(int, string)> combined;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                combined = OnityTask.WhenAll(OnityTask<int>.FromTask(first.Task), OnityTask<string>.FromTask(second.Task));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            Join(Task.Run(() =>
            {
                first.TrySetResult(1);
                second.TrySetResult("two");
            }));
            Wait(() => combined.IsCompleted);

            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo((1, "two")));
            Assert.That(Volatile.Read(ref context.Posts), Is.Zero);
        }

        [Test]
        public void CompletionCallback_CanStartAndConsumeAnotherTupleWhenAll()
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
            OnityTask<(int, int)> combined = OnityTask.WhenAll(first.Task, second.Task);
            (int, int) nested = default;
            Exception error = null;
            combined.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    combined.GetAwaiter().GetResult();
                    OnityTaskCompletionSource<int> inner = new OnityTaskCompletionSource<int>();
                    OnityTask<(int, int)> innerCombined = OnityTask.WhenAll(inner.Task, OnityTask.FromResult(2));
                    inner.TrySetResult(1);
                    nested = innerCombined.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });

            first.TrySetResult(10);
            second.TrySetResult(20);

            Assert.That(error, Is.Null);
            Assert.That(nested, Is.EqualTo((1, 2)));
        }

        private readonly struct PoolProbe : IEquatable<PoolProbe>
        {
            public PoolProbe(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public bool Equals(PoolProbe other)
            {
                return string.Equals(Name, other.Name, StringComparison.Ordinal);
            }

            public override bool Equals(object other)
            {
                return other is PoolProbe probe && Equals(probe);
            }

            public override int GetHashCode()
            {
                return Name == null ? 0 : Name.GetHashCode();
            }
        }

        private sealed class CountingContext : SynchronizationContext
        {
            public int Posts;

            public override void Post(SendOrPostCallback callback, object state)
            {
                Interlocked.Increment(ref Posts);
            }
        }
    }
}
