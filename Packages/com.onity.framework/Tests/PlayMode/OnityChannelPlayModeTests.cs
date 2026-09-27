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
    public sealed class OnityChannelPlayModeTests
    {
        [UnityTest]
        public IEnumerator NativePendingReader_PublicationRunsOnProducerWorker()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            var channel = OnityChannel.CreateBounded<int>(1);
            var read = channel.Reader.ReadAsync();
            var observed = new TaskCompletionSource<(int value, int thread, Exception error)>();
            read.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    observed.TrySetResult((read.GetAwaiter().GetResult(), Thread.CurrentThread.ManagedThreadId, null));
                }
                catch (Exception exception)
                {
                    observed.TrySetResult((0, Thread.CurrentThread.ManagedThreadId, exception));
                }
            });
            Task<int> worker = Task.Run(() =>
            {
                if (!channel.Writer.TryWrite(101))
                {
                    throw new InvalidOperationException("Pending direct handoff rejected the producer.");
                }
                return Thread.CurrentThread.ManagedThreadId;
            });
            try
            {
                yield return Wait(() => worker.IsCompleted && observed.Task.IsCompleted);
                var result = observed.Task.GetAwaiter().GetResult();
                Assert.That(result.error, Is.Null);
                Assert.That(result.value, Is.EqualTo(101));
                Assert.That(result.thread, Is.EqualTo(worker.GetAwaiter().GetResult()));
                Assert.That(result.thread, Is.Not.EqualTo(main));
            }
            finally
            {
                channel.Writer.TryComplete();
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                worker.GetAwaiter().GetResult();
            }
        }

        [UnityTest]
        public IEnumerator OrdinaryTaskBridgeAwait_RetainsUnityContextCapture()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext context = SynchronizationContext.Current;
            Assert.That(context, Is.Not.Null);
            var channel = OnityChannel.CreateUnbounded<int>();
            Task<int> bridge = channel.Reader.ReadAsync().AsTask();
            var observed = Observe(bridge);
            Task worker = Task.Run(() => { channel.Writer.WriteAsync(103).GetAwaiter().GetResult(); });
            try
            {
                yield return Wait(() => worker.IsCompleted && observed.IsCompleted);
                worker.GetAwaiter().GetResult();
                var result = observed.GetAwaiter().GetResult();
                Assert.That(result.value, Is.EqualTo(103));
                Assert.That(result.thread, Is.EqualTo(main));
                Assert.That(result.context, Is.SameAs(context));
            }
            finally
            {
                channel.Writer.TryComplete();
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                worker.GetAwaiter().GetResult();
                if (bridge.IsFaulted)
                {
                    var ignored = bridge.Exception;
                }
            }
        }

        private static async Task<(int value, int thread, SynchronizationContext context)> Observe(Task<int> task)
        {
            int value = await task;
            return (value, Thread.CurrentThread.ManagedThreadId, SynchronizationContext.Current);
        }

        private static IEnumerator Wait(Func<bool> predicate)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!predicate())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Channel worker timed out.");
                yield return null;
            }
        }
    }
}
