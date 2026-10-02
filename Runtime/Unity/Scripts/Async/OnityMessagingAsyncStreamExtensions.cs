using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Onity.Messaging;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Native async consumption of Onity message channels: one-shot receives, a buffered async stream
    /// over a synchronous subscriber, and a queued consumer that gives async publishers backpressure.
    /// </summary>
    /// <remarks>
    /// Onity's channels are not thread-safe: subscribe, publish and dispose on the channel's thread
    /// (Unity's main thread). Cancellation tokens may be canceled from any thread; a token canceled on
    /// another thread releases the channel subscription later, on the channel's thread (at the next
    /// delivery or disposal).
    /// </remarks>
    public static class OnityMessagingAsyncStreamExtensions
    {
        /// <summary>
        /// Waits for the next published message.
        /// </summary>
        /// <typeparam name="T">Message type.</typeparam>
        /// <param name="subscriber">Subscriber to receive from.</param>
        /// <param name="cancellationToken">Token that cancels the wait.</param>
        /// <returns>A single-consumption task that yields the next message.</returns>
        /// <remarks>
        /// Receives on one subscriber share one subscription and are pooled, replacing
        /// <c>Observe().Where().FirstOnityTask()</c>. A canceled receive gets no later message. A receive
        /// that starts while the channel is publishing (for example from a continuation that the
        /// publication resumed) waits for the following message, so a receive loop sees each message
        /// once. A receive started on a disposed channel is canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="subscriber" /> is null.</exception>
        public static OnityTask<T> ReceiveAsync<T>(
            this ISubscriber<T> subscriber,
            CancellationToken cancellationToken = default)
        {
            if (subscriber == null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            return OnitySubscriberWaitHub<T>.Get(subscriber).Wait(null, cancellationToken);
        }

        /// <summary>
        /// Waits for the next published message that matches <paramref name="predicate" />.
        /// </summary>
        /// <typeparam name="T">Message type.</typeparam>
        /// <param name="subscriber">Subscriber to receive from.</param>
        /// <param name="predicate">Condition each message is tested with.</param>
        /// <param name="cancellationToken">Token that cancels the wait.</param>
        /// <returns>A single-consumption task that yields the first matching message. An exception
        /// thrown by the predicate faults the task.</returns>
        /// <remarks>Shares the subscription and the delivery rules of
        /// <see cref="ReceiveAsync{T}(ISubscriber{T}, CancellationToken)" />.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="subscriber" /> or
        /// <paramref name="predicate" /> is null.</exception>
        public static OnityTask<T> ReceiveAsync<T>(
            this ISubscriber<T> subscriber,
            Func<T, bool> predicate,
            CancellationToken cancellationToken = default)
        {
            if (subscriber == null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return OnitySubscriberWaitHub<T>.Get(subscriber).Wait(predicate, cancellationToken);
        }

        /// <summary>
        /// Buffers a synchronous subscriber's messages for native async consumption. A synchronous
        /// publisher cannot wait for space, so <paramref name="overflow" /> decides what happens to a
        /// message that arrives while the buffer is full.
        /// </summary>
        /// <typeparam name="T">Message type.</typeparam>
        /// <param name="subscriber">Subscriber to read from.</param>
        /// <param name="capacity">Positive number of messages buffered per enumeration.</param>
        /// <param name="overflow">Policy for a message that arrives while the buffer is full.</param>
        /// <returns>A reusable description; each enumerator subscribes at its first move and
        /// unsubscribes on disposal.</returns>
        /// <remarks>
        /// The streaming counterpart of <see cref="ReceiveAsync{T}(ISubscriber{T}, CancellationToken)" />:
        /// use <c>ReceiveAsync</c> for one message and <c>ReceiveAllAsync</c> for every message.
        /// Messages are yielded in publish order. A message published while a move is pending completes
        /// that move directly. With <see cref="OnityBufferOverflow.Fault" /> the enumerator stops
        /// accepting messages at the first overflow, yields the buffered ones, then faults with
        /// <see cref="InvalidOperationException" />. Cancellation and disposal discard buffered
        /// messages. Enumerating a disposed channel ends immediately. The stream never ends on its own.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="subscriber" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity" /> is not positive or
        /// <paramref name="overflow" /> is not a defined policy.</exception>
        public static IOnityAsyncEnumerable<T> ReceiveAllAsync<T>(
            this ISubscriber<T> subscriber,
            int capacity,
            OnityBufferOverflow overflow = OnityBufferOverflow.Fault)
        {
            if (subscriber == null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (overflow != OnityBufferOverflow.Fault
                && overflow != OnityBufferOverflow.DropOldest
                && overflow != OnityBufferOverflow.DropNewest)
            {
                throw new ArgumentOutOfRangeException(nameof(overflow));
            }

            return new BufferedEnumerable<T>(subscriber, capacity, overflow);
        }

        /// <summary>
        /// Subscribes a handler through a bounded queue, so a slow handler does not stall the
        /// publishers: <c>PublishAsync</c> only waits while the queue is full, which is real
        /// backpressure. One consumer runs the handler for each queued message in order.
        /// </summary>
        /// <typeparam name="T">Message type.</typeparam>
        /// <param name="subscriber">Async subscriber to read from.</param>
        /// <param name="handler">Handler run for each message, one at a time in publish order. Its
        /// token is canceled when the subscription stops.</param>
        /// <param name="capacity">Positive number of messages queued before publishers wait.</param>
        /// <param name="lifetimeToken">Token that ends the subscription, typically the scope token.</param>
        /// <returns>The subscription; dispose it (or cancel <paramref name="lifetimeToken" />) to stop.</returns>
        /// <remarks>
        /// Stopping unsubscribes, cancels the running handler, discards queued messages, and releases
        /// publishers waiting for space without an exception (their messages are dropped). A publisher
        /// whose own token is canceled while it waits gets an <see cref="OperationCanceledException" />
        /// and its message is not queued. A handler exception is logged and the next message is
        /// handled. Stop the subscription on the channel's thread. A handler that publishes to its own
        /// channel while the queue is full waits for itself.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="subscriber" /> or
        /// <paramref name="handler" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity" /> is not positive.</exception>
        public static IDisposable SubscribeQueued<T>(
            this IAsyncSubscriber<T> subscriber,
            Func<T, CancellationToken, OnityTask> handler,
            int capacity,
            CancellationToken lifetimeToken)
        {
            if (subscriber == null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            QueuedSubscription<T> subscription = new QueuedSubscription<T>(handler, capacity);
            subscription.Start(subscriber, lifetimeToken);
            return subscription;
        }

        private static CancellationTokenRegistration RegisterWithoutContext(
            CancellationToken token,
            Action<object> callback,
            object state)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return token.Register(callback, state, false);
            }

            AsyncFlowControl flow = ExecutionContext.SuppressFlow();

            try
            {
                return token.Register(callback, state, false);
            }
            finally
            {
                flow.Undo();
            }
        }

        private sealed class BufferedEnumerable<T> : IOnityAsyncEnumerable<T>
        {
            private readonly ISubscriber<T> m_subscriber;
            private readonly int m_capacity;
            private readonly OnityBufferOverflow m_overflow;

            public BufferedEnumerable(ISubscriber<T> subscriber, int capacity, OnityBufferOverflow overflow)
            {
                m_subscriber = subscriber;
                m_capacity = capacity;
                m_overflow = overflow;
            }

            public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new BufferedEnumerator<T>(m_subscriber, m_capacity, m_overflow, cancellationToken);
            }
        }

        // A ring buffer filled by the channel's deliveries and drained by moves. Deliveries and moves
        // run on the channel's thread; the token may cancel from any thread, so state is under m_gate.
        private sealed class BufferedEnumerator<T> : IOnityAsyncEnumerator<T>
        {
            private const int k_initialBufferSize = 8;

            private static readonly Action<object> s_onCanceled = state => ((BufferedEnumerator<T>)state).OnCanceled();

            private readonly object m_gate = new object();
            private readonly ISubscriber<T> m_subscriber;
            private readonly int m_capacity;
            private readonly OnityBufferOverflow m_overflow;
            private readonly CancellationToken m_token;
            private readonly MessageHandler<T> m_onMessage;
            private IDisposable m_subscription;
            private int m_subscriptionThreadId;
            private CancellationTokenRegistration m_registration;
            private OnityAutoResetTaskCompletionSource<bool> m_pendingMove;
            private T[] m_items;
            private int m_head;
            private int m_count;
            private T m_current;
            private bool m_hasCurrent;
            private Exception m_overflowError;
            private bool m_isStarted;
            private bool m_isSourceEnded;
            private bool m_isCanceled;
            private bool m_isDisposed;

            public BufferedEnumerator(
                ISubscriber<T> subscriber,
                int capacity,
                OnityBufferOverflow overflow,
                CancellationToken token)
            {
                m_subscriber = subscriber;
                m_capacity = capacity;
                m_overflow = overflow;
                m_token = token;
                m_onMessage = OnMessage;
            }

            public T Current
            {
                get
                {
                    lock (m_gate)
                    {
                        if (m_hasCurrent == false)
                        {
                            throw new InvalidOperationException("Current is unavailable outside a successful move.");
                        }

                        return m_current;
                    }
                }
            }

            public OnityTask<bool> MoveNextAsync()
            {
                lock (m_gate)
                {
                    if (m_pendingMove != null)
                    {
                        throw new InvalidOperationException("Only one move may be outstanding.");
                    }

                    m_hasCurrent = false;
                    m_current = default;

                    if (m_isDisposed)
                    {
                        return OnityTask.FromResult(false);
                    }
                }

                if (m_isStarted == false && m_token.IsCancellationRequested == false)
                {
                    Start();
                }

                OnityTask<bool> result;
                IDisposable canceledSubscription = null;

                lock (m_gate)
                {
                    if (m_isCanceled || m_token.IsCancellationRequested)
                    {
                        if (m_isCanceled == false)
                        {
                            canceledSubscription = Cancel();
                        }

                        result = OnityAutoResetTaskCompletionSource<bool>.CreateFromCanceled(m_token, out _).Task;
                    }
                    else if (m_count != 0)
                    {
                        m_current = Dequeue();
                        m_hasCurrent = true;
                        result = OnityTask.FromResult(true);
                    }
                    else if (m_overflowError != null)
                    {
                        result = OnityAutoResetTaskCompletionSource<bool>.CreateFromException(m_overflowError, out _).Task;
                    }
                    else if (m_isSourceEnded)
                    {
                        result = OnityTask.FromResult(false);
                    }
                    else
                    {
                        m_pendingMove = OnityAutoResetTaskCompletionSource<bool>.Create();
                        result = m_pendingMove.Task;
                    }
                }

                canceledSubscription?.Dispose();
                return result;
            }

            public OnityTask DisposeAsync()
            {
                OnityAutoResetTaskCompletionSource<bool> move;
                IDisposable subscription;

                lock (m_gate)
                {
                    if (m_isDisposed)
                    {
                        return OnityTask.CompletedTask;
                    }

                    m_isDisposed = true;
                    move = m_pendingMove;
                    m_pendingMove = null;
                    subscription = m_subscription;
                    m_subscription = null;
                    ClearBuffer();
                    m_current = default;
                    m_hasCurrent = false;
                }

                m_registration.Dispose();
                subscription?.Dispose();
                move?.TrySetResult(false);
                return OnityTask.CompletedTask;
            }

            private void Start()
            {
                m_isStarted = true;
                IDisposable subscription = null;

                try
                {
                    subscription = m_subscriber.Subscribe(m_onMessage);
                }
                catch (ObjectDisposedException)
                {
                    // A disposed channel publishes nothing more: the stream ends.
                    m_isSourceEnded = true;
                }

                lock (m_gate)
                {
                    m_subscription = subscription;
                    m_subscriptionThreadId = Environment.CurrentManagedThreadId;
                }

                if (subscription != null && m_token.CanBeCanceled)
                {
                    m_registration = RegisterWithoutContext(m_token, s_onCanceled, this);
                }
            }

            private void OnMessage(T message)
            {
                OnityAutoResetTaskCompletionSource<bool> move = null;
                IDisposable overflowSubscription = null;

                lock (m_gate)
                {
                    if (m_isDisposed || m_isCanceled || m_overflowError != null)
                    {
                        return;
                    }

                    if (m_pendingMove != null)
                    {
                        move = m_pendingMove;
                        m_pendingMove = null;
                        m_current = message;
                        m_hasCurrent = true;
                    }
                    else if (m_count < m_capacity)
                    {
                        Enqueue(message);
                    }
                    else if (m_overflow == OnityBufferOverflow.DropOldest)
                    {
                        Dequeue();
                        Enqueue(message);
                    }
                    else if (m_overflow == OnityBufferOverflow.Fault)
                    {
                        // Stop accepting; the buffered messages drain before the fault. Unsubscribing
                        // during a publication is safe for Onity's channels.
                        m_overflowError = new InvalidOperationException("The message buffer overflowed.");
                        overflowSubscription = m_subscription;
                        m_subscription = null;
                    }
                }

                overflowSubscription?.Dispose();
                move?.TrySetResult(true);
            }

            private void OnCanceled()
            {
                OnityAutoResetTaskCompletionSource<bool> move;
                IDisposable subscription;

                lock (m_gate)
                {
                    if (m_isDisposed || m_isCanceled)
                    {
                        return;
                    }

                    move = m_pendingMove;
                    m_pendingMove = null;
                    subscription = Cancel();
                }

                subscription?.Dispose();
                move?.TrySetCanceled(m_token);
            }

            // Called under m_gate. Returns the subscription to release now (on the channel's thread
            // only); otherwise DisposeAsync releases it.
            private IDisposable Cancel()
            {
                m_isCanceled = true;
                ClearBuffer();

                if (Environment.CurrentManagedThreadId != m_subscriptionThreadId)
                {
                    return null;
                }

                IDisposable subscription = m_subscription;
                m_subscription = null;
                return subscription;
            }

            private void Enqueue(T message)
            {
                if (m_items == null || m_count == m_items.Length)
                {
                    int size = m_items == null ? Math.Min(m_capacity, k_initialBufferSize) : Math.Min(m_capacity, m_items.Length * 2);
                    T[] items = new T[size];

                    for (int i = 0; i < m_count; i++)
                    {
                        items[i] = m_items[(m_head + i) % m_items.Length];
                    }

                    m_items = items;
                    m_head = 0;
                }

                m_items[(m_head + m_count) % m_items.Length] = message;
                m_count++;
            }

            private T Dequeue()
            {
                T message = m_items[m_head];
                m_items[m_head] = default;
                m_head = (m_head + 1) % m_items.Length;
                m_count--;
                return message;
            }

            private void ClearBuffer()
            {
                if (m_items != null)
                {
                    Array.Clear(m_items, 0, m_items.Length);
                }

                m_head = 0;
                m_count = 0;
            }
        }

        // Owns the async subscription, the bounded queue and the consumer loop; stops on Dispose or when
        // the lifetime token is canceled.
        private sealed class QueuedSubscription<T> : IDisposable
        {
            private static readonly Action<object> s_onLifetimeEnded = state => ((QueuedSubscription<T>)state).Dispose();

            private readonly Func<T, CancellationToken, OnityTask> m_handler;
            private readonly OnityChannel<T> m_queue;
            private readonly CancellationTokenSource m_stopSource = new CancellationTokenSource();
            private IDisposable m_subscription;
            private CancellationTokenRegistration m_lifetimeRegistration;
            private int m_isStopped;

            public QueuedSubscription(Func<T, CancellationToken, OnityTask> handler, int capacity)
            {
                m_handler = handler;
                m_queue = OnityChannel.CreateBounded<T>(capacity);
            }

            public void Start(IAsyncSubscriber<T> subscriber, CancellationToken lifetimeToken)
            {
                if (lifetimeToken.IsCancellationRequested)
                {
                    m_isStopped = 1;
                    return;
                }

                m_subscription = subscriber.Subscribe(EnqueueAsync);
                ConsumeAsync(m_stopSource.Token).Forget();

                if (lifetimeToken.CanBeCanceled)
                {
                    // Runs Dispose inline when the token was canceled meanwhile.
                    m_lifetimeRegistration = RegisterWithoutContext(lifetimeToken, s_onLifetimeEnded, this);
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_isStopped, 1) != 0)
                {
                    return;
                }

                m_lifetimeRegistration.Dispose();
                m_subscription?.Dispose();
                m_subscription = null;
                // Cancels the running handler and the consumer's pending move, then releases publishers
                // still waiting for space (EnqueueAsync drops their messages).
                m_stopSource.Cancel();
                m_queue.Writer.TryComplete();
            }

            private ValueTask EnqueueAsync(T message, CancellationToken publishToken)
            {
                if (Volatile.Read(ref m_isStopped) != 0)
                {
                    return default;
                }

                OnityTask write = m_queue.Writer.WriteAsync(message, publishToken);

                if (write.IsCompletedSuccessfully)
                {
                    return default;
                }

                return AwaitWriteAsync(write);
            }

            private async ValueTask AwaitWriteAsync(OnityTask write)
            {
                try
                {
                    await write;
                }
                catch (OnityChannelClosedException) when (Volatile.Read(ref m_isStopped) != 0)
                {
                    // The subscription stopped while this publisher waited for space; the message is dropped.
                }
            }

            private async OnityTaskVoid ConsumeAsync(CancellationToken stopToken)
            {
                IOnityAsyncEnumerator<T> enumerator = m_queue.Reader.ReadAllAsync().GetAsyncEnumerator(stopToken);

                try
                {
                    while (await enumerator.MoveNextAsync())
                    {
                        T message = enumerator.Current;

                        try
                        {
                            await m_handler(message, stopToken);
                        }
                        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception);
                        }
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync();
                }
            }
        }
    }

    // Shares one subscription per message subscriber between its pending receives (see OnityWaitHub).
    internal sealed class OnitySubscriberWaitHub<T> : OnityWaitHub<T>
    {
        private static readonly ConditionalWeakTable<ISubscriber<T>, OnitySubscriberWaitHub<T>> s_hubs =
            new ConditionalWeakTable<ISubscriber<T>, OnitySubscriberWaitHub<T>>();

        private static readonly ConditionalWeakTable<ISubscriber<T>, OnitySubscriberWaitHub<T>>.CreateValueCallback
            s_create = subscriber => new OnitySubscriberWaitHub<T>(subscriber);

        private readonly ISubscriber<T> m_subscriber;
        private readonly MessageHandler<T> m_onMessage;

        private OnitySubscriberWaitHub(ISubscriber<T> subscriber)
        {
            m_subscriber = subscriber;
            m_onMessage = OnNext;
        }

        internal static OnitySubscriberWaitHub<T> Get(ISubscriber<T> subscriber)
        {
            return s_hubs.GetValue(subscriber, s_create);
        }

        protected override IDisposable SubscribeSource()
        {
            return m_subscriber.Subscribe(m_onMessage);
        }
    }
}
