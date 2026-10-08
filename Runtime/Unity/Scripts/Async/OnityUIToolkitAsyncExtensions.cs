using System;
using System.Threading;
using Onity.Core;
using UnityEngine.UIElements;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Awaitable waits and async sequences over UI Toolkit events. This is an Onity extension beyond
    /// UniTask 2.5.11, which has no UI Toolkit adapters. Every member requires Unity's main thread.
    /// </summary>
    /// <remarks>
    /// A wait registers its callback when the method is called and unregisters it when the wait
    /// completes or its token is canceled; the token is optional, but a wait on an event that never fires
    /// keeps its callback on the element until the element is collected. Waits are not tied to the
    /// panel: they survive detaching the element. Continuations run inline inside the UI Toolkit
    /// callback. UI Toolkit pools its event objects and recycles them after dispatch, so a task or
    /// sequence item that carries an event (<see cref="OnEventAsync{TEvent}"/>,
    /// <see cref="OnEventAsAsyncEnumerable{TEvent}"/>) must be read before the continuation yields
    /// for the first time; copy what you need first.
    /// </remarks>
    public static class OnityUIToolkitAsyncExtensions
    {
        /// <summary>Waits for the next click; the callback is removed at that click.</summary>
        /// <param name="button">Button to listen to.</param>
        /// <param name="cancellationToken">Cancels the wait and removes the callback.</param>
        /// <returns>A single-consumption task; canceled by the token.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="button"/> is null.</exception>
        public static OnityTask OnClickAsync(this Button button, CancellationToken cancellationToken = default)
        {
            if (button == null)
            {
                throw new ArgumentNullException(nameof(button));
            }

            return new OnityButtonClickListener(button, cancellationToken, true).WaitAsync();
        }

        /// <summary>Exposes the clicks as an async sequence.</summary>
        /// <param name="button">Button to listen to.</param>
        /// <param name="cancellationToken">Ends every enumeration when canceled.</param>
        /// <returns>A sequence that listens while an enumerator moves; a click while no move is pending is skipped.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="button"/> is null.</exception>
        public static IOnityAsyncEnumerable<Unit> OnClickAsAsyncEnumerable(
            this Button button, CancellationToken cancellationToken = default)
        {
            if (button == null)
            {
                throw new ArgumentNullException(nameof(button));
            }

            return new OnityButtonClickAsyncEnumerable(button, cancellationToken);
        }

        /// <summary>Waits for the next event of a type; the callback is removed at that event.</summary>
        /// <typeparam name="TEvent">UI Toolkit event type.</typeparam>
        /// <param name="element">Element to listen on.</param>
        /// <param name="cancellationToken">Cancels the wait and removes the callback.</param>
        /// <param name="trickleDown">Whether the callback runs in the trickle-down phase.</param>
        /// <returns>
        /// A single-consumption task completed with the event, which is valid only until the continuation
        /// first yields; canceled by the token.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="element"/> is null.</exception>
        public static OnityTask<TEvent> OnEventAsync<TEvent>(
            this VisualElement element,
            CancellationToken cancellationToken = default,
            TrickleDown trickleDown = TrickleDown.NoTrickleDown)
            where TEvent : EventBase<TEvent>, new()
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            return new OnityElementEventListener<TEvent>(element, trickleDown, cancellationToken, true).WaitValueAsync();
        }

        /// <summary>Exposes the events of a type as an async sequence.</summary>
        /// <typeparam name="TEvent">UI Toolkit event type.</typeparam>
        /// <param name="element">Element to listen on.</param>
        /// <param name="cancellationToken">Ends every enumeration when canceled.</param>
        /// <param name="trickleDown">Whether the callback runs in the trickle-down phase.</param>
        /// <returns>
        /// A sequence that listens while an enumerator moves; an event while no move is pending is skipped.
        /// Each item is valid only until the consumer first yields.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="element"/> is null.</exception>
        public static IOnityAsyncEnumerable<TEvent> OnEventAsAsyncEnumerable<TEvent>(
            this VisualElement element,
            CancellationToken cancellationToken = default,
            TrickleDown trickleDown = TrickleDown.NoTrickleDown)
            where TEvent : EventBase<TEvent>, new()
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            return new OnityElementEventAsyncEnumerable<TEvent>(element, trickleDown, cancellationToken);
        }

        /// <summary>Waits for the next change of the control's value; the callback is removed at that change.</summary>
        /// <typeparam name="T">Value type of the control.</typeparam>
        /// <param name="element">Control to listen to; it must be a UI Toolkit element.</param>
        /// <param name="cancellationToken">Cancels the wait and removes the callback.</param>
        /// <returns>A single-consumption task completed with the new value; canceled by the token.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="element"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="element"/> is not an element that raises events.</exception>
        public static OnityTask<T> OnValueChangedAsync<T>(
            this INotifyValueChanged<T> element, CancellationToken cancellationToken = default)
        {
            ThrowIfInvalid(element);
            return new OnityValueChangedListener<T>(element, cancellationToken, true).WaitValueAsync();
        }

        /// <summary>Exposes the value changes of a control as an async sequence of the new value.</summary>
        /// <typeparam name="T">Value type of the control.</typeparam>
        /// <param name="element">Control to listen to; it must be a UI Toolkit element.</param>
        /// <param name="cancellationToken">Ends every enumeration when canceled.</param>
        /// <returns>A sequence that listens while an enumerator moves; a change while no move is pending is skipped.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="element"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="element"/> is not an element that raises events.</exception>
        public static IOnityAsyncEnumerable<T> OnValueChangedAsAsyncEnumerable<T>(
            this INotifyValueChanged<T> element, CancellationToken cancellationToken = default)
        {
            ThrowIfInvalid(element);
            return new OnityValueChangedAsyncEnumerable<T>(element, cancellationToken);
        }

        private static void ThrowIfInvalid<T>(INotifyValueChanged<T> element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (!(element is CallbackEventHandler))
            {
                throw new ArgumentException("The control does not raise UI Toolkit events.", nameof(element));
            }
        }
    }

    internal sealed class OnityButtonClickListener : OnityEventListener<Unit>
    {
        private readonly Button m_button;
        private readonly Action m_action;

        internal OnityButtonClickListener(Button button, CancellationToken cancellationToken, bool callOnce)
            : base(cancellationToken, callOnce)
        {
            m_button = button;
            m_action = Invoke;
            Start();
        }

        protected override void AddListener()
        {
            m_button.clicked += m_action;
        }

        protected override void RemoveListener()
        {
            m_button.clicked -= m_action;
        }

        private void Invoke()
        {
            Deliver(Unit.Default);
        }
    }

    internal sealed class OnityButtonClickEnumerator : OnityEventListenerEnumerator<Unit>
    {
        private readonly Button m_button;
        private readonly Action m_action;

        internal OnityButtonClickEnumerator(Button button, CancellationToken sourceToken, CancellationToken consumerToken)
            : base(sourceToken, consumerToken)
        {
            m_button = button;
            m_action = Invoke;
        }

        protected override void AddListener()
        {
            m_button.clicked += m_action;
        }

        protected override void RemoveListener()
        {
            m_button.clicked -= m_action;
        }

        private void Invoke()
        {
            Deliver(Unit.Default);
        }
    }

    internal sealed class OnityButtonClickAsyncEnumerable : IOnityAsyncEnumerable<Unit>
    {
        private readonly Button m_button;
        private readonly CancellationToken m_cancellationToken;

        internal OnityButtonClickAsyncEnumerable(Button button, CancellationToken cancellationToken)
        {
            m_button = button;
            m_cancellationToken = cancellationToken;
        }

        public IOnityAsyncEnumerator<Unit> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new OnityButtonClickEnumerator(m_button, m_cancellationToken, cancellationToken);
        }
    }

    internal sealed class OnityElementEventListener<TEvent> : OnityEventListener<TEvent>
        where TEvent : EventBase<TEvent>, new()
    {
        private readonly VisualElement m_element;
        private readonly TrickleDown m_trickleDown;
        private readonly EventCallback<TEvent> m_callback;

        internal OnityElementEventListener(
            VisualElement element, TrickleDown trickleDown, CancellationToken cancellationToken, bool callOnce)
            : base(cancellationToken, callOnce)
        {
            m_element = element;
            m_trickleDown = trickleDown;
            m_callback = Invoke;
            Start();
        }

        protected override void AddListener()
        {
            m_element.RegisterCallback(m_callback, m_trickleDown);
        }

        protected override void RemoveListener()
        {
            m_element.UnregisterCallback(m_callback, m_trickleDown);
        }

        private void Invoke(TEvent evt)
        {
            Deliver(evt);
        }
    }

    internal sealed class OnityElementEventEnumerator<TEvent> : OnityEventListenerEnumerator<TEvent>
        where TEvent : EventBase<TEvent>, new()
    {
        private readonly VisualElement m_element;
        private readonly TrickleDown m_trickleDown;
        private readonly EventCallback<TEvent> m_callback;

        internal OnityElementEventEnumerator(
            VisualElement element,
            TrickleDown trickleDown,
            CancellationToken sourceToken,
            CancellationToken consumerToken)
            : base(sourceToken, consumerToken)
        {
            m_element = element;
            m_trickleDown = trickleDown;
            m_callback = Invoke;
        }

        protected override void AddListener()
        {
            m_element.RegisterCallback(m_callback, m_trickleDown);
        }

        protected override void RemoveListener()
        {
            m_element.UnregisterCallback(m_callback, m_trickleDown);
        }

        private void Invoke(TEvent evt)
        {
            Deliver(evt);
        }
    }

    internal sealed class OnityElementEventAsyncEnumerable<TEvent> : IOnityAsyncEnumerable<TEvent>
        where TEvent : EventBase<TEvent>, new()
    {
        private readonly VisualElement m_element;
        private readonly TrickleDown m_trickleDown;
        private readonly CancellationToken m_cancellationToken;

        internal OnityElementEventAsyncEnumerable(
            VisualElement element, TrickleDown trickleDown, CancellationToken cancellationToken)
        {
            m_element = element;
            m_trickleDown = trickleDown;
            m_cancellationToken = cancellationToken;
        }

        public IOnityAsyncEnumerator<TEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new OnityElementEventEnumerator<TEvent>(m_element, m_trickleDown, m_cancellationToken, cancellationToken);
        }
    }

    internal sealed class OnityValueChangedListener<T> : OnityEventListener<T>
    {
        private readonly INotifyValueChanged<T> m_element;
        private readonly EventCallback<ChangeEvent<T>> m_callback;

        internal OnityValueChangedListener(INotifyValueChanged<T> element, CancellationToken cancellationToken, bool callOnce)
            : base(cancellationToken, callOnce)
        {
            m_element = element;
            m_callback = Invoke;
            Start();
        }

        protected override void AddListener()
        {
            m_element.RegisterValueChangedCallback(m_callback);
        }

        protected override void RemoveListener()
        {
            m_element.UnregisterValueChangedCallback(m_callback);
        }

        private void Invoke(ChangeEvent<T> evt)
        {
            Deliver(evt.newValue);
        }
    }

    internal sealed class OnityValueChangedEnumerator<T> : OnityEventListenerEnumerator<T>
    {
        private readonly INotifyValueChanged<T> m_element;
        private readonly EventCallback<ChangeEvent<T>> m_callback;

        internal OnityValueChangedEnumerator(
            INotifyValueChanged<T> element, CancellationToken sourceToken, CancellationToken consumerToken)
            : base(sourceToken, consumerToken)
        {
            m_element = element;
            m_callback = Invoke;
        }

        protected override void AddListener()
        {
            m_element.RegisterValueChangedCallback(m_callback);
        }

        protected override void RemoveListener()
        {
            m_element.UnregisterValueChangedCallback(m_callback);
        }

        private void Invoke(ChangeEvent<T> evt)
        {
            Deliver(evt.newValue);
        }
    }

    internal sealed class OnityValueChangedAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly INotifyValueChanged<T> m_element;
        private readonly CancellationToken m_cancellationToken;

        internal OnityValueChangedAsyncEnumerable(INotifyValueChanged<T> element, CancellationToken cancellationToken)
        {
            m_element = element;
            m_cancellationToken = cancellationToken;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new OnityValueChangedEnumerator<T>(m_element, m_cancellationToken, cancellationToken);
        }
    }
}
