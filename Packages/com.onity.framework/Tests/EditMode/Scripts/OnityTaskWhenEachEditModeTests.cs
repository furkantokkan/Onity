using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using static Onity.Tests.EditMode.OnityTaskCompositionTestSupport;

namespace Onity.Tests.EditMode
{
    /// <summary>Pins <c>OnityTask.WhenEach</c> and <see cref="OnityWhenEachResult{T}"/>.</summary>
    [TestFixture]
    public sealed class OnityTaskWhenEachEditModeTests
    {
        [Test]
        public void PendingInputs_AreYieldedInCompletionOrder()
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(3);
            List<int> received = new List<int>();
            OnityTask<int> consumer = Collect(OnityTask.WhenEach(s[0].Task, s[1].Task, s[2].Task), received);

            s[2].TrySetResult(30);
            Assert.That(received, Is.EqualTo(new[] { 30 }));
            s[0].TrySetResult(10);
            Assert.That(consumer.IsCompleted, Is.False);
            s[1].TrySetResult(20);

            Assert.That(consumer.IsCompletedSuccessfully, Is.True);
            Assert.That(consumer.GetAwaiter().GetResult(), Is.EqualTo(3));
            Assert.That(received, Is.EqualTo(new[] { 30, 10, 20 }));
        }

        [Test]
        public void AlreadyCompletedInputs_AreYieldedInArgumentOrder()
        {
            OnityTaskCompletionSource<int>[] s = Sources<int>(3);
            s[2].TrySetResult(3);
            s[0].TrySetResult(1);
            s[1].TrySetResult(2);

            OnityWhenEachResult<int>[] results = Read(OnityTask.WhenEach(s[0].Task, s[1].Task, s[2].Task).ToArrayAsync());

            Assert.That(results.Length, Is.EqualTo(3));
            Assert.That(results[0].Result, Is.EqualTo(1));
            Assert.That(results[1].Result, Is.EqualTo(2));
            Assert.That(results[2].Result, Is.EqualTo(3));
        }

        [Test]
        public void FaultsAndCancellations_BecomeFailedItems_WithoutEndingTheStream()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            InvalidOperationException failure = new InvalidOperationException("input failed");
            OnityTaskCompletionSource<int>[] s = Sources<int>(3);
            s[0].TrySetResult(5);
            s[1].TrySetException(failure);
            s[2].TrySetCanceled(cancellation.Token);

            OnityWhenEachResult<int>[] results = Read(OnityTask.WhenEach(s[0].Task, s[1].Task, s[2].Task).ToArrayAsync());

