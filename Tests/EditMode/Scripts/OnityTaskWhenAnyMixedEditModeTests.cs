using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using static Onity.Tests.EditMode.OnityTaskCompositionTestSupport;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Pins the generated mixed <c>OnityTask.WhenAny&lt;T1..Tn&gt;</c> overloads, which return
    /// <c>(int winArgumentIndex, T1 result1, ..., Tn resultN)</c>.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskWhenAnyMixedEditModeTests
    {
        [TestCase(0)]
        [TestCase(1)]
        public void PendingWinner_ReportsItsIndexAndResult_AndLeavesTheOtherElementDefault(int winner)
        {
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<string> second = new OnityTaskCompletionSource<string>();
            OnityTask<(int winArgumentIndex, int result1, string result2)> race =
                OnityTask.WhenAny(first.Task, second.Task);

            Assert.That(race.IsCompleted, Is.False);
            if (winner == 0)
            {
                first.TrySetResult(7);
            }
            else
            {
                second.TrySetResult("b");
            }

            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo(winner == 0 ? (0, 7, null) : (1, 0, "b")));
            first.TrySetResult(70);
            second.TrySetResult("late");
        }

        [Test]
        public void AlreadyCompletedInputs_TheLowestArgumentIndexWins()
        {
            OnityTask<(int winArgumentIndex, string result1, int result2, double result3)> race =
                OnityTask.WhenAny(OnityTask.FromResult("a"), OnityTask.FromResult(2), OnityTask.FromResult(3.0));

            Assert.That(race.IsCompletedSuccessfully, Is.True);
            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((0, "a", 0, 0.0)));
        }

        [Test]
        public void EightInputs_PlaceTheWinnerAcrossTheNestedTupleBoundary()
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(8);
            var race = OnityTask.WhenAny(
                s[0].Task, s[1].Task, s[2].Task, s[3].Task, s[4].Task, s[5].Task, s[6].Task, s[7].Task);

            s[7].TrySetResult(77);

            var result = race.GetAwaiter().GetResult();
            Assert.That(result.winArgumentIndex, Is.EqualTo(7));
            Assert.That(result.result8, Is.EqualTo(77));
            Assert.That(result.result1 + result.result7, Is.Zero);
            Finish(s);
        }

        [Test]
        public void FifteenInputs_ReportTheLastArgument()
        {
            OnityTaskCompletionSource<string>[] s = Sources<string>(15);
            var race = OnityTask.WhenAny(
                s[0].Task, s[1].Task, s[2].Task, s[3].Task, s[4].Task, s[5].Task, s[6].Task, s[7].Task,
                s[8].Task, s[9].Task, s[10].Task, s[11].Task, s[12].Task, s[13].Task, s[14].Task);

            s[14].TrySetResult("fifteenth");

            var result = race.GetAwaiter().GetResult();
            Assert.That(result.winArgumentIndex, Is.EqualTo(14));
            Assert.That(result.result15, Is.EqualTo("fifteenth"));
            Assert.That(result.result1, Is.Null);
            Assert.That(result.result14, Is.Null);
            Finish(s);
        }

        [TestCase(0)]
        [TestCase(2)]
        public void WinnerFault_PropagatesTheSameInstance(int winner)
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(3);
            InvalidOperationException failure = new InvalidOperationException("winner failed");
            OnityTask<(int winArgumentIndex, int result1, int result2, int result3)> race =
                OnityTask.WhenAny(s[0].Task, s[1].Task, s[2].Task);

            s[winner].TrySetException(failure);

            Assert.That(race.IsFaulted, Is.True);
            Assert.That(Assert.Throws<InvalidOperationException>(() => race.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Finish(s);
        }

        [Test]
        public void WinnerCancellation_PreservesTheToken()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<string> second = new OnityTaskCompletionSource<string>();
            OnityTask<(int winArgumentIndex, int result1, string result2)> race =
                OnityTask.WhenAny(first.Task, second.Task);

            second.TrySetCanceled(cancellation.Token);

            Assert.That(race.IsCanceled, Is.True);
            Assert.That(Assert.Catch<OperationCanceledException>(() => race.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(cancellation.Token));
            first.TrySetResult(1);
        }

        [Test]
        public void FaultedOperationCanceledException_RemainsAFault()
        {
            OperationCanceledException failure = new OperationCanceledException("faulted input");
            OnityTask<(int winArgumentIndex, int result1, string result2)> race =
                OnityTask.WhenAny(OnityTask<int>.FromException(failure), OnityTask.FromResult("unused"));

            Assert.That(race.IsFaulted, Is.True);
            Task<(int winArgumentIndex, int result1, string result2)> bridge = race.AsTask();
            Assert.That(bridge.IsFaulted, Is.True);
            Assert.That(bridge.Exception.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void LateLoserFaults_AreObserved_AndDoNotChangeTheWinner()
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(3);
            OnityTask<(int winArgumentIndex, int result1, int result2, int result3)> race =
                OnityTask.WhenAny(s[0].Task, s[1].Task, s[2].Task);
            Task<(int winArgumentIndex, int result1, int result2, int result3)> bridge = race.AsTask();

            s[1].TrySetResult(5);
            s[0].TrySetException(new InvalidOperationException("late first loser"));
            s[2].TrySetCanceled();

            Assert.That(bridge.Result, Is.EqualTo((1, 0, 5, 0)));
            Assert.That(FaultObserved(s[0].Task.AsTask()), Is.True);
        }

        [Test]
        public void NativeLoser_IsClaimed_ThenConsumedWhenItCompletes()
        {
            CompositionGate gate = new CompositionGate();
            OnityTask<string> loser = Native(gate, "late");
            OnityTask<(int winArgumentIndex, int result1, string result2)> race =
                OnityTask.WhenAny(OnityTask.FromResult(4), loser);

            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((0, 4, (string)null)));
            Assert.Throws<InvalidOperationException>(() => loser.GetAwaiter().OnCompleted(() => { }),
                "The race still observes the loser.");
            gate.Complete();
            Assert.Throws<InvalidOperationException>(() => _ = loser.IsCompleted,
                "The loser is consumed when it completes.");
        }

        [Test]
        public void DuplicateSingleConsumerInput_IsRejectedBeforeAnyInputIsClaimed()
        {
            CompositionGate gate = new CompositionGate();
            OnityTask<int> native = Native(gate, 9);
            OnityTaskCompletionSource<string> other = new OnityTaskCompletionSource<string>();

            ArgumentException rejected = Assert.Throws<ArgumentException>(
                () => OnityTask.WhenAny(native, other.Task, native));

            Assert.That(rejected.ParamName, Is.EqualTo("task3"));
            gate.Complete();
            Assert.That(native.GetAwaiter().GetResult(), Is.EqualTo(9));
            other.TrySetResult("unused");
        }

        [Test]
        public void DuplicateShareableInputs_AreAllowed_AndTheFirstArgumentWinsTheTie()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            OnityTask<(int winArgumentIndex, int result1, int result2)> race =
                OnityTask.WhenAny(source.Task, source.Task);

            source.TrySetResult(17);

            Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((0, 17, 0)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StaleOrPreclaimedInput_FaultsTheOutput_AndStillObservesTheOthers(bool stale)
        {
            CompositionGate gate = new CompositionGate();
            OnityTask<int> invalid = stale ? CreateStaleTask() : Native(gate, 1);
            if (!stale)
            {
                invalid.GetAwaiter().UnsafeOnCompleted(() => invalid.GetAwaiter().GetResult());
            }

            OnityTaskCompletionSource<string> other = new OnityTaskCompletionSource<string>();
            OnityTask<(int winArgumentIndex, int result1, string result2)> race = OnityTask.WhenAny(invalid, other.Task);

            Assert.That(race.IsFaulted, Is.True);
            Assert.Throws<InvalidOperationException>(() => race.GetAwaiter().GetResult());
            other.TrySetException(new ArgumentException("observed despite an invalid input"));
            Assert.That(FaultObserved(other.Task.AsTask()), Is.True);
            gate.Complete();
        }

        [Test]
        public void Output_NativeIsExclusive_BridgeIsCached_AndPreserveIsShareable()
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(6);
            var native = OnityTask.WhenAny(s[0].Task, s[1].Task);
            s[0].TrySetResult(1);
            Assert.That(native.GetAwaiter().GetResult(), Is.EqualTo((0, 1, 0)));
            Assert.Throws<InvalidOperationException>(() => native.GetAwaiter().GetResult());

            var bridged = OnityTask.WhenAny(s[2].Task, s[3].Task);
            Task<(int winArgumentIndex, int result1, int result2)> bridge = bridged.AsTask();
            Assert.That(bridged.AsTask(), Is.SameAs(bridge));
            s[3].TrySetResult(4);
            Join(bridge);
            Assert.That(bridge.Result, Is.EqualTo((1, 0, 4)));
            Assert.Throws<InvalidOperationException>(() => bridged.GetAwaiter().GetResult());

            var shared = OnityTask.WhenAny(s[4].Task, s[5].Task).Preserve();
            s[5].TrySetResult(6);
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo((1, 0, 6)));
            Assert.That(shared.GetAwaiter().GetResult(), Is.EqualTo((1, 0, 6)));
            Finish(s);
        }

        [Test]
        public void SettledSource_IsReusedOnlyAfterTheLoserSettles_WithoutRetainingInputs()
        {
            object previous = null;
            for (int i = 0; i < 3; i++)
            {
                OnityTaskCompletionSource<PoolProbe> first = new OnityTaskCompletionSource<PoolProbe>();
                OnityTaskCompletionSource<PoolProbe> second = new OnityTaskCompletionSource<PoolProbe>();
                var race = OnityTask.WhenAny(first.Task, second.Task);
                object state = GetState(race);
                if (previous != null)
                {
                    Assert.That(state, Is.SameAs(previous), "A settled source returns to its pool.");
                }

                first.TrySetResult(new PoolProbe(i));
                Assert.That(race.GetAwaiter().GetResult(), Is.EqualTo((0, new PoolProbe(i), default(PoolProbe))));
                OnityTaskCompletionSource<PoolProbe> next = new OnityTaskCompletionSource<PoolProbe>();
                var whileLoserPending = OnityTask.WhenAny(next.Task, next.Task);
                Assert.That(GetState(whileLoserPending), Is.Not.SameAs(state),
                    "A released output is not rented while its loser is pending.");
                next.TrySetResult(new PoolProbe(-1));
                whileLoserPending.GetAwaiter().GetResult();

                second.TrySetResult(new PoolProbe(i + 10));
                Assert.That(IsReleasedTaskValue(GetField(state, "m_task1")), Is.True);
                Assert.That(IsReleasedTaskValue(GetField(state, "m_task2")), Is.True);
                previous = state;
            }
        }

        [Test]
        public void ConcurrentCompletions_KeepTheIndexAndItsElementTogether()
        {
            for (int repeat = 0; repeat < 32; repeat++)
            {
                TaskCompletionSource<int> first =
                    new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<string> second =
                    new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var race = OnityTask.WhenAny(OnityTask<int>.FromTask(first.Task), OnityTask<string>.FromTask(second.Task));
                Task<(int winArgumentIndex, int result1, string result2)> bridge = race.AsTask();

                Join(Task.Run(() => first.SetResult(11)), Task.Run(() => second.SetResult("22")));
                Join(bridge);

                (int index, int number, string text) = bridge.Result;
                Assert.That((number, text), Is.EqualTo(index == 0 ? (11, null) : (0, "22")));
            }
        }

        private static void Finish<T>(OnityTaskCompletionSource<T>[] sources)
        {
            foreach (OnityTaskCompletionSource<T> source in sources)
            {
                source.TrySetResult(default);
            }
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
