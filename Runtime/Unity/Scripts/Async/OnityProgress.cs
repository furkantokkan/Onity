using System;
using System.Collections.Generic;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Factory for <see cref="IProgress{T}"/> reporters that invoke the callback synchronously.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Progress{T}"/>, the callback runs inline on the thread that calls
    /// <see cref="IProgress{T}.Report"/> and does not capture a synchronization context.
    /// Unity operation tasks report on Unity's main thread.
    /// </remarks>
    public static class OnityProgress
    {
        /// <summary>
        /// Creates a progress reporter that forwards every reported value to a callback.
        /// </summary>
        /// <param name="callback">Callback invoked inline for each reported value.</param>
        /// <returns>Progress reporter.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public static IProgress<float> Create(Action<float> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            return new CallbackProgress(callback);
        }

        /// <summary>
        /// Creates a progress reporter for any value type that forwards every reported value to a
        /// callback, inline on the reporting thread.
        /// </summary>
        /// <remarks>
        /// Like the float overload, and unlike UniTask's <c>Progress.Create</c>, a null callback is
        /// rejected instead of producing a reporter that ignores values.
        /// </remarks>
        /// <typeparam name="T">Reported value type.</typeparam>
        /// <param name="callback">Callback invoked inline for each reported value.</param>
        /// <returns>Progress reporter.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public static IProgress<T> Create<T>(Action<T> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            return new CallbackProgress<T>(callback);
        }

        /// <summary>
        /// Creates a progress reporter that forwards a value to the callback only when it differs from
        /// the value forwarded last. The first reported value is always forwarded.
        /// </summary>
        /// <remarks>
        /// The reporter is not thread-safe: report from one thread at a time, as the Unity operation
        /// adapters do. The callback runs inline on the reporting thread.
        /// </remarks>
        /// <typeparam name="T">Reported value type.</typeparam>
        /// <param name="callback">Callback invoked inline for each changed value.</param>
        /// <param name="comparer">Equality comparer; null uses <see cref="EqualityComparer{T}.Default"/>.</param>
        /// <returns>Progress reporter.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public static IProgress<T> CreateOnlyValueChanged<T>(
            Action<T> callback,
            IEqualityComparer<T> comparer = null)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            return new OnlyValueChangedProgress<T>(callback, comparer ?? EqualityComparer<T>.Default);
        }

        private sealed class CallbackProgress<T> : IProgress<T>
        {
            private readonly Action<T> m_callback;

            public CallbackProgress(Action<T> callback)
            {
                m_callback = callback;
            }

            public void Report(T value)
            {
                m_callback(value);
            }
        }

        private sealed class OnlyValueChangedProgress<T> : IProgress<T>
        {
            private readonly Action<T> m_callback;
            private readonly IEqualityComparer<T> m_comparer;
            private bool m_hasValue;
            private T m_latest;

            public OnlyValueChangedProgress(Action<T> callback, IEqualityComparer<T> comparer)
            {
                m_callback = callback;
                m_comparer = comparer;
            }

            public void Report(T value)
            {
                if (m_hasValue && m_comparer.Equals(value, m_latest))
                {
                    return;
                }

                m_hasValue = true;
                m_latest = value;
                m_callback(value);
            }
        }

        private sealed class CallbackProgress : IProgress<float>
        {
            private readonly Action<float> m_callback;

            public CallbackProgress(Action<float> callback)
            {
                m_callback = callback;
            }

            public void Report(float value)
            {
                m_callback(value);
            }
        }
    }
}
