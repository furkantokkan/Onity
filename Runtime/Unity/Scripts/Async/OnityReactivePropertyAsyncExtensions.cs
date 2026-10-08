using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Onity.Reactive;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Native async bridges between <see cref="IReadOnlyReactiveProperty{T}" /> and
    /// <see cref="OnityTask" />, <see cref="IOnityAsyncEnumerable{T}" /> and
    /// <see cref="OnityAsyncReactiveProperty{T}" />.
    /// </summary>
    /// <remarks>
    /// Use these on the property's thread (Unity's main thread for Onity's reactive types); a wait's
    /// cancellation token may be canceled from any thread. Continuations run inline on the thread that
    /// sets the property or cancels the token. Onity's <see cref="ReactiveProperty{T}" /> does not
    /// notify observers when it is disposed, so a pending wait or enumeration is not completed by
    /// disposing the property: pass the scope token (an injected <c>IOnityScopeLifetime</c> or
    /// <c>GetScopeCancellationToken()</c>) so the wait ends with its scope. A wait started on an
    /// already disposed property is canceled.
    /// </remarks>
    public static class OnityReactivePropertyAsyncExtensions
    {
        /// <summary>
        /// Waits for the next accepted change of the property. The current value does not complete
        /// the wait, and a set that the property skips (an equal value) is not a change.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="property">Property to observe.</param>
        /// <param name="cancellationToken">Token that cancels the wait.</param>
        /// <returns>A single-consumption task that yields the next published value.</returns>
        /// <remarks>
        /// Waiters on one property share one subscription and are pooled. A wait that starts while
        /// the property is publishing (for example from a continuation that the publication resumed)
        /// waits for the following change, so <c>while (...) value = await property.WaitAsync();</c>
        /// observes every change once.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="property" /> is null.</exception>
        public static OnityTask<T> WaitAsync<T>(
            this IReadOnlyReactiveProperty<T> property,
            CancellationToken cancellationToken = default)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            return OnityPropertyWaitHub<T>.Get(property).Wait(null, cancellationToken);
        }

        /// <summary>
        /// Waits until the property holds a value that matches <paramref name="predicate" />. Completes
        /// synchronously when the current value already matches.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="property">Property to observe.</param>
        /// <param name="predicate">Condition evaluated for the current value, then for each change.</param>
        /// <param name="cancellationToken">Token that cancels the wait; a pre-canceled token wins over a
        /// matching current value.</param>
        /// <returns>A single-consumption task that yields the first matching value. An exception thrown
        /// by the predicate faults the task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="property" /> or
        /// <paramref name="predicate" /> is null.</exception>
        public static OnityTask<T> WaitUntilAsync<T>(
            this IReadOnlyReactiveProperty<T> property,
            Func<T, bool> predicate,
            CancellationToken cancellationToken = default)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityAutoResetTaskCompletionSource<T>.CreateFromCanceled(cancellationToken, out _).Task;
            }

            T current = property.Value;
            bool matches;

            try
            {
                matches = predicate(current);
            }
            catch (Exception exception)
            {
                return OnityAutoResetTaskCompletionSource<T>.CreateFromException(exception, out _).Task;
            }

            if (matches)
            {
                return OnityTask.FromResult(current);
            }

            return OnityPropertyWaitHub<T>.Get(property).Wait(predicate, cancellationToken);
        }

        /// <summary>
        /// Exposes the property as a conflating asynchronous stream: a consumer that is slower than the
        /// changes receives the latest value, never a backlog and never an overflow fault.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="property">Property to observe.</param>
        /// <param name="includeCurrent">When true (default), the first move yields the value current at
        /// that move; otherwise the stream starts with the next change.</param>
        /// <returns>A reusable description; each enumerator subscribes at its first move and
        /// unsubscribes on disposal.</returns>
        /// <remarks>
        /// Each move yields the latest value published since the previous move, or waits for the next
        /// change. The enumeration never ends on its own; its token cancels it, and disposing the
        /// enumerator ends a pending move with false. Enumerating a disposed property yields the
        /// current value (when included) and then ends.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="property" /> is null.</exception>
        public static IOnityAsyncEnumerable<T> AsLatestAsyncEnumerable<T>(
            this IReadOnlyReactiveProperty<T> property,
            bool includeCurrent = true)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            return new LatestEnumerable<T>(property, includeCurrent);
        }

        /// <summary>
        /// Creates an <see cref="OnityReadOnlyAsyncReactiveProperty{T}" /> that starts with the
        /// property's current value and follows its changes until <paramref name="cancellationToken" />
        /// is canceled or the async property is disposed.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="property">Property to follow.</param>
        /// <param name="cancellationToken">Token that stops following the property.</param>
        /// <returns>The async property; dispose it to stop following and unsubscribe.</returns>
        /// <remarks>Changes are pumped through <see cref="AsLatestAsyncEnumerable{T}" />, so a change
        /// made while the async property is still publishing is delivered after that publication.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="property" /> is null.</exception>
        public static OnityReadOnlyAsyncReactiveProperty<T> ToAsyncReactiveProperty<T>(
            this IReadOnlyReactiveProperty<T> property,
            CancellationToken cancellationToken)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            return new OnityReadOnlyAsyncReactiveProperty<T>(
                property.Value,
                property.AsLatestAsyncEnumerable(false),
                cancellationToken);
        }

        /// <summary>
        /// Writes the async property's current value and every later value into
        /// <paramref name="target" /> until the returned binding is disposed, the token is canceled,
        /// or the target is disposed.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Async property to read.</param>
        /// <param name="target">Reactive property to write; its equality check still applies.</param>
        /// <param name="cancellationToken">Token that ends the binding.</param>
        /// <returns>The binding; dispose it to stop writing.</returns>
        /// <remarks>Main thread only. The current value is written before this method returns. A value
        /// published while the target's subscribers are still handling the previous one follows the
        /// source's enumeration rules.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> or
        /// <paramref name="target" /> is null.</exception>
        public static IDisposable BindTo<T>(
            this IOnityReadOnlyAsyncReactiveProperty<T> source,
            ReactiveProperty<T> target,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            ReactivePropertyBinding<T> binding = new ReactivePropertyBinding<T>(target);
            binding.Start(source, cancellationToken);
            return binding;
        }

        private sealed class LatestEnumerable<T> : IOnityAsyncEnumerable<T>
        {
            private readonly IReadOnlyReactiveProperty<T> m_property;
            private readonly bool m_includeCurrent;

            public LatestEnumerable(IReadOnlyReactiveProperty<T> property, bool includeCurrent)
            {
                m_property = property;
                m_includeCurrent = includeCurrent;
            }

            public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new LatestEnumerator<T>(m_property, m_includeCurrent, cancellationToken);
            }
        }

        // Holds at most one undelivered value (the latest). Deliveries and moves run on the property's
        // thread; the token may cancel from any thread, so the state is guarded by m_gate.
        private sealed class LatestEnumerator<T> : IOnityAsyncEnumerator<T>
        {
            private static readonly Action<object> s_onCanceled = state => ((LatestEnumerator<T>)state).OnCanceled();

            private readonly object m_gate = new object();
            private readonly IReadOnlyReactiveProperty<T> m_property;
            private readonly bool m_includeCurrent;
            private readonly CancellationToken m_token;
            private readonly Observer<T> m_onNext;
            private IDisposable m_subscription;
            private int m_subscriptionThreadId;
            private CancellationTokenRegistration m_registration;
            private OnityAutoResetTaskCompletionSource<bool> m_pendingMove;
            private T m_latest;
            private T m_current;
            private bool m_hasLatest;
            private bool m_hasCurrent;
            private bool m_isStarted;
            private bool m_isSourceEnded;
            private bool m_isCanceled;
            private bool m_isDisposed;

            public LatestEnumerator(IReadOnlyReactiveProperty<T> property, bool includeCurrent, CancellationToken token)
            {
                m_property = property;
                m_includeCurrent = includeCurrent;
                m_token = token;
                m_onNext = OnNext;
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

                lock (m_gate)
                {
                    if (m_isCanceled || m_token.IsCancellationRequested)
                    {
                        m_isCanceled = true;
                        return OnityAutoResetTaskCompletionSource<bool>.CreateFromCanceled(m_token, out _).Task;
                    }

                    if (m_hasLatest)
                    {
                        m_current = m_latest;
                        m_latest = default;
                        m_hasLatest = false;
                        m_hasCurrent = true;
                        return OnityTask.FromResult(true);
                    }

                    if (m_isSourceEnded || m_isDisposed)
                    {
                        return OnityTask.FromResult(false);
                    }

                    m_pendingMove = OnityAutoResetTaskCompletionSource<bool>.Create();
                    return m_pendingMove.Task;
                }
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
                    m_latest = default;
                    m_hasLatest = false;
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

                if (m_includeCurrent)
                {
                    m_latest = m_property.Value;
                    m_hasLatest = true;
                }

                IDisposable subscription = null;

                try
                {
                    subscription = m_property.Subscribe(m_onNext, false);
                }
                catch (ObjectDisposedException)
                {
                    // A disposed property publishes nothing more: the stream ends after the current value.
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

            private void OnNext(T value)
            {
                OnityAutoResetTaskCompletionSource<bool> move;

                lock (m_gate)
                {
                    if (m_isDisposed || m_isCanceled)
                    {
                        return;
                    }

                    move = m_pendingMove;

                    if (move == null)
                    {
                        // Conflate: only the latest undelivered value is kept.
                        m_latest = value;
                        m_hasLatest = true;
                        return;
                    }

                    m_pendingMove = null;
                    m_current = value;
                    m_hasCurrent = true;
                }

                move.TrySetResult(true);
            }

            private void OnCanceled()
            {
                OnityAutoResetTaskCompletionSource<bool> move;
                IDisposable subscription = null;

                lock (m_gate)
                {
                    if (m_isDisposed || m_isCanceled)
                    {
                        return;
                    }

                    m_isCanceled = true;
                    m_latest = default;
                    m_hasLatest = false;
                    move = m_pendingMove;
                    m_pendingMove = null;

                    // Unsubscribe now on the property's thread; otherwise DisposeAsync does it.
                    if (Environment.CurrentManagedThreadId == m_subscriptionThreadId)
                    {
                        subscription = m_subscription;
                        m_subscription = null;
                    }
                }

                subscription?.Dispose();
                move?.TrySetCanceled(m_token);
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
        }

        // Pumps an async property into a ReactiveProperty. Owned by the returned binding: it stops when
        // the binding is disposed (the enumerator ends), the token is canceled, or the target is disposed.
        private sealed class ReactivePropertyBinding<T> : IDisposable
        {
            private readonly ReactiveProperty<T> m_target;
            private IOnityAsyncEnumerator<T> m_enumerator;
            private int m_isDisposed;

            public ReactivePropertyBinding(ReactiveProperty<T> target)
            {
                m_target = target;
            }

            public void Start(IOnityReadOnlyAsyncReactiveProperty<T> source, CancellationToken cancellationToken)
            {
                PumpAsync(source, cancellationToken).Forget();
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_isDisposed, 1) != 0)
                {
                    return;
                }

                m_enumerator?.DisposeAsync().Forget();
            }

            private async OnityTaskVoid PumpAsync(
                IOnityReadOnlyAsyncReactiveProperty<T> source,
                CancellationToken cancellationToken)
            {
                IOnityAsyncEnumerator<T> enumerator = source.GetAsyncEnumerator(cancellationToken);
                m_enumerator = enumerator;

                try
                {
                    while (Volatile.Read(ref m_isDisposed) == 0 && await enumerator.MoveNextAsync())
                    {
                        m_target.Value = enumerator.Current;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // The target was disposed: the binding ends with it.
                }
                finally
                {
                    m_enumerator = null;
                    await enumerator.DisposeAsync();
                }
            }
        }
    }

    // Shares one subscription per reactive property between its pending waits (see OnityWaitHub).
    internal sealed class OnityPropertyWaitHub<T> : OnityWaitHub<T>
    {
        private static readonly ConditionalWeakTable<IReadOnlyReactiveProperty<T>, OnityPropertyWaitHub<T>> s_hubs =
            new ConditionalWeakTable<IReadOnlyReactiveProperty<T>, OnityPropertyWaitHub<T>>();

        private static readonly ConditionalWeakTable<IReadOnlyReactiveProperty<T>, OnityPropertyWaitHub<T>>.CreateValueCallback
            s_create = property => new OnityPropertyWaitHub<T>(property);

        private readonly IReadOnlyReactiveProperty<T> m_property;
        private readonly Observer<T> m_onNext;

        private OnityPropertyWaitHub(IReadOnlyReactiveProperty<T> property)
        {
            m_property = property;
            m_onNext = OnNext;
        }

        internal static OnityPropertyWaitHub<T> Get(IReadOnlyReactiveProperty<T> property)
        {
            return s_hubs.GetValue(property, s_create);
        }

        protected override IDisposable SubscribeSource()
        {
            return m_property.Subscribe(m_onNext, false);
        }
    }
}
