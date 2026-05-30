using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityReactiveObserveOnTests
    {
        [Test]
        public void ObserveOn_DoesNotForwardValueSynchronously()
        {
            using Subject<int> source = new Subject<int>();
            ManualFrameProvider frameProvider = new ManualFrameProvider();
            List<int> observed = new List<int>();
            IDisposable subscription = source.ObserveOn(frameProvider).Subscribe(observed.Add);

            source.OnNext(1);

            Assert.That(observed.Count, Is.EqualTo(0), "Value must be deferred to the provider, not emitted synchronously.");

            subscription.Dispose();
        }

        [Test]
        public void ObserveOn_EmitsBufferedValuesOnNextFrame()
        {
            using Subject<int> source = new Subject<int>();
            ManualFrameProvider frameProvider = new ManualFrameProvider();
            List<int> observed = new List<int>();
            IDisposable subscription = source.ObserveOn(frameProvider).Subscribe(observed.Add);

            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);

            Assert.That(observed.Count, Is.EqualTo(0));

            frameProvider.Tick();

            Assert.That(observed, Is.EqualTo(new[] { 1, 2, 3 }));

            subscription.Dispose();
        }

        [Test]
        public void ObserveOn_ValuesArrivingAcrossFramesEmitInOrder()
        {
            using Subject<int> source = new Subject<int>();
            ManualFrameProvider frameProvider = new ManualFrameProvider();
            List<int> observed = new List<int>();
            IDisposable subscription = source.ObserveOn(frameProvider).Subscribe(observed.Add);

            source.OnNext(1);
            frameProvider.Tick();
            Assert.That(observed, Is.EqualTo(new[] { 1 }));

            source.OnNext(2);
            source.OnNext(3);
            frameProvider.Tick();
            Assert.That(observed, Is.EqualTo(new[] { 1, 2, 3 }));

            subscription.Dispose();
        }

        [Test]
        public void ObserveOn_FrameTickWithoutValues_EmitsNothing()
        {
            using Subject<int> source = new Subject<int>();
            ManualFrameProvider frameProvider = new ManualFrameProvider();
            List<int> observed = new List<int>();
            IDisposable subscription = source.ObserveOn(frameProvider).Subscribe(observed.Add);

            frameProvider.Tick();
            frameProvider.Tick();

            Assert.That(observed.Count, Is.EqualTo(0));

            subscription.Dispose();
        }

        [Test]
        public void ObserveOn_AfterDispose_StopsEmitting()
        {
            using Subject<int> source = new Subject<int>();
            ManualFrameProvider frameProvider = new ManualFrameProvider();
            List<int> observed = new List<int>();
            IDisposable subscription = source.ObserveOn(frameProvider).Subscribe(observed.Add);

            source.OnNext(1);
            subscription.Dispose();

            source.OnNext(2);
            frameProvider.Tick();

            Assert.That(observed.Count, Is.EqualTo(0), "No values should be emitted once the subscription is disposed.");
        }

        [Test]
        public void ObserveOn_ValueProducedOffThread_IsReplayedOnFrameTick()
        {
            using Subject<int> source = new Subject<int>();
            ManualFrameProvider frameProvider = new ManualFrameProvider();
            List<int> observed = new List<int>();
            IDisposable subscription = source.ObserveOn(frameProvider).Subscribe(observed.Add);

            using ManualResetEventSlim produced = new ManualResetEventSlim(false);
            Thread producer = new Thread(
                () =>
                {
                    source.OnNext(42);
                    produced.Set();
                });

            producer.Start();
            produced.Wait();
            producer.Join();

            Assert.That(observed.Count, Is.EqualTo(0), "Off-thread value must wait for the provider tick.");

            frameProvider.Tick();

            Assert.That(observed, Is.EqualTo(new[] { 42 }));

            subscription.Dispose();
        }

        [Test]
        public void ObserveOn_NullSource_Throws()
        {
            ManualFrameProvider frameProvider = new ManualFrameProvider();

            Assert.That(
                () => ((IOnityObservable<int>)null).ObserveOn(frameProvider),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ObserveOn_NullFrameProvider_Throws()
        {
            using Subject<int> source = new Subject<int>();

            Assert.That(
                () => source.ObserveOn(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        /// <summary>
        /// Deterministic in-memory frame provider whose frame ticks are driven manually.
        /// </summary>
        private sealed class ManualFrameProvider : OnityFrameProvider
        {
            private readonly Subject<Unit> m_frames = new Subject<Unit>();

            public override string Name => "Manual";

            public override IOnityObservable<Unit> EveryFrame()
            {
                return m_frames;
            }

            public void Tick()
            {
                m_frames.OnNext(Unit.Default);
            }
        }
    }
}
