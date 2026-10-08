using System;
using System.Collections.Generic;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Await shorthands for task collections: <c>await tasks</c> is <c>await OnityTask.WhenAll(tasks)</c>
    /// for arrays, sequences and tuples of Onity tasks.
    /// </summary>
    public static partial class OnityTaskCompositionAwaitExtensions
    {
        /// <summary>Awaits every task in an array, as <see cref="OnityTask.WhenAll(OnityTask[])"/>.</summary>
        /// <param name="tasks">Tasks to await.</param>
        /// <returns>The awaiter of the combined task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        public static OnityTaskAwaiter GetAwaiter(this OnityTask[] tasks)
        {
            return OnityTask.WhenAll(tasks).GetAwaiter();
        }

        /// <summary>
        /// Awaits every task in a sequence, as <see cref="OnityTask.WhenAll(IEnumerable{OnityTask})"/>.
        /// </summary>
        /// <param name="tasks">Tasks to await. The sequence is enumerated once.</param>
        /// <returns>The awaiter of the combined task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        public static OnityTaskAwaiter GetAwaiter(this IEnumerable<OnityTask> tasks)
        {
            return OnityTask.WhenAll(tasks).GetAwaiter();
        }

        /// <summary>
        /// Awaits every typed task in an array and returns the results in input order, as
        /// <see cref="OnityTask.WhenAll{T}(OnityTask{T}[])"/>.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="tasks">Tasks to await.</param>
        /// <returns>The awaiter of the combined task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        public static OnityTaskAwaiter<T[]> GetAwaiter<T>(this OnityTask<T>[] tasks)
        {
            return OnityTask.WhenAll(tasks).GetAwaiter();
        }

        /// <summary>
        /// Awaits every typed task in a sequence and returns the results in sequence order, as
        /// <see cref="OnityTask.WhenAll{T}(IEnumerable{OnityTask{T}})"/>.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="tasks">Tasks to await. The sequence is enumerated once.</param>
        /// <returns>The awaiter of the combined task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        public static OnityTaskAwaiter<T[]> GetAwaiter<T>(this IEnumerable<OnityTask<T>> tasks)
        {
            return OnityTask.WhenAll(tasks).GetAwaiter();
        }
    }
}
