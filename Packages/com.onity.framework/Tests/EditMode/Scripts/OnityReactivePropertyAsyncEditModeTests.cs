using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Composition;
using Onity.DI;
using Onity.Reactive;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the ReactiveProperty async bridges: WaitAsync / WaitUntilAsync, the conflating
    /// AsLatestAsyncEnumerable stream, ToAsyncReactiveProperty and BindTo.
    /// </summary>
    [TestFixture]
    public sealed class OnityReactivePropertyAsyncEditModeTests
    {
        [Test]
        public void WaitAsync_CompletesWithNextChange_NotWithCurrentValue()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(1);

            OnityTask<int> wait = property.WaitAsync();

            Assert.That(wait.IsCompleted, Is.False);

            property.Value = 2;

            Assert.That(wait.IsCompletedSuccessfully, Is.True);
            Assert.That(wait.GetAwaiter().GetResult(), Is.EqualTo(2));
        }

        [Test]
        public void WaitAsync_EqualValue_IsNotAChange()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(1);
            OnityTask<int> wait = property.WaitAsync();

            property.Value = 1;

            Assert.That(wait.IsCompleted, Is.False);

            property.Value = 3;

            Assert.That(wait.GetAwaiter().GetResult(), Is.EqualTo(3));
        }

        [Test]
        public void WaitUntilAsync_CurrentValueMatches_CompletesSynchronously()
        {
            CountingProperty property = new CountingProperty();
            property.Publish(5);

            OnityTask<int> wait = property.WaitUntilAsync(value => value > 3);

            Assert.That(wait.IsCompletedSuccessfully, Is.True);
            Assert.That(wait.GetAwaiter().GetResult(), Is.EqualTo(5));
            Assert.That(property.SubscribeCount, Is.EqualTo(0));
        }

        [Test]
        public void WaitUntilAsync_WaitsForFirstMatchingChange()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(10);
            OnityTask<int> wait = property.WaitUntilAsync(value => value <= 0);

            property.Value = 5;

            Assert.That(wait.IsCompleted, Is.False);

            property.Value = 0;

            Assert.That(wait.GetAwaiter().GetResult(), Is.EqualTo(0));
        }

        [Test]
        public void WaitUntilAsync_ThrowingPredicate_FaultsTheWait()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(1);
            OnityTask<int> wait = property.WaitUntilAsync(
                value =>
                {
                    if (value == 2)
                    {
                        throw new InvalidOperationException("predicate");
                    }

                    return false;
                });

            property.Value = 2;

            Assert.That(wait.IsFaulted, Is.True);
            Assert.That(() => wait.GetAwaiter().GetResult(), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void WaitAsync_PreCanceledToken_IsCanceledWithoutSubscribing()
        {
            CountingProperty property = new CountingProperty();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            OnityTask<int> wait = property.WaitAsync(cancellation.Token);

            Assert.That(wait.IsCanceled, Is.True);
            Assert.That(property.SubscribeCount, Is.EqualTo(0));
            Assert.That(() => wait.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void WaitAsync_CanceledWhilePending_UnsubscribesAndIgnoresLaterValues()
        {
            CountingProperty property = new CountingProperty();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTask<int> wait = property.WaitAsync(cancellation.Token);

            Assert.That(property.ActiveSubscriptions, Is.EqualTo(1));

            cancellation.Cancel();

            Assert.That(wait.IsCanceled, Is.True);
            Assert.That(property.ActiveSubscriptions, Is.EqualTo(0));

            property.Publish(5);

            Assert.That(() => wait.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void WaitAsync_WaitersShareOneSubscription()
        {
            CountingProperty property = new CountingProperty();
            OnityTask<int> first = property.WaitAsync();
            OnityTask<int> second = property.WaitUntilAsync(value => value == 2);

            Assert.That(property.SubscribeCount, Is.EqualTo(1));

            property.Publish(1);

            Assert.That(first.GetAwaiter().GetResult(), Is.EqualTo(1));
            Assert.That(second.IsCompleted, Is.False);

            property.Publish(2);

            Assert.That(second.GetAwaiter().GetResult(), Is.EqualTo(2));
            Assert.That(property.SubscribeCount, Is.EqualTo(1));
        }

        [Test]
        public void WaitAsync_ContinuationThatWaitsAgain_ObservesEachChangeOnce()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            List<int> observed = new List<int>();

            WaitLoopAsync(property, observed, 3).Forget();

            property.Value = 1;
            property.Value = 2;
            property.Value = 3;
            property.Value = 4;

            Assert.That(observed, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void WaitAsync_DisposedProperty_IsCanceled()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            property.Dispose();

            OnityTask<int> wait = property.WaitAsync();

            Assert.That(wait.IsCanceled, Is.True);
            Assert.That(() => wait.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void WaitAsync_ScopeToken_CancelsWaitWhenScopeDisposesProperty()
        {
            OnityContainer container = new OnityContainer();
            ReactiveProperty<int> property = container.BindReactiveProperty(0);
            OnityTask<int> wait = property.WaitAsync(container.LifetimeToken);

            container.Dispose();

            Assert.That(wait.IsCanceled, Is.True);
            Assert.That(() => property.Value = 1, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => wait.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void WaitAsync_SteadyStateCycle_ReusesPooledWaitersWithoutAllocating()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            int value = 0;

            for (int i = 0; i < 16; i++)
            {
                RunWaitCycle(property, ++value);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 100; i++)
            {
                RunWaitCycle(property, ++value);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public async Task AsLatestAsyncEnumerable_YieldsCurrent_ThenConflatesToLatest()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(10);
            IOnityAsyncEnumerator<int> enumerator = property.AsLatestAsyncEnumerable().GetAsyncEnumerator();

            try
            {
                Assert.That(await enumerator.MoveNextAsync(), Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(10));

                property.Value = 11;
                property.Value = 12;
                property.Value = 13;

                Assert.That(await enumerator.MoveNextAsync(), Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(13));

                OnityTask<bool> pending = enumerator.MoveNextAsync();

                Assert.That(pending.IsCompleted, Is.False);

                property.Value = 14;

                Assert.That(await pending, Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(14));
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        [Test]
        public async Task AsLatestAsyncEnumerable_WithoutCurrent_StartsWithNextChange()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(10);
            IOnityAsyncEnumerator<int> enumerator = property.AsLatestAsyncEnumerable(false).GetAsyncEnumerator();

            try
            {
                OnityTask<bool> pending = enumerator.MoveNextAsync();

                Assert.That(pending.IsCompleted, Is.False);

                property.Value = 20;

                Assert.That(await pending, Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(20));
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        [Test]
        public async Task AsLatestAsyncEnumerable_SecondOutstandingMove_Throws()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(0);
            IOnityAsyncEnumerator<int> enumerator = property.AsLatestAsyncEnumerable(false).GetAsyncEnumerator();
            OnityTask<bool> pending = enumerator.MoveNextAsync();

            Assert.That(() => enumerator.MoveNextAsync(), Throws.TypeOf<InvalidOperationException>());

            await enumerator.DisposeAsync();

            Assert.That(await pending, Is.False);
        }

        [Test]
        public void AsLatestAsyncEnumerable_Cancel_CancelsPendingMoveAndUnsubscribes()
        {
            CountingProperty property = new CountingProperty();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator =
                property.AsLatestAsyncEnumerable(false).GetAsyncEnumerator(cancellation.Token);
            OnityTask<bool> pending = enumerator.MoveNextAsync();

            Assert.That(property.ActiveSubscriptions, Is.EqualTo(1));

            cancellation.Cancel();

            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(property.ActiveSubscriptions, Is.EqualTo(0));
            Assert.That(() => pending.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());

            OnityTask<bool> next = enumerator.MoveNextAsync();

            Assert.That(next.IsCanceled, Is.True);
            Assert.That(() => next.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
            enumerator.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void AsLatestAsyncEnumerable_Dispose_EndsPendingMoveAndUnsubscribes()
        {
            CountingProperty property = new CountingProperty();
            IOnityAsyncEnumerator<int> enumerator = property.AsLatestAsyncEnumerable(false).GetAsyncEnumerator();
            OnityTask<bool> pending = enumerator.MoveNextAsync();

            enumerator.DisposeAsync().GetAwaiter().GetResult();

            Assert.That(pending.GetAwaiter().GetResult(), Is.False);
            Assert.That(property.ActiveSubscriptions, Is.EqualTo(0));
            Assert.That(enumerator.MoveNextAsync().GetAwaiter().GetResult(), Is.False);
        }

        [Test]
        public void ToAsyncReactiveProperty_StartsWithCurrentValueAndFollowsChanges()
        {
            ReactiveProperty<int> property = new ReactiveProperty<int>(1);
            OnityReadOnlyAsyncReactiveProperty<int> asyncProperty =
                property.ToAsyncReactiveProperty(CancellationToken.None);

            try
            {
                Assert.That(asyncProperty.Value, Is.EqualTo(1));

                property.Value = 2;

                Assert.That(asyncProperty.Value, Is.EqualTo(2));

                OnityTask<int> wait = asyncProperty.WaitAsync();
                property.Value = 3;

                Assert.That(wait.GetAwaiter().GetResult(), Is.EqualTo(3));
            }
            finally
            {
                asyncProperty.Dispose();
            }
        }

        [Test]
        public void ToAsyncReactiveProperty_DisposeOrCancel_Unsubscribes()
        {
            CountingProperty disposedSource = new CountingProperty();
            OnityReadOnlyAsyncReactiveProperty<int> disposed =
                disposedSource.ToAsyncReactiveProperty(CancellationToken.None);
            CountingProperty canceledSource = new CountingProperty();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityReadOnlyAsyncReactiveProperty<int> canceled =
                canceledSource.ToAsyncReactiveProperty(cancellation.Token);

            Assert.That(disposedSource.ActiveSubscriptions, Is.EqualTo(1));
            Assert.That(canceledSource.ActiveSubscriptions, Is.EqualTo(1));

            disposed.Dispose();
            cancellation.Cancel();

            Assert.That(disposedSource.ActiveSubscriptions, Is.EqualTo(0));
            Assert.That(canceledSource.ActiveSubscriptions, Is.EqualTo(0));
            canceled.Dispose();
        }

        [Test]
        public void BindTo_WritesCurrentAndLaterValues_UntilDisposed()
        {
            OnityAsyncReactiveProperty<int> source = new OnityAsyncReactiveProperty<int>(5);
            ReactiveProperty<int> target = new ReactiveProperty<int>(0);

            IDisposable binding = source.BindTo(target, CancellationToken.None);

            Assert.That(target.Value, Is.EqualTo(5));

            source.Value = 7;

            Assert.That(target.Value, Is.EqualTo(7));

            binding.Dispose();
            source.Value = 9;

            Assert.That(target.Value, Is.EqualTo(7));
        }

        [Test]
        public void BindTo_CanceledToken_StopsWriting()
        {
            OnityAsyncReactiveProperty<int> source = new OnityAsyncReactiveProperty<int>(1);
            ReactiveProperty<int> target = new ReactiveProperty<int>(0);
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            using IDisposable binding = source.BindTo(target, cancellation.Token);
            cancellation.Cancel();
            source.Value = 2;

            Assert.That(target.Value, Is.EqualTo(1));
        }

        [Test]
        public void RoundTrip_ReactiveToAsyncAndBack_KeepsValuesInSync()
        {
            ReactiveProperty<int> origin = new ReactiveProperty<int>(1);
            ReactiveProperty<int> mirror = new ReactiveProperty<int>(0);
            OnityReadOnlyAsyncReactiveProperty<int> bridge = origin.ToAsyncReactiveProperty(CancellationToken.None);
            IDisposable binding = bridge.BindTo(mirror, CancellationToken.None);

            try
            {
                Assert.That(mirror.Value, Is.EqualTo(1));

                origin.Value = 4;

                Assert.That(mirror.Value, Is.EqualTo(4));
            }
            finally
            {
                binding.Dispose();
                bridge.Dispose();
            }
        }

        private static async OnityTaskVoid WaitLoopAsync(
            IReadOnlyReactiveProperty<int> property,
            List<int> observed,
            int count)
        {
            while (observed.Count < count)
            {
                observed.Add(await property.WaitAsync());
            }
        }

        private static void RunWaitCycle(ReactiveProperty<int> property, int value)
        {
            OnityTask<int> wait = property.WaitAsync();
            property.Value = value;

            if (wait.GetAwaiter().GetResult() != value)
            {
                throw new AssertionException("The wait did not complete with the published value.");
            }
        }

        // Counts subscriptions so the tests can observe subscribe/unsubscribe behavior.
        private sealed class CountingProperty : IReadOnlyReactiveProperty<int>
        {
            private readonly Subject<int> m_subject = new Subject<int>();

            public int Value { get; private set; }

            public int SubscribeCount { get; private set; }

            public int ActiveSubscriptions { get; private set; }

            public IDisposable Subscribe(Observer<int> observer, bool emitCurrentValue = true)
            {
                SubscribeCount++;
                ActiveSubscriptions++;

                if (emitCurrentValue)
                {
                    observer(Value);
                }

                return new Subscription(this, m_subject.Subscribe(observer));
            }

            public void Publish(int value)
            {
                Value = value;
                m_subject.OnNext(value);
            }

            private sealed class Subscription : IDisposable
            {
                private CountingProperty m_owner;
                private readonly IDisposable m_inner;

                public Subscription(CountingProperty owner, IDisposable inner)
                {
                    m_owner = owner;
                    m_inner = inner;
                }

                public void Dispose()
                {
                    if (m_owner == null)
                    {
                        return;
                    }

                    m_owner.ActiveSubscriptions--;
                    m_owner = null;
                    m_inner.Dispose();
                }
            }
        }
    }
}
