using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityTaskWhenAnyArrayEditModeTests
    {
        private const BindingFlags k_private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void InvalidArrays_ThrowBeforeCreatingAConsumer()
        {
            Assert.Throws<ArgumentNullException>(() => OnityTask.WhenAny((OnityTask[])null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.WhenAny((OnityTask<int>[])null));
            Assert.Throws<ArgumentException>(() => OnityTask.WhenAny(new OnityTask[0]));
            Assert.Throws<ArgumentException>(() => OnityTask.WhenAny(new OnityTask<int>[0]));
        }

        [Test]
        public void DefaultAndSingleInputs_ReturnIndexZeroAndExactValue()
        {
            Assert.That(OnityTask.WhenAny(new OnityTask[] { default }).GetAwaiter().GetResult(), Is.Zero);
            var emptyValue = OnityTask.WhenAny(new OnityTask<int>[] { default }).GetAwaiter().GetResult();
            Assert.That(emptyValue.winnerIndex, Is.Zero);
            Assert.That(emptyValue.result, Is.Zero);
            object value = new object();
            var reference = OnityTask.WhenAny(new[] { OnityTask<object>.FromResult(value) });
            Assert.That(reference.GetAwaiter().GetResult().result, Is.SameAs(value));
        }

        [TestCase(2)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(32)]
        [TestCase(33)]
        public void PendingInputs_WinnerKeepsIndexAndValue_AndEveryLoserSettles(int count)
        {
            var sources = Sources(count);
            var race = OnityTask.WhenAny(Inputs(sources));
            try
            {
                Assert.That(race.IsCompleted, Is.False);
                sources[count - 1].TrySetResult(123);
                var result = race.GetAwaiter().GetResult();
                Assert.That(result.winnerIndex, Is.EqualTo(count - 1));
                Assert.That(result.result, Is.EqualTo(123));
            }
            finally
            {
                Finish(sources);
            }
        }

        [Test]
        public void SnapshotAndAlreadyTerminalTie_UseOriginalLowestIndex()
        {
            var source = new OnityTaskCompletionSource<int>();
            var inputs = new[] { source.Task, OnityTask<int>.FromResult(20), OnityTask<int>.FromResult(30) };
            var race = OnityTask.WhenAny(inputs);
            inputs[0] = OnityTask<int>.FromResult(999);
            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((1, 20)));
            source.TrySetException(new InvalidOperationException("snapshotted loser"));
            Assert.That(FaultObserved(source.Task.AsTask()), Is.True);

            var pending = Sources(3);
            inputs = Inputs(pending);
            race = OnityTask.WhenAny(inputs);
            inputs[2] = OnityTask<int>.FromResult(999);
            try
            {
                pending[2].TrySetResult(33);
                Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((2, 33)));
            }
            finally
            {
                Finish(pending);
            }
        }

        [Test]
        public void DuplicateNative_RejectionClaimsNothing_AndNextRentalIsUsable()
        {
            for (int i = 0; i < 8; i++)
            {
                OnityTask<int> completed = OnityTask.WhenAny(new[] { OnityTask.Completed });
                var gate = new Gate();
                var duplicate = Native(gate, 7);
                Assert.Throws<ArgumentException>(() => OnityTask.WhenAny(
                    new[] { completed, duplicate, duplicate }));
                Assert.That(completed.GetAwaiter().GetResult(), Is.Zero);
                gate.Complete();
                Assert.That(duplicate.GetAwaiter().GetResult(), Is.EqualTo(7));
                Assert.That(OnityTask.WhenAny(new[] { OnityTask<int>.FromResult(i) })
                    .GetAwaiter().GetResult(), Is.EqualTo((0, i)));
            }
            var plainGate = new Gate();
            var plain = NativeUntyped(plainGate);
            Assert.Throws<ArgumentException>(() => OnityTask.WhenAny(new[] { OnityTask.Completed, plain, plain }));
            plainGate.Complete();
            Assert.DoesNotThrow(() => plain.GetAwaiter().GetResult());
        }

        [TestCase(0, 3)]
        [TestCase(1, 3)]
        [TestCase(2, 3)]
        [TestCase(0, 17)]
        [TestCase(1, 17)]
        [TestCase(2, 17)]
        [TestCase(0, 32)]
        [TestCase(1, 32)]
        [TestCase(2, 32)]
        [TestCase(0, 33)]
        [TestCase(1, 33)]
        [TestCase(2, 33)]
        public void ShareablePreservedAndTaskDuplicates_AreAllowed(int kind, int count)
        {
            var source = new OnityTaskCompletionSource<int>();
            var gate = new Gate();
            OnityTask<int> input = kind == 0 ? source.Task : kind == 1
                ? Native(gate, 44).Preserve() : OnityTask<int>.FromTask(source.Task.AsTask());
            var inputs = new OnityTask<int>[count];
            for (int i = 0; i < count; i++)
            {
                inputs[i] = input;
            }
            var race = OnityTask.WhenAny(inputs);
            var plainSource = new OnityTaskCompletionSource();
            var plainGate = new Gate();
            OnityTask plain = kind == 0 ? plainSource.Task : kind == 1
                ? NativeUntyped(plainGate).Preserve() : OnityTask.FromTask(plainSource.Task.AsTask());
            var plainInputs = new OnityTask[count];
            for (int i = 0; i < count; i++)
            {
                plainInputs[i] = plain;
            }
            var plainRace = OnityTask.WhenAny(plainInputs);
            try
            {
                source.TrySetResult(44);
                gate.Complete();
                Wait(() => race.IsCompleted);
                var result = race.GetAwaiter().GetResult();
                Assert.That(result.result, Is.EqualTo(44));
                if (kind == 2)
                {
                    Assert.That(result.winnerIndex, Is.InRange(0, count - 1));
                }
                else
                {
                    Assert.That(result.winnerIndex, Is.Zero);
                }
                plainSource.TrySetResult();
                plainGate.Complete();
                Wait(() => plainRace.IsCompleted);
                int plainIndex = plainRace.GetAwaiter().GetResult();
                if (kind == 2)
                {
                    Assert.That(plainIndex, Is.InRange(0, count - 1));
                }
                else
                {
                    Assert.That(plainIndex, Is.Zero);
                }
            }
            finally
            {
                source.TrySetResult(44);
                plainSource.TrySetResult();
                gate.Complete();
                plainGate.Complete();
            }
        }

        [TestCase(17)]
        [TestCase(32)]
        [TestCase(33)]
        public void LargeDuplicateNative_RejectionClaimsNothing_AndNextRentalIsUsable(int count)
        {
            var gate = new Gate();
            var native = Native(gate, 73);
            var completed = OnityTask.WhenAny(new[] { OnityTask.Completed });
            var inputs = new OnityTask<int>[count];
            inputs[0] = completed;
            inputs[count - 2] = native;
            inputs[count - 1] = native;
            var plainGate = new Gate();
            var plain = NativeUntyped(plainGate);
            var plainInputs = new OnityTask[count];
            plainInputs[count - 2] = plain;
            plainInputs[count - 1] = plain;
            try
            {
                Assert.Throws<ArgumentException>(() => OnityTask.WhenAny(inputs));
                Assert.That(completed.GetAwaiter().GetResult(), Is.Zero);
                gate.Complete();
                Assert.That(native.GetAwaiter().GetResult(), Is.EqualTo(73));
                Assert.Throws<ArgumentException>(() => OnityTask.WhenAny(plainInputs));
                plainGate.Complete();
                Assert.DoesNotThrow(() => plain.GetAwaiter().GetResult());

                var sources = Sources(count);
                try
                {
                    var recovered = OnityTask.WhenAny(Inputs(sources));
                    sources[count - 1].TrySetResult(91);
                    Assert.That(recovered.GetAwaiter().GetResult(), Is.EqualTo((count - 1, 91)));
                }
                finally
                {
                    Finish(sources);
                }
            }
            finally
            {
                gate.Complete();
                plainGate.Complete();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MediumPool_32Then17Then32_ReusesSourceWithoutRetainedInputsOrIndexState(bool typed)
        {
            object previous = null;
            foreach (int count in new[] { 32, 17, 32 })
            {
                var sources = Sources(count);
                var plainSources = new OnityTaskCompletionSource[count];
                for (int i = 0; i < count; i++)
                {
                    plainSources[i] = new OnityTaskCompletionSource();
                }
                try
                {
                    object state;
                    if (typed)
                    {
                        var race = OnityTask.WhenAny(Inputs(sources));
                        state = GetState(race);
                        if (previous != null)
                        {
                            Assert.That(state, Is.SameAs(previous));
                        }
                        sources[count - 1].TrySetResult(count * 10);
                        Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((count - 1, count * 10)));
                    }
                    else
                    {
                        var inputs = new OnityTask[count];
                        for (int i = 0; i < count; i++)
                        {
                            inputs[i] = plainSources[i].Task;
                        }
                        var race = OnityTask.WhenAny(inputs);
                        state = GetState(race);
                        if (previous != null)
                        {
                            Assert.That(state, Is.SameAs(previous));
                        }
                        plainSources[count - 1].TrySetResult();
                        Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo(count - 1));
                    }
                    Finish(sources);
                    foreach (var source in plainSources)
                    {
                        source.TrySetResult();
                    }
                    Array retainedInputs = (Array)state.GetType().GetField("m_inputs", k_private).GetValue(state);
                    for (int i = 0; i < retainedInputs.Length; i++)
                    {
                        object input = retainedInputs.GetValue(i);
                        Assert.That(input.GetType().GetField("m_state", k_private).GetValue(input), Is.Null,
                            "A settled pool entry must not retain input " + i);
                    }
                    previous = state;
                }
                finally
                {
                    Finish(sources);
                    foreach (var source in plainSources)
                    {
                        source.TrySetResult();
                    }
                }
            }
        }

        [TestCase(16)]
        [TestCase(17)]
        [TestCase(32)]
        [TestCase(33)]
        public void ReleasedOutput_IsNotRentedWhileLosersArePending_AndLateLosersLeaveFreshRaceAlone(int count)
        {
            var oldSources = Sources(count);
            var freshSources = Sources(count);
            var old = OnityTask.WhenAny(Inputs(oldSources));
            object oldState = GetState(old);
            try
            {
                oldSources[0].TrySetResult(1);
                Assert.That(old.GetAwaiter().GetResult(), Is.EqualTo((0, 1)));
                var fresh = OnityTask.WhenAny(Inputs(freshSources));
                Assert.That(GetState(fresh), Is.Not.SameAs(oldState));
                Finish(oldSources);
                Assert.That(fresh.IsCompleted, Is.False);
                freshSources[count - 1].TrySetResult(2);
                Assert.That(fresh.GetAwaiter().GetResult(), Is.EqualTo((count - 1, 2)));
                Assert.Throws<InvalidOperationException>(() => old.GetAwaiter().GetResult());
            }
            finally
            {
                Finish(oldSources);
                Finish(freshSources);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void WinnerFailure_PreservesOriginalFaultCancellationAndFaultedOce(int kind)
        {
            var sources = Sources(3);
            var race = OnityTask.WhenAny(Inputs(sources));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Exception fault = kind == 2 ? new OperationCanceledException(cancellation.Token)
                    : new AggregateException(new InvalidOperationException("original nested fault"));
                try
                {
                    if (kind == 1)
                    {
                        sources[1].TrySetCanceled(cancellation.Token);
                    }
                    else if (kind == 2)
                    {
                        SetFault(sources[1], fault);
                    }
                    else
                    {
                        sources[1].TrySetException(fault);
                    }
                    if (kind == 1)
                    {
                        Assert.That(race.IsCanceled, Is.True);
                        Assert.That(Assert.Throws<OperationCanceledException>(() => race.GetAwaiter().GetResult())
                            .CancellationToken, Is.EqualTo(cancellation.Token));
                    }
                    else
                    {
                        Assert.That(race.IsFaulted, Is.True);
                        Assert.That(Assert.Catch(() => race.GetAwaiter().GetResult()), Is.SameAs(fault));
                    }
                }
                finally
                {
                    Finish(sources);
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LateLoserFaults_AreObservedBeforeDuringAndAfterInputBridgeCreation(int bridgeTiming)
        {
            var sources = Sources(3);
            Task<int> bridge = bridgeTiming == 0 ? sources[2].Task.AsTask() : null;
            var race = OnityTask.WhenAny(Inputs(sources));
            sources[0].TrySetResult(5);
            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((0, 5)));
            Task worker = null;
            try
            {
                if (bridgeTiming == 1)
                {
                    worker = Task.Run(() =>
                    {
                        bridge = sources[2].Task.AsTask();
                    });
                }
                sources[1].TrySetException(new InvalidOperationException("first loser"));
                sources[2].TrySetException(new InvalidOperationException("second loser"));
                if (worker != null)
                {
                    Join(worker);
                }
                bridge = bridge ?? sources[2].Task.AsTask();
                Assert.That(bridge.IsFaulted, Is.True);
                Assert.That(FaultObserved(bridge), Is.True);
                Assert.That(FaultObserved(sources[1].Task.AsTask()), Is.True);
            }
            finally
            {
                Finish(sources);
                if (worker != null)
                {
                    Join(worker);
                }
            }
        }

        [Test]
        public void Output_NativeIsExclusive_BridgeIsCached_AndPreserveIsShareable()
        {
            var sources = Sources(3);
            var native = OnityTask.WhenAny(Inputs(sources));
            sources[0].TrySetResult(1);
            native.GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => native.GetAwaiter().GetResult());
            Finish(sources);
            sources = Sources(3);
            var bridged = OnityTask.WhenAny(Inputs(sources));
            Task<(int winnerIndex, int result)> bridge = bridged.AsTask();
            Assert.That(bridged.AsTask(), Is.SameAs(bridge));
            sources[1].TrySetResult(2);
            Join(bridge);
            Assert.That(bridge.GetAwaiter().GetResult(), Is.EqualTo((1, 2)));
            Assert.Throws<InvalidOperationException>(() => bridged.GetAwaiter().GetResult());
            Finish(sources);
            sources = Sources(3);
            var preserved = OnityTask.WhenAny(Inputs(sources)).Preserve();
            sources[2].TrySetResult(3);
            Assert.That(preserved.GetAwaiter().GetResult(), Is.EqualTo((2, 3)));
            Assert.That(preserved.GetAwaiter().GetResult(), Is.EqualTo((2, 3)));
            Finish(sources);
        }

        [Test]
        public void ReleasedOutput_StillObservesNativeLoser_AndCannotCorruptFreshRentals()
        {
            var gate = new Gate();
            var loser = Native(gate, 17);
            var winner = new OnityTaskCompletionSource<int>();
            var old = OnityTask.WhenAny(new[] { winner.Task, loser, OnityTask<int>.FromResult(10) });
            Assert.That(old.GetAwaiter().GetResult(), Is.EqualTo((2, 10)));
            for (int count = 16; count >= 2; count -= 14)
            {
                var sources = Sources(count);
                var fresh = OnityTask.WhenAny(Inputs(sources));
                sources[count - 1].TrySetResult(count);
                Assert.That(fresh.GetAwaiter().GetResult(), Is.EqualTo((count - 1, count)));
                Finish(sources);
            }
            gate.Complete();
            winner.TrySetResult(9);
            Assert.Throws<InvalidOperationException>(() => loser.GetAwaiter().GetResult());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StaleAndPreclaimedInputs_AreFaultOutcomes_AndDoNotReplaceEarlierWinner(bool stale)
        {
            for (int earlier = 0; earlier < 2; earlier++)
            {
                var gate = new Gate();
                var invalid = stale ? OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed)
                    : Native(gate, 1);
                if (stale)
                {
                    object state = typeof(OnityTask<int>).GetField("m_state", k_private).GetValue(invalid);
                    state.GetType().BaseType.GetMethod("Reset", k_private)
                        .Invoke(state, new object[] { CancellationToken.None });
                }
                else
                {
                    invalid.GetAwaiter().UnsafeOnCompleted(() => invalid.GetAwaiter().GetResult());
                }
                var other = new OnityTaskCompletionSource<int>();
                var inputs = earlier == 0 ? new[] { invalid, other.Task }
                    : new[] { OnityTask<int>.FromResult(55), invalid, other.Task };
                var race = OnityTask.WhenAny(inputs);
                if (earlier == 0)
                {
                    Assert.That(race.IsFaulted, Is.True);
                    Assert.Throws<InvalidOperationException>(() => race.GetAwaiter().GetResult());
                }
                else
                {
                    Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((0, 55)));
                }
                if (!stale)
                {
                    gate.Complete();
                }
                other.TrySetException(new InvalidOperationException("observed despite bad input"));
                Assert.That(FaultObserved(other.Task.AsTask()), Is.True);
            }
        }

        [Test]
        public void RegistrationAndConcurrentCompletionRaces_AreBoundedAndKeepWinningPairTogether()
        {
            for (int repeat = 0; repeat < 24; repeat++)
            {
                var sources = Sources(8);
                Task[] workers = new Task[sources.Length];
                for (int i = 0; i < workers.Length; i++)
                {
                    int index = i;
                    workers[i] = Task.Run(() => sources[index].TrySetResult(index * 10));
                }
                try
                {
                    var race = OnityTask.WhenAny(Inputs(sources));
                    Join(workers);
                    Wait(() => race.IsCompleted);
                    var result = race.GetAwaiter().GetResult();
                    Assert.That(result.winnerIndex, Is.InRange(0, 7));
                    Assert.That(result.result, Is.EqualTo(result.winnerIndex * 10));
                }
                finally
                {
                    Finish(sources);
                    Join(workers);
                }
            }
        }

        [Test]
        public void CompletionCallback_CanRegisterAndConsumeAnotherArrayRace()
        {
            var sources = Sources(3);
            var race = OnityTask.WhenAny(Inputs(sources));
            (int winnerIndex, int result) nested = default;
            Exception error = null;
            race.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    race.GetAwaiter().GetResult();
                    nested = OnityTask.WhenAny(new[] { OnityTask<int>.FromResult(7), default })
                        .GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });
            sources[1].TrySetResult(22);
            Finish(sources);
            Assert.That(error, Is.Null);
            Assert.That(nested, Is.EqualTo((0, 7)));
        }

        [Test]
        public void TaskBackedObservers_DoNotCaptureQueuedSynchronizationContext()
        {
            SynchronizationContext previous = SynchronizationContext.Current;
            var context = new CountingContext();
            var sources = new[] { new TaskCompletionSource<int>(), new TaskCompletionSource<int>(),
                new TaskCompletionSource<int>() };
            OnityTask<(int winnerIndex, int result)> race;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                race = OnityTask.WhenAny(new[] { OnityTask<int>.FromTask(sources[0].Task),
                    OnityTask<int>.FromTask(sources[1].Task), OnityTask<int>.FromTask(sources[2].Task) });
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
            Task worker = Task.Run(() => sources[1].TrySetResult(31));
            try
            {
                Join(worker);
                Wait(() => race.IsCompleted);
                Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((1, 31)));
                Assert.That(Volatile.Read(ref context.posts), Is.Zero);
            }
            finally
            {
                foreach (var source in sources)
                {
                    source.TrySetResult(0);
                }
                Join(worker);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void UntypedMixedArray_PreservesWinnerOutcome_AndObservesLateTaskFault(int outcome)
        {
            var source = new OnityTaskCompletionSource();
            var loser = new TaskCompletionSource<bool>();
            var gate = new Gate();
            var native = NativeUntyped(gate);
            var race = OnityTask.WhenAny(new[] { native, source.Task.Preserve(), OnityTask.FromTask(loser.Task) });
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var fault = new InvalidOperationException("untyped winner");
                try
                {
                    if (outcome == 0)
                    {
                        source.TrySetResult();
                    }
                    else if (outcome == 1)
                    {
                        source.TrySetException(fault);
                    }
                    else
                    {
                        source.TrySetCanceled(cancellation.Token);
                    }
                    if (outcome == 0)
                    {
                        Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo(1));
                    }
                    else if (outcome == 1)
                    {
                        Assert.That(Assert.Throws<InvalidOperationException>(() => race.GetAwaiter().GetResult()), Is.SameAs(fault));
                    }
                    else
                    {
                        Assert.That(Assert.Throws<OperationCanceledException>(() => race.GetAwaiter().GetResult())
                            .CancellationToken, Is.EqualTo(cancellation.Token));
                    }
                    loser.TrySetException(new InvalidOperationException("late Task loser"));
                    Wait(() => FaultObserved(loser.Task));
                    gate.Complete();
                    Assert.Throws<InvalidOperationException>(() => native.GetAwaiter().GetResult());
                }
                finally
                {
                    source.TrySetResult();
                    loser.TrySetResult(false);
                    gate.Complete();
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DerivedCompletionSource_LoserFaultObservesExistingConcurrentAndFutureBridge(
            int bridgeTiming)
        {
            var derived = new DerivedSource<int>();
            Task<int> bridge = bridgeTiming == 0 ? derived.Task.AsTask() : null;
            var race = OnityTask.WhenAny(new[] { OnityTask<int>.FromResult(7), derived.Task });
            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((0, 7)));
            Task worker = null;
            try
            {
                if (bridgeTiming == 1)
                {
                    worker = Task.Run(() =>
                    {
                        bridge = derived.Task.AsTask();
                    });
                }
                derived.TrySetException(new InvalidOperationException("derived native loser"));
                if (worker != null)
                {
                    Join(worker);
                }
                bridge = bridge ?? derived.Task.AsTask();
                Assert.That(bridge.IsFaulted, Is.True);
                Assert.That(FaultObserved(bridge), Is.True,
                    "Consuming a supported derived source must observe existing and future Task bridge faults.");
            }
            finally
            {
                derived.TrySetResult(0);
                if (worker != null)
                {
                    Join(worker);
                }
                // Avoid leaving the deliberate regression fault unobserved after recording its state.
                if (bridge != null && bridge.IsFaulted)
                {
                    GC.KeepAlive(bridge.Exception);
                }
            }
        }

        private static object GetState<T>(OnityTask<T> task) =>
            typeof(OnityTask<T>).GetField("m_state", k_private).GetValue(task);

        private sealed class DerivedSource<T> : OnityTaskCompletionSource<T>
        {
        }

        private static OnityTaskCompletionSource<int>[] Sources(int count)
        {
            var sources = new OnityTaskCompletionSource<int>[count];
            for (int i = 0; i < count; i++)
            {
                sources[i] = new OnityTaskCompletionSource<int>();
            }
            return sources;
        }

        private static OnityTask<int>[] Inputs(OnityTaskCompletionSource<int>[] sources)
        {
            var inputs = new OnityTask<int>[sources.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = sources[i].Task;
            }
            return inputs;
        }

        private static void Finish(OnityTaskCompletionSource<int>[] sources)
        {
            foreach (var source in sources)
            {
                source.TrySetResult(0);
            }
        }

        private static void Join(params Task[] tasks) => Assert.That(
            Task.WaitAll(tasks, TimeSpan.FromSeconds(5)), Is.True, "Owned worker timed out.");

        private static void Wait(Func<bool> completed) => Assert.That(
            SpinWait.SpinUntil(completed, TimeSpan.FromSeconds(5)), Is.True, "Array race timed out.");

        private static bool FaultObserved(Task task)
        {
            object contingent = typeof(Task).GetField("m_contingentProperties", k_private).GetValue(task);
            object holder = contingent.GetType().GetField("m_exceptionsHolder", k_private).GetValue(contingent);
            return (bool)holder.GetType().GetField("m_isHandled", k_private).GetValue(holder);
        }

        private static void SetFault(OnityTaskCompletionSource<int> source, Exception fault) =>
            typeof(OnityTaskCompletionSource<int>).GetMethod("TrySetFault", k_private)
                .Invoke(source, new object[] { fault });

        private static async OnityTask<int> Native(Gate gate, int value)
        {
            await gate;
            return value;
        }

        private static async OnityTask NativeUntyped(Gate gate)
        {
            await gate;
        }

        private sealed class Gate : ICriticalNotifyCompletion
        {
            private Action m_continuation;
            public bool IsCompleted => false;
            public Gate GetAwaiter() => this;
            public void GetResult()
            {
            }
            public void OnCompleted(Action continuation) => m_continuation = continuation;
            public void UnsafeOnCompleted(Action continuation) => m_continuation = continuation;
            public void Complete() => Interlocked.Exchange(ref m_continuation, null)?.Invoke();
        }

        private sealed class CountingContext : SynchronizationContext
        {
            public int posts;
            public override void Post(SendOrPostCallback callback, object state) => Interlocked.Increment(ref posts);
        }
    }
}
