using System;
using System.Buffers;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// A readable value that can also be awaited and enumerated as a sequence of changes.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public interface IOnityReadOnlyAsyncReactiveProperty<T> : IOnityAsyncEnumerable<T>
    {
        /// <summary>Gets the latest value.</summary>
        T Value { get; }

        /// <summary>
        /// Creates an enumeration that yields only values published after the enumerator was created.
        /// </summary>
        /// <returns>An enumerable that does not start with the current value.</returns>
        IOnityAsyncEnumerable<T> WithoutCurrent();

        /// <summary>
        /// Waits for the next published value.
        /// </summary>
        /// <param name="cancellationToken">Token that cancels the wait.</param>
        /// <returns>A single-consumption task that yields the next published value.</returns>
        OnityTask<T> WaitAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A writable value that can also be awaited and enumerated as a sequence of changes.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public interface IOnityAsyncReactiveProperty<T> : IOnityReadOnlyAsyncReactiveProperty<T>
    {
        /// <summary>Gets or sets the value. Setting publishes the value to every current waiter.</summary>
        /// <exception cref="InvalidOperationException">
        /// The set runs while the same property is still publishing a value, for example from a
        /// continuation or callback that the publication resumed.
        /// </exception>
        new T Value { get; set; }
    }

    internal interface IOnityAsyncReactiveValueReader<T>
    {
        T ReadLatestValue();
    }

    /// <summary>
    /// A writable value whose changes can be awaited with <see cref="WaitAsync"/> or consumed as an
    /// <see cref="IOnityAsyncEnumerable{T}"/>. This mirrors UniTask's <c>AsyncReactiveProperty</c>.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <remarks>
    /// Setting <see cref="Value"/> always publishes, even when the new value equals the old one; there
    /// is no equality check, unlike <c>ReactiveProperty&lt;T&gt;</c> from <c>Onity.Reactive</c>, which
    /// skips equal values. A publication completes every pending <see cref="WaitAsync"/> task once and
    /// wakes every enumerator that currently awaits a move. An enumerator that is not awaiting a move
    /// when a value is published does not see that value; it receives the next one. Setting the value
    /// while nothing has subscribed only stores it and creates no publication state.
    /// Thread affinity: intended for the Unity main thread. The wait list is synchronized, so a
    /// cancellation token may be canceled from any thread, but continuations run inline on the thread
    /// that publishes or cancels. Setting <see cref="Value"/> from such an inline continuation, or from
    /// an enumerator callback, while the same property is still publishing throws
    /// <see cref="InvalidOperationException"/> (UniTask parity) and leaves the value and the running
    /// publication unchanged; set the value after the publication has returned instead. A set from
    /// another thread that overlaps a running publication is not supported and throws the same
    /// exception.
    /// </remarks>
    [Serializable]
    public sealed class OnityAsyncReactiveProperty<T> :
        IOnityAsyncReactiveProperty<T>, IOnityAsyncReactiveValueReader<T>, IDisposable
    {
        private static readonly bool s_isValueType = typeof(T).IsValueType;

        [SerializeField]
        private T m_latestValue;

        [NonSerialized]
        private OnityAsyncReactiveCore<T> m_core;

        /// <summary>
        /// Initializes the property.
        /// </summary>
        /// <param name="value">Initial value.</param>
        public OnityAsyncReactiveProperty(T value)
        {
            m_latestValue = value;
        }

        private OnityAsyncReactiveCore<T> Core
        {
            get
            {
                OnityAsyncReactiveCore<T> core = Volatile.Read(ref m_core);
                if (core != null)
                {
                    return core;
                }

                core = new OnityAsyncReactiveCore<T>(this);
                OnityAsyncReactiveCore<T> existing = Interlocked.CompareExchange(ref m_core, core, null);
                return existing ?? core;
            }
        }

        /// <summary>
        /// Gets or sets the value. Setting stores the value and publishes it to every current waiter,
        /// whether or not it equals the previous value. Without any subscriber it only stores the value.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The set runs while this property is still publishing a value, for example from a continuation
        /// or callback that the publication resumed. The value is left unchanged.
        /// </exception>
        public T Value
        {
            get => m_latestValue;
            set
            {
                // The publication state exists only after something subscribed; nothing to publish to
                // (and nothing that could already be publishing) until then.
                OnityAsyncReactiveCore<T> core = Volatile.Read(ref m_core);
                if (core == null)
                {
                    m_latestValue = value;
                    return;
                }

                core.Publish(value, ref m_latestValue);
            }
        }

        T IOnityAsyncReactiveValueReader<T>.ReadLatestValue() => m_latestValue;

        /// <summary>
        /// Creates an enumeration that yields only values published after the enumerator was created.
        /// </summary>
        /// <returns>An enumerable that does not start with the current value.</returns>
        public IOnityAsyncEnumerable<T> WithoutCurrent() => Core.WithoutCurrent;

        /// <summary>
        /// Creates an enumerator whose first move yields the current value, followed by later values.
        /// </summary>
        /// <param name="cancellationToken">Token that terminates the enumerator; a pending move is canceled.</param>
        /// <returns>A caller-owned enumerator.</returns>
        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return Core.CreateEnumerator(cancellationToken, true);
        }

        /// <summary>
        /// Waits for the next published value.
        /// </summary>
        /// <param name="cancellationToken">Token that cancels the wait.</param>
        /// <returns>A single-consumption task that yields the next published value.</returns>
        public OnityTask<T> WaitAsync(CancellationToken cancellationToken = default)
        {
            return Core.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Completes the enumerators registered now (their pending move returns false) and cancels the
        /// pending <see cref="WaitAsync"/> tasks. The property stays usable for later subscribers.
        /// </summary>
        public void Dispose()
        {
            Volatile.Read(ref m_core)?.Complete();
        }

        /// <summary>
        /// Reads the latest value.
        /// </summary>
        /// <param name="value">Property to read.</param>
        /// <returns>The latest value.</returns>
        public static implicit operator T(OnityAsyncReactiveProperty<T> value)
        {
            return value.Value;
        }

        /// <summary>
        /// Returns the string form of the latest value.
        /// </summary>
        /// <returns>The value's string form, or null when a reference value is null.</returns>
        public override string ToString()
        {
            if (s_isValueType)
            {
                return m_latestValue.ToString();
            }

            return m_latestValue?.ToString();
        }
    }

    /// <summary>
    /// A read-only value driven by an <see cref="IOnityAsyncEnumerable{T}"/>. It mirrors UniTask's
    /// <c>ReadOnlyAsyncReactiveProperty</c>: each item the source yields becomes the latest value and
    /// is published to waiters and enumerators exactly like <see cref="OnityAsyncReactiveProperty{T}"/>.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <remarks>
    /// The source is enumerated by one pump that starts in the constructor and ends when the source
    /// completes, faults, is canceled through the constructor token, or <see cref="Dispose"/> is called.
    /// A fault of the source is logged and ends the pump; the last value stays readable. Cancellation,
    /// including any <see cref="OperationCanceledException"/> the source throws, ends the pump silently.
    /// When the source ends, waiters and enumerators are not completed (UniTask parity); they complete
    /// only on <see cref="Dispose"/>. Thread affinity follows <see cref="OnityAsyncReactiveProperty{T}"/>:
    /// use it on the Unity main thread, and the pump runs wherever the source delivers its items.
    /// A source that yields an item from inside a continuation of this same property re-enters the
    /// running publication; as for <see cref="OnityAsyncReactiveProperty{T}.Value"/> that throws
    /// <see cref="InvalidOperationException"/>, so the pump faults and the fault is logged.
    /// </remarks>
    public sealed class OnityReadOnlyAsyncReactiveProperty<T> :
        IOnityReadOnlyAsyncReactiveProperty<T>, IOnityAsyncReactiveValueReader<T>, IDisposable
    {
        private static readonly bool s_isValueType = typeof(T).IsValueType;

        private readonly OnityAsyncReactiveCore<T> m_core;
        private T m_latestValue;
        private IOnityAsyncEnumerator<T> m_enumerator;

        /// <summary>
        /// Initializes the property with an initial value and starts consuming the source.
        /// </summary>
        /// <param name="initialValue">Value reported until the source yields its first item.</param>
        /// <param name="source">Sequence whose items become the value.</param>
        /// <param name="cancellationToken">Token that stops consuming the source.</param>
        public OnityReadOnlyAsyncReactiveProperty(
            T initialValue,
            IOnityAsyncEnumerable<T> source,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            m_core = new OnityAsyncReactiveCore<T>(this);
            m_latestValue = initialValue;
            ConsumeAsync(source, cancellationToken).Forget();
        }

        /// <summary>
        /// Initializes the property with the default value and starts consuming the source.
        /// </summary>
        /// <param name="source">Sequence whose items become the value.</param>
        /// <param name="cancellationToken">Token that stops consuming the source.</param>
        public OnityReadOnlyAsyncReactiveProperty(
            IOnityAsyncEnumerable<T> source,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            m_core = new OnityAsyncReactiveCore<T>(this);
            ConsumeAsync(source, cancellationToken).Forget();
        }

        /// <summary>Gets the latest value.</summary>
        public T Value => m_latestValue;

        T IOnityAsyncReactiveValueReader<T>.ReadLatestValue() => m_latestValue;

        private async OnityTask ConsumeAsync(IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = source.GetAsyncEnumerator(cancellationToken);
            m_enumerator = enumerator;
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    m_core.Publish(enumerator.Current, ref m_latestValue);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation ends the pump without being reported as a fault.
            }
            finally
            {
                await enumerator.DisposeAsync();
                m_enumerator = null;
            }
        }

        /// <summary>
        /// Creates an enumeration that yields only values published after the enumerator was created.
        /// </summary>
        /// <returns>An enumerable that does not start with the current value.</returns>
        public IOnityAsyncEnumerable<T> WithoutCurrent() => m_core.WithoutCurrent;

        /// <summary>
        /// Creates an enumerator whose first move yields the current value, followed by later values.
        /// </summary>
        /// <param name="cancellationToken">Token that terminates the enumerator; a pending move is canceled.</param>
        /// <returns>A caller-owned enumerator.</returns>
        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return m_core.CreateEnumerator(cancellationToken, true);
        }

        /// <summary>
        /// Waits for the next published value.
        /// </summary>
        /// <param name="cancellationToken">Token that cancels the wait.</param>
        /// <returns>A single-consumption task that yields the next published value.</returns>
        public OnityTask<T> WaitAsync(CancellationToken cancellationToken = default)
        {
            return m_core.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Stops consuming the source, completes the enumerators registered now and cancels the
        /// pending <see cref="WaitAsync"/> tasks.
        /// </summary>
        public void Dispose()
        {
            IOnityAsyncEnumerator<T> enumerator = m_enumerator;
            if (enumerator != null)
            {
                enumerator.DisposeAsync().Forget();
            }

            m_core.Complete();
        }

        /// <summary>
        /// Reads the latest value.
        /// </summary>
        /// <param name="value">Property to read.</param>
        /// <returns>The latest value.</returns>
        public static implicit operator T(OnityReadOnlyAsyncReactiveProperty<T> value)
        {
            return value.Value;
        }

        /// <summary>
        /// Returns the string form of the latest value.
        /// </summary>
        /// <returns>The value's string form, or null when a reference value is null.</returns>
        public override string ToString()
        {
            if (s_isValueType)
            {
                return m_latestValue.ToString();
            }

            return m_latestValue?.ToString();
        }
    }

    /// <summary>
    /// Conversions from an asynchronous sequence to a read-only async reactive property.
    /// </summary>
    public static class OnityAsyncReactivePropertyExtensions
    {
        /// <summary>
        /// Converts a sequence into a read-only property whose value starts as the default value.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Sequence whose items become the value.</param>
        /// <param name="cancellationToken">Token that stops consuming the source.</param>
        /// <returns>A property that is driven by the source until it ends or is disposed.</returns>
        public static OnityReadOnlyAsyncReactiveProperty<T> ToReadOnlyAsyncReactiveProperty<T>(
            this IOnityAsyncEnumerable<T> source,
            CancellationToken cancellationToken)
        {
            return new OnityReadOnlyAsyncReactiveProperty<T>(source, cancellationToken);
        }

        /// <summary>
        /// Converts a sequence into a read-only property with an initial value.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Sequence whose items become the value.</param>
        /// <param name="initialValue">Value reported until the source yields its first item.</param>
        /// <param name="cancellationToken">Token that stops consuming the source.</param>
        /// <returns>A property that is driven by the source until it ends or is disposed.</returns>
        public static OnityReadOnlyAsyncReactiveProperty<T> ToReadOnlyAsyncReactiveProperty<T>(
            this IOnityAsyncEnumerable<T> source,
            T initialValue,
            CancellationToken cancellationToken)
        {
            return new OnityReadOnlyAsyncReactiveProperty<T>(initialValue, source, cancellationToken);
        }
    }

    // Shared engine of both property types. One synchronized list holds the one-shot waiters and the
    // long-lived enumerators in registration order. A publication snapshots the list into a pooled
    // array under the gate, drains the waiters from it, and delivers outside the gate, so
    // continuations that run inline may subscribe or unsubscribe. They may not publish: a nested
    // publication would hand the remaining entries of the outer pass the older value after the newer
    // one, so it is rejected up front (m_isPublishing), as UniTask does.
    internal sealed class OnityAsyncReactiveCore<T>
    {
        private const int k_waiterPoolCapacity = 256;

        private static readonly Action<object> s_cancelWaiter = state => ((Waiter)state).OnCancel();
        private static readonly Action<object> s_cancelEnumerator = state => ((Enumerator)state).OnCancel();
        private static OnityRunnerPool<Waiter> s_waiterPool;

        private readonly object m_gate = new object();
        private readonly IOnityAsyncReactiveValueReader<T> m_reader;
        private Entry[] m_entries = Array.Empty<Entry>();
        private int m_count;
        private bool m_isPublishing;
        private WithoutCurrentEnumerable m_withoutCurrent;

        internal OnityAsyncReactiveCore(IOnityAsyncReactiveValueReader<T> reader)
        {
            m_reader = reader;
        }

        internal IOnityAsyncEnumerable<T> WithoutCurrent =>
            m_withoutCurrent ?? (m_withoutCurrent = new WithoutCurrentEnumerable(this));

        internal OnityTask<T> WaitAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityAutoResetTaskCompletionSource<T>.CreateFromCanceled(cancellationToken, out _).Task;
            }

            return Waiter.Start(this, cancellationToken);
        }

        internal IOnityAsyncEnumerator<T> CreateEnumerator(CancellationToken cancellationToken, bool publishCurrent)
        {
            Enumerator enumerator = new Enumerator(this, cancellationToken, publishCurrent);
            Add(enumerator, 0);
            enumerator.Initialize();
            return enumerator;
        }

        // Stores the value into the owner's field and delivers it to the entries registered now. The
        // check comes first, so a rejected nested publication changes neither the stored value nor the
        // running pass.
        internal void Publish(T value, ref T latest)
        {
            Entry[] buffer;
            int count;
            lock (m_gate)
            {
                if (m_isPublishing)
                {
                    throw new InvalidOperationException(
                        "The property is already publishing a value. Set it after that publication has returned.");
                }

                latest = value;
                count = m_count;
                if (count == 0)
                {
                    return;
                }

                buffer = ArrayPool<Entry>.Shared.Rent(count);
                Array.Copy(m_entries, buffer, count);
                int kept = 0;
                for (int i = 0; i < count; i++)
                {
                    if (m_entries[i].Generation == 0)
                    {
                        m_entries[kept++] = m_entries[i];
                    }
                }

                Array.Clear(m_entries, kept, count - kept);
                m_count = kept;
                m_isPublishing = true;
            }

            try
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        buffer[i].Handler.Deliver(value, buffer[i].Generation);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                Array.Clear(buffer, 0, count);
                ArrayPool<Entry>.Shared.Return(buffer);
                lock (m_gate)
                {
                    m_isPublishing = false;
                }
            }
        }

        internal void Complete()
        {
            Entry[] buffer;
            int count;
            lock (m_gate)
            {
                count = m_count;
                if (count == 0)
                {
                    return;
                }

                buffer = ArrayPool<Entry>.Shared.Rent(count);
                Array.Copy(m_entries, buffer, count);
                Array.Clear(m_entries, 0, count);
                m_count = 0;
            }

            try
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        buffer[i].Handler.Complete(buffer[i].Generation);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                Array.Clear(buffer, 0, count);
                ArrayPool<Entry>.Shared.Return(buffer);
            }
        }

        private void Add(Handler handler, long generation)
        {
            lock (m_gate)
            {
                if (m_count == m_entries.Length)
                {
                    Array.Resize(ref m_entries, m_count == 0 ? 4 : m_count * 2);
                }

                m_entries[m_count++] = new Entry { Handler = handler, Generation = generation };
            }
        }

        private void Remove(Handler handler, long generation)
        {
            lock (m_gate)
            {
                for (int i = 0; i < m_count; i++)
                {
                    if (ReferenceEquals(m_entries[i].Handler, handler) && m_entries[i].Generation == generation)
                    {
                        m_count--;
                        Array.Copy(m_entries, i + 1, m_entries, i, m_count - i);
                        m_entries[m_count] = default;
                        return;
                    }
                }
            }
        }

        private static CancellationTokenRegistration RegisterToken(
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

        // Generation 0 marks a long-lived handler; a nonzero generation marks a one-shot waiter cycle.
        private struct Entry
        {
            internal Handler Handler;
            internal long Generation;
        }

        private abstract class Handler
        {
            internal abstract void Deliver(T value, long generation);

            internal abstract void Complete(long generation);
        }

        // Pooled one-shot waiter. m_state holds (cycle << 1) | claimed. Exactly one party wins the claim
        // for a cycle (publication, property completion or cancellation) and then releases and
        // recycles the instance; every party that lost sees a claimed or newer state and does nothing.
        private sealed class Waiter : Handler, IOnityPooledRunner<Waiter>
        {
            private OnityAsyncReactiveCore<T> m_parent;
            private OnityAutoResetTaskCompletionSource<T> m_source;
            private CancellationToken m_token;
            private CancellationTokenRegistration m_registration;
            private Waiter m_nextPooled;
            private long m_state;

            ref Waiter IOnityPooledRunner<Waiter>.NextPooled => ref m_nextPooled;

            internal static OnityTask<T> Start(OnityAsyncReactiveCore<T> parent, CancellationToken token)
            {
                if (!s_waiterPool.TryPop(out Waiter waiter))
                {
                    waiter = new Waiter();
                }

                OnityAutoResetTaskCompletionSource<T> source = OnityAutoResetTaskCompletionSource<T>.Create();
                OnityTask<T> task = source.Task;
                long generation = waiter.Begin(parent, source, token);
                if (!token.CanBeCanceled)
                {
                    parent.Add(waiter, generation);
                    return task;
                }

                // The registration may run inline when the token is canceled right now. That path
                // claims and recycles the waiter before it was added, so the add below is conditional.
                CancellationTokenRegistration registration = RegisterToken(token, s_cancelWaiter, waiter);
                bool discard = false;
                lock (waiter)
                {
                    if (Volatile.Read(ref waiter.m_state) == (generation << 1))
                    {
                        waiter.m_registration = registration;
                        parent.Add(waiter, generation);
                    }
                    else
                    {
                        discard = true;
                    }
                }

                if (discard)
                {
                    registration.Dispose();
                }

                return task;
            }

            internal override void Deliver(T value, long generation)
            {
                if (Claim(generation))
                {
                    Release(false, generation).TrySetResult(value);
                }
            }

            internal override void Complete(long generation)
            {
                if (Claim(generation))
                {
                    // Completing the property cancels waiters, like UniTask.
                    Release(false, generation).TrySetCanceled(CancellationToken.None);
                }
            }

            internal void OnCancel()
            {
                long state = Volatile.Read(ref m_state);
                if ((state & 1) != 0)
                {
                    return;
                }

                // The registration is disposed before the instance is recycled, so while this callback
                // is running the instance still belongs to the cycle that registered it.
                CancellationToken token = m_token;
                long generation = state >> 1;
                if (Claim(generation))
                {
                    Release(true, generation).TrySetCanceled(token);
                }
            }

            private long Begin(
                OnityAsyncReactiveCore<T> parent,
                OnityAutoResetTaskCompletionSource<T> source,
                CancellationToken token)
            {
                long generation = (Volatile.Read(ref m_state) >> 1) + 1;
                m_parent = parent;
                m_source = source;
                m_token = token;
                Volatile.Write(ref m_state, generation << 1);
                return generation;
            }

            private bool Claim(long generation)
            {
                long expected = generation << 1;
                return Interlocked.CompareExchange(ref m_state, expected | 1, expected) == expected;
            }

            private OnityAutoResetTaskCompletionSource<T> Release(bool removeFromParent, long generation)
            {
                OnityAutoResetTaskCompletionSource<T> source = m_source;
                OnityAsyncReactiveCore<T> parent = m_parent;
                CancellationTokenRegistration registration;
                lock (this)
                {
                    registration = m_registration;
                    m_registration = default;
                }

                m_source = null;
                m_parent = null;
                m_token = default;
                if (removeFromParent)
                {
                    parent.Remove(this, generation);
                }

                // Disposing waits for a callback that is still running on another thread.
                registration.Dispose();
                s_waiterPool.TryPush(this, k_waiterPoolCapacity);
                return source;
            }
        }

        private sealed class WithoutCurrentEnumerable : IOnityAsyncEnumerable<T>
        {
            private readonly OnityAsyncReactiveCore<T> m_parent;

            internal WithoutCurrentEnumerable(OnityAsyncReactiveCore<T> parent)
            {
                m_parent = parent;
            }

            public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return m_parent.CreateEnumerator(cancellationToken, false);
            }
        }

        // One outstanding move. A value published while no move is pending is not queued, which is
        // the UniTask behavior; the next move waits for the following publication.
        private sealed class Enumerator : Handler, IOnityAsyncEnumerator<T>
        {
            private readonly OnityAsyncReactiveCore<T> m_parent;
            private readonly CancellationToken m_token;
            private CancellationTokenRegistration m_registration;
            private OnityAutoResetTaskCompletionSource<bool> m_pending;
            private T m_current;
            private bool m_currentValid;
            private bool m_firstCall;
            private bool m_closed;
            private bool m_canceled;

            internal Enumerator(OnityAsyncReactiveCore<T> parent, CancellationToken token, bool publishCurrent)
            {
                m_parent = parent;
                m_token = token;
                m_firstCall = publishCurrent;
            }

            public T Current
            {
                get
                {
                    lock (this)
                    {
                        if (!m_currentValid)
                        {
                            throw new InvalidOperationException("The asynchronous enumerator has no current item.");
                        }

                        return m_current;
                    }
                }
            }

            internal void Initialize()
            {
                if (!m_token.CanBeCanceled)
                {
                    return;
                }

                CancellationTokenRegistration registration = RegisterToken(m_token, s_cancelEnumerator, this);
                bool discard = false;
                lock (this)
                {
                    if (m_closed)
                    {
                        discard = true;
                    }
                    else
                    {
                        m_registration = registration;
                    }
                }

                if (discard)
                {
                    registration.Dispose();
                }
            }

            public OnityTask<bool> MoveNextAsync()
            {
                lock (this)
                {
                    if (m_pending != null)
                    {
                        throw new InvalidOperationException("An asynchronous move is already outstanding.");
                    }

                    m_current = default;
                    m_currentValid = false;
                    if (m_closed)
                    {
                        return m_canceled
                            ? OnityTask<bool>.FromCanceled(m_token)
                            : OnityTask<bool>.FromResult(false);
                    }

                    if (m_firstCall)
                    {
                        m_firstCall = false;
                        m_current = m_parent.m_reader.ReadLatestValue();
                        m_currentValid = true;
                        return OnityTask<bool>.FromResult(true);
                    }

                    OnityAutoResetTaskCompletionSource<bool> pending =
                        OnityAutoResetTaskCompletionSource<bool>.Create();
                    m_pending = pending;
                    return pending.Task;
                }
            }

            public OnityTask DisposeAsync()
            {
                OnityAutoResetTaskCompletionSource<bool> pending;
                CancellationTokenRegistration registration;
                lock (this)
                {
                    if (m_closed)
                    {
                        return OnityTask.Completed;
                    }

                    pending = Close(out registration);
                }

                m_parent.Remove(this, 0);
                registration.Dispose();
                pending?.TrySetResult(false);
                return OnityTask.Completed;
            }

            internal void OnCancel()
            {
                OnityAutoResetTaskCompletionSource<bool> pending;
                CancellationTokenRegistration registration;
                lock (this)
                {
                    if (m_closed)
                    {
                        return;
                    }

                    m_canceled = true;
                    pending = Close(out registration);
                }

                m_parent.Remove(this, 0);
                registration.Dispose();
                pending?.TrySetCanceled(m_token);
            }

            internal override void Deliver(T value, long generation)
            {
                OnityAutoResetTaskCompletionSource<bool> pending;
                lock (this)
                {
                    if (m_closed || m_pending == null)
                    {
                        return;
                    }

                    pending = m_pending;
                    m_pending = null;
                    m_current = value;
                    m_currentValid = true;
                }

                pending.TrySetResult(true);
            }

            internal override void Complete(long generation)
            {
                OnityAutoResetTaskCompletionSource<bool> pending;
                CancellationTokenRegistration registration;
                lock (this)
                {
                    if (m_closed)
                    {
                        return;
                    }

                    pending = Close(out registration);
                }

                registration.Dispose();
                pending?.TrySetResult(false);
            }

            // Called under the lock; the caller completes the returned move outside it.
            private OnityAutoResetTaskCompletionSource<bool> Close(out CancellationTokenRegistration registration)
            {
                m_closed = true;
                m_current = default;
                m_currentValid = false;
                registration = m_registration;
                m_registration = default;
                OnityAutoResetTaskCompletionSource<bool> pending = m_pending;
                m_pending = null;
                return pending;
            }
        }
    }
}
