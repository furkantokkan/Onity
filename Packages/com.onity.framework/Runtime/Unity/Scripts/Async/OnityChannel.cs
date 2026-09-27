using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>Creates channels with FIFO acceptance and one consumer lease.</summary>
    public static class OnityChannel
    {
        /// <summary>Creates a channel whose item buffer can grow.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <returns>A channel with reusable reader/writer handles.</returns>
        /// <remarks>Buffer growth allocates. Warm buffered TryRead/TryWrite paths with no
        /// pending waiter publication allocate no channel storage.</remarks>
        public static OnityChannel<T> CreateUnbounded<T>() => new OnityChannel<T>(0);

        /// <summary>Creates a channel with fixed buffered item capacity.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="capacity">Positive buffered item capacity.</param>
        /// <returns>A channel with FIFO pending writer backpressure.</returns>
        /// <remarks>Capacity bounds buffered items, not the number of pending WriteAsync calls.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">Capacity is not positive.</exception>
        public static OnityChannel<T> CreateBounded<T>(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            return new OnityChannel<T>(capacity);
        }
    }

    /// <summary>A thread-safe FIFO item channel with multiple producers and one consumer lease.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <remarks>FIFO is gate acceptance order, not cross-thread invocation order. Pending waiters
    /// are unpooled and allocate. No caller cancellation source is canceled or disposed.</remarks>
    public sealed class OnityChannel<T>
    {
        private const int k_initialCapacity = 4;
        private readonly object m_gate = new object();
        private readonly int m_capacity;
        private T[] m_items;
        private int m_head;
        private int m_count;
        private bool m_closed;
        private Exception m_error;
        private object m_lease;
        private OnityChannelReadWaiter<T> m_reader;
        private OnityChannelWriteWaiter<T> m_firstWriter;
        private OnityChannelWriteWaiter<T> m_lastWriter;

        internal OnityChannel(int capacity)
        {
            m_capacity = capacity;
            m_items = new T[capacity == 0 ? k_initialCapacity : capacity];
            Reader = new OnityChannelReader<T>(this);
            Writer = new OnityChannelWriter<T>(this);
        }

        /// <summary>Gets the channel's single-consumer handle.</summary>
        public OnityChannelReader<T> Reader { get; }

        /// <summary>Gets the channel's shared producer handle.</summary>
        public OnityChannelWriter<T> Writer { get; }

        internal object Gate => m_gate;

        internal bool TryWrite(T item)
        {
            OnityChannelReadWaiter<T> reader = null;
            lock (m_gate)
            {
                if (m_closed || m_firstWriter != null)
                {
                    return false;
                }
                if (m_reader != null)
                {
                    reader = m_reader;
                    CommitRead(reader, OnityAsyncStreamOutcome.Success(true), item);
                }
                else if (HasCapacity())
                {
                    AddItem(item);
                }
                else
                {
                    return false;
                }
            }
            reader?.Finish();
            return true;
        }

        internal OnityTask WriteAsync(T item, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                var canceled = new OnityTaskCompletionSource();
                canceled.TrySetCanceled(token);
                return canceled.Task;
            }
            OnityChannelReadWaiter<T> reader = null;
            OnityChannelWriteWaiter<T> writer = null;
            Exception error = null;
            lock (m_gate)
            {
                if (m_closed)
                {
                    error = new OnityChannelClosedException(m_error);
                }
                else if (m_firstWriter == null && m_reader != null)
                {
                    reader = m_reader;
                    CommitRead(reader, OnityAsyncStreamOutcome.Success(true), item);
                }
                else if (m_firstWriter == null && HasCapacity())
                {
                    AddItem(item);
                }
                else
                {
                    writer = new OnityChannelWriteWaiter<T>(this, item, token);
                    writer.Previous = m_lastWriter;
                    if (m_lastWriter == null)
                    {
                        m_firstWriter = writer;
                    }
                    else
                    {
                        m_lastWriter.Next = writer;
                    }
                    m_lastWriter = writer;
                }
            }
            reader?.Finish();
            if (error != null)
            {
                var failed = new OnityTaskCompletionSource();
                failed.TrySetFault(error);
                return failed.Task;
            }
            if (writer != null)
            {
                writer.Initialize();
                return writer.Source.Task;
            }
            return OnityTask.Completed;
        }

        internal bool TryRead(out T item)
        {
            OnityChannelWriteWaiter<T> writer;
            lock (m_gate)
            {
                if (m_lease != null || m_count == 0)
                {
                    item = default;
                    return false;
                }
                item = RemoveItem();
                writer = PromoteWriter();
            }
            writer?.Finish();
            return true;
        }

        internal OnityTask<T> ReadAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                var canceled = new OnityTaskCompletionSource<T>();
                canceled.TrySetCanceled(token);
                return canceled.Task;
            }
            OnityChannelReadWaiter<T> reader = null;
            OnityChannelWriteWaiter<T> writer = null;
            T item = default;
            Exception error = null;
            lock (m_gate)
            {
                if (m_lease != null)
                {
                    throw new InvalidOperationException("Another channel consumer holds the reader lease.");
                }
                if (m_count != 0)
                {
                    item = RemoveItem();
                    writer = PromoteWriter();
                }
                else if (m_closed)
                {
                    error = m_error ?? new OnityChannelClosedException();
                }
                else
                {
                    reader = new OnityChannelReadWaiter<T>(this, token);
                    m_reader = reader;
                    m_lease = reader;
                }
            }
            writer?.Finish();
            if (error != null)
            {
                var failed = new OnityTaskCompletionSource<T>();
                failed.TrySetFault(error);
                return failed.Task;
            }
            if (reader != null)
            {
                reader.Initialize();
                return reader.DirectSource.Task;
            }
            return OnityTask<T>.FromResult(item);
        }

        internal OnityTask<bool> Move(OnityChannelReader<T>.Enumerator enumerator)
        {
            OnityChannelReadWaiter<T> reader = null;
            OnityChannelWriteWaiter<T> writer = null;
            OnityTaskCompletionSource<bool> terminal = null;
            OnityAsyncStreamOutcome outcome = default;
            OnityTask<bool> result;
            lock (m_gate)
            {
                if (HasOutstandingMove(enumerator))
                {
                    throw new InvalidOperationException("Only one channel enumeration move may be outstanding.");
                }
                if (enumerator.Disposed)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (enumerator.Ended)
                {
                    return enumerator.TerminalTask;
                }
                CancellationToken canceled = enumerator.CapturedToken.IsCancellationRequested
                    ? enumerator.CapturedToken : enumerator.EnumerationToken;
                if (canceled.IsCancellationRequested)
                {
                    outcome = new OnityAsyncStreamOutcome
                    {
                        Status = OnityTaskSourceStatus.Canceled,
                        Token = canceled
                    };
                    terminal = SetTerminal(enumerator, outcome);
                    result = enumerator.TerminalTask;
                }
                else
                {
                    if (!enumerator.Started)
                    {
                        if (m_lease != null)
                        {
                            throw new InvalidOperationException("Another channel consumer holds the reader lease.");
                        }
                        m_lease = enumerator;
                        enumerator.Started = true;
                    }
                    enumerator.CurrentValid = false;
                    enumerator.Item = default;
                    if (m_count != 0)
                    {
                        enumerator.Item = RemoveItem();
                        enumerator.CurrentValid = true;
                        writer = PromoteWriter();
                        result = OnityTask<bool>.FromResult(true);
                    }
                    else if (m_closed)
                    {
                        outcome = m_error == null ? OnityAsyncStreamOutcome.Success(false)
                            : OnityAsyncStreamOutcome.Failure(m_error);
                        terminal = SetTerminal(enumerator, outcome);
                        result = enumerator.TerminalTask;
                    }
                    else
                    {
                        reader = new OnityChannelReadWaiter<T>(this, enumerator);
                        m_reader = reader;
                        enumerator.Pending = reader;
                        enumerator.Outstanding = true;
                        enumerator.OutstandingSource = reader.MoveSource;
                        result = reader.MoveSource.Task;
                    }
                }
            }
            writer?.Finish();
            if (terminal != null)
            {
                outcome.Publish(terminal, false);
            }
            reader?.Initialize();
            return result;
        }

        private OnityTaskCompletionSource<bool> SetTerminal(OnityChannelReader<T>.Enumerator enumerator,
            OnityAsyncStreamOutcome outcome)
        {
            enumerator.Ended = true;
            enumerator.CurrentValid = false;
            enumerator.Item = default;
            ReleaseLease(enumerator);
            if (outcome.Status == OnityTaskSourceStatus.Succeeded)
            {
                enumerator.TerminalTask = OnityTask<bool>.FromResult(false);
                return null;
            }
            var source = new OnityTaskCompletionSource<bool>();
            enumerator.TerminalTask = source.Task;
            enumerator.Outstanding = true;
            enumerator.OutstandingSource = source;
            source.Task.GetAwaiter().UnsafeOnCompleted(() => EndPublication(enumerator, source));
            return source;
        }

        internal OnityTask Dispose(OnityChannelReader<T>.Enumerator enumerator)
        {
            OnityChannelReadWaiter<T> reader;
            OnityTask result;
            lock (m_gate)
            {
                if (enumerator.Disposed)
                {
                    return enumerator.DisposalSource?.Task ?? OnityTask.Completed;
                }
                enumerator.Disposed = true;
                enumerator.CurrentValid = false;
                enumerator.Item = default;
                reader = enumerator.Pending;
                if (reader != null && !reader.Committed)
                {
                    CommitRead(reader, OnityAsyncStreamOutcome.Success(false), default);
                }
                if (HasOutstandingMove(enumerator))
                {
                    enumerator.DisposalSource = new OnityTaskCompletionSource();
                    enumerator.DisposalWaitSource = enumerator.OutstandingSource;
                    result = enumerator.DisposalSource.Task;
                }
                else
                {
                    ReleaseLease(enumerator);
                    result = OnityTask.Completed;
                }
            }
            reader?.Finish();
            return result;
        }

        internal void FinishRead(OnityChannelReadWaiter<T> reader)
        {
            lock (m_gate)
            {
                var enumerator = reader.Enumerator;
                if (enumerator != null && ReferenceEquals(enumerator.Pending, reader))
                {
                    enumerator.Pending = null;
                    if (enumerator.Disposed || enumerator.Ended)
                    {
                        ReleaseLease(enumerator);
                    }
                }
            }
        }

        internal void EndPublication(OnityChannelReader<T>.Enumerator enumerator,
            OnityTaskCompletionSource<bool> source)
        {
            OnityTaskCompletionSource disposal = null;
            lock (m_gate)
            {
                if (ReferenceEquals(enumerator.OutstandingSource, source))
                {
                    enumerator.Outstanding = false;
                    enumerator.OutstandingSource = null;
                }
                if (enumerator.Disposed && enumerator.Pending == null && !enumerator.DisposalPublished &&
                    ReferenceEquals(enumerator.DisposalWaitSource, source))
                {
                    enumerator.DisposalPublished = true;
                    enumerator.DisposalWaitSource = null;
                    disposal = enumerator.DisposalSource;
                }
            }
            disposal?.TrySetResult();
        }

        private bool HasOutstandingMove(OnityChannelReader<T>.Enumerator enumerator)
        {
            if (enumerator.Outstanding && enumerator.OutstandingSource.Task.IsCompleted)
            {
                // The built-in source commits status before invoking continuations.
                // This permits continuation reentry without a pending overlap window.
                enumerator.Outstanding = false;
                enumerator.OutstandingSource = null;
            }
            return enumerator.Outstanding;
        }

        internal void CancelRead(OnityChannelReadWaiter<T> reader, CancellationToken token)
        {
            CompleteRead(reader, new OnityAsyncStreamOutcome
            {
                Status = OnityTaskSourceStatus.Canceled,
                Token = token
            });
        }

        internal void FailRead(OnityChannelReadWaiter<T> reader, Exception exception) =>
            CompleteRead(reader, OnityAsyncStreamOutcome.Failure(exception));

        private void CompleteRead(OnityChannelReadWaiter<T> reader, OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                if (reader.Committed || !ReferenceEquals(m_reader, reader))
                {
                    return;
                }
                CommitRead(reader, outcome, default);
            }
            reader.Finish();
        }

        private void CommitRead(OnityChannelReadWaiter<T> reader,
            OnityAsyncStreamOutcome outcome, T item)
        {
            m_reader = null;
            reader.Committed = true;
            reader.Outcome = outcome;
            reader.Item = item;
            var enumerator = reader.Enumerator;
            if (enumerator == null)
            {
                // Direct reads release at commitment; old callbacks can only
                // act on this detached, already committed waiter identity.
                ReleaseLease(reader);
            }
            else if (outcome.Status == OnityTaskSourceStatus.Succeeded && outcome.Value)
            {
                enumerator.Item = item;
                enumerator.CurrentValid = true;
            }
            else
            {
                enumerator.Item = default;
                enumerator.CurrentValid = false;
                enumerator.Ended = true;
                enumerator.TerminalTask = outcome.Status == OnityTaskSourceStatus.Succeeded
                    ? OnityTask<bool>.FromResult(false) : reader.MoveSource.Task;
            }
        }

        internal void CancelWrite(OnityChannelWriteWaiter<T> writer, CancellationToken token) =>
            CompleteWrite(writer, new OnityAsyncStreamOutcome
            {
                Status = OnityTaskSourceStatus.Canceled,
                Token = token
            });

        internal void FailWrite(OnityChannelWriteWaiter<T> writer, Exception exception) =>
            CompleteWrite(writer, OnityAsyncStreamOutcome.Failure(exception));

        private void CompleteWrite(OnityChannelWriteWaiter<T> writer, OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                if (writer.Committed)
                {
                    return;
                }
                RemoveWriter(writer);
                writer.Committed = true;
                writer.Outcome = outcome;
            }
            writer.Finish();
        }

        internal bool TryComplete(Exception error)
        {
            OnityChannelWriteWaiter<T> writers;
            OnityChannelReadWaiter<T> reader;
            lock (m_gate)
            {
                if (m_closed)
                {
                    return false;
                }
                m_closed = true;
                m_error = error;
                writers = m_firstWriter;
                m_firstWriter = null;
                m_lastWriter = null;
                for (var writer = writers; writer != null; writer = writer.Next)
                {
                    writer.Previous = null;
                    writer.Committed = true;
                    writer.Outcome = OnityAsyncStreamOutcome.Failure(new OnityChannelClosedException(error));
                }
                reader = m_reader;
                if (reader != null)
                {
                    OnityAsyncStreamOutcome outcome = error != null ? OnityAsyncStreamOutcome.Failure(error)
                        : reader.Enumerator != null ? OnityAsyncStreamOutcome.Success(false)
                        : OnityAsyncStreamOutcome.Failure(new OnityChannelClosedException());
                    CommitRead(reader, outcome, default);
                }
            }
            // Retain each next node before publication/reentrant continuations.
            Exception failure = null;
            while (writers != null)
            {
                var next = writers.Next;
                writers.Next = null;
                try
                {
                    writers.Finish();
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
                writers = next;
            }
            try
            {
                reader?.Finish();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return true;
        }

        private bool HasCapacity() => m_capacity == 0 || m_count < m_capacity;

        private void AddItem(T item)
        {
            if (m_count == m_items.Length)
            {
                var items = new T[checked(m_items.Length * 2)];
                for (int i = 0; i < m_count; i++)
                {
                    items[i] = m_items[(m_head + i) % m_items.Length];
                }
                m_items = items;
                m_head = 0;
            }
            m_items[(m_head + m_count) % m_items.Length] = item;
            m_count++;
        }

        private T RemoveItem()
        {
            T item = m_items[m_head];
            m_items[m_head] = default;
            m_head = (m_head + 1) % m_items.Length;
            m_count--;
            return item;
        }

        private OnityChannelWriteWaiter<T> PromoteWriter()
        {
            var writer = m_firstWriter;
            if (writer == null)
            {
                return null;
            }
            RemoveWriter(writer);
            AddItem(writer.Item);
            writer.Item = default;
            writer.Committed = true;
            writer.Outcome = OnityAsyncStreamOutcome.Success(false);
            return writer;
        }

        private void RemoveWriter(OnityChannelWriteWaiter<T> writer)
        {
            if (writer.Previous == null)
            {
                m_firstWriter = writer.Next;
            }
            else
            {
                writer.Previous.Next = writer.Next;
            }
            if (writer.Next == null)
            {
                m_lastWriter = writer.Previous;
            }
            else
            {
                writer.Next.Previous = writer.Previous;
            }
            writer.Previous = null;
            writer.Next = null;
        }

        private void ReleaseLease(object owner)
        {
            if (ReferenceEquals(m_lease, owner))
            {
                m_lease = null;
            }
        }
    }
}
