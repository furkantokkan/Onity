using System;
using System.Collections;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Coroutine interop of <see cref="OnityTask"/>.
    /// </summary>
    public readonly partial struct OnityTask
    {
        /// <summary>
        /// Creates the task with a factory and returns a Unity coroutine enumerator that runs until the
        /// task completes. The factory is called immediately, so the async method starts at the call;
        /// the enumerator starts awaiting the task at its first <c>MoveNext</c>.
        /// </summary>
        /// <remarks>
        /// Pass the enumerator to <c>StartCoroutine</c> or yield it from another coroutine. A failure
        /// is rethrown from <c>MoveNext</c>, which makes Unity log it and end the coroutine. Use
        /// <see cref="OnityTaskCoroutineExtensions.ToCoroutine(OnityTask, Action{Exception})"/> to
        /// handle the failure instead.
        /// </remarks>
        /// <param name="taskFactory">Factory that starts the work.</param>
        /// <returns>A coroutine enumerator that completes with the task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="taskFactory"/> is null.</exception>
        public static IEnumerator ToCoroutine(Func<OnityTask> taskFactory)
        {
            if (taskFactory == null)
            {
                throw new ArgumentNullException(nameof(taskFactory));
            }

            return OnityTaskCoroutineExtensions.ToCoroutine(taskFactory(), null);
        }
    }
}
