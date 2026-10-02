using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using static Onity.Tests.EditMode.OnityTaskCompositionTestSupport;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Pins the sequence overloads of WhenAll/WhenAny, the left/right WhenAny and the
    /// <see cref="OnityEnumerableAsyncExtensions"/> projections.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskCompositionEnumerableEditModeTests
    {
        [Test]
        public void SequenceOverloads_NullSequence_ThrowsArgumentNullException()
        {
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => OnityTask.WhenAll((IEnumerable<OnityTask>)null)).ParamName, Is.EqualTo("tasks"));
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => OnityTask.WhenAll((IEnumerable<OnityTask<int>>)null)).ParamName, Is.EqualTo("tasks"));
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => OnityTask.WhenAny((IEnumerable<OnityTask>)null)).ParamName, Is.EqualTo("tasks"));
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => OnityTask.WhenAny((IEnumerable<OnityTask<int>>)null)).ParamName, Is.EqualTo("tasks"));
        }

        [Test]
        public void WhenAll_EmptySequences_CompleteImmediately()
        {
            OnityTask untyped = OnityTask.WhenAll(new List<OnityTask>());
            OnityTask<int[]> typed = OnityTask.WhenAll(new List<OnityTask<int>>());

            Wait(() => untyped.IsCompleted);
            Assert.That(untyped.IsCompletedSuccessfully, Is.True);
            Assert.That(typed.GetAwaiter().GetResult(), Is.Empty);
        }

        [Test]
        public void WhenAll_TypedList_ReturnsResultsInSequenceOrder()
        {
            OnityTaskCompletionSource<int>[] sources = Sources<int>(3);
            List<OnityTask<int>> inputs = new List<OnityTask<int>>
            {
                sources[0].Task,
                sources[1].Task,
                sources[2].Task
            };

            OnityTask<int[]> combined = OnityTask.WhenAll(inputs);
            sources[2].TrySetResult(30);
            sources[0].TrySetResult(10);
            Assert.That(combined.IsCompleted, Is.False);
            sources[1].TrySetResult(20);

            Wait(() => combined.IsCompleted);
            Assert.That(combined.GetAwaiter().GetResult(), Is.EqualTo(new[] { 10, 20, 30 }));
        }

        [Test]
        public void WhenAll_LazySequences_AreEnumeratedOnce_AcrossBufferGrowth()
        {
            int typedEnumerations = 0;
            int untypedEnumerations = 0;

            IEnumerable<OnityTask<int>> ProduceTyped()
            {
                typedEnumerations++;
                for (int i = 0; i < 9; i++)
                {
                    yield return OnityTask.FromResult(i);
                }
            }

            IEnumerable<OnityTask> ProduceUntyped()
            {
                untypedEnumerations++;
                for (int i = 0; i < 5; i++)
                {
                    yield return OnityTask.CompletedTask;
                }
            }

            int[] results = OnityTask.WhenAll(ProduceTyped()).GetAwaiter().GetResult();
            OnityTask untyped = OnityTask.WhenAll(ProduceUntyped());

            Assert.That(results, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }));
            Assert.That(typedEnumerations, Is.EqualTo(1));
            Wait(() => untyped.IsCompleted);
            Assert.That(untyped.IsCompletedSuccessfully, Is.True);
            Assert.That(untypedEnumerations, Is.EqualTo(1));
        }

        [Test]
        public void WhenAll_UntypedSequence_WaitsForEveryInput_AndKeepsFaultsInSequenceOrder()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTaskCompletionSource third = new OnityTaskCompletionSource();
            Exception firstFailure = new InvalidOperationException("first");
            Exception thirdFailure = new ArgumentException("third");
            OnityTask combined = OnityTask.WhenAll(new List<OnityTask> { first.Task, second.Task, third.Task });

            third.TrySetException(thirdFailure);
            second.TrySetResult();
            Assert.That(combined.IsCompleted, Is.False);
            first.TrySetException(firstFailure);

            Wait(() => combined.IsCompleted);
            Assert.That(combined.IsFaulted, Is.True);
            AggregateException aggregate = combined.AsTask().Exception;
            Assert.That(aggregate.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(aggregate.InnerExceptions[0], Is.SameAs(firstFailure));
            Assert.That(aggregate.InnerExceptions[1], Is.SameAs(thirdFailure));
        }

        [Test]
        public void WhenAny_Sequences_ReturnWinnerIndexAndResult()
        {
            OnityTaskCompletionSource<int>[] sources = Sources<int>(3);
            OnityTaskCompletionSource[] plainSources =
            {
                new OnityTaskCompletionSource(),
                new OnityTaskCompletionSource(),
                new OnityTaskCompletionSource()
            };
            OnityTask<(int winnerIndex, int result)> typed = OnityTask.WhenAny(
                new List<OnityTask<int>> { sources[0].Task, sources[1].Task, sources[2].Task });
            OnityTask<int> plain = OnityTask.WhenAny(
                new List<OnityTask> { plainSources[0].Task, plainSources[1].Task, plainSources[2].Task });

            sources[1].TrySetResult(22);
            plainSources[2].TrySetResult();

            Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo((1, 22)));
            Assert.That(plain.GetAwaiter().GetResult(), Is.EqualTo(2));
            sources[0].TrySetResult(0);
            sources[2].TrySetResult(0);
            plainSources[0].TrySetResult();
            plainSources[1].TrySetResult();
        }

        [Test]
        public void WhenAny_EmptySequences_ThrowArgumentException()
        {
            Assert.That(Assert.Throws<ArgumentException>(
                () => OnityTask.WhenAny(new List<OnityTask>())).ParamName, Is.EqualTo("tasks"));
            Assert.That(Assert.Throws<ArgumentException>(
                () => OnityTask.WhenAny(new List<OnityTask<int>>())).ParamName, Is.EqualTo("tasks"));
        }

        [Test]
        public void WhenAny_SequenceRepeatingSingleConsumerTask_IsRejectedWithoutConsumingIt()
        {
            CompositionGate gate = new CompositionGate();
            OnityTask<int> native = Native(gate, 5);

            Assert.Throws<ArgumentException>(
                () => OnityTask.WhenAny(new List<OnityTask<int>> { native, native }));

            gate.Complete();
            Assert.That(native.GetAwaiter().GetResult(), Is.EqualTo(5));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WhenAnyLeftRight_PendingWinner_ReportsTheWinningSide(bool leftWins)
        {
            OnityTaskCompletionSource<int> left = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource right = new OnityTaskCompletionSource();
            OnityTask<(bool hasResultLeft, int result)> race = OnityTask.WhenAny(left.Task, right.Task);

            Assert.That(race.IsCompleted, Is.False);
            if (leftWins)
            {
                left.TrySetResult(7);
            }
            else
            {
                right.TrySetResult();
            }

            Assert.That(race.IsCompletedSuccessfully, Is.True);
            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo(leftWins ? (true, 7) : (false, 0)));
            Assert.That(left.TrySetResult(9) || right.TrySetResult(), Is.True, "The loser still completes.");
        }

        [Test]
        public void WhenAnyLeftRight_AlreadyCompletedInputs_LeftWinsTheTie()
        {
            OnityTask<(bool hasResultLeft, int result)> both =
                OnityTask.WhenAny(OnityTask.FromResult(3), OnityTask.CompletedTask);
            OnityTaskCompletionSource<int> pendingLeft = new OnityTaskCompletionSource<int>();
            OnityTask<(bool hasResultLeft, int result)> rightOnly =
                OnityTask.WhenAny(pendingLeft.Task, OnityTask.CompletedTask);

            Assert.That(both.GetAwaiter().GetResult(), Is.EqualTo((true, 3)));
            Assert.That(rightOnly.GetAwaiter().GetResult(), Is.EqualTo((false, 0)));
            pendingLeft.TrySetResult(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WhenAnyLeftRight_WinnerFault_PropagatesTheSameInstance(bool leftFaults)
        {
            OnityTaskCompletionSource<int> left = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource right = new OnityTaskCompletionSource();
            InvalidOperationException failure = new InvalidOperationException("winner failed");
            OnityTask<(bool hasResultLeft, int result)> race = OnityTask.WhenAny(left.Task, right.Task);

            if (leftFaults)
            {
                left.TrySetException(failure);
            }
            else
            {
                right.TrySetException(failure);
            }

            Assert.That(race.IsFaulted, Is.True);
            Assert.That(Assert.Throws<InvalidOperationException>(() => race.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            left.TrySetResult(1);
            right.TrySetResult();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WhenAnyLeftRight_WinnerCancellation_PreservesTheToken(bool leftCancels)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int> left = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource right = new OnityTaskCompletionSource();
            OnityTask<(bool hasResultLeft, int result)> race = OnityTask.WhenAny(left.Task, right.Task);

            if (leftCancels)
            {
                left.TrySetCanceled(cancellation.Token);
            }
            else
            {
                right.TrySetCanceled(cancellation.Token);
            }

            Assert.That(race.IsCanceled, Is.True);
            Assert.That(Assert.Catch<OperationCanceledException>(() => race.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(cancellation.Token));
            left.TrySetResult(1);
            right.TrySetResult();
        }

        [Test]
        public void WhenAnyLeftRight_FaultedOperationCanceledException_RemainsFaulted()
        {
            OperationCanceledException failure = new OperationCanceledException("faulted input");
            OnityTask<(bool hasResultLeft, int result)> race = OnityTask.WhenAny(
                OnityTask<int>.FromException(failure), OnityTask.CompletedTask);

            Assert.That(race.IsFaulted, Is.True);
            Task<(bool hasResultLeft, int result)> bridge = race.AsTask();
            Assert.That(bridge.IsFaulted, Is.True);
            Assert.That(bridge.Exception.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void WhenAnyLeftRight_LateLoserFault_IsObserved()
        {
            OnityTaskCompletionSource<int> left = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource right = new OnityTaskCompletionSource();
            OnityTask<(bool hasResultLeft, int result)> race = OnityTask.WhenAny(left.Task, right.Task);

            left.TrySetResult(1);
            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((true, 1)));
            right.TrySetException(new InvalidOperationException("late loser"));

            Assert.That(FaultObserved(right.Task.AsTask()), Is.True);
        }

        [Test]
        public void WhenAnyLeftRight_NativeLoser_IsClaimedAndConsumedWhenItCompletes()
        {
            CompositionGate gate = new CompositionGate();
            OnityTask loser = NativeUntyped(gate);
            OnityTask<(bool hasResultLeft, int result)> race = OnityTask.WhenAny(OnityTask.FromResult(4), loser);

            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((true, 4)));
            Assert.Throws<InvalidOperationException>(() => loser.GetAwaiter().OnCompleted(() => { }),
                "The race still observes the loser.");
            gate.Complete();
            Assert.Throws<InvalidOperationException>(() => _ = loser.IsCompleted,
                "The loser is consumed when it completes.");
        }

        [Test]
        public void WhenAnyLeftRight_SameShareableSourceOnBothSides_IsAllowed()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTask<bool> typedView = ((OnityTaskCompletionSource<bool>)source).Task;
            OnityTask<(bool hasResultLeft, bool result)> race = OnityTask.WhenAny(typedView, source.Task);

            source.TrySetResult();

            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((true, true)));
        }

        [Test]
        public void WhenAnyLeftRight_StaleLeft_FaultsAndStillObservesRight()
        {
            OnityTask<int> stale = CreateStaleTask();
            OnityTaskCompletionSource right = new OnityTaskCompletionSource();
            OnityTask<(bool hasResultLeft, int result)> race = OnityTask.WhenAny(stale, right.Task);

            Assert.That(race.IsFaulted, Is.True);
            Assert.Throws<InvalidOperationException>(() => race.GetAwaiter().GetResult());
            right.TrySetException(new InvalidOperationException("observed despite a stale left input"));
            Assert.That(FaultObserved(right.Task.AsTask()), Is.True);
        }

        [Test]
        public void WhenAnyLeftRight_Output_NativeIsExclusive_BridgeIsCached_AndPreserveIsShareable()
        {
            OnityTaskCompletionSource<int> left = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource right = new OnityTaskCompletionSource();
            OnityTask<(bool hasResultLeft, int result)> bridged = OnityTask.WhenAny(left.Task, right.Task);
            Task<(bool hasResultLeft, int result)> bridge = bridged.AsTask();
            Assert.That(bridged.AsTask(), Is.SameAs(bridge));
            right.TrySetResult();
            Join(bridge);
            Assert.That(bridge.Result, Is.EqualTo((false, 0)));
            Assert.Throws<InvalidOperationException>(() => bridged.GetAwaiter().GetResult());
            left.TrySetResult(1);

            OnityTaskCompletionSource<int> sharedLeft = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource sharedRight = new OnityTaskCompletionSource();
            OnityTask<(bool hasResultLeft, int result)> shared =
                OnityTask.WhenAny(sharedLeft.Task, sharedRight.Task).Preserve();
            sharedLeft.TrySetResult(8);
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo((true, 8)));
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo((true, 8)));
            sharedRight.TrySetResult();
        }

        [Test]
        public void WhenAnyLeftRight_SettledSource_IsReusedWithoutRetainingInputs()
        {
            object previous = null;
            for (int i = 0; i < 3; i++)
            {
                OnityTaskCompletionSource<PoolProbe> left = new OnityTaskCompletionSource<PoolProbe>();
                OnityTaskCompletionSource right = new OnityTaskCompletionSource();
                OnityTask<(bool hasResultLeft, PoolProbe result)> race = OnityTask.WhenAny(left.Task, right.Task);
                object state = GetState(race);
                if (previous != null)
                {
                    Assert.That(state, Is.SameAs(previous), "A settled source returns to its pool.");
                }

                left.TrySetResult(new PoolProbe(i));
                Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((true, new PoolProbe(i))));
                right.TrySetResult();

                Assert.That(IsReleasedTaskValue(GetField(state, "m_left")), Is.True);
                Assert.That(IsReleasedTaskValue(GetField(state, "m_right")), Is.True);
                previous = state;
            }
        }

        [Test]
        public void WhenAnyLeftRight_ConcurrentCompletions_KeepSideAndValueTogether()
        {
            for (int i = 0; i < 32; i++)
            {
                TaskCompletionSource<int> left =
                    new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<bool> right =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                OnityTask<(bool hasResultLeft, int result)> race =
                    OnityTask.WhenAny(OnityTask<int>.FromTask(left.Task), OnityTask.FromTask(right.Task));
                Task<(bool hasResultLeft, int result)> bridge = race.AsTask();

                Join(Task.Run(() => left.SetResult(11)), Task.Run(() => right.SetResult(true)));
                Join(bridge);
                (bool hasResultLeft, int result) = bridge.Result;
                Assert.That(result, Is.EqualTo(hasResultLeft ? 11 : 0));
            }
        }

        [Test]
        public void Select_ProjectsLazily_AndRunsTheSelectorAgainPerEnumeration()
        {
            int calls = 0;
            int[] items = { 1, 2, 3 };
            IEnumerable<OnityTask<int>> projected = items.Select(item =>
            {
                calls++;
                return OnityTask.FromResult(item * 10);
            });

            Assert.That(calls, Is.Zero);
            Assert.That(OnityTask.WhenAll(projected).GetAwaiter().GetResult(), Is.EqualTo(new[] { 10, 20, 30 }));
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(OnityTask.WhenAll(projected).GetAwaiter().GetResult(), Is.EqualTo(new[] { 10, 20, 30 }));
            Assert.That(calls, Is.EqualTo(6));
        }

        [Test]
        public void Select_Indexed_PassesTheZeroBasedIndex()
        {
            string[] items = { "a", "b", "c" };
            List<int> seen = new List<int>();

            string[] typed = OnityTask.WhenAll(items.Select((item, index) => OnityTask.FromResult(item + index)))
                .GetAwaiter().GetResult();
            OnityTask untyped = OnityTask.WhenAll(items.Select((item, index) =>
            {
                seen.Add(index);
                return OnityTask.CompletedTask;
            }));

            Assert.That(typed, Is.EqualTo(new[] { "a0", "b1", "c2" }));
            Wait(() => untyped.IsCompleted);
            Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void Select_AsyncLambdas_ProduceOnityTasks()
        {
            CompositionGate[] gates = { new CompositionGate(), new CompositionGate() };
            int[] items = { 0, 1 };

            IEnumerable<OnityTask> untyped = items.Select(async item => { await gates[item]; });
            OnityTask allUntyped = OnityTask.WhenAll(untyped);
            gates[1].Complete();
            Assert.That(allUntyped.IsCompleted, Is.False);
            gates[0].Complete();
            Wait(() => allUntyped.IsCompleted);
            Assert.That(allUntyped.IsCompletedSuccessfully, Is.True);

            CompositionGate[] typedGates = { new CompositionGate(), new CompositionGate() };
            IEnumerable<OnityTask<int>> typed = items.Select(async item =>
            {
                await typedGates[item];
                return item + 100;
            });
            OnityTask<int[]> allTyped = OnityTask.WhenAll(typed);
            typedGates[0].Complete();
            typedGates[1].Complete();
            Wait(() => allTyped.IsCompleted);
            Assert.That(allTyped.GetAwaiter().GetResult(), Is.EqualTo(new[] { 100, 101 }));
        }

        [Test]
        public void Select_NullArguments_ThrowWhenCalled()
        {
            int[] items = { 1 };
            Assert.Throws<ArgumentNullException>(
                () => ((IEnumerable<int>)null).Select(item => OnityTask.CompletedTask));
            Assert.Throws<ArgumentNullException>(() => items.Select((Func<int, OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => items.Select((Func<int, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => items.Select((Func<int, int, OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => items.Select((Func<int, int, OnityTask<int>>)null));
        }

        private readonly struct PoolProbe : IEquatable<PoolProbe>
        {
            public PoolProbe(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(PoolProbe other)
            {
                return Value == other.Value;
            }

            public override bool Equals(object other)
            {
                return other is PoolProbe probe && Equals(probe);
            }

            public override int GetHashCode()
            {
                return Value;
            }
        }
    }
}
