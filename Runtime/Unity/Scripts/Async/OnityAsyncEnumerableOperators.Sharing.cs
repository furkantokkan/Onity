using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    // One shared enumeration of a source (the pump), started by Connect, and any number of
    // subscribers that each buffer every item published after they registered. A single driver
    // serializes every call into the source enumerator; subscriber continuations run outside locks.
    internal sealed class OnityPublishAsyncEnumerable<T> : IOnityConnectableAsyncEnumerable<T>
    {
        private enum Step
        {
            None,
            Move,
            Item,
            Dispose,
            Finish
        }

        private readonly object m_gate = new object();
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly Action<OnityAsyncStreamOutcome> m_onMove;
        private readonly Action<OnityAsyncStreamOutcome> m_onDispose;
        private Subscriber[] m_subscribers = Array.Empty<Subscriber>();
        private Connection m_connection;
        private CancellationTokenSource m_cancellation;
        private IOnityAsyncEnumerator<T> m_enumerator;
        private OnityAsyncStreamOutcome m_outcome;
        private OnityAsyncStreamOutcome m_stopOutcome;
        private OnityAsyncStreamOutcome m_disposeOutcome;
        private OnityAsyncStreamOutcome m_terminalOutcome;
        private bool m_driving;
        private bool m_again;
        private bool m_moving;
        private bool m_hasOutcome;
        private bool m_stopRequested;
        private bool m_stopping;
        private bool m_disposeStarted;
        private bool m_disposeDone;
        private bool m_terminal;

        internal OnityPublishAsyncEnumerable(IOnityAsyncEnumerable<T> source)
        {
            m_source = source;
            m_onMove = RecordMove;
            m_onDispose = RecordDispose;
        }

        public IDisposable Connect()
        {
            Connection connection;
            lock (m_gate)
            {
                if (m_connection != null)
                {
                    return m_connection;
                }
                m_cancellation = new CancellationTokenSource();
                m_connection = new Connection(this);
                connection = m_connection;
            }
            Drive();
            return connection;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var subscriber = new Subscriber(this, cancellationToken);
            lock (m_gate)
            {
                if (m_terminal)
                {
                    subscriber.SetTerminal(m_terminalOutcome);
                }
                else
                {
                    var subscribers = new Subscriber[m_subscribers.Length + 1];
                    Array.Copy(m_subscribers, subscribers, m_subscribers.Length);
                    subscribers[m_subscribers.Length] = subscriber;
                    m_subscribers = subscribers;
                }
            }
            return subscriber;
        }

        private void Unsubscribe(Subscriber subscriber)
        {
            lock (m_gate)
            {
                int index = Array.IndexOf(m_subscribers, subscriber);
                if (index < 0)
                {
                    return;
                }
                var subscribers = new Subscriber[m_subscribers.Length - 1];
                Array.Copy(m_subscribers, 0, subscribers, 0, index);
                Array.Copy(m_subscribers, index + 1, subscribers, index, m_subscribers.Length - index - 1);
                m_subscribers = subscribers;
            }
        }

        private void Disconnect()
        {
            lock (m_gate)
            {
                if (m_stopRequested)
                {
                    return;
                }
                m_stopRequested = true;
            }
            Drive();
        }

        private void RecordMove(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                m_hasOutcome = true;
                m_outcome = outcome;
            }
            Drive();
        }

        private void RecordDispose(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                m_disposeDone = true;
                m_disposeOutcome = outcome;
            }
            Drive();
        }

        private void Drive()
        {
            lock (m_gate)
            {
                if (m_driving)
                {
                    m_again = true;
                    return;
                }
                m_driving = true;
                m_again = false;
            }
            while (true)
            {
                Step step = Step.None;
                Subscriber[] subscribers = null;
                OnityAsyncStreamOutcome terminal = default;
                lock (m_gate)
                {
                    if (m_hasOutcome)
                    {
                        m_hasOutcome = false;
                        m_moving = false;
                        OnityAsyncStreamOutcome outcome = m_outcome;
                        m_outcome = default;
                        if (!m_stopping)
                        {
                            if (outcome.Status == OnityTaskSourceStatus.Succeeded && outcome.Value)
                            {
                                step = Step.Item;
                            }
                            else
                            {
                                m_stopping = true;
                                m_stopOutcome = outcome.Status == OnityTaskSourceStatus.Succeeded
                                    ? OnityAsyncStreamOutcome.Success(false) : outcome;
                            }
                        }
                    }
                    if (step == Step.None)
                    {
                        if (!m_stopping && m_stopRequested)
                        {
                            m_stopping = true;
                            m_stopOutcome = OnityAsyncStreamOutcome.Success(false);
                        }
                        if (m_stopping)
                        {
                            if (!m_disposeStarted)
                            {
                                m_disposeStarted = true;
                                step = Step.Dispose;
                            }
                            else if (m_disposeDone && !m_moving && !m_terminal)
                            {
                                m_terminal = true;
                                m_terminalOutcome = m_disposeOutcome.Status != OnityTaskSourceStatus.Succeeded
                                    ? m_disposeOutcome : m_stopOutcome;
                                terminal = m_terminalOutcome;
                                subscribers = m_subscribers;
                                m_subscribers = Array.Empty<Subscriber>();
                                step = Step.Finish;
                            }
                        }
                        else if (m_connection != null && !m_moving)
                        {
                            m_moving = true;
                            step = Step.Move;
                        }
                    }
                    if (step == Step.None)
                    {
                        if (m_again)
                        {
                            m_again = false;
                            continue;
                        }
                        m_driving = false;
                        return;
                    }
                }

                if (step == Step.Move)
                {
                    StartMove();
                }
                else if (step == Step.Item)
                {
                    Broadcast();
                }
                else if (step == Step.Dispose)
                {
                    StartDispose();
                }
                else
                {
                    Finish(subscribers, terminal);
                }
            }
        }

        private void StartMove()
        {
            OnityTask<bool> task;
            try
            {
                if (m_enumerator == null)
                {
                    IOnityAsyncEnumerator<T> enumerator = m_source.GetAsyncEnumerator(m_cancellation.Token);
                    if (enumerator == null)
                    {
                        throw new InvalidOperationException("The upstream description returned a null enumerator.");
                    }
                    lock (m_gate)
                    {
                        m_enumerator = enumerator;
                    }
                }
                task = m_enumerator.MoveNextAsync();
            }
            catch (Exception exception)
            {
                RecordMove(OnityAsyncStreamOutcome.Failure(exception));
                return;
            }
            if (task.IsCompleted)
            {
                RecordMove(Read(task));
            }
            else
            {
                new OnityAsyncStreamMoveObserver(task, m_onMove).Register();
            }
        }

        private void Broadcast()
        {
            T item;
            try
            {
                item = m_enumerator.Current;
            }
            catch (Exception exception)
            {
                lock (m_gate)
                {
                    if (!m_stopping)
                    {
                        m_stopping = true;
                        m_stopOutcome = OnityAsyncStreamOutcome.Failure(exception);
                    }
                }
                return;
            }
            Subscriber[] subscribers;
            lock (m_gate)
            {
                subscribers = m_subscribers;
            }
            for (int i = 0; i < subscribers.Length; i++)
            {
                subscribers[i].Push(item);
            }
        }

        private void StartDispose()
        {
            OnityAsyncStreamOutcome failure = OnityAsyncStreamOutcome.Success(false);
            try
            {
                m_cancellation.Cancel();
            }
            catch (Exception exception)
            {
                failure = OnityAsyncStreamOutcome.Failure(exception);
            }
            IOnityAsyncEnumerator<T> enumerator;
            lock (m_gate)
            {
                enumerator = m_enumerator;
            }
            if (enumerator == null)
            {
                RecordDispose(failure);
                return;
            }
            OnityTask task;
            try
            {
                task = enumerator.DisposeAsync();
            }
            catch (Exception exception)
            {
                RecordDispose(OnityAsyncStreamOutcome.Failure(exception));
                return;
            }
            if (task.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome = Read(task);
                RecordDispose(outcome.Status != OnityTaskSourceStatus.Succeeded ? outcome : failure);
            }
            else
            {
                new OnityAsyncStreamCleanupObserver(task, outcome =>
                    RecordDispose(outcome.Status != OnityTaskSourceStatus.Succeeded ? outcome : failure)).Register();
            }
        }

        private void Finish(Subscriber[] subscribers, OnityAsyncStreamOutcome terminal)
        {
            CancellationTokenSource cancellation;
            lock (m_gate)
            {
                m_enumerator = null;
                cancellation = m_cancellation;
            }
            cancellation?.Dispose();
            for (int i = 0; i < subscribers.Length; i++)
            {
                subscribers[i].Terminate(terminal);
            }
        }

        private static OnityAsyncStreamOutcome Read(OnityTask<bool> task)
        {
            try
            {
                return OnityAsyncStreamOutcome.Read(task);
            }
            catch (Exception exception)
            {
                return OnityAsyncStreamOutcome.Failure(exception);
            }
        }

        private static OnityAsyncStreamOutcome Read(OnityTask task)
        {
            try
            {
                return OnityAsyncStreamOutcome.Read(task);
            }
            catch (Exception exception)
            {
                return OnityAsyncStreamOutcome.Failure(exception);
            }
        }

        private sealed class Connection : IDisposable
        {
            private readonly OnityPublishAsyncEnumerable<T> m_owner;

            internal Connection(OnityPublishAsyncEnumerable<T> owner)
            {
                m_owner = owner;
            }

            public void Dispose() => m_owner.Disconnect();
        }

        // One enumerator of the published stream: an unbounded buffer of the items published since it
        // registered, then the shared terminal outcome.
        private sealed class Subscriber : OnityAsyncEnumeratorBase<T>
        {
            private static readonly Action<object> s_cancel = state => ((Subscriber)state).Cancel();

            private readonly object m_lock = new object();
            private readonly OnityPublishAsyncEnumerable<T> m_owner;
            private readonly CancellationToken m_token;
            private readonly Queue<T> m_items = new Queue<T>();
            private CancellationTokenRegistration m_registration;
            private OnityTaskCompletionSource<bool> m_waiting;
            private OnityAsyncStreamOutcome m_terminalOutcome;
            private T m_current;
            private bool m_terminal;
            private bool m_closed;

            internal Subscriber(OnityPublishAsyncEnumerable<T> owner, CancellationToken token)
            {
                m_owner = owner;
                m_token = token;
                if (token.CanBeCanceled)
                {
                    m_registration = OnityStreamTokens.Register(token, s_cancel, this);
                }
            }

            internal void SetTerminal(OnityAsyncStreamOutcome outcome)
            {
                lock (m_lock)
                {
                    m_terminal = true;
                    m_terminalOutcome = outcome;
                }
            }

            internal void Push(T item)
            {
                OnityTaskCompletionSource<bool> waiting;
                lock (m_lock)
                {
                    if (m_closed || m_terminal)
                    {
                        return;
                    }
                    waiting = m_waiting;
                    m_waiting = null;
                    if (waiting != null)
                    {
                        m_current = item;
                    }
                    else
                    {
                        m_items.Enqueue(item);
                    }
                }
                waiting?.TrySetResult(true);
            }

            internal void Terminate(OnityAsyncStreamOutcome outcome)
            {
                OnityTaskCompletionSource<bool> waiting;
                lock (m_lock)
                {
                    if (m_closed || m_terminal)
                    {
                        return;
                    }
                    m_terminal = true;
                    m_terminalOutcome = outcome;
                    waiting = m_waiting;
                    m_waiting = null;
                }
                if (waiting != null)
                {
                    Publish(waiting, outcome);
                }
            }

            private void Cancel()
            {
                OnityTaskCompletionSource<bool> waiting;
                lock (m_lock)
                {
                    waiting = m_waiting;
                    m_waiting = null;
                }
                waiting?.TrySetCanceled(m_token);
            }

            protected override OnityTask<bool> MoveCore()
            {
                if (IsClosing)
                {
                    return OnityStageTasks.False;
                }
                if (m_token.IsCancellationRequested)
                {
                    return OnityStageTasks.Failed(OnityStageTasks.Canceled(m_token));
                }
                lock (m_lock)
                {
                    if (m_items.Count != 0)
                    {
                        m_current = m_items.Dequeue();
                        return OnityStageTasks.True;
                    }
                    if (m_terminal)
                    {
                        return m_terminalOutcome.Status == OnityTaskSourceStatus.Succeeded
                            ? OnityStageTasks.False : OnityStageTasks.Failed(m_terminalOutcome);
                    }
                    m_waiting = new OnityTaskCompletionSource<bool>();
                    return m_waiting.Task;
                }
            }

            protected override bool ReadCurrentCore(out T current)
            {
                lock (m_lock)
                {
                    current = m_current;
                    m_current = default;
                    return true;
                }
            }

            protected override OnityTask CleanupCore()
            {
                OnityTaskCompletionSource<bool> waiting;
                lock (m_lock)
                {
                    m_closed = true;
                    waiting = m_waiting;
                    m_waiting = null;
                    m_items.Clear();
                    m_current = default;
                }
                m_owner.Unsubscribe(this);
                m_registration.Dispose();
                waiting?.TrySetResult(false);
                return OnityTask.Completed;
            }

            private static void Publish(OnityTaskCompletionSource<bool> waiting, OnityAsyncStreamOutcome outcome)
            {
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    waiting.TrySetResult(false);
                }
                else
                {
                    outcome.Publish(waiting, false);
                }
            }
        }
    }

    // Each enumeration pulls its source eagerly into an unbounded buffer, independent of the consumer.
    internal sealed class OnityQueueEnumerator<T> : OnityMultiSourceAsyncEnumerator<T>
    {
        private readonly OnityStreamSlot<T> m_slot;

        private OnityQueueEnumerator(OnityStreamSlot<T> slot, CancellationToken token)
            : base(new OnityStreamSlot[] { slot }, 1, token)
        {
            m_slot = slot;
        }

        internal static OnityQueueEnumerator<T> Create(IOnityAsyncEnumerable<T> source, CancellationToken token)
        {
            return new OnityQueueEnumerator<T>(new OnityStreamSlot<T>(source), token);
        }

        protected override void OnStart() => WatchToken(0, Token);
        protected override void OnRequest() => RequestMove(m_slot);

        protected override void OnItem(OnityStreamSlot slot)
        {
            Emit(m_slot.Value);
            RequestMove(m_slot);
        }

        protected override void OnEnd(OnityStreamSlot slot) => Complete();
        protected override void OnSignal(int id, OnityAsyncStreamOutcome outcome) => Abort(outcome);
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- Publish / Queue ----------------------------------------------------------------

        /// <summary>Shares one enumeration of the source among every enumerator of the returned stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description, enumerated once per connection.</param>
        /// <returns>A connectable description; nothing is enumerated before
        /// <see cref="IOnityConnectableAsyncEnumerable{T}.Connect"/>.</returns>
        /// <remarks>
        /// <para>Connection: the first Connect acquires the source with a token owned by the connection and
        /// pulls it continuously, one move at a time, on the thread that completes each move (inside Connect
        /// for a synchronous source, so an endless synchronous source never returns). Later Connect calls
        /// return the same connection; the source is never restarted, as in UniTask.</para>
        /// <para>Enumerators register when they are created and buffer, without bound, every item published
        /// after that until they read it, so a registered enumerator loses no item (UniTask drops items while
        /// an enumerator is not waiting). Items published while no enumerator is registered are dropped. When
        /// the source ends or faults, every enumerator first drains its buffer, then ends or faults with the
        /// same exception; an enumerator created afterwards ends or faults at once.</para>
        /// <para>Disposal: disposing the connection cancels the owned token and disposes the source
        /// enumeration (settling its pending move); enumerators then drain and end normally (UniTask reports
        /// an OperationCanceledException instead), and a source disposal failure becomes their fault. The end
        /// or fault is delivered only after the source enumeration has been disposed. Disposing an enumerator
        /// only unregisters it and never stops the shared enumeration; its token cancels only its own pending
        /// move.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityConnectableAsyncEnumerable<T> Publish<T>(this IOnityAsyncEnumerable<T> source)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityPublishAsyncEnumerable<T>(source);
        }

        /// <summary>Decouples the source from the consumer: the source is pulled as fast as it yields and
        /// buffered until read.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <returns>A lazy description with independent enumerations.</returns>
        /// <remarks>Each enumeration acquires the source at its first move with the enumeration token and
        /// keeps exactly one move outstanding until the source ends, buffering items without bound; an
        /// endless synchronous source therefore never yields control. The consumer drains the buffer, then
        /// sees the end or fault. A canceled enumeration token cancels the next or pending move at once and
        /// discards buffered items, like a channel's ReadAllAsync. Disposal disposes the source (settling its
        /// pending move) and drops buffered items.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Queue<T>(this IOnityAsyncEnumerable<T> source)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => OnityQueueEnumerator<T>.Create(upstream, token));
        }
    }
}
