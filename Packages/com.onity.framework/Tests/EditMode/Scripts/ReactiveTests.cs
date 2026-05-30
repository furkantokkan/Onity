using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Core;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class ReactiveTests
    {
        [Test]
        public void ReactiveProperty_Subscribe_EmitsCurrentValueByDefault()
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(42);

            int observed = -1;
            IDisposable subscription = property.Subscribe(value => observed = value);

            Assert.That(observed, Is.EqualTo(42));
            subscription.Dispose();
        }

        [Test]
        public void ReactiveProperty_SetValue_SameValue_DoesNotEmit()
        {
            using ReactiveProperty<int> property = new ReactiveProperty<int>(5);

            int callCount = 0;
            IDisposable subscription = property.Subscribe(_ => callCount++, false);

            bool changed = property.SetValue(5);
            property.SetValue(6);

            Assert.That(changed, Is.False);
            Assert.That(callCount, Is.EqualTo(1));
            subscription.Dispose();
        }

        [Test]
        public void Subject_DisposeSubscriptionDuringOnNext_RemovesObserver()
        {
            using Subject<int> subject = new Subject<int>();

            int selfDisposeCalls = 0;
            int persistentCalls = 0;
            IDisposable selfDisposer = null;

            selfDisposer = subject.Subscribe(
                _ =>
                {
                    selfDisposeCalls++;
                    selfDisposer.Dispose();
                });

            IDisposable persistent = subject.Subscribe(_ => persistentCalls++);

            subject.OnNext(1);
            subject.OnNext(2);

            Assert.That(selfDisposeCalls, Is.EqualTo(1));
            Assert.That(persistentCalls, Is.EqualTo(2));

            persistent.Dispose();
        }

        [Test]
        public void CompositeDisposable_Dispose_DisposesAllItems()
        {
            using CompositeDisposable disposables = new CompositeDisposable();
            DisposeProbe first = new DisposeProbe();
            DisposeProbe second = new DisposeProbe();

            disposables.Add(first);
            disposables.Add(second);
            disposables.Dispose();

            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(disposables.Count, Is.EqualTo(0));
        }

        [Test]
        public void ObservableWhereSelect_FiltersAndProjectsValues()
        {
            using Subject<int> source = new Subject<int>();
            IOnityObservable<int> pipeline = source
                .Where(value => value % 2 == 0)
                .Select(value => value * 10);

            int observed = 0;
            IDisposable subscription = pipeline.Subscribe(value => observed = value);

            source.OnNext(3);
            source.OnNext(4);

            Assert.That(observed, Is.EqualTo(40));
            subscription.Dispose();
        }

        [Test]
        public void ObservableDistinctUntilChanged_SuppressesConsecutiveDuplicates()
        {
            using Subject<int> source = new Subject<int>();
            IOnityObservable<int> pipeline = source.DistinctUntilChanged();

            int callCount = 0;
            int lastValue = 0;
            IDisposable subscription =
                pipeline.Subscribe(
                    value =>
                    {
                        callCount++;
                        lastValue = value;
                    });

            source.OnNext(3);
            source.OnNext(3);
            source.OnNext(5);
            source.OnNext(5);
            source.OnNext(5);

            Assert.That(callCount, Is.EqualTo(2));
            Assert.That(lastValue, Is.EqualTo(5));
            subscription.Dispose();
        }

        [Test]
        public void ObservableFromEvent_SubscribeAndDispose_WiresHandlersCorrectly()
        {
            Action<int> eventHandlers = null;
            IOnityObservable<int> stream = OnityObservable.FromEvent<int>(
                handler => eventHandlers += handler,
                handler => eventHandlers -= handler);

            int observed = 0;
            IDisposable subscription = stream.Subscribe(value => observed = value);

            eventHandlers?.Invoke(5);
            Assert.That(observed, Is.EqualTo(5));

            subscription.Dispose();
            eventHandlers?.Invoke(10);
            Assert.That(observed, Is.EqualTo(5));
        }

        [Test]
        public void ObservableTakeUntilCancellation_StopsReceivingValues()
        {
            using Subject<int> source = new Subject<int>();
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            IOnityObservable<int> stream = source.TakeUntilCancellation(cancellationTokenSource.Token);

            int callCount = 0;
            IDisposable subscription = stream.Subscribe(_ => callCount++);

            source.OnNext(1);
            cancellationTokenSource.Cancel();
            source.OnNext(2);

            Assert.That(callCount, Is.EqualTo(1));
            subscription.Dispose();
        }

        [Test]
        public void DisposableAddTo_CompositeDisposable_RegistersAndDisposes()
        {
            using CompositeDisposable disposables = new CompositeDisposable();
            DisposeProbe probe = new DisposeProbe();

            probe.AddTo(disposables);
            disposables.Dispose();

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ObservableFirstAsync_ReturnsFirstValue()
        {
            using Subject<int> subject = new Subject<int>();

            Task<int> task = subject.FirstAsync();
            subject.OnNext(11);
            subject.OnNext(24);

            int value = await task;
            Assert.That(value, Is.EqualTo(11));
        }

        [Test]
        public void ObservableFirstAsync_CanceledToken_ReturnsCanceledTask()
        {
            using Subject<int> subject = new Subject<int>();
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Task<int> task = subject.FirstAsync(cancellationTokenSource.Token);

            Assert.That(task.IsCanceled, Is.True);
        }

        [Test]
        public async Task ObservableToTask_UnitSource_Completes()
        {
            using Subject<Unit> subject = new Subject<Unit>();
            Task task = subject.ToTask();

            subject.OnNext(Unit.Default);
            await task;

            Assert.That(task.IsCompleted, Is.True);
        }

        [Test]
        public void ReactivePipeline_OnityObserverSubscribe_ForwardsValue()
        {
            using Subject<int> source = new Subject<int>();

            int observed = 0;
            IDisposable subscription =
                source.Subscribe(
                    value => observed = value,
                    _ => { },
                    _ => { });

            source.OnNext(17);

            Assert.That(observed, Is.EqualTo(17));
            subscription.Dispose();
        }

        [Test]
        public async Task Reactive_Debounce_EmitsLatestOnly()
        {
            using Subject<int> source = new Subject<int>();
            ManualTimeProvider timeProvider = new ManualTimeProvider();
            List<int> values = new List<int>();
            IDisposable subscription =
                source
                    .Debounce(TimeSpan.FromSeconds(1d), timeProvider)
                    .Subscribe(values.Add);

            source.OnNext(1);
            source.OnNext(2);

            timeProvider.AdvanceOne();
            await Task.Yield();
            timeProvider.AdvanceOne();
            await Task.Yield();

            Assert.That(values.Count, Is.EqualTo(1));
            Assert.That(values[0], Is.EqualTo(2));

            subscription.Dispose();
        }

        [Test]
        public async Task Reactive_ThrottleLast_EmitsLatestPerInterval()
        {
            using Subject<int> source = new Subject<int>();
            ManualTimeProvider timeProvider = new ManualTimeProvider();
            List<int> values = new List<int>();
            IDisposable subscription =
                source
                    .ThrottleLast(TimeSpan.FromSeconds(1d), timeProvider)
                    .Subscribe(values.Add);

            source.OnNext(1);
            source.OnNext(2);
            timeProvider.AdvanceOne();
            await Task.Yield();

            source.OnNext(3);
            timeProvider.AdvanceOne();
            await Task.Yield();

            Assert.That(values.Count, Is.EqualTo(2));
            Assert.That(values[0], Is.EqualTo(2));
            Assert.That(values[1], Is.EqualTo(3));

            subscription.Dispose();
        }

        [Test]
        public async Task Reactive_SelectAwait_ProjectsSequentialValues()
        {
            using Subject<int> source = new Subject<int>();
            List<int> values = new List<int>();
            IDisposable subscription =
                source
                    .SelectAwait(
                        async (value, _) =>
                        {
                            await Task.Yield();
                            return value * 10;
                        })
                    .Subscribe(values.Add);

            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);

            await Task.Delay(10);

            Assert.That(values.Count, Is.EqualTo(3));
            Assert.That(values[0], Is.EqualTo(10));
            Assert.That(values[1], Is.EqualTo(20));
            Assert.That(values[2], Is.EqualTo(30));

            subscription.Dispose();
        }

        [Test]
        public async Task Reactive_WhereAwait_FiltersSequentialValues()
        {
            using Subject<int> source = new Subject<int>();
            List<int> values = new List<int>();
            IDisposable subscription =
                source
                    .WhereAwait(
                        async (value, _) =>
                        {
                            await Task.Yield();
                            return value % 2 == 0;
                        })
                    .Subscribe(values.Add);

            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);
            source.OnNext(4);

            await Task.Delay(10);

            Assert.That(values.Count, Is.EqualTo(2));
            Assert.That(values[0], Is.EqualTo(2));
            Assert.That(values[1], Is.EqualTo(4));

            subscription.Dispose();
        }

        [Test]
        public async Task Reactive_TakeUntilTask_StopsAfterSignal()
        {
            using Subject<int> source = new Subject<int>();
            TaskCompletionSource<bool> stopSignal =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            int observed = 0;
            IDisposable subscription =
                source
                    .TakeUntil(stopSignal.Task)
                    .Subscribe(_ => observed++);

            source.OnNext(1);
            stopSignal.SetResult(true);
            await Task.Yield();
            source.OnNext(2);

            Assert.That(observed, Is.EqualTo(1));
            subscription.Dispose();
        }

        [Test]
        public void Reactive_Tracker_TracksActiveAndDisposedSubscriptions()
        {
            bool previousTracking = OnityObservableTracker.EnableTracking;
            bool previousStackTrace = OnityObservableTracker.EnableStackTrace;

            try
            {
                OnityObservableTracker.EnableTracking = true;
                OnityObservableTracker.EnableStackTrace = false;
                OnityObservableTracker.ClearAll();

                using Subject<int> source = new Subject<int>();
                List<OnityTrackedObservableInfo> rows = new List<OnityTrackedObservableInfo>();
                IDisposable subscription =
                    source.Subscribe(
                        _ => { },
                        _ => { },
                        _ => { });

                OnityObservableTracker.GetSnapshot(rows, includeDisposed: true);
                Assert.That(rows.Count, Is.GreaterThan(0));
                Assert.That(rows[0].IsActive, Is.True);

                subscription.Dispose();

                OnityObservableTracker.GetSnapshot(rows, includeDisposed: true);
                Assert.That(rows.Count, Is.GreaterThan(0));
                Assert.That(rows[0].IsActive, Is.False);
            }
            finally
            {
                OnityObservableTracker.ClearAll();
                OnityObservableTracker.EnableTracking = previousTracking;
                OnityObservableTracker.EnableStackTrace = previousStackTrace;
            }
        }

        private sealed class DisposeProbe : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class ManualTimeProvider : OnityTimeProvider
        {
            private readonly Queue<TaskCompletionSource<bool>> m_pending =
                new Queue<TaskCompletionSource<bool>>();

            public override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
            {
                if (delay <= TimeSpan.Zero)
                {
                    return Task.CompletedTask;
                }

                TaskCompletionSource<bool> completionSource =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
                }

                m_pending.Enqueue(completionSource);
                return completionSource.Task;
            }

            public void AdvanceOne()
            {
                Assert.That(m_pending.Count, Is.GreaterThan(0));
                TaskCompletionSource<bool> completionSource = m_pending.Dequeue();
                completionSource.TrySetResult(true);
            }
        }
    }
}
