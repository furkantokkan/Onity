using System;
using System.Threading;
using UnityEngine.UIElements;

namespace Onity.Unity.Async
{
    /// <summary>
    /// One-way bindings from an async sequence to a UI Toolkit element, in the shape of UniTask's
    /// <c>BindTo</c> for uGUI. Any <see cref="IOnityAsyncEnumerable{T}"/> works, including
    /// <see cref="OnityAsyncReactiveProperty{T}"/>, which starts with its current value. Every member
    /// requires Unity's main thread.
    /// </summary>
    /// <remarks>
    /// A binding starts at once and ends when the sequence completes, the token is canceled, or the
    /// element is detached from its panel, whichever comes first; it is not restored when the element
    /// is attached again, so call <c>BindTo</c> again then. An element that is never attached to a panel
    /// is not unbound by this rule: pass a token for it. A binding never raises a <c>ChangeEvent</c>, so
    /// a two-way setup does not loop. When the sequence faults, the binding retries once with a new
    /// enumerator if <c>rebindOnError</c> is set (the default); a second consecutive fault is logged.
    /// Cancellation and a destroyed target end the binding silently or log, as
    /// <see cref="OnityTask.Forget(Action{Exception})"/> does. Overload resolution note: in UI Toolkit
    /// a <c>TextElement</c> (<c>Label</c>, <c>Button</c>) is itself a string-valued control, so a string
    /// sequence bound to a statically typed <c>Label</c> or <c>Button</c> resolves to the value-control
    /// overload; both overloads set the element's text the same way.
    /// </remarks>
    public static class OnityUIToolkitBindingExtensions
    {
        /// <summary>
        /// Binds each item of the sequence, converted with <c>ToString()</c>, to the text of an element
        /// without raising a <c>ChangeEvent</c>. A null item sets an empty text.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="textElement">Element whose text is set, such as a <c>Label</c> or <c>Button</c>.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo<T>(
            this IOnityAsyncEnumerable<T> source, TextElement textElement, bool rebindOnError = true)
        {
            Bind(source, textElement, TextSetter<T>.Set, default, rebindOnError);
        }

        /// <summary>
        /// Binds each item of the sequence, converted with <c>ToString()</c>, to the text of an element
        /// without raising a <c>ChangeEvent</c>. A null item sets an empty text.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="textElement">Element whose text is set, such as a <c>Label</c> or <c>Button</c>.</param>
        /// <param name="cancellationToken">Ends the binding when canceled.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo<T>(
            this IOnityAsyncEnumerable<T> source,
            TextElement textElement,
            CancellationToken cancellationToken,
            bool rebindOnError = true)
        {
            Bind(source, textElement, TextSetter<T>.Set, cancellationToken, rebindOnError);
        }

        /// <summary>
        /// Binds each item of the sequence to the value of a control with
        /// <c>SetValueWithoutNotify</c>, so no <c>ChangeEvent</c> is raised and a two-way setup does not loop.
        /// A <c>TextElement</c> is a string-valued control, so string items set its text through this overload.
        /// </summary>
        /// <typeparam name="T">Item and control value type.</typeparam>
        /// <typeparam name="TElement">Control type.</typeparam>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="element">Control whose value is set, such as a <c>TextField</c> or <c>Slider</c>.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo<T, TElement>(
            this IOnityAsyncEnumerable<T> source, TElement element, bool rebindOnError = true)
            where TElement : VisualElement, INotifyValueChanged<T>
        {
            Bind(source, element, ValueSetter<T, TElement>.Set, default, rebindOnError);
        }

        /// <summary>
        /// Binds each item of the sequence to the value of a control with
        /// <c>SetValueWithoutNotify</c>, so no <c>ChangeEvent</c> is raised and a two-way setup does not loop.
        /// </summary>
        /// <typeparam name="T">Item and control value type.</typeparam>
        /// <typeparam name="TElement">Control type.</typeparam>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="element">Control whose value is set, such as a <c>TextField</c> or <c>Slider</c>.</param>
        /// <param name="cancellationToken">Ends the binding when canceled.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo<T, TElement>(
            this IOnityAsyncEnumerable<T> source,
            TElement element,
            CancellationToken cancellationToken,
            bool rebindOnError = true)
            where TElement : VisualElement, INotifyValueChanged<T>
        {
            Bind(source, element, ValueSetter<T, TElement>.Set, cancellationToken, rebindOnError);
        }

        private static void Bind<TSource, TElement>(
            IOnityAsyncEnumerable<TSource> source,
            TElement element,
            Action<TElement, TSource> apply,
            CancellationToken cancellationToken,
            bool rebindOnError)
            where TElement : VisualElement
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            // The binding ends with the caller's token or when the element leaves its panel.
            CancellationTokenSource lifetime = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();
            EventCallback<DetachFromPanelEvent> onDetach = _ => CancelQuietly(lifetime);
            element.RegisterCallback(onDetach);

            RunAsync(source, element, apply, lifetime, onDetach, rebindOnError).Forget();
        }

        private static async OnityTask RunAsync<TSource, TElement>(
            IOnityAsyncEnumerable<TSource> source,
            TElement element,
            Action<TElement, TSource> apply,
            CancellationTokenSource lifetime,
            EventCallback<DetachFromPanelEvent> onDetach,
            bool rebindOnError)
            where TElement : VisualElement
        {
            try
            {
                await OnityAsyncBindingLoop.RunAsync(source, element, apply, lifetime.Token, rebindOnError);
            }
            finally
            {
                element.UnregisterCallback(onDetach);
                lifetime.Dispose();
            }
        }

        private static void CancelQuietly(CancellationTokenSource lifetime)
        {
            try
            {
                lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The binding already ended and released its source.
            }
        }

        private static class TextSetter<T>
        {
            // Not "element.text = ...": that setter raises ChangeEvent<string> while the element is in a panel.
            internal static readonly Action<TextElement, T> Set = (element, value) =>
                ((INotifyValueChanged<string>)element).SetValueWithoutNotify(value?.ToString() ?? string.Empty);
        }

        private static class ValueSetter<T, TElement>
            where TElement : VisualElement, INotifyValueChanged<T>
        {
            internal static readonly Action<TElement, T> Set = (element, value) => element.SetValueWithoutNotify(value);
        }
    }
}
