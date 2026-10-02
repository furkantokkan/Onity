using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Reactive;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableReactiveEditModeTests
    {
        private Action<Exception> m_previous;
        private readonly List<Exception> m_global = new List<Exception>();

        [SetUp]
        public void SetUp()
        {
            m_previous = OnityObservableExceptionHandler.Handler;
            m_global.Clear();
            OnityObservableExceptionHandler.Handler = exception =>
            {
                lock (m_global)
                {
                    m_global.Add(exception);
                }
            };
        }

        [TearDown]
        public void TearDown() => OnityObservableExceptionHandler.Handler = m_previous;

        [Test]
        public void ValidationAndLaziness_FirstMoveOwnsSubscription()
        {
            IOnityObservable<int> missing = null;
            IOnityAsyncEnumerable<int> native = null;
            Assert.Throws<ArgumentNullException>(() => missing.AsOnityAsyncEnumerable(1));
            Assert.Throws<ArgumentNullException>(() => native.AsObservable());
            var source = new Push();
            Assert.Throws<ArgumentOutOfRangeException>(() => source.AsOnityAsyncEnumerable(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.AsOnityAsyncEnumerable(-1));
            var description = source.AsOnityAsyncEnumerable(2);
            var idle = description.GetAsyncEnumerator();
            Read(idle.DisposeAsync());
            Assert.That(source.Subscriptions, Is.Zero);
            var iterator = description.GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            try
            {
                Assert.That(source.Subscriptions, Is.EqualTo(1));
                Assert.That(move.IsCompleted, Is.False);
                Assert.Throws<InvalidOperationException>(() => iterator.MoveNextAsync());
                Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
                source.Observer.OnNext(3);
                Assert.That(Read(move), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(3));
            }
            finally
            {
                Read(iterator.DisposeAsync());
            }
            Assert.That(source.Detaches, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SynchronousSubscribe_DrainsFifoBeforeSuccessErrorFailedCompletionOrOverflow(int terminal)
        {
            var fault = new OperationCanceledException("observable fault is not cancellation");
            var source = new Push();
            source.SubscribeAction = observer =>
            {
                observer.OnNext(11);
                observer.OnNext(12);
                if (terminal == 0)
                {
                    observer.OnCompleted();
                }
                else if (terminal == 1)
                {
                    observer.OnError(fault);
                }
                else if (terminal == 2)
                {
                    observer.OnCompleted(OnityResult.Failure(fault));
                }
                else
                {
                    observer.OnNext(13);
                    observer.OnNext(14);
                }
            };
            var iterator = source.AsOnityAsyncEnumerable(2).GetAsyncEnumerator();
            try
            {
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(11));
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(12));
                var end = iterator.MoveNextAsync();
                if (terminal == 0)
                {
                    Assert.That(Read(end), Is.False);
                    Assert.That(Read(iterator.MoveNextAsync()), Is.False);
                }
                else
                {
                    Assert.That(end.IsFaulted, Is.True);
                    Assert.That(end.IsCanceled, Is.False);
                    Exception error = Catch(() => Read(end));
                    if (terminal == 3)
                    {
                        Assert.That(error, Is.InstanceOf<InvalidOperationException>());
                        StringAssert.Contains("overflow", error.Message.ToLowerInvariant());
                    }
                    else
                    {
                        Assert.That(error, Is.SameAs(fault));
                    }
                }
                Assert.That(source.Detaches, Is.EqualTo(1));
            }
            finally
            {
                Catch(() => Read(iterator.DisposeAsync()));
            }
            Assert.That(source.Detaches, Is.EqualTo(1));
            Assert.That(m_global, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullHandleOrThrowingSubscribe_NoFabricatedUnsubscribe(bool throws)
        {
            var fault = new InvalidOperationException("subscribe failed");
            var source = new Push { NullHandle = true };
            source.SubscribeAction = observer =>
            {
                if (throws)
                {
                    throw fault;
                }
                observer.OnNext(9);
                observer.OnCompleted();
            };
            var iterator = source.AsOnityAsyncEnumerable(1).GetAsyncEnumerator();
            try
            {
                var move = iterator.MoveNextAsync();
                if (throws)
                {
                    Assert.That(Catch(() => Read(move)), Is.SameAs(fault));
                }
                else
                {
                    Assert.That(Read(move), Is.True);
                    Assert.That(iterator.Current, Is.EqualTo(9));
                    Assert.That(Read(iterator.MoveNextAsync()), Is.False);
                }
            }
            finally
            {
                Read(iterator.DisposeAsync());
            }
            Assert.That(source.Detaches, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancellationBeforeSubscribeOrBeforeHandleAssignment_PreservesTokenAndDetachesOnce(bool duringSubscribe)
        {
            using (var caller = new CancellationTokenSource())
            {
                var source = new Push { SubscribeAction = observer => caller.Cancel() };
                if (!duringSubscribe)
                {
                    caller.Cancel();
                }
                var iterator = source.AsOnityAsyncEnumerable(1).GetAsyncEnumerator(caller.Token);
                try
                {
                    var move = iterator.MoveNextAsync();
                    Assert.That(move.IsCanceled, Is.True);
                    var error = Catch(() => Read(move)) as OperationCanceledException;
                    Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
                    Assert.That(source.Subscriptions, Is.EqualTo(duringSubscribe ? 1 : 0));
                }
                finally
                {
                    Read(iterator.DisposeAsync());
                }
                Assert.That(source.Detaches, Is.EqualTo(duringSubscribe ? 1 : 0));
                Assert.That(Read(iterator.MoveNextAsync()), Is.False);
                Assert.That(source.Subscriptions, Is.EqualTo(duringSubscribe ? 1 : 0));
            }
        }

        [Test]
        public void ReentrantDisposeInsideSubscribe_DefersHandleCleanupUntilAssignment()
        {
            var source = new Push();
            IOnityAsyncEnumerator<int> iterator = null;
            OnityTask disposal = default;
            bool completedInsideSubscribe = true;
            source.SubscribeAction = observer =>
            {
                disposal = iterator.DisposeAsync();
                completedInsideSubscribe = disposal.IsCompleted;
                observer.OnNext(99);
            };
            iterator = source.AsOnityAsyncEnumerable(1).GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Read(disposal);
            Assert.That(completedInsideSubscribe, Is.False);
            Assert.That(source.Detaches, Is.EqualTo(1));
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
        }

        [Test]
        public void HeldSubscribe_DisposalWaitsForInvocationAndLateHandle()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                bool timedOut = false;
                var source = new Push
                {
                    SubscribeAction = observer =>
                    {
                        entered.Set();
                        timedOut = !release.Wait(TimeSpan.FromSeconds(5));
                        observer.OnNext(99);
                    }
                };
                var iterator = source.AsOnityAsyncEnumerable(1).GetAsyncEnumerator();
                OnityTask<bool> move = default;
                Task worker = Task.Run(() => { move = iterator.MoveNextAsync(); });
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    var disposal = iterator.DisposeAsync();
                    Assert.That(disposal.IsCompleted, Is.False);
                    Assert.That(source.Detaches, Is.Zero);
                    release.Set();
                    Join(worker);
                    Assert.That(timedOut, Is.False);
                    Assert.That(Read(move), Is.False);
                    Read(disposal);
                    Assert.That(source.Detaches, Is.EqualTo(1));
                }
                finally
                {
                    release.Set();
                    Join(worker);
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void RealSubject_IndependentBuffers_DisposalDiscardsOnlyItsOwnAcceptedItems()
        {
            using (var subject = new Subject<int>())
            {
                var stream = subject.AsOnityAsyncEnumerable(2);
                var first = stream.GetAsyncEnumerator();
                var second = stream.GetAsyncEnumerator();
                var firstMove = first.MoveNextAsync();
                var secondMove = second.MoveNextAsync();
                try
                {
                    subject.OnNext(1);
                    Assert.That(Read(firstMove), Is.True);
                    Assert.That(Read(secondMove), Is.True);
                    subject.OnNext(2);
                    Read(first.DisposeAsync());
                    subject.OnNext(3);
                    Assert.That(Read(second.MoveNextAsync()), Is.True);
                    Assert.That(second.Current, Is.EqualTo(2));
                    Assert.That(Read(second.MoveNextAsync()), Is.True);
                    Assert.That(second.Current, Is.EqualTo(3));
                    Assert.That(Read(first.MoveNextAsync()), Is.False);
                }
                finally
                {
                    Read(first.DisposeAsync());
                    Read(second.DisposeAsync());
                }
            }
        }

        [Test]
        public void CancellationDiscardsPrivateBuffer_ButDoesNotRewriteCommittedDelivery()
        {
            using (var caller = new CancellationTokenSource())
            using (var subject = new Subject<int>())
            {
                var iterator = subject.AsOnityAsyncEnumerable(2).GetAsyncEnumerator(caller.Token);
                var first = iterator.MoveNextAsync();
                try
                {
                    subject.OnNext(1);
                    subject.OnNext(2);
                    caller.Cancel();
                    Assert.That(Read(first), Is.True);
                    Assert.That(iterator.Current, Is.EqualTo(1));
                    var canceled = iterator.MoveNextAsync();
                    Assert.That(canceled.IsCanceled, Is.True);
                    var error = Catch(() => Read(canceled)) as OperationCanceledException;
                    Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
                }
                finally
                {
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void ActivePushCallback_ReentrantDisposeWaitsForCallbackReturnWithoutRewritingTrue()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var source = new Push();
                var iterator = source.AsOnityAsyncEnumerable(1).GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                OnityTask disposal = default;
                Exception error = null;
                bool timedOut = false;
                move.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        if (!move.GetAwaiter().GetResult())
                        {
                            throw new InvalidOperationException("Expected committed delivery.");
                        }
                        disposal = iterator.DisposeAsync();
                        entered.Set();
                        timedOut = !release.Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                        entered.Set();
                    }
                });
                Task worker = Task.Run(() => source.Observer.OnNext(1));
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(error, Is.Null);
                    Assert.That(disposal.IsCompleted, Is.False);
                    release.Set();
                    Join(worker);
                    Assert.That(timedOut, Is.False);
                    Read(disposal);
                    Assert.That(source.Detaches, Is.EqualTo(1));
                }
                finally
                {
                    release.Set();
                    Join(worker);
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void UnsubscriptionFailure_ReplacesTerminalFault_AndExplicitFalseRemainsFalse()
        {
            var original = new OperationCanceledException("source");
            var cleanup = new InvalidOperationException("unsubscribe");
            var source = new Push { Detach = () => throw cleanup, SubscribeAction = observer => observer.OnError(original) };
            var iterator = source.AsOnityAsyncEnumerable(1).GetAsyncEnumerator();
            Assert.That(Catch(() => Read(iterator.MoveNextAsync())), Is.SameAs(cleanup));
            Catch(() => Read(iterator.DisposeAsync()));
            Assert.That(source.Detaches, Is.EqualTo(1));
            source = new Push { Detach = () => throw cleanup };
            iterator = source.AsOnityAsyncEnumerable(1).GetAsyncEnumerator();
            var pending = iterator.MoveNextAsync();
            var dispose = iterator.DisposeAsync();
            Assert.That(Read(pending), Is.False);
            Assert.That(Catch(() => Read(dispose)), Is.SameAs(cleanup));
            Assert.That(source.Detaches, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Reverse_SequentialValues_TerminalAfterCleanup_ActualFaultAndCancel(int outcome)
        {
            using (var token = new CancellationTokenSource())
            {
                token.Cancel();
                var input = new OnityTaskCompletionSource<bool>();
                var cleanup = new OnityTaskCompletionSource();
                var fault = new OperationCanceledException("native actual fault", token.Token);
                var source = new Native
                {
                    Move = () => outcome == 1 ? OnityTask<bool>.FromTask(Task.FromException<bool>(fault)) : input.Task,
                    Cleanup = () => cleanup.Task
                };
                var observer = new Sink();
                IDisposable subscription = source.AsObservable().Subscribe(observer);
                try
                {
                    if (outcome == 0)
                    {
                        input.TrySetResult(false);
                    }
                    else if (outcome == 2)
                    {
                        input.TrySetCanceled(token.Token);
                    }
                    Assert.That(source.Disposals, Is.EqualTo(1));
                    Assert.That(observer.Completions + observer.Errors, Is.Zero);
                    cleanup.TrySetResult();
                    Wait(() => observer.Disposals == 1);
                    if (outcome == 0)
                    {
                        Assert.That(observer.Completions, Is.EqualTo(1));
                    }
                    else
                    {
                        Assert.That(observer.Errors, Is.EqualTo(1));
                        if (outcome == 1)
                        {
                            Assert.That(observer.Error, Is.SameAs(fault));
                        }
                        else
                        {
                            Assert.That(((OperationCanceledException)observer.Error).CancellationToken, Is.EqualTo(token.Token));
                        }
                    }
                }
                finally
                {
                    input.TrySetResult(false);
                    cleanup.TrySetResult();
                    subscription.Dispose();
                }
            }
            Assert.That(m_global, Is.Empty);
        }

        [Test]
        public void ReverseRange_IndependentSubscriptions_ValuesAndCleanupExactlyOnce()
        {
            var observable = OnityAsyncEnumerable.Range(4, 3).AsObservable();
            var first = new Sink();
            var second = new Sink();
            var firstHandle = observable.Subscribe(first);
            var secondHandle = observable.Subscribe(second);
            firstHandle.Dispose();
            secondHandle.Dispose();
            Assert.That(first.Values, Is.EqualTo(new[] { 4, 5, 6 }));
            Assert.That(second.Values, Is.EqualTo(first.Values));
            Assert.That(first.Completions, Is.EqualTo(1));
            Assert.That(second.Completions, Is.EqualTo(1));
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void ReverseExplicitDispose_StartsNativeCleanupWhileMovePending_SuppressesTerminal()
        {
            var pending = new OnityTaskCompletionSource<bool>();
            var cleanup = new OnityTaskCompletionSource();
            var source = new Native { Move = () => pending.Task, Cleanup = () => cleanup.Task };
            var observer = new Sink();
            var subscription = source.AsObservable().Subscribe(observer);
            try
            {
                subscription.Dispose();
                subscription.Dispose();
                Assert.That(source.Disposals, Is.EqualTo(1));
                Assert.That(observer.Disposals, Is.EqualTo(1));
                Assert.That(source.Token.IsCancellationRequested, Is.True);
                pending.TrySetResult(true);
                cleanup.TrySetResult();
                Assert.That(observer.Values, Is.Empty);
                Assert.That(observer.Errors + observer.Completions, Is.Zero);
                Assert.That(m_global, Is.Empty);
            }
            finally
            {
                pending.TrySetResult(false);
                cleanup.TrySetResult();
                subscription.Dispose();
            }
        }

        [Test]
        public void ReverseNativeDisposeMaySettlePendingMove_NoCircularCleanupWait()
        {
            var pending = new OnityTaskCompletionSource<bool>();
            var source = new Native { Move = () => pending.Task, Cleanup = () => { pending.TrySetResult(false); return default; } };
            var observer = new Sink();
            var subscription = source.AsObservable().Subscribe(observer);
            subscription.Dispose();
            Assert.That(pending.Task.IsCompleted, Is.True);
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(observer.Disposals, Is.EqualTo(1));
            Assert.That(observer.Completions + observer.Errors, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HeldObserverCallback_DisposeDoesNotInvokeOnDisposedConcurrently(bool terminal)
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var input = new OnityTaskCompletionSource<bool>();
                var source = new Native { Move = () => input.Task };
                bool active = false;
                bool concurrentDispose = false;
                bool timedOut = false;
                Action hold = () =>
                {
                    active = true;
                    entered.Set();
                    timedOut = !release.Wait(TimeSpan.FromSeconds(5));
                    active = false;
                };
                var observer = new Sink { Next = value => hold(), Complete = () => hold(), OnDispose = () => concurrentDispose = active };
                var subscription = source.AsObservable().Subscribe(observer);
                Task worker = Task.Run(() => { input.TrySetResult(!terminal); });
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    subscription.Dispose();
                    Assert.That(observer.Disposals, Is.Zero);
                    release.Set();
                    Join(worker);
                    Assert.That(timedOut, Is.False);
                    Assert.That(concurrentDispose, Is.False);
                    Assert.That(observer.Disposals, Is.EqualTo(1));
                    Assert.That(source.Disposals, Is.EqualTo(1));
                }
                finally
                {
                    try
                    {
                        subscription.Dispose();
                    }
                    finally
                    {
                        release.Set();
                        input.TrySetResult(false);
                        Join(worker);
                    }
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ThrowingObserver_ReportsOnceWithoutRecursiveNotification_IndependentCleanupAlsoReports(int callback)
        {
            var fault = new InvalidOperationException("observer callback");
            var cleanupFault = new InvalidOperationException("cleanup separately");
            var source = new Native { Count = callback == 0 ? 1 : 0 };
            if (callback == 0)
            {
                source.Cleanup = () => OnityTask.FromTask(Task.FromException(cleanupFault));
            }
            if (callback == 1)
            {
                source.Move = () => OnityTask<bool>.FromTask(Task.FromException<bool>(new InvalidOperationException("upstream")));
            }
            var observer = new Sink
            {
                Next = value => { if (callback == 0) { throw fault; } },
                Fail = error => { if (callback == 1) { throw fault; } },
                Complete = () => { if (callback == 2) { throw fault; } },
                OnDispose = () => { if (callback == 3) { throw fault; } }
            };
            var handle = source.AsObservable().Subscribe(observer);
            handle.Dispose();
            Wait(() => m_global.Count == (callback == 0 ? 2 : 1));
            Assert.That(m_global.FindAll(error => ReferenceEquals(error, fault)).Count, Is.EqualTo(1));
            if (callback == 0)
            {
                Assert.That(m_global, Does.Contain(cleanupFault));
                Assert.That(observer.Errors + observer.Completions, Is.Zero);
            }
            Assert.That(observer.Disposals, Is.EqualTo(1));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ValueOnlyObserver_IgnoresSuccess_RoutesFaultAndIndependentCancellationGlobally(int outcome)
        {
            using (var caller = new CancellationTokenSource())
            {
                caller.Cancel();
                var canceled = new OnityTaskCompletionSource<bool>();
                canceled.TrySetCanceled(caller.Token);
                var fault = new OperationCanceledException("value-only actual fault", caller.Token);
                var source = new Native { Count = 0 };
                if (outcome != 0)
                {
                    source.Move = () => outcome == 1
                        ? OnityTask<bool>.FromTask(Task.FromException<bool>(fault)) : canceled.Task;
                }
                var handle = source.AsObservable().Subscribe((Observer<int>)(value => Assert.Fail("Unexpected value")));
                handle.Dispose();
                Assert.That(m_global.Count, Is.EqualTo(outcome == 0 ? 0 : 1));
                if (outcome == 1)
                {
                    Assert.That(m_global[0], Is.SameAs(fault));
                }
                else if (outcome == 2)
                {
                    Assert.That(((OperationCanceledException)m_global[0]).CancellationToken, Is.EqualTo(caller.Token));
                }
            }
        }

        [Test]
        public void CancellationRoundTrip_BecomesNativeFaultedOceWithoutCanceledStatus()
        {
            using (var caller = new CancellationTokenSource())
            {
                var canceled = new OnityTaskCompletionSource<bool>();
                canceled.TrySetCanceled(caller.Token);
                var source = new Native { Move = () => canceled.Task };
                var iterator = source.AsObservable().AsOnityAsyncEnumerable(1).GetAsyncEnumerator();
                try
                {
                    var move = iterator.MoveNextAsync();
                    Assert.That(move.IsFaulted, Is.True);
                    Assert.That(move.IsCanceled, Is.False);
                    var error = Catch(() => Read(move)) as OperationCanceledException;
                    Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
                }
                finally
                {
                    Read(iterator.DisposeAsync());
                }
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

        private static void Wait(Func<bool> done) => Assert.That(SpinWait.SpinUntil(done, TimeSpan.FromSeconds(5)), Is.True);

        private static void Join(Task worker)
        {
            Wait(() => worker.IsCompleted);
            worker.GetAwaiter().GetResult();
        }

        private sealed class Push : IOnityObservable<int>
        {
            internal OnityObserver<int> Observer;
            internal int Subscriptions;
            internal int Detaches;
            internal bool NullHandle;
            internal Action<OnityObserver<int>> SubscribeAction;
            internal Action Detach;
            public IDisposable Subscribe(Observer<int> observer) => throw new InvalidOperationException("Lifecycle overload required");
            public IDisposable Subscribe(OnityObserver<int> observer)
            {
                Subscriptions++;
                Observer = observer;
                SubscribeAction?.Invoke(observer);
                return NullHandle ? null : new Handle(this);
            }
            private sealed class Handle : IDisposable
            {
                private Push m_owner;
                internal Handle(Push owner) => m_owner = owner;
                public void Dispose()
                {
                    Push owner = Interlocked.Exchange(ref m_owner, null);
                    if (owner != null)
                    {
                        owner.Detaches++;
                        owner.Detach?.Invoke();
                    }
                }
            }
        }

        private sealed class Native : IOnityAsyncEnumerable<int>, IOnityAsyncEnumerator<int>
        {
            internal int Count = 2;
            internal int Moves;
            internal int Disposals;
            internal CancellationToken Token;
            internal Func<OnityTask<bool>> Move;
            internal Func<OnityTask> Cleanup = () => default;
            public int Current => Moves;
            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Token = cancellationToken;
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

        private sealed class Sink : OnityObserver<int>
        {
            internal readonly List<int> Values = new List<int>();
            internal int Errors;
            internal int Completions;
            internal int Disposals;
            internal Exception Error;
            internal Action<int> Next;
            internal Action<Exception> Fail;
            internal Action Complete;
            internal Action OnDispose;
            protected override void OnNextCore(int value)
            {
                Values.Add(value);
                Next?.Invoke(value);
            }
            protected override void OnErrorCore(Exception error)
            {
                Errors++;
                Error = error;
                Fail?.Invoke(error);
            }
            protected override void OnCompletedCore(OnityResult result)
            {
                Completions++;
                Complete?.Invoke();
            }
            protected override void OnDisposed()
            {
                Disposals++;
                OnDispose?.Invoke();
            }
        }
    }
}
