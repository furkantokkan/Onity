using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityAsyncEnumerableEditModeTests
    {
        [Test]
        public void InvalidArguments_ThrowSynchronously_AndRangeUsesWideArithmetic()
        {
            IOnityAsyncEnumerable<int> missing = null;
            Assert.Throws<ArgumentNullException>(() => missing.WithCancellation(default));
            Assert.Throws<ArgumentNullException>(() => missing.Select(value => value));
            Assert.Throws<ArgumentNullException>(() => missing.Where(value => true));
            Assert.Throws<ArgumentNullException>(() => missing.Take(0));
            Assert.Throws<ArgumentNullException>(() => missing.FirstAsync());
            Assert.Throws<ArgumentNullException>(() => missing.ToArrayAsync());
            Assert.Throws<ArgumentNullException>(() => OnityAsyncEnumerable.Return(1).Select<int, int>((Func<int, int>)null));
            Assert.Throws<ArgumentNullException>(() => OnityAsyncEnumerable.Return(1).Where((Func<int, bool>)null));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityAsyncEnumerable.Return(1).Take(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityAsyncEnumerable.Range(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityAsyncEnumerable.Range(int.MaxValue, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityAsyncEnumerable.Range(2, int.MaxValue));
            Assert.That(Read(OnityAsyncEnumerable.Range(int.MaxValue - 1, 2).ToArrayAsync()),
                Is.EqualTo(new[] { int.MaxValue - 1, int.MaxValue }));
            var wide = OnityAsyncEnumerable.Range(int.MinValue, int.MaxValue).GetAsyncEnumerator();
            try
            {
                Assert.That(Read(wide.MoveNextAsync()), Is.True);
                Assert.That(wide.Current, Is.EqualTo(int.MinValue));
            }
            finally
            {
                Read(wide.DisposeAsync());
            }
        }

        [Test]
        public void EmptyAndZeroTake_IgnorePrecancellation_AndNeverAcquireUpstream()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var probe = new Probe();
                IOnityAsyncEnumerable<int>[] streams =
                {
                    OnityAsyncEnumerable.Empty<int>(),
                    OnityAsyncEnumerable.Range(int.MaxValue, 0),
                    probe.Take(0).WithCancellation(cancellation.Token)
                };
                foreach (var stream in streams)
                {
                    var enumerator = stream.GetAsyncEnumerator(cancellation.Token);
                    Assert.That(Read(enumerator.MoveNextAsync()), Is.False);
                    Assert.That(Read(enumerator.MoveNextAsync()), Is.False);
                    Read(enumerator.DisposeAsync());
                    Assert.That(Read(stream.ToArrayAsync(cancellation.Token)), Is.Empty);
                    var first = stream.FirstAsync(cancellation.Token);
                    Assert.That(first.IsFaulted, Is.True);
                    Assert.Throws<InvalidOperationException>(() => Read(first));
                }
                Assert.That(probe.Acquires, Is.Zero);
                Assert.That(probe.Moves, Is.Zero);
                Assert.That(probe.Disposals, Is.Zero);
            }
        }

        [Test]
        public void DescriptionsAreLazy_EnumerationsIndependent_AndCurrentHasStrictLifetime()
        {
            var probe = new Probe();
            var lazy = probe.Select(value => value + 1).Where(value => true).Take(2);
            var unstarted = lazy.GetAsyncEnumerator();
            Assert.That(probe.Acquires, Is.Zero);
            Read(unstarted.DisposeAsync());
            Assert.That(probe.Acquires, Is.Zero);
            var description = OnityAsyncEnumerable.Range(7, 2);
            var first = description.GetAsyncEnumerator();
            var second = description.GetAsyncEnumerator();
            try
            {
                Assert.Throws<InvalidOperationException>(() => { var ignored = first.Current; });
                Assert.That(Read(first.MoveNextAsync()), Is.True);
                Assert.That(first.Current, Is.EqualTo(7));
                Assert.That(Read(first.MoveNextAsync()), Is.True);
                Assert.That(first.Current, Is.EqualTo(8));
                Assert.That(Read(second.MoveNextAsync()), Is.True);
                Assert.That(second.Current, Is.EqualTo(7));
                Assert.That(Read(first.MoveNextAsync()), Is.False);
                Assert.Throws<InvalidOperationException>(() => { var ignored = first.Current; });
            }
            finally
            {
                Read(first.DisposeAsync());
                Read(second.DisposeAsync());
            }
            Assert.That(Read(second.MoveNextAsync()), Is.False);
            Assert.Throws<InvalidOperationException>(() => { var ignored = second.Current; });
        }

        [Test]
        public void InlinePipeline_PreservesOrder_TakeLimit_AndLongWhereDoesNotRecurse()
        {
            int predicates = 0;
            int selectors = 0;
            var stream = OnityAsyncEnumerable.Range(1, 10)
                .Where(value => { predicates++; return value % 2 == 0; })
                .Select(value => { selectors++; return value * 3; }).Take(3);
            Assert.That(Read(stream.ToArrayAsync()), Is.EqualTo(new[] { 6, 12, 18 }));
            Assert.That(predicates, Is.EqualTo(6));
            Assert.That(selectors, Is.EqualTo(3));
            int rejected = 0;
            var enumerator = OnityAsyncEnumerable.Range(0, 100000)
                .Where(value => { rejected++; return false; }).GetAsyncEnumerator();
            try
            {
                var move = enumerator.MoveNextAsync();
                Assert.That(move.IsCompletedSuccessfully, Is.True);
                Assert.That(Read(move), Is.False);
                Assert.That(rejected, Is.EqualTo(100000));
            }
            finally
            {
                Read(enumerator.DisposeAsync());
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActualAwaitForeach_Covariance_BreakAndBodyThrow_CleanExactlyOnce(bool throwBody)
        {
            var probe = new Probe();
            var sentinel = new InvalidOperationException("foreach body");
            var iteration = Consume(probe, throwBody, sentinel);
            Assert.That(iteration.IsCompleted, Is.True);
            if (throwBody)
            {
                Assert.That(Catch(() => Read(iteration)), Is.SameAs(sentinel));
            }
            else
            {
                Assert.That(Read(iteration), Is.EqualTo(17));
            }
            Assert.That(probe.Acquires, Is.EqualTo(1));
            Assert.That(probe.Moves, Is.EqualTo(1));
            Assert.That(probe.Disposals, Is.EqualTo(1));
            IOnityAsyncEnumerable<object> covariant = OnityAsyncEnumerable.Return("covariant");
            IOnityAsyncEnumerator<object> enumerator = OnityAsyncEnumerable.Return("enumerator").GetAsyncEnumerator();
            Assert.That(Read(covariant.FirstAsync()), Is.EqualTo("covariant"));
            Assert.That(Read(enumerator.MoveNextAsync()), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo("enumerator"));
            Read(enumerator.DisposeAsync());
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void WithCancellation_ForwardsSoleEqualAndDistinctTokens_WithoutOwningCaller(int mode)
        {
            using (var wrapper = new CancellationTokenSource())
            using (var enumeration = new CancellationTokenSource())
            {
                var probe = new Probe();
                CancellationToken other = mode == 0 ? default : mode == 1 ? wrapper.Token : enumeration.Token;
                var iterator = probe.WithCancellation(wrapper.Token).GetAsyncEnumerator(other);
                try
                {
                    Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                    Assert.That(probe.Token.CanBeCanceled, Is.True);
                    if (mode != 2)
                    {
                        Assert.That(probe.Token, Is.EqualTo(wrapper.Token));
                    }
                    else
                    {
                        Assert.That(probe.Token, Is.Not.EqualTo(wrapper.Token));
                        Assert.That(probe.Token, Is.Not.EqualTo(enumeration.Token));
                        enumeration.Cancel();
                        Assert.That(probe.Token.IsCancellationRequested, Is.True);
                    }
                }
                finally
                {
                    Read(iterator.DisposeAsync());
                }
                Assert.That(wrapper.IsCancellationRequested, Is.False);
                Assert.DoesNotThrow(() => wrapper.Cancel());
                Assert.DoesNotThrow(() => enumeration.Cancel());
                Assert.That(probe.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedCancellation_UsesEitherToken_AndPreservesActualUpstreamCancellation(bool cancelOuter)
        {
            using (var first = new CancellationTokenSource())
            using (var second = new CancellationTokenSource())
            using (var original = new CancellationTokenSource())
            {
                var pending = new OnityTaskCompletionSource<bool>();
                var probe = new Probe { Move = () => pending.Task };
                var iterator = probe.WithCancellation(first.Token).WithCancellation(second.Token).GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                try
                {
                    if (cancelOuter)
                    {
                        second.Cancel();
                    }
                    else
                    {
                        first.Cancel();
                    }
                    Assert.That(probe.Token.IsCancellationRequested, Is.True);
                    original.Cancel();
                    pending.TrySetCanceled(original.Token);
                    Assert.That(move.IsCanceled, Is.True);
                    Assert.That(((OperationCanceledException)Catch(() => Read(move))).CancellationToken,
                        Is.EqualTo(original.Token));
                }
                finally
                {
                    pending.TrySetResult(false);
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NonemptyBuiltins_CancelBeforeAdvance_AndEndedStreamsStayFalse(bool single)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var stream = single ? OnityAsyncEnumerable.Return(5) : OnityAsyncEnumerable.Range(5, 1);
                var finished = stream.GetAsyncEnumerator(cancellation.Token);
                Assert.That(Read(finished.MoveNextAsync()), Is.True);
                Assert.That(Read(finished.MoveNextAsync()), Is.False);
                cancellation.Cancel();
                Assert.That(Read(finished.MoveNextAsync()), Is.False);
                Read(finished.DisposeAsync());
                var canceled = stream.GetAsyncEnumerator(cancellation.Token);
                try
                {
                    var move = canceled.MoveNextAsync();
                    Assert.That(move.IsCanceled, Is.True);
                    Assert.That(((OperationCanceledException)Catch(() => Read(move))).CancellationToken,
                        Is.EqualTo(cancellation.Token));
                    Assert.That(canceled.MoveNextAsync().IsCanceled, Is.True);
                    Catch(() => Read(canceled.MoveNextAsync()));
                }
                finally
                {
                    Read(canceled.DisposeAsync());
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DelegateFaults_AreStickyIncludingOce_AndDisposeResetsToFalse(int operation)
        {
            var sentinel = new OperationCanceledException("delegate is a fault");
            var probe = new Probe();
            var stream = operation == 0 ? probe.Select<int, int>(value => throw sentinel)
                : operation == 1 ? probe.Where(value => throw sentinel)
                : new Probe { ReadCurrent = () => throw sentinel }.Select(value => value);
            var iterator = stream.GetAsyncEnumerator();
            var first = iterator.MoveNextAsync();
            Assert.That(first.IsFaulted, Is.True);
            Assert.That(first.IsCanceled, Is.False);
            Assert.That(Catch(() => Read(first)), Is.SameAs(sentinel));
            var again = iterator.MoveNextAsync();
            Assert.That(again.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(again)), Is.SameAs(sentinel));
            Read(iterator.DisposeAsync());
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            if (operation != 2)
            {
                Assert.That(probe.Disposals, Is.EqualTo(1));
            }
        }

        [Test]
        public void PendingMove_InvalidatesCurrent_AndOverlappingMoveRejectsWithoutAdvancing()
        {
            var pending = new OnityTaskCompletionSource<bool>();
            var probe = new Probe();
            var iterator = probe.Select(value => value).GetAsyncEnumerator();
            try
            {
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(17));
                probe.Move = () => pending.Task;
                var move = iterator.MoveNextAsync();
                Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
                Exception rejected = Catch(() => Read(iterator.MoveNextAsync()));
                Assert.That(rejected, Is.TypeOf<InvalidOperationException>());
                Assert.That(probe.Moves, Is.EqualTo(2));
                pending.TrySetResult(true);
                Assert.That(Read(move), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(17));
            }
            finally
            {
                pending.TrySetResult(false);
                Read(iterator.DisposeAsync());
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitDispose_PublishesFalseEarly_SharesCleanup_AndAwaitsBothInputs(bool cleanupFirst)
        {
            var producer = new OnityTaskCompletionSource<bool>();
            var cleanup = new OnityTaskCompletionSource();
            var probe = new Probe { Move = () => producer.Task, Cleanup = () => cleanup.Task };
            var iterator = probe.Where(value => true).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            var dispose = iterator.DisposeAsync();
            var repeat = iterator.DisposeAsync();
            try
            {
                Assert.That(Read(move), Is.False);
                Assert.That(dispose.IsCompleted, Is.False);
                Assert.That(repeat, Is.EqualTo(dispose));
                Assert.That(probe.Disposals, Is.EqualTo(1));
                if (cleanupFirst)
                {
                    cleanup.TrySetResult();
                    Assert.That(dispose.IsCompleted, Is.False);
                    producer.TrySetResult(true);
                }
                else
                {
                    producer.TrySetResult(true);
                    Assert.That(dispose.IsCompleted, Is.False);
                    cleanup.TrySetResult();
                }
                Read(dispose);
                Read(repeat);
                Assert.That(Read(iterator.MoveNextAsync()), Is.False);
                Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
            }
            finally
            {
                producer.TrySetResult(false);
                cleanup.TrySetResult();
                Read(iterator.DisposeAsync());
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void NativeCleanupMaySettleMove_IgnoredCallerTokenCannotDeadlockDisposal(int inputKind)
        {
            var gate = new Gate();
            var taskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            OnityTask<bool> input = inputKind == 2 ? new OnityTask<bool>(taskSource.Task) : Native(gate);
            if (inputKind == 1)
            {
                input = input.Preserve();
            }
            var cleanup = new OnityTaskCompletionSource();
            var probe = new Probe
            {
                Move = () => input,
                Cleanup = () =>
                {
                    gate.Complete();
                    taskSource.TrySetResult(true);
                    return cleanup.Task;
                }
            };
            using (var token = new CancellationTokenSource())
            {
                var iterator = probe.Select(value => value).GetAsyncEnumerator(token.Token);
                var move = iterator.MoveNextAsync();
                token.Cancel();
                Assert.That(move.IsCompleted, Is.False, "The test upstream deliberately ignores cancellation.");
                var dispose = iterator.DisposeAsync();
                try
                {
                    Assert.That(Read(move), Is.False);
                    Assert.That(probe.Disposals, Is.EqualTo(1));
                    Assert.That(dispose.IsCompleted, Is.False);
                    cleanup.TrySetResult();
                    Wait(() => dispose.IsCompleted);
                    Read(dispose);
                }
                finally
                {
                    gate.Complete();
                    taskSource.TrySetResult(false);
                    cleanup.TrySetResult();
                    Wait(() => dispose.IsCompleted);
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void ExplicitFalse_IsNotReplacedByLateMoveOrCleanupFault_AndLateBridgesAreObserved()
        {
            var producer = new OnityTaskCompletionSource<bool>();
            var cleanup = new OnityTaskCompletionSource();
            var late = new InvalidOperationException("late producer");
            var cleanupFault = new OperationCanceledException("cleanup faulted OCE");
            var cleanupTask = new OnityTask(Task.FromException(cleanupFault));
            var probe = new Probe { Move = () => producer.Task, Cleanup = () => cleanup.Task };
            var iterator = probe.Select(value => value).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            var disposal = iterator.DisposeAsync();
            try
            {
                Assert.That(Read(move), Is.False);
                producer.TrySetException(late);
                cleanup.TrySetException(new InvalidOperationException("cleanup"));
                Assert.That(disposal.IsFaulted, Is.True);
                Assert.That(Catch(() => Read(disposal)), Is.TypeOf<InvalidOperationException>());
                var bridge = producer.Task.AsTask();
                Assert.That(FaultObserved(bridge), Is.True);
                var faultProbe = new Probe { Move = () => OnityTask<bool>.FromResult(true), Cleanup = () => cleanupTask };
                var faultIterator = faultProbe.Select(value => value).GetAsyncEnumerator();
                Assert.That(Read(faultIterator.MoveNextAsync()), Is.True);
                var faultDispose = faultIterator.DisposeAsync();
                Wait(() => faultDispose.IsCompleted);
                Assert.That(faultDispose.IsFaulted, Is.True);
                Assert.That(faultDispose.IsCanceled, Is.False);
                Assert.That(Catch(() => Read(faultDispose)), Is.SameAs(cleanupFault));
            }
            finally
            {
                producer.TrySetResult(false);
                cleanup.TrySetResult();
                Catch(() => Read(iterator.DisposeAsync()));
                var ignored = producer.Task.AsTask().Exception;
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void OrdinaryCleanup_PrecedesResult_AndItsFaultReplacesProposedOutcome(int operation)
        {
            var cleanup = new OnityTaskCompletionSource();
            var sentinel = new InvalidOperationException("cleanup replaces result");
            var probe = new Probe
            {
                Move = () => OnityTask<bool>.FromResult(operation != 0),
                Cleanup = () => cleanup.Task
            };
            OnityTask<bool> move = default;
            OnityTask<int> first = default;
            OnityTask<int[]> array = default;
            IOnityAsyncEnumerator<int> iterator = null;
            if (operation < 2)
            {
                iterator = (operation == 0 ? probe.Where(value => true) : probe.Take(1)).GetAsyncEnumerator();
                move = iterator.MoveNextAsync();
                Assert.That(move.IsCompleted, Is.False);
            }
            else if (operation == 2)
            {
                first = probe.FirstAsync();
                Assert.That(first.IsCompleted, Is.False);
            }
            else
            {
                probe.Move = () => throw new InvalidOperationException("original operation");
                array = probe.ToArrayAsync();
                Assert.That(array.IsCompleted, Is.False);
            }
            try
            {
                Assert.That(probe.Disposals, Is.EqualTo(1));
                cleanup.TrySetException(sentinel);
                Exception actual = operation < 2 ? Catch(() => Read(move))
                    : operation == 2 ? Catch(() => Read(first)) : Catch(() => Read(array));
                Assert.That(actual, Is.SameAs(sentinel));
            }
            finally
            {
                cleanup.TrySetResult();
                if (iterator != null)
                {
                    Catch(() => Read(iterator.DisposeAsync()));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerminalConsumers_RetainActualFaultedOceOrCancellation(bool canceled)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var fault = new OperationCanceledException("fault", cancellation.Token);
                OnityTask<bool> input = canceled ? OnityTask<bool>.FromCanceled(cancellation.Token)
                    : new OnityTask<bool>(Task.FromException<bool>(fault));
                var firstProbe = new Probe { Move = () => input };
                var first = firstProbe.FirstAsync();
                Wait(() => first.IsCompleted);
                Assert.That(first.IsCanceled, Is.EqualTo(canceled));
                Assert.That(first.IsFaulted, Is.EqualTo(!canceled));
                var error = Catch(() => Read(first));
                if (canceled)
                {
                    Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(cancellation.Token));
                }
                else
                {
                    Assert.That(error, Is.SameAs(fault));
                }
                Assert.That(firstProbe.Disposals, Is.EqualTo(1));
                var arrayProbe = new Probe { Move = () => input };
                var array = arrayProbe.ToArrayAsync();
                Wait(() => array.IsCompleted);
                Assert.That(array.IsCanceled, Is.EqualTo(canceled));
                Assert.That(array.IsFaulted, Is.EqualTo(!canceled));
                Catch(() => Read(array));
                Assert.That(arrayProbe.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReentrantAcquireOrCurrent_DisposalStopsFreshMoveAndDelegate(bool fromCurrent)
        {
            IOnityAsyncEnumerator<int> iterator = null;
            OnityTask disposal = default;
            int delegates = 0;
            var probe = new Probe();
            if (fromCurrent)
            {
                probe.ReadCurrent = () => { disposal = iterator.DisposeAsync(); return 17; };
            }
            else
            {
                probe.Acquire = () => disposal = iterator.DisposeAsync();
            }
            iterator = probe.Select(value => { delegates++; return value; }).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Assert.That(Read(move), Is.False);
            Read(disposal);
            Assert.That(probe.Moves, Is.EqualTo(fromCurrent ? 1 : 0));
            Assert.That(delegates, Is.Zero);
            Assert.That(probe.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void BlockedSelector_DisposeWaitsForActiveCall_WithoutHoldingPublicationLock()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var probe = new Probe();
                bool timedOut = false;
                var iterator = probe.Select(value =>
                {
                    entered.Set();
                    timedOut = !release.Wait(TimeSpan.FromSeconds(5));
                    return value;
                }).GetAsyncEnumerator();
                OnityTask<bool> move = default;
                Task worker = Task.Run(() => { move = iterator.MoveNextAsync(); });
                OnityTask disposal = default;
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    disposal = iterator.DisposeAsync();
                    Assert.That(disposal.IsCompleted, Is.False);
                    Assert.That(probe.Disposals, Is.Zero);
                    release.Set();
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(timedOut, Is.False);
                    Assert.That(Read(move), Is.False);
                    Read(disposal);
                    Assert.That(probe.Disposals, Is.EqualTo(1));
                }
                finally
                {
                    release.Set();
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void PublicCompletionRegistrationRace_SettlesOnce_WithoutContextPosts()
        {
            SynchronizationContext previous = SynchronizationContext.Current;
            var context = new CountingContext();
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                for (int round = 0; round < 32; round++)
                {
                    var source = new OnityTaskCompletionSource<bool>();
                    var probe = new Probe { Move = () => source.Task };
                    var iterator = probe.Select(value => value).GetAsyncEnumerator();
                    Task worker = Task.Run(() => { source.TrySetResult(true); });
                    var move = iterator.MoveNextAsync();
                    try
                    {
                        Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                        Wait(() => move.IsCompleted);
                        Assert.That(Read(move), Is.True);
                        Assert.That(iterator.Current, Is.EqualTo(17));
                    }
                    finally
                    {
                        source.TrySetResult(false);
                        Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                        Read(iterator.DisposeAsync());
                    }
                }
                Assert.That(context.Posts, Is.Zero);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Test]
        public void ExplicitDisposeDuringTakeCleanup_ReplacesUncommittedLastItemWithFalse()
        {
            var cleanup = new OnityTaskCompletionSource();
            var probe = new Probe { Cleanup = () => cleanup.Task };
            var iterator = probe.Take(1).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            var dispose = iterator.DisposeAsync();
            try
            {
                Assert.That(Read(move), Is.False);
                Assert.That(dispose.IsCompleted, Is.False);
                Assert.That(probe.Disposals, Is.EqualTo(1));
                cleanup.TrySetResult();
                Read(dispose);
                Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            }
            finally
            {
                cleanup.TrySetResult();
                Read(iterator.DisposeAsync());
            }
        }

        [Test]
        public void CleanupCancellation_ReplacesFirstItem_WithOriginalTokenAndCanceledStatus()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var probe = new Probe { Cleanup = () => OnityTask.FromCanceled(cancellation.Token) };
                var first = probe.FirstAsync();
                Assert.That(first.IsCanceled, Is.True);
                Assert.That(first.IsFaulted, Is.False);
                var error = (OperationCanceledException)Catch(() => Read(first));
                Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
                Assert.That(probe.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StaleOrPreclaimedNativeMove_IsBoundedFault_AndValidConsumerStillCompletes(bool preclaimed)
        {
            var gate = new Gate();
            var input = Native(gate);
            bool consumed = false;
            Exception validConsumerError = null;
            if (preclaimed)
            {
                input.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        consumed = input.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        validConsumerError = exception;
                    }
                });
            }
            else
            {
                gate.Complete();
                Assert.That(Read(input), Is.True);
            }
            var probe = new Probe { Move = () => input };
            var iterator = probe.Select(value => value).GetAsyncEnumerator();
            try
            {
                var move = iterator.MoveNextAsync();
                Assert.That(move.IsFaulted, Is.True);
                Assert.That(Catch(() => Read(move)), Is.TypeOf<InvalidOperationException>());
                gate.Complete();
                if (preclaimed)
                {
                    Assert.That(consumed, Is.True);
                    Assert.That(validConsumerError, Is.Null);
                }
            }
            finally
            {
                gate.Complete();
                Read(iterator.DisposeAsync());
            }
        }

        private static async OnityTask<int> Consume(Probe source, bool throwBody, Exception fault)
        {
            int result = 0;
            await foreach (int value in source.Select(value => value))
            {
                if (throwBody)
                {
                    throw fault;
                }
                result = value;
                break;
            }
            return result;
        }

        private static async OnityTask<bool> Native(Gate gate)
        {
            await gate;
            return true;
        }

        private static T Read<T>(OnityTask<T> task)
        {
            Assert.That(task.IsCompleted, Is.True, "A supposedly inline or settled operation is pending.");
            return task.GetAwaiter().GetResult();
        }

        private static void Read(OnityTask task)
        {
            Assert.That(task.IsCompleted, Is.True, "A supposedly inline or settled cleanup is pending.");
            task.GetAwaiter().GetResult();
        }

        private static Exception Catch(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static void Wait(Func<bool> predicate)
        {
            Assert.That(SpinWait.SpinUntil(predicate, TimeSpan.FromSeconds(5)), Is.True, "Worker outcome timed out.");
        }

        private static bool FaultObserved(Task task)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            object properties = typeof(Task).GetField("m_contingentProperties", flags)?.GetValue(task);
            object holder = properties?.GetType().GetField("m_exceptionsHolder", flags)?.GetValue(properties);
            return holder != null && (bool)holder.GetType().GetField("m_isHandled", flags).GetValue(holder);
        }

        private sealed class CountingContext : SynchronizationContext
        {
            internal int Posts;
            public override void Post(SendOrPostCallback callback, object state)
            {
                Interlocked.Increment(ref Posts);
            }
        }

        private sealed class Probe : IOnityAsyncEnumerable<int>, IOnityAsyncEnumerator<int>
        {
            internal int Acquires;
            internal int Moves;
            internal int Disposals;
            internal CancellationToken Token;
            internal Action Acquire;
            internal Func<OnityTask<bool>> Move = () => OnityTask<bool>.FromResult(true);
            internal Func<OnityTask> Cleanup = () => default;
            internal Func<int> ReadCurrent = () => 17;

            public int Current => ReadCurrent();

            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Acquires);
                Token = cancellationToken;
                Acquire?.Invoke();
                return this;
            }

            public OnityTask<bool> MoveNextAsync()
            {
                Interlocked.Increment(ref Moves);
                return Move();
            }

            public OnityTask DisposeAsync()
            {
                Interlocked.Increment(ref Disposals);
                return Cleanup();
            }
        }

        private sealed class Gate : ICriticalNotifyCompletion
        {
            private Action m_continuation;
            public bool IsCompleted => false;
            public Gate GetAwaiter() => this;
            public void GetResult()
            {
            }
            public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);
            public void UnsafeOnCompleted(Action continuation) => m_continuation = continuation;
            internal void Complete() => Interlocked.Exchange(ref m_continuation, null)?.Invoke();
        }
    }
}
