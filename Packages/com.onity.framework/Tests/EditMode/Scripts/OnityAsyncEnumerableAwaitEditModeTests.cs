using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableAwaitEditModeTests
    {
        [Test]
        public void ArgumentsAndAdmission_ValidateSynchronously_DescriptionsAreLazy_ForEachStarts()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = new Probe();
            Assert.Throws<ArgumentNullException>(() => missing.SelectAwait((value, token) => OnityTask<int>.FromResult(value)));
            Assert.Throws<ArgumentNullException>(() => missing.WhereAwait((value, token) => OnityTask<bool>.FromResult(true)));
            Assert.Throws<ArgumentNullException>(() => missing.ForEachAsync((value, token) => default));
            Assert.Throws<ArgumentNullException>(() => source.SelectAwait<int, int>(null));
            Assert.Throws<ArgumentNullException>(() => source.WhereAwait(null));
            Assert.Throws<ArgumentNullException>(() => source.ForEachAsync(null));
            source.SelectAwait((value, token) => OnityTask<int>.FromResult(value)).GetAsyncEnumerator().DisposeAsync();
            source.WhereAwait((value, token) => OnityTask<bool>.FromResult(true)).GetAsyncEnumerator().DisposeAsync();
            Assert.That(source.Acquires, Is.Zero);
            Read(source.ForEachAsync((value, token) => default));
            Assert.That(source.Moves, Is.EqualTo(3));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void InlinePipeline_OrdersItems_AndLargeRejectedRunDoesNotRecurse()
        {
            int predicates = 0;
            var values = Read(OnityAsyncEnumerable.Range(0, 100000)
                .WhereAwait((value, token) => { predicates++; return OnityTask<bool>.FromResult(false); })
                .ToArrayAsync());
            Assert.That(values, Is.Empty);
            Assert.That(predicates, Is.EqualTo(100000));
            values = Read(OnityAsyncEnumerable.Range(1, 6)
                .SelectAwait((value, token) => OnityTask<int>.FromResult(value * 3))
                .WhereAwait((value, token) => OnityTask<bool>.FromResult(value % 2 == 0)).ToArrayAsync());
            Assert.That(values, Is.EqualTo(new[] { 6, 12, 18 }));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void PendingDelegate_PreventsFreshUpstreamMove_AndConsumesNativePreservedOrTaskOnce(int kind)
        {
            var gate = new Gate();
            var task = new TaskCompletionSource<int>();
            var source = new Probe();
            int calls = 0;
            OnityTask<int> accepted = kind == 2 ? OnityTask<int>.FromTask(task.Task) : Native(gate);
            if (kind == 1)
            {
                accepted = accepted.Preserve();
            }
            var iterator = source.SelectAwait((value, token) =>
            {
                calls++;
                return calls == 1 ? accepted : OnityTask<int>.FromResult(value * 10);
            }).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            try
            {
                Assert.That(move.IsCompleted, Is.False);
                Assert.That(source.Moves, Is.EqualTo(1));
                Assert.That(source.CurrentReads, Is.EqualTo(1));
                Assert.Throws<InvalidOperationException>(() => iterator.MoveNextAsync());
                Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
                task.TrySetResult(91);
                gate.Complete();
                Wait(() => move.IsCompleted);
                Assert.That(Read(move), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(91));
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(20));
                Assert.That(Read(iterator.MoveNextAsync()), Is.False);
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(source.CurrentReads, Is.EqualTo(2));
                Assert.That(gate.Results, Is.EqualTo(kind == 2 ? 0 : 1));
            }
            finally
            {
                task.TrySetResult(91);
                gate.Complete();
                Read(iterator.DisposeAsync());
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WhereAndForEach_PendingDelegateRemainsSequential(bool terminal)
        {
            var source = new Probe();
            var predicate = new OnityTaskCompletionSource<bool>();
            var action = new OnityTaskCompletionSource();
            int calls = 0;
            var iterator = source.WhereAwait((value, token) =>
            {
                calls++;
                return calls == 1 ? predicate.Task : OnityTask<bool>.FromResult(true);
            }).GetAsyncEnumerator();
            OnityTask finish = default;
            OnityTask<bool> move = default;
            try
            {
                if (terminal)
                {
                    finish = source.ForEachAsync((value, token) =>
                    {
                        calls++;
                        return calls == 1 ? action.Task : default;
                    });
                }
                else
                {
                    move = iterator.MoveNextAsync();
                }
                Assert.That(source.Moves, Is.EqualTo(1));
                Assert.That(source.CurrentReads, Is.EqualTo(1));
                Assert.That(calls, Is.EqualTo(1));
                action.TrySetResult();
                predicate.TrySetResult(false);
                if (terminal)
                {
                    Read(finish);
                    Assert.That(source.Disposals, Is.EqualTo(1));
                }
                else
                {
                    Assert.That(Read(move), Is.True);
                    Assert.That(iterator.Current, Is.EqualTo(2));
                    Assert.That(Read(iterator.MoveNextAsync()), Is.False);
                }
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(source.CurrentReads, Is.EqualTo(2));
            }
            finally
            {
                action.TrySetResult();
                predicate.TrySetResult(false);
                Read(iterator.DisposeAsync());
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TerminalConsumption_CleanupFaultReplacesSuccessfulProposedResult(int terminal)
        {
            var fault = new InvalidOperationException("terminal cleanup");
            var source = new Probe { Count = 1, Cleanup = () => OnityTask.FromTask(Task.FromException(fault)) };
            var stream = source.SelectAwait((value, token) => OnityTask<int>.FromResult(value));
            Exception error = terminal == 0 ? Catch(() => Read(stream.FirstAsync()))
                : terminal == 1 ? Catch(() => Read(stream.ToArrayAsync()))
                : Catch(() => Read(source.ForEachAsync((value, token) => default)));
            Assert.That(error, Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(0, 2)]
        [TestCase(1, 0)]
        [TestCase(1, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 0)]
        [TestCase(2, 1)]
        [TestCase(2, 2)]
        public void DelegateOutcomes_PreserveActualCancellationAndFaultedOrThrownOce(int operation, int outcome)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var fault = new OperationCanceledException("actual delegate fault", cancellation.Token);
                var typed = new OnityTaskCompletionSource<int>();
                var predicate = new OnityTaskCompletionSource<bool>();
                var action = new OnityTaskCompletionSource();
                if (outcome == 0)
                {
                    typed.TrySetCanceled(cancellation.Token);
                    predicate.TrySetCanceled(cancellation.Token);
                    action.TrySetCanceled(cancellation.Token);
                }
                else
                {
                    typed.TrySetResult(1);
                    predicate.TrySetResult(true);
                    action.TrySetResult();
                }
                OnityTask<int> typedInput = outcome == 0 ? typed.Task : OnityTask<int>.FromTask(Task.FromException<int>(fault));
                OnityTask<bool> predicateInput = outcome == 0 ? predicate.Task : OnityTask<bool>.FromTask(Task.FromException<bool>(fault));
                OnityTask actionInput = outcome == 0 ? action.Task : OnityTask.FromTask(Task.FromException(fault));
                var source = new Probe();
                IOnityAsyncEnumerator<int> iterator = operation == 0
                    ? source.SelectAwait((value, token) => outcome == 2 ? throw fault : typedInput).GetAsyncEnumerator()
                    : source.WhereAwait((value, token) => outcome == 2 ? throw fault : predicateInput).GetAsyncEnumerator();
                try
                {
                    if (operation == 2)
                    {
                        Check(source.ForEachAsync((value, token) => outcome == 2 ? throw fault : actionInput), outcome, fault, cancellation.Token);
                    }
                    else
                    {
                        var move = iterator.MoveNextAsync();
                        Check(move, outcome, fault, cancellation.Token);
                        Check(iterator.MoveNextAsync(), outcome, fault, cancellation.Token);
                    }
                }
                finally
                {
                    Read(iterator.DisposeAsync());
                    // Only one operation claims these public sources; observe the others too.
                    Catch(() => Read(typed.Task));
                    Catch(() => Read(predicate.Task));
                    Catch(() => Read(action.Task));
                    Catch(() => Read(typedInput));
                    Catch(() => Read(predicateInput));
                    Catch(() => Read(actionInput));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreCanceledEmptyEndsNormally_ButIgnoringNonemptyUpstreamDoesNotInvokeDelegate(bool empty)
        {
            using (var caller = new CancellationTokenSource())
            {
                caller.Cancel();
                int calls = 0;
                var source = new Probe { Count = empty ? 0 : 2 };
                var iterator = source.SelectAwait((value, token) =>
                {
                    calls++;
                    return OnityTask<int>.FromResult(value);
                }).GetAsyncEnumerator(caller.Token);
                try
                {
                    var move = iterator.MoveNextAsync();
                    if (empty)
                    {
                        Assert.That(Read(move), Is.False);
                    }
                    else
                    {
                        Assert.That(move.IsCanceled, Is.True);
                        Assert.That(Catch(() => Read(move)), Is.InstanceOf<OperationCanceledException>());
                    }
                    Assert.That(calls, Is.Zero);
                    Assert.That(source.Token, Is.EqualTo(caller.Token));
                }
                finally
                {
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void OriginalTokenGoesUpstream_OwnedTokenGoesDelegate_IgnoreCancellationSuccessWinsCurrentMove()
        {
            using (var caller = new CancellationTokenSource())
            {
                var pending = new OnityTaskCompletionSource<int>();
                var source = new Probe();
                CancellationToken owned = default;
                var iterator = source.SelectAwait((value, token) => { owned = token; return pending.Task; })
                    .GetAsyncEnumerator(caller.Token);
                var move = iterator.MoveNextAsync();
                try
                {
                    Assert.That(source.Token, Is.EqualTo(caller.Token));
                    Assert.That(owned.CanBeCanceled, Is.True);
                    Assert.That(owned, Is.Not.EqualTo(caller.Token));
                    caller.Cancel();
                    Assert.That(owned.IsCancellationRequested, Is.True);
                    pending.TrySetResult(77);
                    Assert.That(Read(move), Is.True);
                    Assert.That(iterator.Current, Is.EqualTo(77));
                    Assert.That(iterator.MoveNextAsync().IsCanceled, Is.True);
                    Catch(() => Read(iterator.MoveNextAsync()));
                }
                finally
                {
                    pending.TrySetResult(77);
                    Read(iterator.DisposeAsync());
                }
                using (caller.Token.Register(() => { }))
                {
                    Assert.That(caller.IsCancellationRequested, Is.True);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitDispose_PublishesFalse_StartsUpstreamCleanupAndWaitsBothOrders(bool cleanupFirst)
        {
            var pending = new OnityTaskCompletionSource<int>();
            var cleanup = new OnityTaskCompletionSource();
            var source = new Probe { Cleanup = () => cleanup.Task };
            var iterator = source.SelectAwait((value, token) => pending.Task).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            var dispose = iterator.DisposeAsync();
            try
            {
                Assert.That(Read(move), Is.False);
                Assert.That(source.Disposals, Is.EqualTo(1));
                Assert.That(dispose.IsCompleted, Is.False);
                Assert.That(iterator.DisposeAsync(), Is.EqualTo(dispose));
                if (cleanupFirst)
                {
                    cleanup.TrySetResult();
                }
                else
                {
                    pending.TrySetResult(99);
                }
                Assert.That(dispose.IsCompleted, Is.False);
                pending.TrySetResult(99);
                cleanup.TrySetResult();
                Read(dispose);
                Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            }
            finally
            {
                pending.TrySetResult(99);
                cleanup.TrySetResult();
                Read(iterator.DisposeAsync());
            }
        }

        [Test]
        public void NativeUpstreamDisposeCanSettleItsPendingMoveWithoutWaitingForThatMove()
        {
            var upstream = new OnityTaskCompletionSource<bool>();
            var source = new Probe { Move = () => upstream.Task, Cleanup = () => { upstream.TrySetResult(false); return default; } };
            var iterator = source.WhereAwait((value, token) => OnityTask<bool>.FromResult(true)).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Read(iterator.DisposeAsync());
            Assert.That(Read(move), Is.False);
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(source.CurrentReads, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DelayedUpstreamCallback_HeldDelegateInvocationPinsCleanup_UntilCallReturns(bool delayedUpstream)
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var upstream = new OnityTaskCompletionSource<bool>();
                var pending = new OnityTaskCompletionSource<int>();
                var source = new Probe { Move = delayedUpstream ? () => upstream.Task : null };
                bool timedOut = false;
                var iterator = source.SelectAwait((value, token) =>
                {
                    entered.Set();
                    timedOut = !release.Wait(TimeSpan.FromSeconds(5));
                    return pending.Task;
                }).GetAsyncEnumerator();
                OnityTask<bool> move = default;
                if (delayedUpstream)
                {
                    move = iterator.MoveNextAsync();
                }
                Task worker = Task.Run(() =>
                {
                    if (delayedUpstream)
                    {
                        upstream.TrySetResult(true);
                    }
                    else
                    {
                        move = iterator.MoveNextAsync();
                    }
                });
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    var dispose = iterator.DisposeAsync();
                    Assert.That(dispose.IsCompleted, Is.False);
                    Assert.That(source.Disposals, Is.Zero);
                    release.Set();
                    Join(worker);
                    Assert.That(timedOut, Is.False);
                    Assert.That(source.Disposals, Is.EqualTo(1));
                    Assert.That(Read(move), Is.False);
                    Assert.That(dispose.IsCompleted, Is.False);
                    pending.TrySetResult(8);
                    Read(dispose);
                }
                finally
                {
                    release.Set();
                    upstream.TrySetResult(false);
                    pending.TrySetResult(8);
                    Join(worker);
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ReentrantAcquireCurrentOrDelegate_DisposePreventsFreshUserCalls(int boundary)
        {
            var source = new Probe();
            IOnityAsyncEnumerator<int> iterator = null;
            OnityTask disposal = default;
            int calls = 0;
            if (boundary == 0)
            {
                source.Acquire = () => disposal = iterator.DisposeAsync();
            }
            if (boundary == 1)
            {
                source.ReadCurrent = () => { disposal = iterator.DisposeAsync(); return 1; };
            }
            iterator = source.SelectAwait((value, token) =>
            {
                calls++;
                disposal = iterator.DisposeAsync();
                return OnityTask<int>.FromResult(value);
            }).GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Read(disposal);
            Assert.That(source.Moves, Is.EqualTo(boundary == 0 ? 0 : 1));
            Assert.That(calls, Is.EqualTo(boundary == 2 ? 1 : 0));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CleanupFaultOverridesOrdinaryOutcome_ButExplicitFalseRemainsFalse(bool explicitDispose)
        {
            var delegateFault = new InvalidOperationException("delegate");
            var cleanupFault = new OperationCanceledException("cleanup fault");
            var cleanup = new TaskCompletionSource<bool>();
            var pending = new OnityTaskCompletionSource<int>();
            var source = new Probe { Cleanup = () => OnityTask.FromTask(cleanup.Task) };
            var iterator = source.SelectAwait((value, token) => pending.Task).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            try
            {
                var disposal = explicitDispose ? iterator.DisposeAsync() : default;
                pending.TrySetException(delegateFault);
                cleanup.TrySetException(cleanupFault);
                if (explicitDispose)
                {
                    Wait(() => disposal.IsCompleted);
                    Assert.That(Read(move), Is.False);
                    Assert.That(disposal.IsFaulted, Is.True);
                    Assert.That(Catch(() => Read(disposal)), Is.SameAs(cleanupFault));
                }
                else
                {
                    Wait(() => move.IsCompleted);
                    Assert.That(move.IsFaulted, Is.True);
                    Assert.That(Catch(() => Read(move)), Is.SameAs(cleanupFault));
                }
            }
            finally
            {
                pending.TrySetException(delegateFault);
                cleanup.TrySetException(cleanupFault);
                var finalDisposal = iterator.DisposeAsync();
                Wait(() => finalDisposal.IsCompleted);
                Catch(() => Read(finalDisposal));
            }
        }

        [Test]
        public void OwnedCancellation_ReentrantDisposalWaitsForCallbackReturn_AndCleanupFaultHasPriority()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var caller = new CancellationTokenSource())
            {
                var pending = new OnityTaskCompletionSource<int>();
                var cleanupFault = new InvalidOperationException("upstream cleanup priority");
                var callbackFault = new InvalidOperationException("token callback");
                var source = new Probe { Cleanup = () => OnityTask.FromTask(Task.FromException(cleanupFault)) };
                IOnityAsyncEnumerator<int> iterator = null;
                OnityTask nested = default;
                CancellationTokenRegistration registration = default;
                Exception callbackError = null;
                bool timedOut = false;
                iterator = source.SelectAwait((value, token) =>
                {
                    registration = token.Register(() =>
                    {
                        try
                        {
                            nested = iterator.DisposeAsync();
                            pending.TrySetResult(7);
                            entered.Set();
                            timedOut = !release.Wait(TimeSpan.FromSeconds(5));
                            var handle = token.WaitHandle;
                        }
                        catch (Exception exception)
                        {
                            callbackError = exception;
                        }
                        throw callbackFault;
                    });
                    return pending.Task;
                }).GetAsyncEnumerator(caller.Token);
                var move = iterator.MoveNextAsync();
                Task worker = Task.Run(() => caller.Cancel());
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(nested.IsCompleted, Is.False);
                    Assert.That(source.Disposals, Is.EqualTo(1), "Upstream cleanup progresses while token quiescence is pinned.");
                    release.Set();
                    Join(worker);
                    Assert.That(callbackError, Is.Null);
                    Assert.That(timedOut, Is.False);
                    Assert.That(Read(move), Is.False);
                    Wait(() => nested.IsCompleted);
                    Assert.That(Catch(() => Read(nested)), Is.SameAs(cleanupFault));
                    Assert.That(source.Disposals, Is.EqualTo(1));
                    using (caller.Token.Register(() => { }))
                    {
                        Assert.That(caller.IsCancellationRequested, Is.True);
                    }
                }
                finally
                {
                    release.Set();
                    pending.TrySetResult(7);
                    Join(worker);
                    registration.Dispose();
                    Catch(() => Read(iterator.DisposeAsync()));
                }
            }
        }

        [Test]
        public void PublicCompletionRace_HasNoObserverContextPosts_AndRestoresCallerSuppression()
        {
            var previous = SynchronizationContext.Current;
            var context = new CountingContext();
            var local = new AsyncLocal<string>();
            local.Value = "caller";
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                for (int round = 0; round < 32; round++)
                {
                    var pending = new OnityTaskCompletionSource<int>();
                    var iterator = OnityAsyncEnumerable.Return(1).SelectAwait((value, token) => pending.Task).GetAsyncEnumerator();
                    OnityTask<bool> move;
                    using (ExecutionContext.SuppressFlow())
                    {
                        move = iterator.MoveNextAsync();
                        Assert.That(ExecutionContext.IsFlowSuppressed(), Is.True);
                    }
                    Task worker = Task.Run(() => { pending.TrySetResult(3); });
                    try
                    {
                        Join(worker);
                        Wait(() => move.IsCompleted);
                        Assert.That(Read(move), Is.True);
                        Assert.That(iterator.Current, Is.EqualTo(3));
                        Assert.That(local.Value, Is.EqualTo("caller"));
                    }
                    finally
                    {
                        pending.TrySetResult(3);
                        Join(worker);
                        Read(iterator.DisposeAsync());
                    }
                }
                Assert.That(context.Posts, Is.Zero);
                Assert.That(ExecutionContext.IsFlowSuppressed(), Is.False);
            }
            finally
            {
                local.Value = null;
                SynchronizationContext.SetSynchronizationContext(previous);
            }
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

        private static void Check<T>(OnityTask<T> task, int outcome, Exception fault, CancellationToken token)
        {
            Assert.That(task.IsCanceled, Is.EqualTo(outcome == 0));
            Assert.That(task.IsFaulted, Is.EqualTo(outcome != 0));
            Exception error = Catch(() => Read(task));
            if (outcome == 0)
            {
                Assert.That(error, Is.InstanceOf<OperationCanceledException>());
                Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(token));
            }
            else
            {
                Assert.That(error, Is.SameAs(fault));
            }
        }

        private static void Check(OnityTask task, int outcome, Exception fault, CancellationToken token)
        {
            Assert.That(task.IsCanceled, Is.EqualTo(outcome == 0));
            Assert.That(task.IsFaulted, Is.EqualTo(outcome != 0));
            Exception error = Catch(() => Read(task));
            if (outcome == 0)
            {
                Assert.That(error, Is.InstanceOf<OperationCanceledException>());
                Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(token));
            }
            else
            {
                Assert.That(error, Is.SameAs(fault));
            }
        }

        private static void Wait(Func<bool> done)
        {
            Assert.That(SpinWait.SpinUntil(done, TimeSpan.FromSeconds(5)), Is.True);
        }

        private static void Join(Task worker)
        {
            Wait(() => worker.IsCompleted);
            worker.GetAwaiter().GetResult();
        }

        private static async OnityTask<int> Native(Gate gate)
        {
            await gate;
            return 91;
        }

        private sealed class CountingContext : SynchronizationContext
        {
            internal int Posts;
            public override void Post(SendOrPostCallback callback, object state) => Interlocked.Increment(ref Posts);
        }

        private sealed class Probe : IOnityAsyncEnumerable<int>, IOnityAsyncEnumerator<int>
        {
            internal int Count = 2;
            internal int Acquires;
            internal int Moves;
            internal int CurrentReads;
            internal int Disposals;
            internal CancellationToken Token;
            internal Action Acquire;
            internal Func<OnityTask<bool>> Move;
            internal Func<int> ReadCurrent;
            internal Func<OnityTask> Cleanup = () => default;
            public int Current
            {
                get
                {
                    CurrentReads++;
                    return ReadCurrent != null ? ReadCurrent() : Moves;
                }
            }
            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Acquires++;
                Token = cancellationToken;
                Acquire?.Invoke();
                return this;
            }
            public OnityTask<bool> MoveNextAsync()
            {
                Moves++;
                return Move != null ? Move() : OnityTask<bool>.FromResult(Moves <= Count);
            }
            public OnityTask DisposeAsync()
            {
                Disposals++;
                return Cleanup();
            }
        }

        private sealed class Gate : ICriticalNotifyCompletion
        {
            private Action m_continuation;
            internal int Results;
            public bool IsCompleted => false;
            public Gate GetAwaiter() => this;
            public void GetResult() => Results++;
            public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);
            public void UnsafeOnCompleted(Action continuation) => m_continuation = continuation;
            internal void Complete() => Interlocked.Exchange(ref m_continuation, null)?.Invoke();
        }
    }
}
