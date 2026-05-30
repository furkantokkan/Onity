using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityReactiveThrottleBufferTests
    {
        [Test]
        public async Task Throttle_EmitsLeadingValueAndDropsRestUntilIntervalElapses()
        {
            using Subject<int> source = new Subject<int>();
            ManualThrottleBufferTimeProvider timeProvider = new ManualThrottleBufferTimeProvider();
            List<int> observed = new List<int>();
            IDisposable subscription =
                source
                    .Throttle(TimeSpan.FromSeconds(1d), timeProvider)
                    .Subscribe(observed.Add);

            source.OnNext(1);
            source.OnNext(2);
            source.OnNext(3);

            Assert.That(observed, Is.EqualTo(new[] { 1 }));

            timeProvider.AdvanceOne();
            await Task.Yield();

            source.OnNext(4);
            source.OnNext(5);

            Assert.That(observed, Is.EqualTo(new[] { 1, 4 }));

            timeProvider.AdvanceOne();
            await Task.Yield();

            source.OnNext(6);

            Assert.That(observed, Is.EqualTo(new[] { 1, 4, 6 }));

            subscription.Dispose();
        }

        [Test]
        public async Task Throttle_AfterDispose_StopsEmitting()
        {
            using Subject<int> source = new Subject<int>();
            ManualThrottleBufferTimeProvider timeProvider = new ManualThrottleBufferTimeProvider();
            List<int> observed = new List<int>();
            IDisposable subscription =
                source
                    .Throttle(TimeSpan.FromSeconds(1d), timeProvider)
                    .Subscribe(observed.Add);

            source.OnNext(1);
            subscription.Dispose();

            timeProvider.AdvanceOne();
            await Task.Yield();
            source.OnNext(2);

            Assert.That(observed, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Throttle_NonPositiveInterval_Throws()
        {
            using Subject<int> source = new Subject<int>();

            Assert.That(
                () => source.Throttle(TimeSpan.Zero),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Buffer_ByCount_EmitsListEveryCountValues()
        {
            using Subject<int> source = new Subject<int>();
            List<IReadOnlyList<int>> observed = new List<IReadOnlyList<int>>();
            IDisposable subscription = source.Buffer(2).Subscribe(observed.Add);

            source.OnNext(1);
            Assert.That(observed.Count, Is.EqualTo(0));

            source.OnNext(2);
            Assert.That(observed.Count, Is.EqualTo(1));
            Assert.That(observed[0], Is.EqualTo(new[] { 1, 2 }));

            source.OnNext(3);
            source.OnNext(4);
            Assert.That(observed.Count, Is.EqualTo(2));
            Assert.That(observed[1], Is.EqualTo(new[] { 3, 4 }));

            subscription.Dispose();
        }

        [Test]
        public void Buffer_ByCount_DoesNotEmitPartialWindow()
        {
            using Subject<int> source = new Subject<int>();
            List<IReadOnlyList<int>> observed = new List<IReadOnlyList<int>>();
            IDisposable subscription = source.Buffer(3).Subscribe(observed.Add);

            source.OnNext(1);
            source.OnNext(2);

            Assert.That(observed.Count, Is.EqualTo(0));

            subscription.Dispose();
        }

        [Test]
        public void Buffer_ByCount_EmittedListsAreDistinctInstances()
        {
            using Subject<int> source = new Subject<int>();
            List<IReadOnlyList<int>> observed = new List<IReadOnlyList<int>>();
            IDisposable subscription = source.Buffer(1).Subscribe(observed.Add);

            source.OnNext(10);
            source.OnNext(20);

            Assert.That(observed.Count, Is.EqualTo(2));
            Assert.That(ReferenceEquals(observed[0], observed[1]), Is.False);
            Assert.That(observed[0], Is.EqualTo(new[] { 10 }));
            Assert.That(observed[1], Is.EqualTo(new[] { 20 }));

            subscription.Dispose();
        }

        [Test]
        public void Buffer_ByCount_NonPositiveCount_Throws()
        {
            using Subject<int> source = new Subject<int>();

            Assert.That(
                () => source.Buffer(0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task Buffer_ByTime_EmitsBufferedValuesPerWindow()
        {
            using Subject<int> source = new Subject<int>();
            ManualThrottleBufferTimeProvider timeProvider = new ManualThrottleBufferTimeProvider();
            List<IReadOnlyList<int>> observed = new List<IReadOnlyList<int>>();
            IDisposable subscription =
                source
                    .Buffer(TimeSpan.FromSeconds(1d), timeProvider)
                    .Subscribe(observed.Add);

            source.OnNext(1);
            source.OnNext(2);

            timeProvider.AdvanceOne();
            await Task.Yield();

            Assert.That(observed.Count, Is.EqualTo(1));
            Assert.That(observed[0], Is.EqualTo(new[] { 1, 2 }));

            source.OnNext(3);

            timeProvider.AdvanceOne();
            await Task.Yield();

            Assert.That(observed.Count, Is.EqualTo(2));
            Assert.That(observed[1], Is.EqualTo(new[] { 3 }));

            subscription.Dispose();
        }

        [Test]
        public async Task Buffer_ByTime_EmptyWindowEmitsNothing()
        {
            using Subject<int> source = new Subject<int>();
            ManualThrottleBufferTimeProvider timeProvider = new ManualThrottleBufferTimeProvider();
            List<IReadOnlyList<int>> observed = new List<IReadOnlyList<int>>();
            IDisposable subscription =
                source
                    .Buffer(TimeSpan.FromSeconds(1d), timeProvider)
                    .Subscribe(observed.Add);

            timeProvider.AdvanceOne();
            await Task.Yield();

            Assert.That(observed.Count, Is.EqualTo(0));

            subscription.Dispose();
        }

        private sealed class ManualThrottleBufferTimeProvider : OnityTimeProvider
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
