using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace Onity.Unity.Async
{
    /// <summary>
    /// One-way bindings from an async sequence to uGUI components (UniTask <c>UnityBindingExtensions</c>
    /// parity). Any <see cref="IOnityAsyncEnumerable{T}"/> works, including
    /// <see cref="OnityAsyncReactiveProperty{T}"/>, which starts with its current value. Every member
    /// requires Unity's main thread.
    /// </summary>
    /// <remarks>
    /// Overloads without a token end the binding when the component is destroyed. A binding also ends when
    /// the sequence completes or the token is canceled. When the sequence faults, the binding retries once
    /// with a new enumerator if <c>rebindOnError</c> is set (the default); a second consecutive fault is
    /// logged. Cancellation ends the binding silently.
    /// </remarks>
    public static class OnityUguiBindingExtensions
    {
        /// <summary>Binds each string of the sequence to <see cref="Text.text"/> until the text is destroyed.</summary>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="text">Text to update.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo(this IOnityAsyncEnumerable<string> source, Text text, bool rebindOnError = true)
        {
            Bind(source, text, TextSetter.Set, GetDestroyToken(text), rebindOnError);
        }

        /// <summary>Binds each string of the sequence to <see cref="Text.text"/> until the token is canceled.</summary>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="text">Text to update.</param>
        /// <param name="cancellationToken">Ends the binding when canceled.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo(
            this IOnityAsyncEnumerable<string> source,
            Text text,
            CancellationToken cancellationToken,
            bool rebindOnError = true)
        {
            Bind(source, text, TextSetter.Set, cancellationToken, rebindOnError);
        }

        /// <summary>
        /// Binds each item of the sequence, converted with <c>ToString()</c>, to <see cref="Text.text"/> until
        /// the text is destroyed. A null item sets an empty text.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="text">Text to update.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo<T>(this IOnityAsyncEnumerable<T> source, Text text, bool rebindOnError = true)
        {
            Bind(source, text, TextSetter<T>.Set, GetDestroyToken(text), rebindOnError);
        }

        /// <summary>
        /// Binds each item of the sequence, converted with <c>ToString()</c>, to <see cref="Text.text"/> until
        /// the token is canceled. A null item sets an empty text.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="text">Text to update.</param>
        /// <param name="cancellationToken">Ends the binding when canceled.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo<T>(
            this IOnityAsyncEnumerable<T> source,
            Text text,
            CancellationToken cancellationToken,
            bool rebindOnError = true)
        {
            Bind(source, text, TextSetter<T>.Set, cancellationToken, rebindOnError);
        }

        /// <summary>Binds each flag of the sequence to <see cref="Selectable.interactable"/> until the selectable is destroyed.</summary>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="selectable">Selectable to update.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo(
            this IOnityAsyncEnumerable<bool> source, Selectable selectable, bool rebindOnError = true)
        {
            Bind(source, selectable, InteractableSetter.Set, GetDestroyToken(selectable), rebindOnError);
        }

        /// <summary>Binds each flag of the sequence to <see cref="Selectable.interactable"/> until the token is canceled.</summary>
        /// <param name="source">Sequence to consume.</param>
        /// <param name="selectable">Selectable to update.</param>
        /// <param name="cancellationToken">Ends the binding when canceled.</param>
        /// <param name="rebindOnError">Retry once with a new enumerator after a fault.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void BindTo(
            this IOnityAsyncEnumerable<bool> source,
            Selectable selectable,
            CancellationToken cancellationToken,
            bool rebindOnError = true)
        {
            Bind(source, selectable, InteractableSetter.Set, cancellationToken, rebindOnError);
        }

        private static CancellationToken GetDestroyToken(Component component)
        {
            if (ReferenceEquals(component, null))
            {
                throw new ArgumentNullException(nameof(component));
            }

            return component.GetCancellationTokenOnDestroy();
        }

        private static void Bind<TSource, TTarget>(
            IOnityAsyncEnumerable<TSource> source,
            TTarget target,
            Action<TTarget, TSource> apply,
            CancellationToken cancellationToken,
            bool rebindOnError)
            where TTarget : Component
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (ReferenceEquals(target, null))
            {
                throw new ArgumentNullException(nameof(target));
            }

            OnityAsyncBindingLoop.Start(source, target, apply, cancellationToken, rebindOnError);
        }

        private static class TextSetter
        {
            internal static readonly Action<Text, string> Set = (text, value) => text.text = value;
        }

        private static class TextSetter<T>
        {
            internal static readonly Action<Text, T> Set =
                (text, value) => text.text = value?.ToString() ?? string.Empty;
        }

        private static class InteractableSetter
        {
            internal static readonly Action<Selectable, bool> Set =
                (selectable, value) => selectable.interactable = value;
        }
    }
}
