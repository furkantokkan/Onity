using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityReactiveThreadPoolTests
    {
        [Test]
        public void ObserveOnThreadPool_EmitsValuesOnWorkerThreadInSourceOrder()
        {
            int callerThreadId = Thread.CurrentThread.ManagedThreadId;

            using Subject<int> source = new Subject<int>();
            using CountdownEvent observedSignals = new CountdownEvent(3);

            object gate = new object();
            List<int> observedValues = new List<int>();
            List<int> observedThreadIds = new List<int>();

            IDisposable subscription =
                source
                    .ObserveOnThreadPool()
                    .Subscribe(
                        value =>
                        {
                            lock (gate)
                            {
                                observedValues.Add(value);
                                observedThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
                            }

                            observedSignals.Signal();
                        });

            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);

            Assert.That(observedSignals.Wait(TimeSpan.FromSeconds(2d)), Is.True);

            lock (gate)
            {
                Assert.That(observedValues, Is.EqualTo(new[] { 1, 2, 3 }));
                Assert.That(observedThreadIds, Has.All.Not.EqualTo(callerThreadId));
            }

            subscription.Dispose();
        }

        [Test]
        public void SelectOnThreadPool_ProcessesValuesConcurrentlyUpToConfiguredLimit()
        {
            using Subject<int> source = new Subject<int>();
            using CountdownEvent selectorsStarted = new CountdownEvent(2);
            using CountdownEvent observedSignals = new CountdownEvent(2);
            using ManualResetEventSlim releaseSelectors = new ManualResetEventSlim(false);

            object gate = new object();
            List<int> observedValues = new List<int>();
            int activeSelectorCount = 0;
            int maxActiveSelectorCount = 0;

            IDisposable subscription =
                source
                    .SelectOnThreadPool(
                        (value, cancellationToken) =>
                        {
                            int activeCount = Interlocked.Increment(ref activeSelectorCount);

                            lock (gate)
                            {
                                if (activeCount > maxActiveSelectorCount)
                                {
                                    maxActiveSelectorCount = activeCount;
                                }
                            }

                            selectorsStarted.Signal();

                            try
                            {
                                releaseSelectors.Wait(cancellationToken);
                                return value * 10;
                            }
                            finally
                            {
                                Interlocked.Decrement(ref activeSelectorCount);
                            }
                        },
                        maxConcurrency: 2)
                    .Subscribe(
                        value =>
                        {
                            lock (gate)
                            {
                                observedValues.Add(value);
                            }

                            observedSignals.Signal();
                        });

            source.OnNext(1);
            source.OnNext(2);

            Assert.That(selectorsStarted.Wait(TimeSpan.FromSeconds(2d)), Is.True);

            lock (gate)
            {
                Assert.That(maxActiveSelectorCount, Is.EqualTo(2));
            }

            releaseSelectors.Set();

            Assert.That(observedSignals.Wait(TimeSpan.FromSeconds(2d)), Is.True);

            lock (gate)
            {
                Assert.That(observedValues, Is.EquivalentTo(new[] { 10, 20 }));
            }

            subscription.Dispose();
        }

        [Test]
        public void SelectOnThreadPool_MaxConcurrencyOne_PreservesSourceOrder()
        {
            using Subject<int> source = new Subject<int>();
            using CountdownEvent observedSignals = new CountdownEvent(3);

            object gate = new object();
            List<int> observedValues = new List<int>();

            IDisposable subscription =
                source
                    .SelectOnThreadPool(value => value * 10, maxConcurrency: 1)
                    .Subscribe(
                        value =>
                        {
                            lock (gate)
                            {
                                observedValues.Add(value);
                            }

                            observedSignals.Signal();
                        });

            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);

            Assert.That(observedSignals.Wait(TimeSpan.FromSeconds(2d)), Is.True);

            lock (gate)
            {
                Assert.That(observedValues, Is.EqualTo(new[] { 10, 20, 30 }));
            }

            subscription.Dispose();
        }

        [Test]
        public void SelectOnThreadPool_NegativeMaxConcurrency_Throws()
        {
            using Subject<int> source = new Subject<int>();

            Assert.That(
                () => source.SelectOnThreadPool(value => value, -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
