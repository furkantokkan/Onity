using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Messaging;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Pins what the delivery pass of <see cref="AsyncMessageChannel{TMessage}"/> does to the caller's
    /// <see cref="AsyncLocal{T}"/> values and <see cref="SynchronizationContext"/>. <c>PublishAsync</c> is not an
    /// <c>async</c> method: a handler that returns its <see cref="ValueTask"/> directly runs synchronously in the
    /// caller's context, so a value or context it sets stays visible to the caller. An <c>async</c> handler that
    /// completes synchronously restores the context through its own builder. Handlers after the first pending one
    /// run inside the awaiting continuation, so the caller never sees their changes. The synchronous prefix of a
    /// non-<c>async</c> first pending handler still runs inline. Every test restores the thread's original
    /// <see cref="SynchronizationContext"/> and resets its <see cref="AsyncLocal{T}"/> in <c>finally</c>, because
    /// the EditMode runner's main thread carries Unity's synchronization context.
    /// </summary>
    [TestFixture]
    public sealed class OnityAsyncMessagingContextTests
    {
        private const int k_handlerValue = 5;

        [Test]
        public void PublishAsync_NonAsyncHandler_AsyncLocalChangeIsVisibleToCaller()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            AsyncLocal<int> local = new AsyncLocal<int>();

            try
            {
                channel.Subscribe(
                    (value, token) =>
                    {
                        local.Value = k_handlerValue;
                        return default;
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

                Assert.That(publish.IsCompletedSuccessfully, Is.True);
                Assert.That(local.Value, Is.EqualTo(k_handlerValue));
            }
            finally
            {
                local.Value = 0;
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        [Test]
        public void PublishAsync_AsyncHandlerCompletingSynchronously_AsyncLocalChangeIsNotVisibleToCaller()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            AsyncLocal<int> local = new AsyncLocal<int>();
            bool handlerRan = false;

            try
            {
                channel.Subscribe(
                    async (value, token) =>
                    {
                        local.Value = k_handlerValue;
                        await Task.CompletedTask;
                        handlerRan = true;
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

                Assert.That(publish.IsCompletedSuccessfully, Is.True);
                Assert.That(handlerRan, Is.True);
                Assert.That(local.Value, Is.EqualTo(0));
            }
            finally
            {
                local.Value = 0;
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        [Test]
        public void PublishAsync_HandlerAfterFirstPendingHandler_AsyncLocalChangeIsNotVisibleToCallerWhilePending()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            AsyncLocal<int> local = new AsyncLocal<int>();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            bool laterRan = false;

            try
            {
                SubscribePending(channel, gate);
                channel.Subscribe(
                    (value, token) =>
                    {
                        laterRan = true;
                        local.Value = k_handlerValue;
                        return default;
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

                Assert.That(publish.IsCompleted, Is.False);
                Assert.That(laterRan, Is.False);
                Assert.That(local.Value, Is.EqualTo(0));

                // Finish the pass so the channel ends the test idle.
                gate.SetResult(true);
            }
            finally
            {
                local.Value = 0;
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        [Test]
        public void PublishAsync_HandlerAfterFirstPendingHandler_AsyncLocalChangeIsNotVisibleToCallerAfterPassFinishes()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            AsyncLocal<int> local = new AsyncLocal<int>();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            bool laterRan = false;

            try
            {
                SubscribePending(channel, gate);
                channel.Subscribe(
                    (value, token) =>
                    {
                        laterRan = true;
                        local.Value = k_handlerValue;
                        return default;
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);
                gate.SetResult(true);

                Assert.That(publish.IsCompletedSuccessfully, Is.True);
                Assert.That(laterRan, Is.True);
                Assert.That(local.Value, Is.EqualTo(0));
            }
            finally
            {
                local.Value = 0;
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        [Test]
        public void PublishAsync_FirstPendingNonAsyncHandler_AsyncLocalChangeOfItsSynchronousPrefixIsVisibleToCaller()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            AsyncLocal<int> local = new AsyncLocal<int>();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();

            try
            {
                channel.Subscribe(
                    (value, token) =>
                    {
                        local.Value = k_handlerValue;
                        return new ValueTask(gate.Task);
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

                Assert.That(publish.IsCompleted, Is.False);
                Assert.That(local.Value, Is.EqualTo(k_handlerValue));

                // Finish the pass so the channel ends the test idle.
                gate.SetResult(true);
            }
            finally
            {
                local.Value = 0;
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        [Test]
        public void PublishAsync_NonAsyncHandler_SynchronizationContextChangeIsVisibleToCaller()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            MarkerSynchronizationContext callerContext = new MarkerSynchronizationContext();
            MarkerSynchronizationContext handlerContext = new MarkerSynchronizationContext();

            try
            {
                SynchronizationContext.SetSynchronizationContext(callerContext);
                channel.Subscribe(
                    (value, token) =>
                    {
                        SynchronizationContext.SetSynchronizationContext(handlerContext);
                        return default;
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

                Assert.That(publish.IsCompletedSuccessfully, Is.True);
                Assert.That(SynchronizationContext.Current, Is.SameAs(handlerContext));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        [Test]
        public void PublishAsync_AsyncHandlerCompletingSynchronously_SynchronizationContextChangeIsNotVisibleToCaller()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            MarkerSynchronizationContext callerContext = new MarkerSynchronizationContext();
            MarkerSynchronizationContext handlerContext = new MarkerSynchronizationContext();
            bool handlerRan = false;

            try
            {
                SynchronizationContext.SetSynchronizationContext(callerContext);
                channel.Subscribe(
                    async (value, token) =>
                    {
                        SynchronizationContext.SetSynchronizationContext(handlerContext);
                        await Task.CompletedTask;
                        handlerRan = true;
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

                Assert.That(publish.IsCompletedSuccessfully, Is.True);
                Assert.That(handlerRan, Is.True);
                Assert.That(SynchronizationContext.Current, Is.SameAs(callerContext));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        [Test]
        public void PublishAsync_HandlerAfterFirstPendingHandler_SynchronizationContextChangeIsNotVisibleToCallerWhilePending()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            MarkerSynchronizationContext callerContext = new MarkerSynchronizationContext();
            MarkerSynchronizationContext handlerContext = new MarkerSynchronizationContext();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            bool laterRan = false;

            try
            {
                SynchronizationContext.SetSynchronizationContext(callerContext);
                SubscribePending(channel, gate);
                channel.Subscribe(
                    (value, token) =>
                    {
                        laterRan = true;
                        SynchronizationContext.SetSynchronizationContext(handlerContext);
                        return default;
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

                Assert.That(publish.IsCompleted, Is.False);
                Assert.That(laterRan, Is.False);
                Assert.That(SynchronizationContext.Current, Is.SameAs(callerContext));

                // Finish the pass so the channel ends the test idle.
                gate.SetResult(true);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        [Test]
        public void PublishAsync_HandlerAfterFirstPendingHandler_SynchronizationContextChangeIsNotVisibleToCallerAfterPassFinishes()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            MarkerSynchronizationContext callerContext = new MarkerSynchronizationContext();
            MarkerSynchronizationContext handlerContext = new MarkerSynchronizationContext();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            bool laterRan = false;

            try
            {
                SynchronizationContext.SetSynchronizationContext(callerContext);
                SubscribePending(channel, gate);
                channel.Subscribe(
                    (value, token) =>
                    {
                        laterRan = true;
                        SynchronizationContext.SetSynchronizationContext(handlerContext);
                        return default;
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);
                gate.SetResult(true);

                Assert.That(publish.IsCompletedSuccessfully, Is.True);
                Assert.That(laterRan, Is.True);
                Assert.That(SynchronizationContext.Current, Is.SameAs(callerContext));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        [Test]
        public void PublishAsync_FirstPendingNonAsyncHandler_SynchronizationContextChangeOfItsSynchronousPrefixIsVisibleToCaller()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            SynchronizationContext original = SynchronizationContext.Current;
            MarkerSynchronizationContext callerContext = new MarkerSynchronizationContext();
            MarkerSynchronizationContext handlerContext = new MarkerSynchronizationContext();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();

            try
            {
                SynchronizationContext.SetSynchronizationContext(callerContext);
                channel.Subscribe(
                    (value, token) =>
                    {
                        SynchronizationContext.SetSynchronizationContext(handlerContext);
                        return new ValueTask(gate.Task);
                    });

                ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

                Assert.That(publish.IsCompleted, Is.False);
                Assert.That(SynchronizationContext.Current, Is.SameAs(handlerContext));

                // Finish the pass so the channel ends the test idle.
                gate.SetResult(true);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        private static IDisposable SubscribePending(AsyncMessageChannel<int> channel, TaskCompletionSource<bool> gate)
        {
            return channel.Subscribe((value, token) => new ValueTask(gate.Task));
        }

        /// <summary>
        /// A distinguishable context the tests compare by reference. A posted continuation runs inline, so a
        /// test never waits on another thread.
        /// </summary>
        private sealed class MarkerSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback callback, object state)
            {
                callback(state);
            }
        }
    }
}
