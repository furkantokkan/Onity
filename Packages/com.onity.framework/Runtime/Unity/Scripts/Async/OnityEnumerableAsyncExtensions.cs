using System;
using System.Collections.Generic;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Projects synchronous sequences into Onity tasks, for example
    /// <c>OnityTask.WhenAll(items.Select(item => LoadAsync(item)))</c>. A selector that returns an
    /// <see cref="OnityTask"/> or <see cref="OnityTask{T}"/> binds to these overloads, including an
    /// <c>async</c> lambda when <c>System.Linq</c> is not imported.
    /// </summary>
    /// <remarks>
    /// Projection is lazy and runs again on every enumeration, like <c>Enumerable.Select</c>, so each
    /// enumeration starts new tasks. The composition overloads enumerate a sequence exactly once.
    /// </remarks>
    public static class OnityEnumerableAsyncExtensions
    {
        /// <summary>Projects each element into an untyped Onity task.</summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="source">Elements to project.</param>
        /// <param name="selector">Creates the task for one element.</param>
        /// <returns>A lazy sequence of the created tasks.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> or <paramref name="selector"/> is null.
        /// </exception>
        public static IEnumerable<OnityTask> Select<T>(this IEnumerable<T> source, Func<T, OnityTask> selector)
        {
            ValidateArguments(source, selector);
            return SelectIterator(source, selector);
        }

        /// <summary>Projects each element into a typed Onity task.</summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <typeparam name="TResult">Task result type.</typeparam>
        /// <param name="source">Elements to project.</param>
        /// <param name="selector">Creates the task for one element.</param>
        /// <returns>A lazy sequence of the created tasks.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> or <paramref name="selector"/> is null.
        /// </exception>
        public static IEnumerable<OnityTask<TResult>> Select<T, TResult>(
            this IEnumerable<T> source,
            Func<T, OnityTask<TResult>> selector)
        {
            ValidateArguments(source, selector);
            return SelectIterator(source, selector);
        }

        /// <summary>Projects each element and its zero-based index into an untyped Onity task.</summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="source">Elements to project.</param>
        /// <param name="selector">Creates the task for one element and its index.</param>
        /// <returns>A lazy sequence of the created tasks.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> or <paramref name="selector"/> is null.
        /// </exception>
        public static IEnumerable<OnityTask> Select<T>(this IEnumerable<T> source, Func<T, int, OnityTask> selector)
        {
            ValidateArguments(source, selector);
            return SelectIndexedIterator(source, selector);
        }

        /// <summary>Projects each element and its zero-based index into a typed Onity task.</summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <typeparam name="TResult">Task result type.</typeparam>
        /// <param name="source">Elements to project.</param>
        /// <param name="selector">Creates the task for one element and its index.</param>
        /// <returns>A lazy sequence of the created tasks.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> or <paramref name="selector"/> is null.
        /// </exception>
        public static IEnumerable<OnityTask<TResult>> Select<T, TResult>(
            this IEnumerable<T> source,
            Func<T, int, OnityTask<TResult>> selector)
        {
            ValidateArguments(source, selector);
            return SelectIndexedIterator(source, selector);
        }

        private static void ValidateArguments<T>(IEnumerable<T> source, Delegate selector)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
        }

        private static IEnumerable<OnityTask> SelectIterator<T>(IEnumerable<T> source, Func<T, OnityTask> selector)
        {
            foreach (T item in source)
            {
                yield return selector(item);
            }
        }

        private static IEnumerable<OnityTask<TResult>> SelectIterator<T, TResult>(
            IEnumerable<T> source,
            Func<T, OnityTask<TResult>> selector)
        {
            foreach (T item in source)
            {
                yield return selector(item);
            }
        }

        private static IEnumerable<OnityTask> SelectIndexedIterator<T>(
            IEnumerable<T> source,
            Func<T, int, OnityTask> selector)
        {
            int index = -1;
            foreach (T item in source)
            {
                index = checked(index + 1);
                yield return selector(item, index);
            }
        }

        private static IEnumerable<OnityTask<TResult>> SelectIndexedIterator<T, TResult>(
            IEnumerable<T> source,
            Func<T, int, OnityTask<TResult>> selector)
        {
            int index = -1;
            foreach (T item in source)
            {
                index = checked(index + 1);
                yield return selector(item, index);
            }
        }
    }
}
