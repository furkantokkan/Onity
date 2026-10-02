using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableConversionEditModeTests
    {
        // A sequence that counts enumerator creation and disposal and can inject failures.
        private sealed class ProbeSequence : IEnumerable<int>
        {
            private readonly int[] m_items;
            internal int Created;
            internal int Disposed;
            internal int MovesMade;
            internal Exception GetEnumeratorFailure;
            internal Exception MoveFailure;
            internal int MoveFailureAt = -1;
            internal Exception DisposeFailure;
            internal bool ReturnNullEnumerator;
            internal Action<int> OnItem;

            internal ProbeSequence(params int[] items)
            {
                m_items = items;
            }

            public IEnumerator<int> GetEnumerator()
            {
                Created++;
                if (GetEnumeratorFailure != null)
                {
                    throw GetEnumeratorFailure;
                }
                return ReturnNullEnumerator ? null : new Enumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator : IEnumerator<int>
            {
                private readonly ProbeSequence m_owner;
                private int m_position = -1;

                internal Enumerator(ProbeSequence owner)
                {
                    m_owner = owner;
                }

                public int Current => m_owner.m_items[m_position];
                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    int move = m_owner.MovesMade++;
                    if (move == m_owner.MoveFailureAt)
                    {
                        throw m_owner.MoveFailure;
                    }
                    m_position++;
                    bool has = m_position < m_owner.m_items.Length;
                    if (has)
                    {
                        m_owner.OnItem?.Invoke(m_owner.m_items[m_position]);
                    }
                    return has;
                }

                public void Reset() => throw new NotSupportedException();

                public void Dispose()
                {
                    m_owner.Disposed++;
                    if (m_owner.DisposeFailure != null)
                    {
                        throw m_owner.DisposeFailure;
                    }
                }
            }
        }

        private static T Read<T>(OnityTask<T> task) => LinqTestSupport.Read(task);
        private static void Read(OnityTask task) => LinqTestSupport.Read(task);
        private static T[] ToArray<T>(IOnityAsyncEnumerable<T> source) => LinqTestSupport.ToArray(source);
        private static Exception Catch(Action action) => LinqTestSupport.Catch(action);
        private static void Wait(Func<bool> done) => LinqTestSupport.Wait(done);

        // ---- IEnumerable -----------------------------------------------------------------

        [Test]
        public void Enumerable_Arguments_AreValidated_AndTheDescriptionIsLazy()
        {
            IEnumerable<int> missing = null;
            Assert.Throws<ArgumentNullException>(() => missing.ToOnityAsyncEnumerable());
            Assert.Throws<ArgumentNullException>(() => ((Task<int>)null).ToOnityAsyncEnumerable());
            IOnityAsyncEnumerable<int> nothing = null;
            Assert.Throws<ArgumentNullException>(() => nothing.AsOnityAsyncEnumerable());

            var sequence = new ProbeSequence(1, 2);
            var stream = sequence.ToOnityAsyncEnumerable();
            stream.GetAsyncEnumerator().DisposeAsync();
            Assert.That(sequence.Created, Is.Zero);
            Assert.That(sequence.Disposed, Is.Zero);
        }

        [Test]
        public void Enumerable_YieldsItemsInOrder_AndDisposesTheEnumeratorOnce()
        {
            var sequence = new ProbeSequence(1, 2, 3);
            Assert.That(ToArray(sequence.ToOnityAsyncEnumerable()), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(sequence.Created, Is.EqualTo(1));
            Assert.That(sequence.Disposed, Is.EqualTo(1));
            Assert.That(ToArray(new ProbeSequence().ToOnityAsyncEnumerable()), Is.Empty);
            Assert.That(ToArray(new List<string> { "a", "b" }.ToOnityAsyncEnumerable()), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(ToArray(new[] { 5, 6 }.ToOnityAsyncEnumerable()), Is.EqualTo(new[] { 5, 6 }));
            Assert.That(ToArray("abc".ToOnityAsyncEnumerable()), Is.EqualTo(new[] { 'a', 'b', 'c' }));
        }

        [Test]
        public void Enumerable_IsReusable_WithIndependentEnumerators()
        {
            var sequence = new ProbeSequence(1, 2);
            var stream = sequence.ToOnityAsyncEnumerable();
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(sequence.Created, Is.EqualTo(2));
            Assert.That(sequence.Disposed, Is.EqualTo(2));
        }

        [Test]
        public void Enumerable_EarlyConsumerExit_DisposesTheEnumeratorOnce()
        {
            var sequence = new ProbeSequence(1, 2, 3, 4);
            Assert.That(Read(sequence.ToOnityAsyncEnumerable().FirstAsync()), Is.EqualTo(1));
            Assert.That(sequence.MovesMade, Is.EqualTo(1));
            Assert.That(sequence.Disposed, Is.EqualTo(1));
            Assert.That(ToArray(sequence.ToOnityAsyncEnumerable().Take(2)), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(sequence.Disposed, Is.EqualTo(2));
        }

        [Test]
        public void Enumerable_MoveNextFailure_FaultsTheStream_AndDisposesOnce()
        {
            var fault = new InvalidOperationException("move");
            var sequence = new ProbeSequence(1, 2, 3);
            sequence.MoveFailureAt = 1;
            sequence.MoveFailure = fault;
            var task = sequence.ToOnityAsyncEnumerable().ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(sequence.Disposed, Is.EqualTo(1));
        }

        [Test]
        public void Enumerable_GetEnumeratorFailureAndNullEnumerator_FaultTheStream()
        {
            var fault = new InvalidOperationException("get");
            var failing = new ProbeSequence(1);
            failing.GetEnumeratorFailure = fault;
            Assert.That(Catch(() => ToArray(failing.ToOnityAsyncEnumerable())), Is.SameAs(fault));

            var nullEnumerator = new ProbeSequence(1);
            nullEnumerator.ReturnNullEnumerator = true;
            Assert.That(Catch(() => ToArray(nullEnumerator.ToOnityAsyncEnumerable())),
                Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Enumerable_DisposeFailure_TakesPrecedence()
        {
            var cleanup = new InvalidOperationException("dispose");
            var sequence = new ProbeSequence(1, 2);
            sequence.DisposeFailure = cleanup;
            var task = sequence.ToOnityAsyncEnumerable().ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(cleanup));
        }

        [Test]
        public void Enumerable_Cancellation_BeforeAndDuringTheEnumeration()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var sequence = new ProbeSequence(1, 2);
                var task = sequence.ToOnityAsyncEnumerable().ToArrayAsync(cts.Token);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(sequence.Created, Is.Zero);
            }
            using (var cts = new CancellationTokenSource())
            {
                var sequence = new ProbeSequence(1, 2, 3, 4);
                sequence.OnItem = item =>
                {
                    if (item == 3)
                    {
                        cts.Cancel();
                    }
                };
                var task = sequence.ToOnityAsyncEnumerable().ToArrayAsync(cts.Token);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(sequence.Disposed, Is.EqualTo(1));
            }
        }

        [Test]
        public void Enumerable_LargeSequence_DoesNotRecurse()
        {
            Assert.That(Read(Enumerable.Range(0, 100000).ToOnityAsyncEnumerable().CountAsync()), Is.EqualTo(100000));
        }

        // ---- Task<T> ---------------------------------------------------------------------

        [Test]
        public void Task_CompletedTask_YieldsItsResultOnce()
        {
            var iterator = Task.FromResult(5).ToOnityAsyncEnumerable().GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.True);
            Assert.That(Read(move), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(5));
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Read(iterator.DisposeAsync());
        }

        [Test]
        public void Task_PendingTask_WaitsForTheResult_AndEndsAfterIt()
        {
            var source = new TaskCompletionSource<int>();
            var task = source.Task.ToOnityAsyncEnumerable().ToArrayAsync();
            Assert.That(task.IsCompleted, Is.False);
            source.SetResult(9);
            Wait(() => task.IsCompleted);
            Assert.That(Read(task), Is.EqualTo(new[] { 9 }));
        }

        [Test]
        public void Task_FaultedAndCanceledTasks_KeepTheirStatus()
        {
            var fault = new InvalidOperationException("task");
            var faulted = Task.FromException<int>(fault).ToOnityAsyncEnumerable().ToArrayAsync();
            Assert.That(faulted.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(faulted)), Is.SameAs(fault));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var canceled = Task.FromCanceled<int>(cts.Token).ToOnityAsyncEnumerable().ToArrayAsync();
                Assert.That(canceled.IsCanceled, Is.True);
            }
            var source = new TaskCompletionSource<int>();
            var late = source.Task.ToOnityAsyncEnumerable().ToArrayAsync();
            source.SetException(fault);
            Wait(() => late.IsCompleted);
            Assert.That(Catch(() => Read(late)), Is.SameAs(fault));
        }

        [Test]
        public void Task_IsReusable_BecauseATaskHasManyConsumers()
        {
            var stream = Task.FromResult(3).ToOnityAsyncEnumerable();
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 3 }));
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void Task_TokenCancellationWhilePending_CancelsTheMove_AndALateResultIsDropped()
        {
            using (var cts = new CancellationTokenSource())
            {
                var source = new TaskCompletionSource<int>();
                var task = source.Task.ToOnityAsyncEnumerable().ToArrayAsync(cts.Token);
                Assert.That(task.IsCompleted, Is.False);
                cts.Cancel();
                Wait(() => task.IsCompleted);
                Assert.That(task.IsCanceled, Is.True);
                Assert.DoesNotThrow(() => source.SetResult(1));
            }
        }

        [Test]
        public void Task_PreCanceledToken_CancelsWithoutObservingTheTask()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var source = new TaskCompletionSource<int>();
                var task = source.Task.ToOnityAsyncEnumerable().ToArrayAsync(cts.Token);
                Assert.That(task.IsCanceled, Is.True);
                source.SetResult(1);
                Assert.That(ToArray(source.Task.ToOnityAsyncEnumerable()), Is.EqualTo(new[] { 1 }));
            }
        }

        [Test]
        public void Task_DisposeWhilePending_EndsTheMoveWithFalse_AndALateFaultIsDropped()
        {
            var source = new TaskCompletionSource<int>();
            var iterator = source.Task.ToOnityAsyncEnumerable().GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);
            var dispose = iterator.DisposeAsync();
            Read(dispose);
            Assert.That(Read(move), Is.False);
            Assert.DoesNotThrow(() => source.SetException(new InvalidOperationException("late")));
        }

        // ---- OnityTask<T> ----------------------------------------------------------------

        [Test]
        public void OnityTask_Typed_YieldsItsResult_ForCompletedPendingAndPooledTasks()
        {
            Assert.That(ToArray(OnityTask<int>.FromResult(4).ToOnityAsyncEnumerable()), Is.EqualTo(new[] { 4 }));

            var source = new OnityTaskCompletionSource<int>();
            var pending = source.Task.ToOnityAsyncEnumerable().ToArrayAsync();
            Assert.That(pending.IsCompleted, Is.False);
            source.TrySetResult(8);
            Assert.That(Read(pending), Is.EqualTo(new[] { 8 }));

            var gate = new OnityTaskCompletionSource();
            async OnityTask<int> Pooled()
            {
                await gate.Task;
                return 11;
            }
            var pooled = Pooled().ToOnityAsyncEnumerable().ToArrayAsync();
            Assert.That(pooled.IsCompleted, Is.False);
            gate.TrySetResult();
            Assert.That(Read(pooled), Is.EqualTo(new[] { 11 }));
        }

        [Test]
        public void OnityTask_Typed_FaultAndCancellation_KeepTheirStatus()
        {
            var fault = new InvalidOperationException("task");
            var faulted = new OnityTaskCompletionSource<int>();
            var faultedTask = faulted.Task.ToOnityAsyncEnumerable().ToArrayAsync();
            faulted.TrySetException(fault);
            Assert.That(faultedTask.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(faultedTask)), Is.SameAs(fault));

            var canceled = new OnityTaskCompletionSource<int>();
            var canceledTask = canceled.Task.ToOnityAsyncEnumerable().ToArrayAsync();
            canceled.TrySetCanceled();
            Assert.That(canceledTask.IsCanceled, Is.True);
        }

        [Test]
        public void OnityTask_Typed_TokenCancellationAndDisposeReleaseAPendingMove()
        {
            using (var cts = new CancellationTokenSource())
            {
                var source = new OnityTaskCompletionSource<int>();
                var task = source.Task.ToOnityAsyncEnumerable().ToArrayAsync(cts.Token);
                Assert.That(task.IsCompleted, Is.False);
                cts.Cancel();
                Assert.That(task.IsCanceled, Is.True);
                Assert.DoesNotThrow(() => source.TrySetResult(1));
            }

            var other = new OnityTaskCompletionSource<int>();
            var iterator = other.Task.ToOnityAsyncEnumerable().GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);
            Read(iterator.DisposeAsync());
            Assert.That(Read(move), Is.False);
            Assert.DoesNotThrow(() => other.TrySetException(new InvalidOperationException("late")));
        }

        // ---- OnityTask (untyped) ---------------------------------------------------------

        [Test]
        public void OnityTask_Untyped_YieldsOneUnitWhenTheTaskCompletes()
        {
            Assert.That(ToArray(OnityTask.Completed.ToOnityAsyncEnumerable()), Is.EqualTo(new[] { Unit.Default }));

            var source = new OnityTaskCompletionSource();
            var pending = source.Task.ToOnityAsyncEnumerable().ToArrayAsync();
            Assert.That(pending.IsCompleted, Is.False);
            source.TrySetResult();
            Assert.That(Read(pending).Length, Is.EqualTo(1));
        }

        [Test]
        public void OnityTask_Untyped_FaultAndCancellation_KeepTheirStatus()
        {
            var fault = new InvalidOperationException("task");
            var faulted = OnityTask.FromException(fault).ToOnityAsyncEnumerable().ToArrayAsync();
            Assert.That(faulted.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(faulted)), Is.SameAs(fault));

            var source = new OnityTaskCompletionSource();
            var canceled = source.Task.ToOnityAsyncEnumerable().ToArrayAsync();
            source.TrySetCanceled();
            Assert.That(canceled.IsCanceled, Is.True);

            var late = new OnityTaskCompletionSource();
            var lateFault = late.Task.ToOnityAsyncEnumerable().ToArrayAsync();
            late.TrySetException(fault);
            Assert.That(Catch(() => Read(lateFault)), Is.SameAs(fault));
        }

        [Test]
        public void OnityTask_Untyped_PreCanceledTokenAndDispose()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var source = new OnityTaskCompletionSource();
                Assert.That(source.Task.ToOnityAsyncEnumerable().ToArrayAsync(cts.Token).IsCanceled, Is.True);
            }
            var pending = new OnityTaskCompletionSource();
            var iterator = pending.Task.ToOnityAsyncEnumerable().GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Read(iterator.DisposeAsync());
            Assert.That(Read(move), Is.False);
            Assert.DoesNotThrow(() => pending.TrySetResult());
        }

        // ---- identity and composition ----------------------------------------------------

        [Test]
        public void AsOnityAsyncEnumerable_ReturnsTheSameInstance()
        {
            var source = OnityAsyncEnumerable.Range(0, 2);
            Assert.That(source.AsOnityAsyncEnumerable(), Is.SameAs(source));
        }

        [Test]
        public void ConvertedStreams_ComposeWithOperators()
        {
            Assert.That(ToArray(new[] { 1, 2, 3, 4 }.ToOnityAsyncEnumerable().Where(value => value % 2 == 0)
                .Select(value => value * 3)), Is.EqualTo(new[] { 6, 12 }));
            Assert.That(Read(Task.FromResult(21).ToOnityAsyncEnumerable().Select(value => value * 2).FirstAsync()),
                Is.EqualTo(42));
            Assert.That(ToArray(new[] { 1, 2, 3 }.ToOnityAsyncEnumerable().Reverse()), Is.EqualTo(new[] { 3, 2, 1 }));
        }
    }
}
