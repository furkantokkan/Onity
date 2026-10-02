using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Locks the subscription list of <see cref="Subject{T}" /> and <see cref="ReactiveProperty{T}" />:
    /// O(1) removal that keeps every remaining subscription addressable, deferred in-order compaction after
    /// nested notifications, delivery to subscriptions added during a notification, and the
    /// <c>Subscribe(Action&lt;T&gt;)</c> fast path.
    /// </summary>
    [TestFixture]
    public sealed class OnityReactiveSubscriptionListTests
    {
        private Action<Exception> m_originalHandler;

        [SetUp]
        public void SetUp()
        {
            m_originalHandler = OnityObservableExceptionHandler.Handler;
        }

        [TearDown]
        public void TearDown()
        {
            OnityObservableExceptionHandler.Handler = m_originalHandler;
        }

        [Test]
        public void Subject_DisposeInEveryOrder_RemovesExactlyTheDisposedSubscription()
        {
            const int subscriberCount = 4;
            List<int[]> orders = new List<int[]>();
            CollectPermutations(new int[subscriberCount], new bool[subscriberCount], 0, orders);

            foreach (int[] order in orders)
            {
                using Subject<int> subject = new Subject<int>();
                List<int>[] received = new List<int>[subscriberCount];
                IDisposable[] subscriptions = new IDisposable[subscriberCount];
                bool[] isActive = new bool[subscriberCount];

                for (int i = 0; i < subscriberCount; i++)
                {
                    List<int> values = new List<int>();
                    received[i] = values;
                    subscriptions[i] = subject.Subscribe(values.Add);
                    isActive[i] = true;
                }

                int value = 0;

                for (int step = 0; step < order.Length; step++)
                {
                    subscriptions[order[step]].Dispose();
                    isActive[order[step]] = false;
                    value++;
                    subject.OnNext(value);

                    for (int i = 0; i < subscriberCount; i++)
                    {
                        bool receivedValue = received[i].Count > 0 && received[i][received[i].Count - 1] == value;
                        Assert.That(
                            receivedValue,
                            Is.EqualTo(isActive[i]),
                            "Order " + string.Join(",", order) + ", step " + step + ", subscriber " + i);
                    }
                }

                for (int i = 0; i < subscriberCount; i++)
                {
                    subscriptions[i].Dispose();
                }
            }
        }

        [Test]
        public void Subject_DisposeMovedSubscription_RemovesItAndKeepsTheOthers()
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable first = subject.Subscribe(value => log.Add("first:" + value));
            IDisposable second = subject.Subscribe(value => log.Add("second:" + value));
            IDisposable third = subject.Subscribe(value => log.Add("third:" + value));
            IDisposable fourth = subject.Subscribe(value => log.Add("fourth:" + value));

            // Removing the first subscription moves the last one into its slot; removal order is not an
            // ordering contract, so only the receiving set is checked.
            first.Dispose();
            fourth.Dispose();
            subject.OnNext(1);

            Assert.That(log, Is.EquivalentTo(new[] { "second:1", "third:1" }));

            log.Clear();
            third.Dispose();
            subject.OnNext(2);

            Assert.That(log, Is.EqualTo(new[] { "second:2" }));

            log.Clear();
            second.Dispose();
            subject.OnNext(3);

            Assert.That(log, Is.Empty);
        }

        [Test]
        public void Subject_RemovalsDuringNestedNotification_CompactInOrderAfterOutermostPass()
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable third = null;
            IDisposable fourth = null;

            IDisposable first = subject.Subscribe(
                value =>
                {
                    log.Add("first:" + value);

                    if (value == 1)
                    {
                        subject.OnNext(2);
                    }
                });

            IDisposable second = subject.Subscribe(
                value =>
                {
                    log.Add("second:" + value);

                    if (value == 2)
                    {
                        third.Dispose();
                        fourth.Dispose();
                    }
                });

            third = subject.Subscribe(value => log.Add("third:" + value));
            fourth = subject.Subscribe(value => log.Add("fourth:" + value));
            IDisposable fifth = subject.Subscribe(value => log.Add("fifth:" + value));
            IDisposable sixth = subject.Subscribe(value => log.Add("sixth:" + value));

            subject.OnNext(1);

            Assert.That(
                log,
                Is.EqualTo(
                    new[]
                    {
                        "first:1", "first:2", "second:2", "fifth:2", "sixth:2", "second:1", "fifth:1", "sixth:1"
                    }));

            // Compaction moved fifth and sixth down; disposing fifth must find its new slot.
            log.Clear();
            fifth.Dispose();
            subject.OnNext(3);

            Assert.That(log, Is.EqualTo(new[] { "first:3", "second:3", "sixth:3" }));

            log.Clear();
            sixth.Dispose();
            first.Dispose();
            subject.OnNext(4);

            Assert.That(log, Is.EqualTo(new[] { "second:4" }));

            second.Dispose();
            third.Dispose();
            fourth.Dispose();
        }

        [Test]
        public void Subject_SubscribeDuringNotification_ReceivesTheValueInProgress()
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable late = null;

            IDisposable early = subject.Subscribe(
                value =>
                {
                    log.Add("early:" + value);

                    if (late == null)
                    {
                        late = subject.Subscribe(lateValue => log.Add("late:" + lateValue));
                    }
                });

            subject.OnNext(1);
            subject.OnNext(2);

            Assert.That(log, Is.EqualTo(new[] { "early:1", "late:1", "early:2", "late:2" }));

            early.Dispose();
            late.Dispose();
        }

        [Test]
        public void Subject_DisposeSubjectDuringNotification_StopsDeliveryAndLaterCallsThrow()
        {
            Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable first = subject.Subscribe(
                value =>
                {
                    log.Add("first:" + value);
                    subject.Dispose();
                });
            IDisposable second = subject.Subscribe(value => log.Add("second:" + value));

            subject.OnNext(1);

            Assert.That(log, Is.EqualTo(new[] { "first:1" }));
            Assert.That(() => subject.OnNext(2), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => subject.Subscribe(_ => { }), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => subject.Subscribe(new Action<int>(_ => { })), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () =>
                {
                    first.Dispose();
                    second.Dispose();
                    first.Dispose();
                },
                Throws.Nothing);
        }

        [Test]
        public void Subject_SubscriptionDispose_IsIdempotent()
        {
            using Subject<int> subject = new Subject<int>();
            List<int> first = new List<int>();
            List<int> second = new List<int>();
            IDisposable firstSubscription = subject.Subscribe(first.Add);
            IDisposable secondSubscription = subject.Subscribe(second.Add);

            firstSubscription.Dispose();
            firstSubscription.Dispose();
            subject.OnNext(1);

            Assert.That(first, Is.Empty);
            Assert.That(second, Is.EqualTo(new[] { 1 }));

            secondSubscription.Dispose();
        }

        [Test]
        public void Subject_ObserverDisposesItselfThenThrows_LaterObserversStillReceiveValue()
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using Subject<int> subject = new Subject<int>();
            List<int> before = new List<int>();
            List<int> after = new List<int>();
            InvalidOperationException thrown = new InvalidOperationException("observer failed");
            IDisposable faulty = null;

            IDisposable beforeSubscription = subject.Subscribe(before.Add);
            faulty = subject.Subscribe(
                value =>
                {
                    faulty.Dispose();
                    throw thrown;
                });
            IDisposable afterSubscription = subject.Subscribe(after.Add);

            subject.OnNext(1);
            subject.OnNext(2);

            Assert.That(before, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(after, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(caught.Count, Is.EqualTo(1));
            Assert.That(caught[0], Is.SameAs(thrown));

            beforeSubscription.Dispose();
            afterSubscription.Dispose();
        }

        [Test]
        public void Subject_ThrowingObserverInNestedNotification_IsReportedByTheNestedPass()
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();

            IDisposable first = subject.Subscribe(
                value =>
                {
                    log.Add("first:" + value);

                    if (value == 1)
                    {
                        subject.OnNext(2);
                    }
                });
            IDisposable second = subject.Subscribe(
                value =>
                {
                    if (value == 2)
                    {
                        throw new InvalidOperationException("nested");
                    }

                    log.Add("second:" + value);
                });
            IDisposable third = subject.Subscribe(value => log.Add("third:" + value));

            subject.OnNext(1);

            Assert.That(log, Is.EqualTo(new[] { "first:1", "first:2", "third:2", "second:1", "third:1" }));
            Assert.That(caught.Count, Is.EqualTo(1));

            first.Dispose();
            second.Dispose();
            third.Dispose();
        }

        [Test]
        public void Subject_NestedPassWithThrowingObserver_KeepsOuterRemovalsDeferred()
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable first = null;

            first = subject.Subscribe(
                value =>
                {
                    log.Add("first:" + value);

                    if (value == 1)
                    {
                        // The nested pass must hand the outer pass its notification depth back, so this
                        // removal still only clears the slot and the outer pass reaches every other node.
                        subject.OnNext(2);
                        first.Dispose();
                    }
                });
            IDisposable second = subject.Subscribe(value => log.Add("second:" + value));
            IDisposable third = subject.Subscribe(
                value =>
                {
                    if (value == 2)
                    {
                        throw new InvalidOperationException("nested");
                    }

                    log.Add("third:" + value);
                });
            IDisposable fourth = subject.Subscribe(value => log.Add("fourth:" + value));

            subject.OnNext(1);

            Assert.That(
                log,
                Is.EqualTo(
                    new[] { "first:1", "first:2", "second:2", "fourth:2", "second:1", "third:1", "fourth:1" }));
            Assert.That(caught.Count, Is.EqualTo(1));

            log.Clear();
            subject.OnNext(3);

            Assert.That(log, Is.EqualTo(new[] { "second:3", "third:3", "fourth:3" }));

            second.Dispose();
            third.Dispose();
            fourth.Dispose();
        }

        [Test]
        public void Subject_MixedSubscriptionKinds_ReceiveInSubscriptionOrder()
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            Action<int> action = value => log.Add("action:" + value);

            IDisposable actionSubscription = subject.Subscribe(action);
            IDisposable observerSubscription = subject.Subscribe(value => log.Add("observer:" + value));
            IDisposable operatorSubscription = subject.Select(value => value * 10).Subscribe(action);
            IDisposable lifecycleSubscription = subject.Subscribe(new RecordingOnityObserver(log));

            subject.OnNext(1);

            Assert.That(log, Is.EqualTo(new[] { "action:1", "observer:1", "action:10", "lifecycle:1" }));

            actionSubscription.Dispose();
            observerSubscription.Dispose();
            operatorSubscription.Dispose();
            lifecycleSubscription.Dispose();
        }

        [Test]
        public void SubscribeAction_Subject_KeepsSubscriptionOrderWithObserverSubscriptions()
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            Action<int> action = value => log.Add("action:" + value);

            IDisposable observerSubscription = subject.Subscribe(value => log.Add("observer:" + value));
            IDisposable actionSubscription = subject.Subscribe(action);

            subject.OnNext(1);
            actionSubscription.Dispose();
            actionSubscription.Dispose();
            subject.OnNext(2);

            Assert.That(log, Is.EqualTo(new[] { "observer:1", "action:1", "observer:2" }));

            observerSubscription.Dispose();
        }

        [Test]
        public void SubscribeAction_SameActionTwice_CreatesTwoIndependentSubscriptions()
        {
            using Subject<int> subject = new Subject<int>();
            List<int> received = new List<int>();
            Action<int> action = received.Add;

            IDisposable first = subject.Subscribe(action);
            IDisposable second = subject.Subscribe(action);

            subject.OnNext(1);
            first.Dispose();
            subject.OnNext(2);

            Assert.That(received, Is.EqualTo(new[] { 1, 1, 2 }));

            second.Dispose();
        }

        [Test]
        public void SubscribeAction_ReactiveProperty_EmitsCurrentValueThenChanges()
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(7);
            List<int> received = new List<int>();
            Action<int> action = received.Add;

            IDisposable subscription = property.Subscribe(action);
            property.Value = 8;
            property.Value = 8;
            subscription.Dispose();
            property.Value = 9;

            Assert.That(received, Is.EqualTo(new[] { 7, 8 }));
        }

        [Test]
        public void SubscribeAction_ThrowingInitialValue_PropagatesAndDoesNotSubscribe()
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(1);
            int calls = 0;
            Action<int> action =
                value =>
                {
                    calls++;
                    throw new InvalidOperationException("initial");
                };

            Assert.That(() => property.Subscribe(action), Throws.TypeOf<InvalidOperationException>());

            property.Value = 2;

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void SubscribeAction_DisposedSources_ThrowObjectDisposedException()
        {
            Subject<int> subject = new Subject<int>();
            ReactiveProperty<int> property = new ReactiveProperty<int>(1);
            Action<int> action = _ => { };
            subject.Dispose();
            property.Dispose();

            Assert.That(() => subject.Subscribe(action), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => property.Subscribe(action), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => property.Subscribe(_ => { }, false), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => property.Value = 2, Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void SubscribeAction_PropertyDisposedByItsInitialValue_ThrowsObjectDisposedException()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(1);
            Action<int> action = _ => property.Dispose();

            Assert.That(() => property.Subscribe(action), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void SubscribeAction_CustomObservable_UsesPublicObserverSubscribe()
        {
            RecordingObservable source = new RecordingObservable();
            List<int> received = new List<int>();
            Action<int> action = received.Add;

            IDisposable subscription = source.Subscribe(action);
            source.Publish(5);
            subscription.Dispose();
            source.Publish(6);

            Assert.That(source.SubscribeCount, Is.EqualTo(1));
            Assert.That(received, Is.EqualTo(new[] { 5 }));
        }

        [Test]
        public void SubscribeAction_NullArguments_Throw()
        {
            using Subject<int> subject = new Subject<int>();

            Assert.That(
                () => OnityObservableExtensions.Subscribe(null, new Action<int>(_ => { })),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => subject.Subscribe((Action<int>)null), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => subject.Subscribe((Observer<int>)null), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ReactiveProperty_RemovalsAndAdditionsDuringNotification_KeepEveryPass()
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<string> log = new List<string>();
            IDisposable second = null;
            IDisposable late = null;

            IDisposable first = property.Subscribe(
                value =>
                {
                    log.Add("first:" + value);

                    if (value == 1)
                    {
                        second.Dispose();
                        late = property.Subscribe(lateValue => log.Add("late:" + lateValue), false);
                    }
                },
                false);
            second = property.Subscribe(value => log.Add("second:" + value), false);
            IDisposable third = property.Subscribe(value => log.Add("third:" + value), false);

            property.Value = 1;
            property.Value = 2;

            Assert.That(
                log,
                Is.EqualTo(new[] { "first:1", "third:1", "late:1", "first:2", "third:2", "late:2" }));

            log.Clear();
            third.Dispose();
            property.Value = 3;

            Assert.That(log, Is.EqualTo(new[] { "first:3", "late:3" }));

            first.Dispose();
            late.Dispose();
        }

        [Test]
        public void ReactiveProperty_NestedSetValueWithThrowingObserver_KeepsOuterRemovalsDeferred()
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<string> log = new List<string>();
            IDisposable first = null;

            first = property.Subscribe(
                value =>
                {
                    log.Add("first:" + value);

                    if (value == 1)
                    {
                        property.Value = 2;
                        first.Dispose();
                    }
                },
                false);
            IDisposable second = property.Subscribe(value => log.Add("second:" + value), false);
            IDisposable third = property.Subscribe(
                value =>
                {
                    if (value == 2)
                    {
                        throw new InvalidOperationException("nested");
                    }

                    log.Add("third:" + value);
                },
                false);
            IDisposable fourth = property.Subscribe(value => log.Add("fourth:" + value), false);

            property.Value = 1;

            Assert.That(
                log,
                Is.EqualTo(
                    new[] { "first:1", "first:2", "second:2", "fourth:2", "second:1", "third:1", "fourth:1" }));
            Assert.That(caught.Count, Is.EqualTo(1));
            Assert.That(property.Value, Is.EqualTo(2));

            log.Clear();
            property.Value = 3;

            Assert.That(log, Is.EqualTo(new[] { "second:3", "third:3", "fourth:3" }));

            second.Dispose();
            third.Dispose();
            fourth.Dispose();
        }

        [Test]
        public void ReactiveProperty_SubscribeDuringNotification_ReceivesCurrentAndInProgressValue()
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<string> log = new List<string>();
            IDisposable late = null;

            IDisposable early = property.Subscribe(
                value =>
                {
                    if (value == 1 && late == null)
                    {
                        late = property.Subscribe(lateValue => log.Add("late:" + lateValue));
                    }
                },
                false);

            property.Value = 1;

            Assert.That(log, Is.EqualTo(new[] { "late:1", "late:1" }));

            early.Dispose();
            late.Dispose();
        }

        [Test]
        public void ReactiveProperty_CustomComparer_DecidesWhetherValueChanged()
        {
            using ReactiveProperty<string> property =
                new ReactiveProperty<string>("a", StringComparer.OrdinalIgnoreCase);
            List<string> received = new List<string>();
            IDisposable subscription = property.Subscribe(received.Add, false);

            Assert.That(property.SetValue("A"), Is.False);
            Assert.That(property.SetValue("b"), Is.True);
            Assert.That(property.Value, Is.EqualTo("b"));
            Assert.That(received, Is.EqualTo(new[] { "b" }));

            subscription.Dispose();
        }

        [Test]
        public void ReactiveProperty_ThrowingObserver_IsIsolatedAndReported()
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<int> after = new List<int>();

            IDisposable faulty = property.Subscribe(value => throw new InvalidOperationException(), false);
            IDisposable afterSubscription = property.Subscribe(after.Add, false);

            Assert.That(property.SetValue(1), Is.True);
            Assert.That(after, Is.EqualTo(new[] { 1 }));
            Assert.That(caught.Count, Is.EqualTo(1));

            faulty.Dispose();
            afterSubscription.Dispose();
        }

        [Test]
        public void ReactiveProperty_DisposeDuringNotification_StopsDelivery()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<string> log = new List<string>();
            IDisposable first = property.Subscribe(
                value =>
                {
                    log.Add("first:" + value);
                    property.Dispose();
                },
                false);
            IDisposable second = property.Subscribe(value => log.Add("second:" + value), false);

            property.Value = 1;

            Assert.That(log, Is.EqualTo(new[] { "first:1" }));
            Assert.That(() => property.Value = 2, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () =>
                {
                    first.Dispose();
                    second.Dispose();
                },
                Throws.Nothing);
        }

        private static void CollectPermutations(int[] current, bool[] used, int depth, List<int[]> results)
        {
            if (depth == current.Length)
            {
                results.Add((int[])current.Clone());
                return;
            }

            for (int i = 0; i < current.Length; i++)
            {
                if (used[i])
                {
                    continue;
                }

                used[i] = true;
                current[depth] = i;
                CollectPermutations(current, used, depth + 1, results);
                used[i] = false;
            }
        }

        private sealed class RecordingOnityObserver : OnityObserver<int>
        {
            private readonly List<string> m_log;

            public RecordingOnityObserver(List<string> log)
            {
                m_log = log;
            }

            protected override void OnNextCore(int value)
            {
                m_log.Add("lifecycle:" + value);
            }
        }

        // A user observable: subscribing through it must use the public Observer<T> path.
        private sealed class RecordingObservable : IOnityObservable<int>
        {
            private readonly Subject<int> m_subject = new Subject<int>();

            public int SubscribeCount { get; private set; }

            public IDisposable Subscribe(Observer<int> observer)
            {
                SubscribeCount++;
                return m_subject.Subscribe(observer);
            }

            public IDisposable Subscribe(OnityObserver<int> observer)
            {
                SubscribeCount++;
                return m_subject.Subscribe(observer);
            }

            public void Publish(int value)
            {
                m_subject.OnNext(value);
            }
        }
    }
}