            Assert.That(results[0].IsCompletedSuccessfully, Is.True);
            Assert.That(results[0].GetResult(), Is.EqualTo(5));
            Assert.That(results[1].IsFaulted, Is.True);
            Assert.That(results[1].Exception, Is.SameAs(failure));
            Assert.That(Assert.Throws<InvalidOperationException>(() => results[1].GetResult()), Is.SameAs(failure));
            Assert.That(results[2].IsFaulted, Is.True);
            Assert.That(results[2].Exception, Is.InstanceOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)results[2].Exception).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(FaultObserved(s[1].Task.AsTask()), Is.True);
        }

        [Test]
        public void EmptyInputs_EndImmediately()
        {
            Assert.That(Read(OnityTask.WhenEach(new OnityTask<int>[0]).ToArrayAsync()), Is.Empty);
            Assert.That(Read(OnityTask.WhenEach(new List<OnityTask<int>>()).ToArrayAsync()), Is.Empty);
        }

        [Test]
        public void NullInputs_ThrowArgumentNullException()
        {
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => OnityTask.WhenEach((OnityTask<int>[])null)).ParamName, Is.EqualTo("tasks"));
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => OnityTask.WhenEach((IEnumerable<OnityTask<int>>)null)).ParamName, Is.EqualTo("tasks"));
        }

        [Test]
        public void DuplicateSingleConsumerInput_IsRejectedAtTheCall_WithoutClaimingIt()
        {
            CompositionGate gate = new CompositionGate();
            OnityTask<int> native = Native(gate, 4);

            Assert.Throws<ArgumentException>(() => OnityTask.WhenEach(native, OnityTask.FromResult(1), native));

            gate.Complete();
            Assert.That(native.GetAwaiter().GetResult(), Is.EqualTo(4));
        }

        [Test]
        public void SequenceInput_IsEnumeratedOnce_AndCallerArrayWritesDoNotChangeTheSnapshot()
        {
            int enumerations = 0;

            IEnumerable<OnityTask<int>> Produce()
            {
                enumerations++;
                yield return OnityTask.FromResult(1);
                yield return OnityTask.FromResult(2);
            }

            OnityWhenEachResult<int>[] fromSequence = Read(OnityTask.WhenEach(Produce()).ToArrayAsync());
            OnityTask<int>[] array = { OnityTask.FromResult(7) };
            IOnityAsyncEnumerable<OnityWhenEachResult<int>> fromArray = OnityTask.WhenEach(array);
            array[0] = OnityTask.FromResult(99);

            Assert.That(enumerations, Is.EqualTo(1));
            Assert.That(fromSequence.Length, Is.EqualTo(2));
            Assert.That(Read(fromArray.ToArrayAsync())[0].Result, Is.EqualTo(7));
        }

        [Test]
        public void SecondEnumeration_Throws()
        {
            IOnityAsyncEnumerable<OnityWhenEachResult<int>> stream = OnityTask.WhenEach(OnityTask.FromResult(1));
            IOnityAsyncEnumerator<OnityWhenEachResult<int>> first = stream.GetAsyncEnumerator();

            Assert.Throws<InvalidOperationException>(() => stream.GetAsyncEnumerator());
            Read(first.DisposeAsync());
        }

        [Test]
        public void NativeInputs_AreClaimedOnEnumeratorCreation_AndConsumedOnce()
        {
            CompositionGate gate = new CompositionGate();
            OnityTask<int> native = Native(gate, 8);
            IOnityAsyncEnumerable<OnityWhenEachResult<int>> stream = OnityTask.WhenEach(native);
            Assert.DoesNotThrow(() => _ = native.IsCompleted, "Creating the stream does not claim the input.");

            IOnityAsyncEnumerator<OnityWhenEachResult<int>> enumerator = stream.GetAsyncEnumerator();
            Assert.Throws<InvalidOperationException>(() => native.GetAwaiter().OnCompleted(() => { }),
                "The enumerator observes the input.");
            OnityTask<bool> move = enumerator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);
            gate.Complete();

            Assert.That(Read(move), Is.True);
            Assert.That(enumerator.Current.Result, Is.EqualTo(8));
            Assert.That(Read(enumerator.MoveNextAsync()), Is.False);
            Read(enumerator.DisposeAsync());
            Assert.Throws<InvalidOperationException>(() => _ = native.IsCompleted);
        }

        [Test]
        public void EarlyDisposal_StillConsumesLaterInputs_AndDiscardsTheirOutcomes()
        {
            CompositionGate gate = new CompositionGate();
            OnityTask<int> native = Native(gate, 2);
            OnityTaskCompletionSource<int> faulting = new OnityTaskCompletionSource<int>();
            IOnityAsyncEnumerator<OnityWhenEachResult<int>> enumerator =
                OnityTask.WhenEach(OnityTask.FromResult(1), native, faulting.Task).GetAsyncEnumerator();

            Assert.That(Read(enumerator.MoveNextAsync()), Is.True);
            Assert.That(enumerator.Current.Result, Is.EqualTo(1));
            OnityTask<bool> pendingMove = enumerator.MoveNextAsync();
            OnityTask disposal = enumerator.DisposeAsync();

            Assert.That(Read(pendingMove), Is.False, "Disposal ends an outstanding move.");
            Read(disposal);
            gate.Complete();
            faulting.TrySetException(new InvalidOperationException("after disposal"));

            Assert.Throws<InvalidOperationException>(() => _ = native.IsCompleted, "The native input was consumed.");
            Assert.That(FaultObserved(faulting.Task.AsTask()), Is.True, "The late fault was observed.");
            Assert.That(Read(enumerator.MoveNextAsync()), Is.False);
        }

        [Test]
        public void CanceledEnumeration_StopsWaiting_ButInputsAreStillObserved()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            CompositionGate gate = new CompositionGate();
            OnityTask<int> native = Native(gate, 3);
            IOnityAsyncEnumerator<OnityWhenEachResult<int>> enumerator =
                OnityTask.WhenEach(native).GetAsyncEnumerator(cancellation.Token);

            OnityTask<bool> move = enumerator.MoveNextAsync();
            cancellation.Cancel();

            Wait(() => move.IsCompleted);
            Assert.That(move.IsCanceled, Is.True);
            Read(enumerator.DisposeAsync());
            gate.Complete();
            Assert.Throws<InvalidOperationException>(() => _ = native.IsCompleted, "The native input was consumed.");
        }

        [Test]
        public void StaleInput_BecomesAFailedItem()
        {
            OnityTask<int> stale = CreateStaleTask();

            OnityWhenEachResult<int>[] results = Read(OnityTask.WhenEach(stale, OnityTask.FromResult(5)).ToArrayAsync());

            Assert.That(results[0].Exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(results[1].Result, Is.EqualTo(5));
        }

        [Test]
        public void ConcurrentProducers_YieldEveryOutcomeOnce()
        {
            for (int repeat = 0; repeat < 16; repeat++)
            {
                OnityTaskCompletionSource<int>[] s = Sources<int>(16);
                OnityTask<int>[] inputs = new OnityTask<int>[s.Length];
                for (int i = 0; i < s.Length; i++)
                {
                    inputs[i] = s[i].Task;
                }

                OnityTask<OnityWhenEachResult<int>[]> all = OnityTask.WhenEach(inputs).ToArrayAsync();
                Task[] workers = new Task[s.Length];
                for (int i = 0; i < workers.Length; i++)
                {
                    int index = i;
                    workers[i] = Task.Run(() => s[index].TrySetResult(index));
                }

                Join(workers);
                Wait(() => all.IsCompleted);
                OnityWhenEachResult<int>[] results = all.GetAwaiter().GetResult();
                bool[] seen = new bool[s.Length];
                foreach (OnityWhenEachResult<int> result in results)
                {
                    Assert.That(seen[result.Result], Is.False);
                    seen[result.Result] = true;
                }

                Assert.That(results.Length, Is.EqualTo(s.Length));
            }
        }

        [Test]
        public void Result_ConstructorsAccessorsAndFormatting()
        {
            InvalidOperationException failure = new InvalidOperationException("broken");
            OnityWhenEachResult<string> success = new OnityWhenEachResult<string>("value");
            OnityWhenEachResult<string> failed = new OnityWhenEachResult<string>(failure);
            OnityWhenEachResult<string> empty = default;

            Assert.Throws<ArgumentNullException>(() => _ = new OnityWhenEachResult<string>((Exception)null));
            Assert.That(success.IsCompletedSuccessfully && !success.IsFaulted, Is.True);
            Assert.That(success.GetResult(), Is.EqualTo("value"));
            Assert.DoesNotThrow(() => success.TryThrow());
            Assert.That(success.ToString(), Is.EqualTo("value"));
            Assert.That(failed.IsFaulted && !failed.IsCompletedSuccessfully, Is.True);
            Assert.That(failed.Result, Is.Null);
            Assert.That(Assert.Throws<InvalidOperationException>(() => failed.TryThrow()), Is.SameAs(failure));
            Assert.That(failed.ToString(), Is.EqualTo("Exception{broken}"));
            Assert.That(empty.IsCompletedSuccessfully, Is.True);
            Assert.That(empty.ToString(), Is.Empty);
        }

        private static async OnityTask<int> Collect(
            IOnityAsyncEnumerable<OnityWhenEachResult<int>> source,
            List<int> received)
        {
            int count = 0;
            await foreach (OnityWhenEachResult<int> item in source)
            {
                received.Add(item.GetResult());
                count++;
            }

            return count;
        }

        private static T Read<T>(OnityTask<T> task)
        {
            Assert.That(task.IsCompleted, Is.True);
            return task.GetAwaiter().GetResult();
        }

        private static void Read(OnityTask task)
        {
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }
    }
}
