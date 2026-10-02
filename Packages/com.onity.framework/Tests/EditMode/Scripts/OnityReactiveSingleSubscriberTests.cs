using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Locks the single-subscriber path of <see cref="Subject{T}" /> and <see cref="ReactiveProperty{T}" />:
    /// the transitions in and out of it, and that every callback-driven change behaves as in the general
    /// pass. Cases taking <c>withTrailingSubscriber</c> run once alone (single-subscriber path) and once with
    /// an extra subscriber after the primary one (general pass); the primary log must be the same.
    /// </summary>
    [TestFixture]
    public sealed class OnityReactiveSingleSubscriberTests
    {
        private const string k_trailing = "trailing:";

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
        public void Subject_SubscriberCountTransitions_DeliverToEveryLiveSubscriptionOnce()
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();

            subject.OnNext(0);
            Assert.That(log, Is.Empty);

            IDisposable first = subject.Subscribe(value => log.Add("first:" + value));
            subject.OnNext(1);
            Assert.That(log, Is.EqualTo(new[] { "first:1" }));

            log.Clear();
            IDisposable second = subject.Subscribe(value => log.Add("second:" + value));
            subject.OnNext(2);
            Assert.That(log, Is.EqualTo(new[] { "first:2", "second:2" }));

            // Back to one subscriber through a swap-back removal of the first slot.
            log.Clear();
            first.Dispose();
            subject.OnNext(3);
            Assert.That(log, Is.EqualTo(new[] { "second:3" }));

            log.Clear();
            IDisposable third = subject.Subscribe(value => log.Add("third:" + value));
            subject.OnNext(4);
            Assert.That(log, Is.EqualTo(new[] { "second:4", "third:4" }));

            // Back to one subscriber by removing the last slot.
            log.Clear();
            third.Dispose();
            subject.OnNext(5);
            Assert.That(log, Is.EqualTo(new[] { "second:5" }));

            log.Clear();
            second.Dispose();
            subject.OnNext(6);
            Assert.That(log, Is.Empty);

            IDisposable fourth = subject.Subscribe(value => log.Add("fourth:" + value));
            subject.OnNext(7);
            Assert.That(log, Is.EqualTo(new[] { "fourth:7" }));

            fourth.Dispose();
            fourth.Dispose();
            subject.OnNext(8);
            Assert.That(log, Is.EqualTo(new[] { "fourth:7" }));
        }

        [Test]
        public void Subject_DeferredRemovalLeavingOneSubscriber_UsesTheRemainingNodeAfterCompaction()
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable second = null;

            IDisposable first = subject.Subscribe(
                value =>
                {
                    log.Add("first:" + value);

                    if (value == 1)
                    {
                        second.Dispose();
                    }
                });
            second = subject.Subscribe(value => log.Add("second:" + value));

            subject.OnNext(1);
            subject.OnNext(2);

            Assert.That(log, Is.EqualTo(new[] { "first:1", "first:2" }));

            log.Clear();
            IDisposable third = subject.Subscribe(value => log.Add("third:" + value));
            subject.OnNext(3);

            Assert.That(log, Is.EqualTo(new[] { "first:3", "third:3" }));

            first.Dispose();
            third.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Subject_SelfUnsubscribeDuringCallback_MatchesGeneralPass(bool withTrailingSubscriber)
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable primary = null;

            primary = subject.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);
                    primary.Dispose();
                });
            IDisposable trailing = SubscribeTrailing(subject, log, withTrailingSubscriber);

            subject.OnNext(1);
            subject.OnNext(2);

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "primary:1" }));

            log.Clear();
            IDisposable later = subject.Subscribe(value => log.Add("later:" + value));
            subject.OnNext(3);

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "later:3" }));

            later.Dispose();
            trailing?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Subject_SubscribeDuringCallback_NewSubscriberReceivesValueInProgress(bool withTrailingSubscriber)
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable late = null;

            IDisposable primary = subject.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);

                    if (late == null)
                    {
                        late = subject.Subscribe(lateValue => log.Add("late:" + lateValue));
                    }
                });
            IDisposable trailing = SubscribeTrailing(subject, log, withTrailingSubscriber);

            subject.OnNext(1);
            subject.OnNext(2);

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "primary:1", "late:1", "primary:2", "late:2" }));

            primary.Dispose();
            late.Dispose();
            trailing?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Subject_ThrowingSingleObserver_IsReportedEveryTimeAndKeepsReceiving(bool withTrailingSubscriber)
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();

            IDisposable primary = subject.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);
                    throw new InvalidOperationException("primary " + value);
                });
            IDisposable trailing = SubscribeTrailing(subject, log, withTrailingSubscriber);

            Assert.That(() => subject.OnNext(1), Throws.Nothing);
            Assert.That(() => subject.OnNext(2), Throws.Nothing);

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "primary:1", "primary:2" }));
            Assert.That(caught.Count, Is.EqualTo(2));
            Assert.That(caught[1].Message, Is.EqualTo("primary 2"));

            primary.Dispose();
            trailing?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Subject_SingleObserverSubscribesThenThrows_NewSubscriberStillReceivesValue(bool withTrailingSubscriber)
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable late = null;

            IDisposable primary = subject.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);

                    if (late == null)
                    {
                        late = subject.Subscribe(lateValue => log.Add("late:" + lateValue));
                        throw new InvalidOperationException("after subscribe");
                    }
                });
            IDisposable trailing = SubscribeTrailing(subject, log, withTrailingSubscriber);

            subject.OnNext(1);

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "primary:1", "late:1" }));
            Assert.That(caught.Count, Is.EqualTo(1));

            primary.Dispose();
            late.Dispose();
            trailing?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Subject_NestedOnNextFromSingleCallback_MatchesGeneralPass(bool withTrailingSubscriber)
        {
            using Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();
            IDisposable late = null;

            IDisposable primary = subject.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);

                    if (value == 1)
                    {
                        subject.OnNext(2);
                    }
                    else if (value == 2 && late == null)
                    {
                        late = subject.Subscribe(lateValue => log.Add("late:" + lateValue));
                    }
                });
            IDisposable trailing = SubscribeTrailing(subject, log, withTrailingSubscriber);

            subject.OnNext(1);

            // The nested pass reaches the subscriber added during it, then the outer pass does too.
            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "primary:1", "primary:2", "late:2", "late:1" }));

            log.Clear();
            subject.OnNext(3);

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "primary:3", "late:3" }));

            primary.Dispose();
            late.Dispose();
            trailing?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Subject_DisposeDuringSingleCallback_StopsAndLaterCallsThrow(bool withTrailingSubscriber)
        {
            Subject<int> subject = new Subject<int>();
            List<string> log = new List<string>();

            IDisposable primary = subject.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);
                    subject.Dispose();
                });
            IDisposable trailing = SubscribeTrailing(subject, log, withTrailingSubscriber);

            subject.OnNext(1);

            Assert.That(log, Is.EqualTo(new[] { "primary:1" }));
            Assert.That(() => subject.OnNext(2), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => subject.Subscribe(_ => { }), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () =>
                {
                    primary.Dispose();
                    trailing?.Dispose();
                },
                Throws.Nothing);
        }

        [Test]
        public void Subject_SingleOperatorSinkAndLifecycleObserver_ReceiveThroughTheSinglePath()
        {
            using Subject<int> first = new Subject<int>();
            using Subject<int> second = new Subject<int>();
            List<int> projected = new List<int>();
            RecordingObserver observer = new RecordingObserver();

            IDisposable sinkSubscription = first.Select(value => value * 3).Subscribe(projected.Add);
            IDisposable observerSubscription = second.Subscribe(observer);

            first.OnNext(2);
            second.OnNext(5);
            sinkSubscription.Dispose();
            observerSubscription.Dispose();
            first.OnNext(4);
            second.OnNext(6);

            Assert.That(projected, Is.EqualTo(new[] { 6 }));
            Assert.That(observer.Values, Is.EqualTo(new[] { 5 }));
            Assert.That(observer.CompletedCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReactiveProperty_SelfUnsubscribeDuringCallback_MatchesGeneralPass(bool withTrailingSubscriber)
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<string> log = new List<string>();
            IDisposable primary = null;

            primary = property.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);
                    primary.Dispose();
                },
                false);
            IDisposable trailing = SubscribeTrailing(property, log, withTrailingSubscriber);

            property.Value = 1;
            property.Value = 2;

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "primary:1" }));

            log.Clear();
            IDisposable later = property.Subscribe(value => log.Add("later:" + value), false);
            property.Value = 3;

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "later:3" }));

            later.Dispose();
            trailing?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReactiveProperty_SubscribeDuringCallback_ReceivesCurrentAndInProgressValue(bool withTrailingSubscriber)
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<string> log = new List<string>();
            IDisposable late = null;

            IDisposable primary = property.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);

                    if (late == null)
                    {
                        late = property.Subscribe(lateValue => log.Add("late:" + lateValue));
                    }
                },
                false);
            IDisposable trailing = SubscribeTrailing(property, log, withTrailingSubscriber);

            property.Value = 1;
            property.Value = 2;

            Assert.That(
                WithoutTrailing(log),
                Is.EqualTo(new[] { "primary:1", "late:1", "late:1", "primary:2", "late:2" }));

            primary.Dispose();
            late.Dispose();
            trailing?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReactiveProperty_ThrowingSingleObserver_IsReportedAndValueStillChanges(bool withTrailingSubscriber)
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<string> log = new List<string>();

            IDisposable primary = property.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);
                    throw new InvalidOperationException("primary " + value);
                },
                false);
            IDisposable trailing = SubscribeTrailing(property, log, withTrailingSubscriber);

            Assert.That(property.SetValue(1), Is.True);
            Assert.That(property.SetValue(1), Is.False);
            Assert.That(property.SetValue(2), Is.True);

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "primary:1", "primary:2" }));
            Assert.That(caught.Count, Is.EqualTo(2));
            Assert.That(property.Value, Is.EqualTo(2));

            primary.Dispose();
            trailing?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReactiveProperty_NestedSetValueFromSingleCallback_MatchesGeneralPass(bool withTrailingSubscriber)
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<string> log = new List<string>();

            IDisposable primary = property.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);

                    if (value == 1)
                    {
                        property.Value = 2;
                    }
                },
                false);
            IDisposable trailing = SubscribeTrailing(property, log, withTrailingSubscriber);

            property.Value = 1;

            Assert.That(WithoutTrailing(log), Is.EqualTo(new[] { "primary:1", "primary:2" }));
            Assert.That(property.Value, Is.EqualTo(2));

            primary.Dispose();
            trailing?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReactiveProperty_DisposeDuringSingleCallback_StopsAndLaterCallsThrow(bool withTrailingSubscriber)
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<string> log = new List<string>();

            IDisposable primary = property.Subscribe(
                value =>
                {
                    log.Add("primary:" + value);
                    property.Dispose();
                },
                false);
            IDisposable trailing = SubscribeTrailing(property, log, withTrailingSubscriber);

            property.Value = 1;

            Assert.That(log, Is.EqualTo(new[] { "primary:1" }));
            Assert.That(() => property.Value = 2, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () =>
                {
                    primary.Dispose();
                    trailing?.Dispose();
                },
                Throws.Nothing);
        }

        private static IDisposable SubscribeTrailing(Subject<int> subject, List<string> log, bool subscribe)
        {
            return subscribe ? subject.Subscribe(value => log.Add(k_trailing + value)) : null;
        }

        private static IDisposable SubscribeTrailing(ReactiveProperty<int> property, List<string> log, bool subscribe)
        {
            return subscribe ? property.Subscribe(value => log.Add(k_trailing + value), false) : null;
        }

        private static List<string> WithoutTrailing(List<string> log)
        {
            List<string> filtered = new List<string>(log.Count);

            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].StartsWith(k_trailing, StringComparison.Ordinal) == false)
                {
                    filtered.Add(log[i]);
                }
            }

            return filtered;
        }

        private sealed class RecordingObserver : OnityObserver<int>
        {
            public List<int> Values { get; } = new List<int>();

            public int CompletedCount { get; private set; }

            protected override void OnNextCore(int value)
            {
                Values.Add(value);
            }

            protected override void OnCompletedCore(OnityResult result)
            {
                CompletedCount++;
            }
        }
    }
}
