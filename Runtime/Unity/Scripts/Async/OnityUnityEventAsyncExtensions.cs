using System;
using System.Threading;
using Onity.Core;
using UnityEngine.Events;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Waits for a <see cref="UnityEvent"/> invocation (UniTask <c>AsyncUnityEventHandler</c> parity). The
    /// handler listens from construction on, until it is disposed, its token is canceled, or, for a
    /// one-shot handler, its first invocation. Each <see cref="OnInvokeAsync"/> starts a new wait for the
    /// next invocation; invocations while no wait is pending are skipped.
    /// </summary>
    /// <remarks>
    /// Thread affinity: main thread only, including canceling the token. A wait started after the handler
    /// stopped listening completes as canceled. Continuations run inline inside the event invocation.
    /// Starting a new wait while one is pending abandons the older one, as in UniTask. Deviations from
    /// UniTask 2.5.11: a one-shot handler unregisters at its first invocation (UniTask waits until the
    /// result is read), and disposing the handler cancels its pending wait with the handler's token.
    /// </remarks>
    public sealed class OnityAsyncUnityEventHandler : IOnityAsyncClickEventHandler
    {
        private readonly OnityUnityEventListener m_listener;

        /// <summary>Starts listening to <paramref name="unityEvent"/>. Main thread only.</summary>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Stops listening and cancels the pending wait when canceled.</param>
        /// <param name="callOnce">True to stop listening at the first invocation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public OnityAsyncUnityEventHandler(UnityEvent unityEvent, CancellationToken cancellationToken, bool callOnce)
        {
            if (unityEvent == null)
            {
                throw new ArgumentNullException(nameof(unityEvent));
            }

            m_listener = new OnityUnityEventListener(unityEvent, cancellationToken, callOnce);
        }

        /// <summary>Waits for the next invocation. Main thread only.</summary>
        /// <returns>A single-consumption task; canceled when the handler stops listening.</returns>
        public OnityTask OnInvokeAsync()
        {
            return m_listener.WaitAsync();
        }

        /// <summary>
        /// Stops listening. A wait pending at this moment completes as canceled. Idempotent. Main thread only.
        /// </summary>
        public void Dispose()
        {
            m_listener.Dispose();
        }

        OnityTask IOnityAsyncClickEventHandler.OnClickAsync()
        {
            return OnInvokeAsync();
        }
    }

    /// <summary>
    /// Waits for a <see cref="UnityEvent{T0}"/> invocation (UniTask <c>AsyncUnityEventHandler&lt;T&gt;</c>
    /// parity); see <see cref="OnityAsyncUnityEventHandler"/> for the lifetime rules. The task completes
    /// with the first argument of the invocation.
    /// </summary>
    /// <typeparam name="T">Event argument type.</typeparam>
    public sealed class OnityAsyncUnityEventHandler<T> :
        IOnityAsyncValueChangedEventHandler<T>,
        IOnityAsyncEndEditEventHandler<T>,
        IOnityAsyncEndTextSelectionEventHandler<T>,
        IOnityAsyncTextSelectionEventHandler<T>,
        IOnityAsyncDeselectEventHandler<T>,
        IOnityAsyncSelectEventHandler<T>,
        IOnityAsyncSubmitEventHandler<T>
    {
        private readonly OnityUnityEventListener<T> m_listener;

        /// <summary>Starts listening to <paramref name="unityEvent"/>. Main thread only.</summary>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Stops listening and cancels the pending wait when canceled.</param>
        /// <param name="callOnce">True to stop listening at the first invocation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public OnityAsyncUnityEventHandler(UnityEvent<T> unityEvent, CancellationToken cancellationToken, bool callOnce)
        {
            if (unityEvent == null)
            {
                throw new ArgumentNullException(nameof(unityEvent));
            }

            m_listener = new OnityUnityEventListener<T>(unityEvent, cancellationToken, callOnce);
        }

        /// <summary>Waits for the next invocation. Main thread only.</summary>
        /// <returns>A single-consumption task completed with the event argument; canceled when the handler stops listening.</returns>
        public OnityTask<T> OnInvokeAsync()
        {
            return m_listener.WaitValueAsync();
        }

        /// <summary>
        /// Stops listening. A wait pending at this moment completes as canceled. Idempotent. Main thread only.
        /// </summary>
        public void Dispose()
        {
            m_listener.Dispose();
        }

        OnityTask<T> IOnityAsyncValueChangedEventHandler<T>.OnValueChangedAsync()
        {
            return OnInvokeAsync();
        }

        OnityTask<T> IOnityAsyncEndEditEventHandler<T>.OnEndEditAsync()
        {
            return OnInvokeAsync();
        }

        OnityTask<T> IOnityAsyncEndTextSelectionEventHandler<T>.OnEndTextSelectionAsync()
        {
            return OnInvokeAsync();
        }

        OnityTask<T> IOnityAsyncTextSelectionEventHandler<T>.OnTextSelectionAsync()
        {
            return OnInvokeAsync();
        }

        OnityTask<T> IOnityAsyncDeselectEventHandler<T>.OnDeselectAsync()
        {
            return OnInvokeAsync();
        }

        OnityTask<T> IOnityAsyncSelectEventHandler<T>.OnSelectAsync()
        {
            return OnInvokeAsync();
        }

        OnityTask<T> IOnityAsyncSubmitEventHandler<T>.OnSubmitAsync()
        {
            return OnInvokeAsync();
        }
    }

    /// <summary>
    /// A sequence of <see cref="UnityEvent"/> invocations (UniTask <c>UnityEventHandlerAsyncEnumerable</c>
    /// parity). Every enumerator listens from its first move on and yields one item per invocation that
    /// arrives while a move is pending; invocations in between are skipped.
    /// </summary>
    /// <remarks>
    /// Thread affinity: main thread only. Two tokens end an enumeration by canceling its pending move:
    /// the one given at construction and the one given to <see cref="GetAsyncEnumerator"/>. Disposing an
    /// enumerator ends its pending move with <c>false</c>.
    /// </remarks>
    public sealed class OnityUnityEventAsyncEnumerable : IOnityAsyncEnumerable<Unit>
    {
        private readonly UnityEvent m_unityEvent;
        private readonly CancellationToken m_cancellationToken;

        /// <summary>Creates the sequence. Nothing is registered until an enumerator moves.</summary>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Ends every enumerator of this sequence when canceled.</param>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public OnityUnityEventAsyncEnumerable(UnityEvent unityEvent, CancellationToken cancellationToken)
        {
            if (unityEvent == null)
            {
                throw new ArgumentNullException(nameof(unityEvent));
            }

            m_unityEvent = unityEvent;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>Creates an independent enumerator. Main thread only.</summary>
        /// <param name="cancellationToken">Ends this enumerator when canceled.</param>
        /// <returns>A caller-owned enumerator; dispose it to stop listening.</returns>
        public IOnityAsyncEnumerator<Unit> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new OnityUnityEventEnumerator(m_unityEvent, m_cancellationToken, cancellationToken);
        }
    }

    /// <summary>
    /// A sequence of <see cref="UnityEvent{T0}"/> invocations (UniTask
    /// <c>UnityEventHandlerAsyncEnumerable&lt;T&gt;</c> parity); see
    /// <see cref="OnityUnityEventAsyncEnumerable"/> for the rules. Each item is the invocation argument.
    /// </summary>
    /// <typeparam name="T">Event argument type.</typeparam>
    public sealed class OnityUnityEventAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly UnityEvent<T> m_unityEvent;
        private readonly CancellationToken m_cancellationToken;

        /// <summary>Creates the sequence. Nothing is registered until an enumerator moves.</summary>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Ends every enumerator of this sequence when canceled.</param>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public OnityUnityEventAsyncEnumerable(UnityEvent<T> unityEvent, CancellationToken cancellationToken)
        {
            if (unityEvent == null)
            {
                throw new ArgumentNullException(nameof(unityEvent));
            }

            m_unityEvent = unityEvent;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>Creates an independent enumerator. Main thread only.</summary>
        /// <param name="cancellationToken">Ends this enumerator when canceled.</param>
        /// <returns>A caller-owned enumerator; dispose it to stop listening.</returns>
        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new OnityUnityEventEnumerator<T>(m_unityEvent, m_cancellationToken, cancellationToken);
        }
    }

    /// <summary>
    /// Awaitable waits and async sequences over <see cref="UnityEvent"/> and <see cref="UnityEvent{T0}"/>
    /// (UniTask <c>UnityAsyncExtensions</c> parity). Every member requires Unity's main thread. The token
    /// is required: it is the only way to stop listening to an event that never fires again.
    /// </summary>
    public static class OnityUnityEventAsyncExtensions
    {
        /// <summary>Creates a reusable handler that stays registered until disposed or canceled.</summary>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Stops listening when canceled.</param>
        /// <returns>The handler; dispose it to unregister.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public static OnityAsyncUnityEventHandler GetAsyncEventHandler(
            this UnityEvent unityEvent, CancellationToken cancellationToken)
        {
            return new OnityAsyncUnityEventHandler(unityEvent, cancellationToken, false);
        }

        /// <summary>Waits for the next invocation; the listener is removed at that invocation.</summary>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Cancels the wait and removes the listener.</param>
        /// <returns>A single-consumption task; canceled by the token.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public static OnityTask OnInvokeAsync(this UnityEvent unityEvent, CancellationToken cancellationToken)
        {
            return new OnityAsyncUnityEventHandler(unityEvent, cancellationToken, true).OnInvokeAsync();
        }

        /// <summary>Exposes the invocations as an async sequence.</summary>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Ends every enumeration when canceled.</param>
        /// <returns>A sequence that listens while an enumerator moves.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public static IOnityAsyncEnumerable<Unit> OnInvokeAsAsyncEnumerable(
            this UnityEvent unityEvent, CancellationToken cancellationToken)
        {
            return new OnityUnityEventAsyncEnumerable(unityEvent, cancellationToken);
        }

        /// <summary>Creates a reusable handler that stays registered until disposed or canceled.</summary>
        /// <typeparam name="T">Event argument type.</typeparam>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Stops listening when canceled.</param>
        /// <returns>The handler; dispose it to unregister.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public static OnityAsyncUnityEventHandler<T> GetAsyncEventHandler<T>(
            this UnityEvent<T> unityEvent, CancellationToken cancellationToken)
        {
            return new OnityAsyncUnityEventHandler<T>(unityEvent, cancellationToken, false);
        }

        /// <summary>Waits for the next invocation; the listener is removed at that invocation.</summary>
        /// <typeparam name="T">Event argument type.</typeparam>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Cancels the wait and removes the listener.</param>
        /// <returns>A single-consumption task completed with the argument; canceled by the token.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public static OnityTask<T> OnInvokeAsync<T>(this UnityEvent<T> unityEvent, CancellationToken cancellationToken)
        {
            return new OnityAsyncUnityEventHandler<T>(unityEvent, cancellationToken, true).OnInvokeAsync();
        }

        /// <summary>Exposes the invocations as an async sequence of their argument.</summary>
        /// <typeparam name="T">Event argument type.</typeparam>
        /// <param name="unityEvent">Event to listen to.</param>
        /// <param name="cancellationToken">Ends every enumeration when canceled.</param>
        /// <returns>A sequence that listens while an enumerator moves.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="unityEvent"/> is null.</exception>
        public static IOnityAsyncEnumerable<T> OnInvokeAsAsyncEnumerable<T>(
            this UnityEvent<T> unityEvent, CancellationToken cancellationToken)
        {
            return new OnityUnityEventAsyncEnumerable<T>(unityEvent, cancellationToken);
        }
    }

    internal sealed class OnityUnityEventListener : OnityEventListener<Unit>
    {
        private readonly UnityEvent m_event;
        private readonly UnityAction m_action;

        internal OnityUnityEventListener(UnityEvent unityEvent, CancellationToken cancellationToken, bool callOnce)
            : base(cancellationToken, callOnce)
        {
            m_event = unityEvent;
            m_action = Invoke;
            Start();
        }

        protected override void AddListener()
        {
            m_event.AddListener(m_action);
        }

        protected override void RemoveListener()
        {
            m_event.RemoveListener(m_action);
        }

        private void Invoke()
        {
            Deliver(Unit.Default);
        }
    }

    internal sealed class OnityUnityEventListener<T> : OnityEventListener<T>
    {
        private readonly UnityEvent<T> m_event;
        private readonly UnityAction<T> m_action;

        internal OnityUnityEventListener(UnityEvent<T> unityEvent, CancellationToken cancellationToken, bool callOnce)
            : base(cancellationToken, callOnce)
        {
            m_event = unityEvent;
            m_action = Invoke;
            Start();
        }

        protected override void AddListener()
        {
            m_event.AddListener(m_action);
        }

        protected override void RemoveListener()
        {
            m_event.RemoveListener(m_action);
        }

        private void Invoke(T value)
        {
            Deliver(value);
        }
    }

    internal sealed class OnityUnityEventEnumerator : OnityEventListenerEnumerator<Unit>
    {
        private readonly UnityEvent m_event;
        private readonly UnityAction m_action;

        internal OnityUnityEventEnumerator(
            UnityEvent unityEvent, CancellationToken sourceToken, CancellationToken consumerToken)
            : base(sourceToken, consumerToken)
        {
            m_event = unityEvent;
            m_action = Invoke;
        }

        protected override void AddListener()
        {
            m_event.AddListener(m_action);
        }

        protected override void RemoveListener()
        {
            m_event.RemoveListener(m_action);
        }

        private void Invoke()
        {
            Deliver(Unit.Default);
        }
    }

    internal sealed class OnityUnityEventEnumerator<T> : OnityEventListenerEnumerator<T>
    {
        private readonly UnityEvent<T> m_event;
        private readonly UnityAction<T> m_action;

        internal OnityUnityEventEnumerator(
            UnityEvent<T> unityEvent, CancellationToken sourceToken, CancellationToken consumerToken)
            : base(sourceToken, consumerToken)
        {
            m_event = unityEvent;
            m_action = Invoke;
        }

        protected override void AddListener()
        {
            m_event.AddListener(m_action);
        }

        protected override void RemoveListener()
        {
            m_event.RemoveListener(m_action);
        }

        private void Invoke(T value)
        {
            Deliver(value);
        }
    }
}
