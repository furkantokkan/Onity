using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityAsyncEnumerablePlayModeTests
    {
        [UnityTest]
        public IEnumerator NativeAwaitForeach_PendingWorkerItemAndBreakCleanup_StayOnProducerThread()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            var source = new PendingSource();
            var consumption = ConsumeAndBreak(source);
            var observed = new TaskCompletionSource<(int value, Exception error, int thread)>();
            consumption.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    observed.TrySetResult((consumption.GetAwaiter().GetResult(), null,
                        Thread.CurrentThread.ManagedThreadId));
                }
                catch (Exception exception)
                {
                    observed.TrySetResult((0, exception, Thread.CurrentThread.ManagedThreadId));
                }
            });
            Task<int> worker = Task.Run(() =>
            {
                source.Move.TrySetResult(true);
                return Thread.CurrentThread.ManagedThreadId;
            });
            try
            {
                yield return Wait(() => worker.IsCompleted && observed.Task.IsCompleted);
                var result = observed.Task.GetAwaiter().GetResult();
                Assert.That(result.error, Is.Null);
                Assert.That(result.value, Is.EqualTo(46));
                Assert.That(result.thread, Is.EqualTo(worker.GetAwaiter().GetResult()));
                Assert.That(result.thread, Is.Not.EqualTo(main));
                Assert.That(source.CleanupThread, Is.EqualTo(result.thread));
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
            finally
            {
                source.Move.TrySetResult(false);
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator ExplicitTerminalTaskBridge_ReturnsToUnitySynchronizationContext()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext context = SynchronizationContext.Current;
            Assert.That(context, Is.Not.Null);
            var source = new PendingSource();
            var observed = ObserveBridge(source.Select(value => value + 1).FirstAsync().AsTask());
            Task worker = Task.Run(() => { source.Move.TrySetResult(true); });
            try
            {
                yield return Wait(() => worker.IsCompleted && observed.IsCompleted);
                var result = observed.GetAwaiter().GetResult();
                Assert.That(result.value, Is.EqualTo(24));
                Assert.That(result.thread, Is.EqualTo(main));
                Assert.That(result.context, Is.SameAs(context));
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
            finally
            {
                source.Move.TrySetResult(false);
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
            }
        }

        private static async OnityTask<int> ConsumeAndBreak(PendingSource source)
        {
            await foreach (int value in source.Where(value => true).Select(value => value * 2))
            {
                return value;
            }
            throw new InvalidOperationException("Expected one item.");
        }

        private static async Task<(int value, int thread, SynchronizationContext context)> ObserveBridge(Task<int> task)
        {
            int value = await task;
            return (value, Thread.CurrentThread.ManagedThreadId, SynchronizationContext.Current);
        }

        private static IEnumerator Wait(Func<bool> predicate)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!predicate())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Worker outcome timed out.");
                yield return null;
            }
        }

        private sealed class PendingSource : IOnityAsyncEnumerable<int>, IOnityAsyncEnumerator<int>
        {
            internal readonly OnityTaskCompletionSource<bool> Move = new OnityTaskCompletionSource<bool>();
            internal int Disposals;
            internal int CleanupThread;
            public int Current => 23;

            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return this;
            }

            public OnityTask<bool> MoveNextAsync()
            {
                return Move.Task;
            }

            public OnityTask DisposeAsync()
            {
                Interlocked.Increment(ref Disposals);
                CleanupThread = Thread.CurrentThread.ManagedThreadId;
                return default;
            }
        }
    }
}
