// ONITY003 is disabled for this file: these tests intentionally drop the IDisposable returned by
// Subscribe/SubscribeAwait. (The rule does not report the void Subscribe(..., CancellationToken) overloads.)
#pragma warning disable ONITY003
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableConsumerEditModeTests
    {
        // A source whose moves are driven by the test: every move returns a pending task that the test
        // completes, and the enumeration token cancels the pending move.
        private sealed class GateSource : IOnityAsyncEnumerable<int>
        {
            internal int Acquires;
            internal int Moves;
            internal int Disposals;
            internal int Current;
            internal CancellationToken Token;
            internal OnityTaskCompletionSource<bool> Pending;

            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Acquires++;
                Token = cancellationToken;
                return new Enumerator(this, cancellationToken);
            }

            internal void Yield(int value)
            {
                Current = value;
                Pending.TrySetResult(true);
            }

            internal void Complete() => Pending.TrySetResult(false);
            internal void Fail(Exception exception) => Pending.TrySetException(exception);

            private sealed class Enumerator : IOnityAsyncEnumerator<int>
            {
                private readonly GateSource m_owner;
                private readonly CancellationToken m_token;
                private CancellationTokenRegistration m_registration;

                internal Enumerator(GateSource owner, CancellationToken token)
                {
                    m_owner = owner;
                    m_token = token;
                }

                public int Current => m_owner.Current;

                public OnityTask<bool> MoveNextAsync()
                {
                    m_owner.Moves++;
                    var pending = new OnityTaskCompletionSource<bool>();
                    m_owner.Pending = pending;
                    if (m_token.CanBeCanceled)
                    {
                        m_registration = m_token.Register(() => pending.TrySetCanceled(m_token));
                    }
                    return pending.Task;
                }

                public OnityTask DisposeAsync()
                {
                    m_owner.Disposals++;
                    m_registration.Dispose();
                    return OnityTask.Completed;
                }
            }
        }

        private sealed class RecordingObserver : IObserver<int>
        {
            internal readonly List<string> Events = new List<string>();

            public void OnNext(int value) => Events.Add("next:" + value);
            public void OnError(Exception error) => Events.Add("error:" + error.Message);
            public void OnCompleted() => Events.Add("completed");
        }

        private static LinqProbe<int> Items(params int[] items) => LinqTestSupport.Items(items);

        // ---- admission -------------------------------------------------------------------

        [Test]
        public void Arguments_ValidateSynchronously_WithoutAcquiringTheStream()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = Items(1, 2, 3);
            Action<int> action = null;
            Func<int, OnityTaskVoid> voidHandler = null;
            Func<int, CancellationToken, OnityTaskVoid> voidToken = null;
            Func<int, OnityTask> awaiting = null;
            Func<int, CancellationToken, OnityTask> awaitToken = null;
            Action<Exception> onError = null;
            Action onCompleted = null;
            IObserver<int> observer = null;
            CancellationToken token = default;

            Assert.Throws<ArgumentNullException>(() => missing.Subscribe(value => { }));
            Assert.Throws<ArgumentNullException>(() => missing.Subscribe(value => { }, token));
            Assert.Throws<ArgumentNullException>(() => missing.Subscribe(new RecordingObserver()));
            Assert.Throws<ArgumentNullException>(() => missing.SubscribeAwait(value => OnityTask.Completed));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(action));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(action, token));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(voidHandler));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(voidHandler, token));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(voidToken));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(voidToken, token));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(value => { }, onError));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(value => { }, onError, token));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(value => { }, onCompleted));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(value => { }, onCompleted, token));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(voidHandler, onError));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(voidHandler, onCompleted, token));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(observer));
            Assert.Throws<ArgumentNullException>(() => source.Subscribe(observer, token));
            Assert.Throws<ArgumentNullException>(() => source.SubscribeAwait(awaiting));
            Assert.Throws<ArgumentNullException>(() => source.SubscribeAwait(awaiting, token));
            Assert.Throws<ArgumentNullException>(() => source.SubscribeAwait(awaitToken));
            Assert.Throws<ArgumentNullException>(() => source.SubscribeAwait(awaitToken, token));
            Assert.Throws<ArgumentNullException>(() => source.SubscribeAwait(value => OnityTask.Completed, onError));
            Assert.Throws<ArgumentNullException>(() => source.SubscribeAwait(value => OnityTask.Completed, onCompleted));
            Assert.Throws<ArgumentNullException>(() => source.SubscribeAwait(
                (value, cancellation) => OnityTask.Completed, onError, token));
            Assert.Throws<ArgumentNullException>(() => source.SubscribeAwait(
                (value, cancellation) => OnityTask.Completed, onCompleted, token));
            Assert.That(source.Acquires, Is.Zero);
        }

        // ---- Subscribe: items, completion, failure ---------------------------------------

        [Test]
        public void Subscribe_Action_DeliversItemsInOrder_BeforeReturning_ForASynchronousStream()
        {
            var seen = new List<int>();
            var source = Items(1, 2, 3);
            IDisposable subscription = source.Subscribe(seen.Add);
            Assert.That(subscription, Is.Not.Null);
            Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
            subscription.Dispose();
            subscription.Dispose();
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_EmptyStream_CompletesWithoutItems()
        {
            var events = new List<string>();
            var source = Items();
            source.Subscribe(value => events.Add("next"), () => events.Add("completed"));
            Assert.That(events, Is.EqualTo(new[] { "completed" }));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_OnCompleted_RunsOnceAfterUpstreamCleanup()
        {
            var source = Items(1, 2);
            int completions = 0;
            int disposalsAtCompletion = -1;
            source.Subscribe(value => { }, () =>
            {
                completions++;
                disposalsAtCompletion = source.Disposals;
            });
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(disposalsAtCompletion, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_OnError_ReceivesTheFailureAfterCleanup_AndCompletionIsSkipped()
        {
            var fault = new InvalidOperationException("source");
            var source = Items(1, 2, 3);
            source.MoveFailureAt = 2;
            source.MoveFailure = fault;
            var seen = new List<int>();
            Exception reported = null;
            int disposalsAtError = -1;
            source.Subscribe(seen.Add, error =>
            {
                reported = error;
                disposalsAtError = source.Disposals;
            });
            Assert.That(seen, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(reported, Is.SameAs(fault));
            Assert.That(disposalsAtError, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_OnCompletedOverload_SkipsCompletionOnFailure_AndLogsTheFailure()
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: source failure"));
            bool completed = false;
            var failing = Items(1);
            failing.MoveFailureAt = 0;
            failing.MoveFailure = new InvalidOperationException("source failure");
            failing.Subscribe(value => { }, () => completed = true, default(CancellationToken));
            Assert.That(completed, Is.False);
        }

        [Test]
        public void Subscribe_CleanupFailure_ReplacesNormalCompletion()
        {
            var cleanup = new InvalidOperationException("cleanup");
            var source = Items(1, 2);
            source.DisposeFailure = cleanup;
            bool completed = false;
            Exception reported = null;
            source.Subscribe(value => { }, error => reported = error, default(CancellationToken));
            Assert.That(reported, Is.SameAs(cleanup));
            Assert.That(completed, Is.False);
        }

        [Test]
        public void Subscribe_CleanupFailureWithoutOnError_SkipsCompletion_AndLogsTheFailure()
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: cleanup"));
            var source = Items(1, 2);
            source.DisposeFailure = new InvalidOperationException("cleanup");
            bool completed = false;
            source.Subscribe(value => { }, () => completed = true);
            Assert.That(completed, Is.False);
        }

        [Test]
        public void Subscribe_Observer_ReceivesItemsCompletionAndFailure()
        {
            var observer = new RecordingObserver();
            Items(1, 2).Subscribe(observer);
            Assert.That(observer.Events, Is.EqualTo(new[] { "next:1", "next:2", "completed" }));

            var failing = new RecordingObserver();
            var source = Items(1, 2);
            source.MoveFailureAt = 1;
            source.MoveFailure = new InvalidOperationException("boom");
            source.Subscribe(failing, default(CancellationToken));
            Assert.That(failing.Events, Is.EqualTo(new[] { "next:1", "error:boom" }));
        }

        // ---- cancellation ----------------------------------------------------------------

        [Test]
        public void Subscribe_WithToken_CancellationEndsSilently()
        {
            using (var cts = new CancellationTokenSource())
            {
                var source = Items(1, 2, 3, 4);
                var events = new List<string>();
                source.Subscribe(value =>
                {
                    events.Add("next:" + value);
                    if (value == 2)
                    {
                        cts.Cancel();
                    }
                }, () => events.Add("completed"), cts.Token);
                Assert.That(events, Is.EqualTo(new[] { "next:1", "next:2" }));
                Assert.That(source.Disposals, Is.EqualTo(1));
                Assert.That(source.Token, Is.EqualTo(cts.Token));
            }

            using (var cts = new CancellationTokenSource())
            {
                var source = Items(1, 2, 3, 4);
                var errors = new List<Exception>();
                source.Subscribe(value =>
                {
                    if (value == 2)
                    {
                        cts.Cancel();
                    }
                }, errors.Add, cts.Token);
                Assert.That(errors, Is.Empty);
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
        }

        [Test]
        public void Subscribe_PreCanceledToken_EndsSilentlyWithoutItems()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var source = Items(1, 2);
                bool called = false;
                source.Subscribe(value => called = true, cts.Token);
                source.SubscribeAwait(value => { called = true; return OnityTask.Completed; }, cts.Token);
                Assert.That(called, Is.False);
                Assert.That(source.Disposals, Is.EqualTo(2));
            }
        }

        [Test]
        public void Subscribe_RunsSynchronouslyUntilTheFirstSuspension_AndDisposeStopsThePendingMove()
        {
            var source = new GateSource();
            var seen = new List<int>();
            var events = new List<string>();
            IDisposable subscription = source.Subscribe(seen.Add, error => events.Add("error"));
            Assert.That(source.Acquires, Is.EqualTo(1));
            Assert.That(source.Moves, Is.EqualTo(1));
            Assert.That(source.Token.CanBeCanceled, Is.True);
            source.Yield(7);
            source.Yield(8);
            Assert.That(seen, Is.EqualTo(new[] { 7, 8 }));
            Assert.That(source.Moves, Is.EqualTo(3));
            subscription.Dispose();
            Assert.That(source.Token.IsCancellationRequested, Is.True);
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(events, Is.Empty);
            subscription.Dispose();
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_TokenOverloadsCompleteWhenTheStreamEnds()
        {
            var source = new GateSource();
            var events = new List<string>();
            source.Subscribe(value => events.Add("next:" + value), () => events.Add("completed"), CancellationToken.None);
            source.Yield(1);
            Assert.That(events, Is.EqualTo(new[] { "next:1" }));
            source.Complete();
            Assert.That(events, Is.EqualTo(new[] { "next:1", "completed" }));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_AfterCompletion_DoesNothing()
        {
            var source = Items(1);
            IDisposable subscription = source.Subscribe(value => { });
            subscription.Dispose();
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_AsyncFailure_ReachesOnErrorWhenItArrives()
        {
            var fault = new InvalidOperationException("late");
            var source = new GateSource();
            Exception reported = null;
            source.Subscribe(value => { }, error => reported = error);
            Assert.That(reported, Is.Null);
            source.Fail(fault);
            Assert.That(reported, Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        // ---- handlers --------------------------------------------------------------------

        [Test]
        public void Subscribe_HandlerThatThrowsOperationCanceled_IsIgnoredAndTheLoopContinues()
        {
            var seen = new List<int>();
            Items(1, 2, 3).Subscribe(value =>
            {
                seen.Add(value);
                if (value == 2)
                {
                    throw new OperationCanceledException();
                }
            });
            Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Subscribe_HandlerThatThrows_IsLoggedAndTheNextItemStillArrives()
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: handler failure"));
            var seen = new List<int>();
            var events = new List<string>();
            Items(1, 2, 3).Subscribe(value =>
            {
                seen.Add(value);
                if (value == 2)
                {
                    throw new InvalidOperationException("handler failure");
                }
            }, () => events.Add("completed"));
            Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(events, Is.EqualTo(new[] { "completed" }));
        }

        [Test]
        public void Subscribe_WithoutOnError_LogsAStreamFailure()
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: source failure"));
            var source = Items(1, 2);
            source.MoveFailureAt = 1;
            source.MoveFailure = new InvalidOperationException("source failure");
            var seen = new List<int>();
            source.Subscribe(seen.Add);
            Assert.That(seen, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Subscribe_CallbackThatThrows_IsLogged()
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: completed failure"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: error failure"));
            Items(1).Subscribe(value => { }, () => throw new InvalidOperationException("completed failure"));
            var failing = Items(1);
            failing.MoveFailureAt = 0;
            failing.MoveFailure = new ArgumentException("source");
            failing.Subscribe(value => { }, error => throw new InvalidOperationException("error failure"));
        }

        [Test]
        public void Subscribe_VoidHandler_IsFireAndForget_SoHandlersMayOverlap()
        {
            var gates = new Dictionary<int, OnityTaskCompletionSource<bool>>();
            var finished = new List<int>();
            async OnityTaskVoid Handler(int value)
            {
                var gate = new OnityTaskCompletionSource<bool>();
                gates[value] = gate;
                await gate.Task;
                finished.Add(value);
            }
            var source = Items(1, 2, 3);
            int completions = 0;
            source.Subscribe(Handler, () => completions++);
            Assert.That(gates.Count, Is.EqualTo(3));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(finished, Is.Empty);
            gates[3].TrySetResult(true);
            gates[1].TrySetResult(true);
            gates[2].TrySetResult(true);
            Assert.That(finished, Is.EqualTo(new[] { 3, 1, 2 }));
        }

        [Test]
        public void SubscribeAwait_AwaitsEachHandler_BeforeRequestingTheNextItem()
        {
            var source = Items(1, 2, 3);
            var gate = new OnityTaskCompletionSource();
            var handled = new List<int>();
            int completions = 0;
            source.SubscribeAwait(value =>
            {
                handled.Add(value);
                return value == 1 ? gate.Task : OnityTask.Completed;
            }, () => completions++);
            Assert.That(handled, Is.EqualTo(new[] { 1 }));
            Assert.That(source.Moves, Is.EqualTo(1));
            gate.TrySetResult();
            Assert.That(handled, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void SubscribeAwait_HandlerFault_IsLoggedAndTheLoopContinues()
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: awaited failure"));
            var handled = new List<int>();
            Items(1, 2, 3).SubscribeAwait(value =>
            {
                handled.Add(value);
                return value == 2 ? OnityTask.FromException(new InvalidOperationException("awaited failure"))
                    : OnityTask.Completed;
            });
            Assert.That(handled, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void SubscribeAwait_HandlerCanceled_IsIgnoredAndTheLoopContinues()
        {
            var handled = new List<int>();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Items(1, 2).SubscribeAwait(value =>
                {
                    handled.Add(value);
                    return OnityTask.FromCanceled(cts.Token);
                });
            }
            Assert.That(handled, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void TokenAwareHandlers_ReceiveTheSubscriptionToken()
        {
            var seen = new List<CancellationToken>();
            var source = new GateSource();
            IDisposable first = source.Subscribe((value, token) =>
            {
                seen.Add(token);
                return default;
            });
            source.Yield(1);
            Assert.That(seen.Count, Is.EqualTo(1));
            Assert.That(seen[0], Is.EqualTo(source.Token));
            Assert.That(seen[0].CanBeCanceled, Is.True);
            first.Dispose();
            Assert.That(seen[0].IsCancellationRequested, Is.True);

            using (var cts = new CancellationTokenSource())
            {
                var second = new GateSource();
                var awaited = new List<CancellationToken>();
                second.SubscribeAwait((value, token) =>
                {
                    awaited.Add(token);
                    return OnityTask.Completed;
                }, cts.Token);
                second.Yield(1);
                Assert.That(awaited.Count, Is.EqualTo(1));
                Assert.That(awaited[0], Is.EqualTo(cts.Token));
                Assert.That(second.Token, Is.EqualTo(cts.Token));
            }
        }

        [Test]
        public void Subscribe_OnAnAsyncReactiveProperty_ReceivesTheCurrentValueAndEveryUpdate()
        {
            var property = new OnityAsyncReactiveProperty<int>(1);
            var seen = new List<int>();
            IDisposable subscription = property.Subscribe(seen.Add);
            property.Value = 2;
            property.Value = 3;
            subscription.Dispose();
            property.Value = 4;
            Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        // ---- every generated overload ----------------------------------------------------

        private sealed class Handlers
        {
            internal readonly List<int> Seen = new List<int>();
            internal readonly List<string> Events = new List<string>();
            internal readonly CancellationToken Token = CancellationToken.None;
            internal readonly RecordingObserver Observer = new RecordingObserver();
            internal Action<int> Action => Seen.Add;
            internal Func<int, OnityTaskVoid> Void => value =>
            {
                Seen.Add(value);
                return default;
            };
            internal Func<int, CancellationToken, OnityTaskVoid> VoidToken => (value, token) =>
            {
                Seen.Add(value);
                return default;
            };
            internal Func<int, OnityTask> Await => value =>
            {
                Seen.Add(value);
                return OnityTask.Completed;
            };
            internal Func<int, CancellationToken, OnityTask> AwaitToken => (value, token) =>
            {
                Seen.Add(value);
                return OnityTask.Completed;
            };
            internal Action<Exception> OnError => error => Events.Add("error");
            internal Action OnCompleted => () => Events.Add("completed");
        }

        private sealed class Shape
        {
            internal readonly string Name;
            internal readonly bool HasError;
            internal readonly bool HasCompleted;
            internal readonly bool IsObserver;
            internal readonly Func<IOnityAsyncEnumerable<int>, Handlers, IDisposable> Run;

            internal Shape(string name, bool hasError, bool hasCompleted, bool isObserver,
                Func<IOnityAsyncEnumerable<int>, Handlers, IDisposable> run)
            {
                Name = name;
                HasError = hasError;
                HasCompleted = hasCompleted;
                IsObserver = isObserver;
                Run = run;
            }
        }

        private static IDisposable Discard(Action action)
        {
            action();
            return null;
        }

        private static List<Shape> AllShapes()
        {
            return new List<Shape>
            {
                new Shape("Subscribe(Action)", false, false, false, (s, h) => s.Subscribe(h.Action)),
                new Shape("Subscribe(Action, ct)", false, false, false,
                    (s, h) => Discard(() => s.Subscribe(h.Action, h.Token))),
                new Shape("Subscribe(Void)", false, false, false, (s, h) => s.Subscribe(h.Void)),
                new Shape("Subscribe(Void, ct)", false, false, false,
                    (s, h) => Discard(() => s.Subscribe(h.Void, h.Token))),
                new Shape("Subscribe(VoidToken)", false, false, false, (s, h) => s.Subscribe(h.VoidToken)),
                new Shape("Subscribe(VoidToken, ct)", false, false, false,
                    (s, h) => Discard(() => s.Subscribe(h.VoidToken, h.Token))),
                new Shape("Subscribe(Action, onError)", true, false, false, (s, h) => s.Subscribe(h.Action, h.OnError)),
                new Shape("Subscribe(Action, onError, ct)", true, false, false,
                    (s, h) => Discard(() => s.Subscribe(h.Action, h.OnError, h.Token))),
                new Shape("Subscribe(Void, onError)", true, false, false, (s, h) => s.Subscribe(h.Void, h.OnError)),
                new Shape("Subscribe(Void, onError, ct)", true, false, false,
                    (s, h) => Discard(() => s.Subscribe(h.Void, h.OnError, h.Token))),
                new Shape("Subscribe(Action, onCompleted)", false, true, false,
                    (s, h) => s.Subscribe(h.Action, h.OnCompleted)),
                new Shape("Subscribe(Action, onCompleted, ct)", false, true, false,
                    (s, h) => Discard(() => s.Subscribe(h.Action, h.OnCompleted, h.Token))),
                new Shape("Subscribe(Void, onCompleted)", false, true, false,
                    (s, h) => s.Subscribe(h.Void, h.OnCompleted)),
                new Shape("Subscribe(Void, onCompleted, ct)", false, true, false,
                    (s, h) => Discard(() => s.Subscribe(h.Void, h.OnCompleted, h.Token))),
                new Shape("Subscribe(observer)", true, true, true, (s, h) => s.Subscribe(h.Observer)),
                new Shape("Subscribe(observer, ct)", true, true, true,
                    (s, h) => Discard(() => s.Subscribe(h.Observer, h.Token))),
                new Shape("SubscribeAwait(Await)", false, false, false, (s, h) => s.SubscribeAwait(h.Await)),
                new Shape("SubscribeAwait(Await, ct)", false, false, false,
                    (s, h) => Discard(() => s.SubscribeAwait(h.Await, h.Token))),
                new Shape("SubscribeAwait(AwaitToken)", false, false, false, (s, h) => s.SubscribeAwait(h.AwaitToken)),
                new Shape("SubscribeAwait(AwaitToken, ct)", false, false, false,
                    (s, h) => Discard(() => s.SubscribeAwait(h.AwaitToken, h.Token))),
                new Shape("SubscribeAwait(Await, onError)", true, false, false,
                    (s, h) => s.SubscribeAwait(h.Await, h.OnError)),
                new Shape("SubscribeAwait(Await, onError, ct)", true, false, false,
                    (s, h) => Discard(() => s.SubscribeAwait(h.Await, h.OnError, h.Token))),
                new Shape("SubscribeAwait(AwaitToken, onError)", true, false, false,
                    (s, h) => s.SubscribeAwait(h.AwaitToken, h.OnError)),
                new Shape("SubscribeAwait(AwaitToken, onError, ct)", true, false, false,
                    (s, h) => Discard(() => s.SubscribeAwait(h.AwaitToken, h.OnError, h.Token))),
                new Shape("SubscribeAwait(Await, onCompleted)", false, true, false,
                    (s, h) => s.SubscribeAwait(h.Await, h.OnCompleted)),
                new Shape("SubscribeAwait(Await, onCompleted, ct)", false, true, false,
                    (s, h) => Discard(() => s.SubscribeAwait(h.Await, h.OnCompleted, h.Token))),
                new Shape("SubscribeAwait(AwaitToken, onCompleted)", false, true, false,
                    (s, h) => s.SubscribeAwait(h.AwaitToken, h.OnCompleted)),
                new Shape("SubscribeAwait(AwaitToken, onCompleted, ct)", false, true, false,
                    (s, h) => Discard(() => s.SubscribeAwait(h.AwaitToken, h.OnCompleted, h.Token)))
            };
        }

        [Test]
        public void EveryOverload_DeliversItems_AndSignalsCompletion()
        {
            List<Shape> shapes = AllShapes();
            Assert.That(shapes.Count, Is.EqualTo(28));
            foreach (Shape shape in shapes)
            {
                var handlers = new Handlers();
                var source = Items(1, 2, 3);
                shape.Run(source, handlers);
                if (shape.IsObserver)
                {
                    Assert.That(handlers.Observer.Events,
                        Is.EqualTo(new[] { "next:1", "next:2", "next:3", "completed" }), shape.Name);
                }
                else
                {
                    Assert.That(handlers.Seen, Is.EqualTo(new[] { 1, 2, 3 }), shape.Name);
                    IResolveConstraint events = shape.HasCompleted ? (IResolveConstraint)Is.EqualTo(new[] { "completed" })
                        : Is.Empty;
                    Assert.That(handlers.Events, events, shape.Name);
                }
                Assert.That(source.Disposals, Is.EqualTo(1), shape.Name);
            }
        }

        [Test]
        public void EveryErrorOverload_ReportsOneStreamFailure_AfterTheEarlierItems()
        {
            int checkedShapes = 0;
            foreach (Shape shape in AllShapes())
            {
                if (!shape.HasError)
                {
                    continue;
                }
                var handlers = new Handlers();
                var source = Items(1, 2, 3);
                source.MoveFailureAt = 2;
                source.MoveFailure = new InvalidOperationException("source");
                shape.Run(source, handlers);
                if (shape.IsObserver)
                {
                    Assert.That(handlers.Observer.Events,
                        Is.EqualTo(new[] { "next:1", "next:2", "error:source" }), shape.Name);
                }
                else
                {
                    Assert.That(handlers.Seen, Is.EqualTo(new[] { 1, 2 }), shape.Name);
                    Assert.That(handlers.Events, Is.EqualTo(new[] { "error" }), shape.Name);
                }
                Assert.That(source.Disposals, Is.EqualTo(1), shape.Name);
                checkedShapes++;
            }
            Assert.That(checkedShapes, Is.EqualTo(10));
        }

        [Test]
        public void LambdaShapes_BindToTheIntendedOverloads()
        {
            var source = Items(1, 2);
            var seen = new List<int>();
            IDisposable action = source.Subscribe(value => seen.Add(value));
            IDisposable asyncVoid = source.Subscribe(async value =>
            {
                await OnityTask.Completed;
                seen.Add(value * 10);
            });
            IDisposable awaited = source.SubscribeAwait(async value =>
            {
                await OnityTask.Completed;
                seen.Add(value * 100);
            });
            IDisposable awaitedWithToken = source.SubscribeAwait(async (value, token) =>
            {
                await OnityTask.Completed;
                seen.Add(value * 1000);
            });
            Assert.That(action, Is.Not.Null);
            Assert.That(asyncVoid, Is.Not.Null);
            Assert.That(awaited, Is.Not.Null);
            Assert.That(awaitedWithToken, Is.Not.Null);
            Assert.That(seen.Count, Is.EqualTo(8));
            Assert.That(seen, Is.EquivalentTo(new[] { 1, 2, 10, 20, 100, 200, 1000, 2000 }));
        }
    }
}
