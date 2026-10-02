using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Core;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Locks the semantics of the sink-based synchronous operators: fused <c>Where</c> + <c>Select</c>,
    /// per-subscription state, upstream disposal of <c>Take</c> and <c>TakeWhile</c> (also when they complete
    /// while subscribing), the <c>Skip(0)</c> and <c>Take(0)</c> shortcuts and argument validation.
    /// </summary>
    [TestFixture]
    public sealed class OnityReactiveOperatorSinkTests
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
        public void WhereSelect_CallsPredicateThenSelectorForPassingValuesOnly()
        {
            using Subject<int> source = new Subject<int>();
            List<string> log = new List<string>();
            IOnityObservable<int> pipeline = source
                .Where(
                    value =>
                    {
                        log.Add("where:" + value);
                        return value % 2 == 0;
                    })
                .Select(
                    value =>
                    {
                        log.Add("select:" + value);
                        return value * 10;
                    });
            Action<int> sink = value => log.Add("sink:" + value);

            IDisposable subscription = pipeline.Subscribe(sink);
            source.OnNext(1);
            source.OnNext(2);
            subscription.Dispose();
            source.OnNext(4);

            Assert.That(log, Is.EqualTo(new[] { "where:1", "where:2", "select:2", "sink:20" }));
        }

        [Test]
        public void WhereSelect_WhereObservableStaysUsableOnItsOwn()
        {
            using Subject<int> source = new Subject<int>();
            IOnityObservable<int> evens = source.Where(value => value % 2 == 0);
            IOnityObservable<string> labels = evens.Select(value => "v" + value);
            List<int> evenValues = new List<int>();
            List<string> labelValues = new List<string>();

            IDisposable evenSubscription = evens.Subscribe(evenValues.Add);
            IDisposable labelSubscription = labels.Subscribe(labelValues.Add);
            source.OnNext(1);
            source.OnNext(2);
            evenSubscription.Dispose();
            source.OnNext(4);

            Assert.That(evenValues, Is.EqualTo(new[] { 2 }));
            Assert.That(labelValues, Is.EqualTo(new[] { "v2", "v4" }));

            labelSubscription.Dispose();
        }

        [Test]
        public void WhereSelect_OverReactiveProperty_FiltersCurrentValueAndChanges()
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(2);
            List<int> received = new List<int>();
            IDisposable subscription = property
                .Where(value => value > 1)
                .Select(value => value * 3)
                .Subscribe(received.Add);

            property.Value = 1;
            property.Value = 5;
            subscription.Dispose();
            property.Value = 7;

            Assert.That(received, Is.EqualTo(new[] { 6, 15 }));
        }

        [Test]
        public void Chain_SubscribeDisposeRepeatedly_LeavesNoSubscriptionBehind()
        {
            using Subject<int> source = new Subject<int>();
            List<int> received = new List<int>();
            Action<int> sink = received.Add;

            for (int i = 0; i < 64; i++)
            {
                source.Where(value => value > 0).Select(value => value + 1).Subscribe(sink).Dispose();
            }

            source.OnNext(1);

            Assert.That(received, Is.Empty);
        }

        [Test]
        public void SelectAfterSelect_ProjectsInOrder()
        {
            using Subject<int> source = new Subject<int>();
            List<string> received = new List<string>();
            IDisposable subscription = source
                .Select(value => value + 1)
                .Select(value => "n" + value)
                .Subscribe(received.Add);

            source.OnNext(1);
            source.OnNext(2);

            Assert.That(received, Is.EqualTo(new[] { "n2", "n3" }));
            subscription.Dispose();
        }

        [Test]
        public void ThrowingPredicate_IsReportedBySourceAndOtherSubscribersStillReceive()
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using Subject<int> source = new Subject<int>();
            List<int> filtered = new List<int>();
            List<int> plain = new List<int>();

            IDisposable faulty = source
                .Where(value => value == 1 ? throw new InvalidOperationException("predicate") : true)
                .Subscribe(filtered.Add);
            IDisposable other = source.Subscribe(plain.Add);

            source.OnNext(1);
            source.OnNext(2);

            Assert.That(filtered, Is.EqualTo(new[] { 2 }));
            Assert.That(plain, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(caught.Count, Is.EqualTo(1));

            faulty.Dispose();
            other.Dispose();
        }

        [Test]
        public void DistinctUntilChanged_KeepsStatePerSubscription()
        {
            using Subject<int> source = new Subject<int>();
            IOnityObservable<int> distinct = source.DistinctUntilChanged();
            List<int> first = new List<int>();
            List<int> second = new List<int>();

            IDisposable firstSubscription = distinct.Subscribe(first.Add);
            source.OnNext(1);
            source.OnNext(1);
            IDisposable secondSubscription = distinct.Subscribe(second.Add);
            source.OnNext(1);
            source.OnNext(2);

            Assert.That(first, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(second, Is.EqualTo(new[] { 1, 2 }));

            firstSubscription.Dispose();
            secondSubscription.Dispose();
        }

        [Test]
        public void Skip_DropsLeadingValuesPerSubscription()
        {
            using Subject<int> source = new Subject<int>();
            IOnityObservable<int> skipped = source.Skip(2);
            List<int> first = new List<int>();
            List<int> second = new List<int>();

            IDisposable firstSubscription = skipped.Subscribe(first.Add);
            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);
            IDisposable secondSubscription = skipped.Subscribe(second.Add);
            source.OnNext(4);
            source.OnNext(5);
            source.OnNext(6);

            Assert.That(first, Is.EqualTo(new[] { 3, 4, 5, 6 }));
            Assert.That(second, Is.EqualTo(new[] { 6 }));

            firstSubscription.Dispose();
            secondSubscription.Dispose();
        }

        [Test]
        public void Skip_ZeroCountReturnsSourceAndNegativeCountThrows()
        {
            using Subject<int> source = new Subject<int>();

            Assert.That(source.Skip(0), Is.SameAs(source));
            Assert.That(() => source.Skip(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => ((IOnityObservable<int>)null).Skip(1), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SkipWhile_ForwardsEverythingAfterFirstFailure()
        {
            using Subject<int> source = new Subject<int>();
            List<int> received = new List<int>();
            IDisposable subscription = source.SkipWhile(value => value < 3).Subscribe(received.Add);

            source.OnNext(1);
            source.OnNext(3);
            source.OnNext(1);

            Assert.That(received, Is.EqualTo(new[] { 3, 1 }));
            Assert.That(() => source.SkipWhile(null), Throws.TypeOf<ArgumentNullException>());
            subscription.Dispose();
        }

        [Test]
        public void Take_ForwardsCountValuesThenDisposesUpstream()
        {
            using Subject<int> source = new Subject<int>();
            int predicateCalls = 0;
            List<int> received = new List<int>();

            IDisposable subscription = source
                .Where(
                    value =>
                    {
                        predicateCalls++;
                        return true;
                    })
                .Take(2)
                .Subscribe(received.Add);

            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);

            Assert.That(received, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(predicateCalls, Is.EqualTo(2), "The upstream subscription must end after the last value.");
            Assert.That(() => subscription.Dispose(), Throws.Nothing);
        }

        [Test]
        public void Take_CompletesWhileSubscribing_DisposesUpstreamAfterSubscribe()
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(5);
            int predicateCalls = 0;
            List<int> received = new List<int>();

            IDisposable subscription = property
                .Where(
                    value =>
                    {
                        predicateCalls++;
                        return true;
                    })
                .Take(1)
                .Subscribe(received.Add);

            property.Value = 6;

            Assert.That(received, Is.EqualTo(new[] { 5 }));
            Assert.That(predicateCalls, Is.EqualTo(1), "Completion during subscribe must still end the upstream subscription.");
            subscription.Dispose();
        }

        [Test]
        public void Take_ObserverThrowsOnLastValue_IgnoresLaterValuesWithoutDisposingUpstream()
        {
            List<Exception> caught = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caught.Add;
            using Subject<int> source = new Subject<int>();
            int predicateCalls = 0;
            int observerCalls = 0;

            IDisposable subscription = source
                .Where(
                    value =>
                    {
                        predicateCalls++;
                        return true;
                    })
                .Take(1)
                .Subscribe(
                    new Action<int>(
                        value =>
                        {
                            observerCalls++;
                            throw new InvalidOperationException("observer");
                        }));

            source.OnNext(1);
            source.OnNext(2);

            Assert.That(observerCalls, Is.EqualTo(1));
            Assert.That(caught.Count, Is.EqualTo(1));
            Assert.That(predicateCalls, Is.EqualTo(2), "The throw skips the upstream disposal, as before.");

            subscription.Dispose();
            source.OnNext(3);

            Assert.That(predicateCalls, Is.EqualTo(2));
        }

        [Test]
        public void Take_ZeroCountReturnsEmptyAndNegativeCountThrows()
        {
            using Subject<int> source = new Subject<int>();
            List<int> received = new List<int>();

            IOnityObservable<int> none = source.Take(0);
            IDisposable subscription = none.Subscribe(received.Add);
            source.OnNext(1);

            Assert.That(none, Is.SameAs(OnityObservable.Empty<int>()));
            Assert.That(received, Is.Empty);
            Assert.That(() => source.Take(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            subscription.Dispose();
        }

        [Test]
        public void TakeWhile_StopsAndDisposesUpstreamAtFirstFailure()
        {
            using Subject<int> source = new Subject<int>();
            int predicateCalls = 0;
            List<int> received = new List<int>();

            IDisposable subscription = source
                .TakeWhile(
                    value =>
                    {
                        predicateCalls++;
                        return value < 3;
                    })
                .Subscribe(received.Add);

            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);
            source.OnNext(1);

            Assert.That(received, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(predicateCalls, Is.EqualTo(3));
            Assert.That(() => source.TakeWhile(null), Throws.TypeOf<ArgumentNullException>());
            subscription.Dispose();
        }

        [Test]
        public void TakeWhile_FailsWhileSubscribing_DisposesUpstreamAfterSubscribe()
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(10);
            int predicateCalls = 0;
            List<int> received = new List<int>();

            IDisposable subscription = property
                .TakeWhile(
                    value =>
                    {
                        predicateCalls++;
                        return value < 3;
                    })
                .Subscribe(received.Add);

            property.Value = 1;

            Assert.That(received, Is.Empty);
            Assert.That(predicateCalls, Is.EqualTo(1));
            subscription.Dispose();
        }

        [Test]
        public void StartWith_EmitsInitialValueBeforeSourceValues()
        {
            using Subject<int> source = new Subject<int>();
            List<int> received = new List<int>();
            IDisposable subscription = source.StartWith(0).StartWith(-1).Subscribe(received.Add);

            source.OnNext(1);
            subscription.Dispose();
            source.OnNext(2);

            Assert.That(received, Is.EqualTo(new[] { -1, 0, 1 }));
        }

        [Test]
        public void StartWith_WithFollowingOperator_SubscribesTheOperatorToTheSource()
        {
            using Subject<int> source = new Subject<int>();
            List<int> received = new List<int>();
            IDisposable subscription = source.StartWith(2).Where(value => value % 2 == 0).Subscribe(received.Add);

            source.OnNext(3);
            source.OnNext(4);
            subscription.Dispose();
            source.OnNext(6);

            Assert.That(received, Is.EqualTo(new[] { 2, 4 }));
        }

        [Test]
        public void Scan_KeepsStatePerSubscription()
        {
            using Subject<int> source = new Subject<int>();
            IOnityObservable<int> sums = source.Scan(100, (state, value) => state + value);
            List<int> first = new List<int>();
            List<int> second = new List<int>();

            IDisposable firstSubscription = sums.Subscribe(first.Add);
            source.OnNext(1);
            IDisposable secondSubscription = sums.Subscribe(second.Add);
            source.OnNext(2);

            Assert.That(first, Is.EqualTo(new[] { 101, 103 }));
            Assert.That(second, Is.EqualTo(new[] { 102 }));

            firstSubscription.Dispose();
            secondSubscription.Dispose();
        }

        [Test]
        public void Pairwise_KeepsPreviousValuePerSubscription()
        {
            using Subject<int> source = new Subject<int>();
            IOnityObservable<OnityPair<int>> pairs = source.Pairwise();
            List<string> first = new List<string>();
            List<string> second = new List<string>();

            IDisposable firstSubscription = pairs.Subscribe(pair => first.Add(pair.Previous + ">" + pair.Current));
            source.OnNext(1);
            IDisposable secondSubscription = pairs.Subscribe(pair => second.Add(pair.Previous + ">" + pair.Current));
            source.OnNext(2);
            source.OnNext(3);

            Assert.That(first, Is.EqualTo(new[] { "1>2", "2>3" }));
            Assert.That(second, Is.EqualTo(new[] { "2>3" }));

            firstSubscription.Dispose();
            secondSubscription.Dispose();
        }

        [Test]
        public void Operator_OverCustomObservable_UsesPublicObserverSubscribe()
        {
            CountingObservable source = new CountingObservable();
            List<int> received = new List<int>();

            IDisposable subscription = source.Where(value => value > 0).Take(2).Subscribe(received.Add);
            source.Publish(-1);
            source.Publish(1);
            source.Publish(2);
            source.Publish(3);

            Assert.That(received, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(source.ActiveSubscriptions, Is.EqualTo(0));
            subscription.Dispose();
            Assert.That(source.ActiveSubscriptions, Is.EqualTo(0));
        }

        [Test]
        public void Operator_SubscribeOnityObserver_CompletesAndDisposesObserverOnUnsubscribe()
        {
            using Subject<int> source = new Subject<int>();
            RecordingObserver observer = new RecordingObserver();

            IDisposable subscription = source.Select(value => value * 2).Subscribe(observer);
            source.OnNext(1);
            subscription.Dispose();
            source.OnNext(2);

            Assert.That(observer.Values, Is.EqualTo(new[] { 2 }));
            Assert.That(observer.CompletedCount, Is.EqualTo(1));
            Assert.That(observer.IsStopped, Is.True);
        }

        [Test]
        public void Operators_NullArguments_Throw()
        {
            using Subject<int> source = new Subject<int>();
            IOnityObservable<int> missing = null;

            Assert.That(() => missing.Where(_ => true), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => source.Where(null), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => missing.Select(value => value), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => source.Select<int, int>(null), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => source.Where(_ => true).Select<int, int>(null), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => missing.DistinctUntilChanged(), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => missing.Take(1), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => missing.StartWith(1), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => missing.Pairwise(), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => source.Where(_ => true).Subscribe((Observer<int>)null), Throws.TypeOf<ArgumentNullException>());
        }

        // A user observable that counts live subscriptions; operators reach it through Subscribe(Observer<T>).
        private sealed class CountingObservable : IOnityObservable<int>
        {
            private readonly Subject<int> m_subject = new Subject<int>();

            public int ActiveSubscriptions { get; private set; }

            public IDisposable Subscribe(Observer<int> observer)
            {
                ActiveSubscriptions++;
                IDisposable inner = m_subject.Subscribe(observer);

                return new DisposableAction(
                    () =>
                    {
                        ActiveSubscriptions--;
                        inner.Dispose();
                    });
            }

            public IDisposable Subscribe(OnityObserver<int> observer)
            {
                return Subscribe(new Observer<int>(observer.OnNext));
            }

            public void Publish(int value)
            {
                m_subject.OnNext(value);
            }
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
