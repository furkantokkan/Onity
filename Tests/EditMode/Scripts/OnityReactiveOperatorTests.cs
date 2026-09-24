using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Core;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityReactiveOperatorTests
    {
        [Test]
        public void Scan_AccumulatesRunningStateForEachValue()
        {
            using Subject<int> source = new Subject<int>();
            List<int> observed = new List<int>();
            IDisposable subscription =
                source
                    .Scan(0, (state, value) => state + value)
                    .Subscribe(observed.Add);

            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);

            Assert.That(observed, Is.EqualTo(new[] { 1, 3, 6 }));
            subscription.Dispose();
        }

        [Test]
        public void Scan_NullAccumulator_Throws()
        {
            using Subject<int> source = new Subject<int>();

            Assert.That(
                () => source.Scan(0, null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Pairwise_SkipsFirstValueThenEmitsConsecutivePairs()
        {
            using Subject<int> source = new Subject<int>();
            List<OnityPair<int>> observed = new List<OnityPair<int>>();
            IDisposable subscription = source.Pairwise().Subscribe(observed.Add);

            source.OnNext(1);
            Assert.That(observed.Count, Is.EqualTo(0));

            source.OnNext(2);
            source.OnNext(3);

            Assert.That(observed.Count, Is.EqualTo(2));
            Assert.That(observed[0].Previous, Is.EqualTo(1));
            Assert.That(observed[0].Current, Is.EqualTo(2));
            Assert.That(observed[1].Previous, Is.EqualTo(2));
            Assert.That(observed[1].Current, Is.EqualTo(3));
            subscription.Dispose();
        }

        [Test]
        public void Merge_ForwardsValuesFromAllSources()
        {
            using Subject<int> first = new Subject<int>();
            using Subject<int> second = new Subject<int>();
            using Subject<int> third = new Subject<int>();
            List<int> observed = new List<int>();
            IDisposable subscription = first.Merge(second, third).Subscribe(observed.Add);

            first.OnNext(1);
            second.OnNext(2);
            third.OnNext(3);
            first.OnNext(4);

            Assert.That(observed, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            subscription.Dispose();
        }

        [Test]
        public void Merge_AfterDispose_StopsForwarding()
        {
            using Subject<int> first = new Subject<int>();
            using Subject<int> second = new Subject<int>();
            List<int> observed = new List<int>();
            IDisposable subscription = first.Merge(second).Subscribe(observed.Add);

            first.OnNext(1);
            subscription.Dispose();
            first.OnNext(2);
            second.OnNext(3);

            Assert.That(observed, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Merge_NullSourceInOthers_Throws()
        {
            using Subject<int> first = new Subject<int>();

            Assert.That(
                () => first.Merge(new IOnityObservable<int>[] { null }),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CombineLatest_WaitsUntilBothSourcesHaveValue()
        {
            using Subject<int> first = new Subject<int>();
            using Subject<string> second = new Subject<string>();
            List<string> observed = new List<string>();
            IDisposable subscription =
                first
                    .CombineLatest(second, (number, text) => text + number)
                    .Subscribe(observed.Add);

            first.OnNext(1);
            Assert.That(observed.Count, Is.EqualTo(0));

            second.OnNext("a");
            Assert.That(observed, Is.EqualTo(new[] { "a1" }));

            first.OnNext(2);
            Assert.That(observed, Is.EqualTo(new[] { "a1", "a2" }));

            second.OnNext("b");
            Assert.That(observed, Is.EqualTo(new[] { "a1", "a2", "b2" }));
            subscription.Dispose();
        }

        [Test]
        public void CombineLatest_NullResultSelector_Throws()
        {
            using Subject<int> first = new Subject<int>();
            using Subject<int> second = new Subject<int>();

            Assert.That(
                () => first.CombineLatest<int, int, int>(second, null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Sample_EmitsLatestSourceValueOnSignal()
        {
            using Subject<int> source = new Subject<int>();
            using Subject<Unit> sampler = new Subject<Unit>();
            List<int> observed = new List<int>();
            IDisposable subscription = source.Sample(sampler).Subscribe(observed.Add);

            sampler.OnNext(Unit.Default);
            Assert.That(observed.Count, Is.EqualTo(0));

            source.OnNext(1);
            source.OnNext(2);
            sampler.OnNext(Unit.Default);
            Assert.That(observed, Is.EqualTo(new[] { 2 }));

            source.OnNext(3);
            sampler.OnNext(Unit.Default);
            Assert.That(observed, Is.EqualTo(new[] { 2, 3 }));
            subscription.Dispose();
        }

        [Test]
        public void Sample_AfterDispose_StopsEmitting()
        {
            using Subject<int> source = new Subject<int>();
            using Subject<Unit> sampler = new Subject<Unit>();
            List<int> observed = new List<int>();
            IDisposable subscription = source.Sample(sampler).Subscribe(observed.Add);

            source.OnNext(1);
            sampler.OnNext(Unit.Default);
            subscription.Dispose();

            source.OnNext(2);
            sampler.OnNext(Unit.Default);

            Assert.That(observed, Is.EqualTo(new[] { 1 }));
        }
    }
}
