using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Onity observer contract with lifecycle callbacks.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public abstract class OnityObserver<T> : IDisposable
    {
        private bool m_isStopped;
        private bool m_isDisposed;

        /// <summary>
        /// True after completion, error, or explicit disposal.
        /// </summary>
        public bool IsStopped => m_isStopped;

        /// <summary>
        /// Pushes a value to the observer.
        /// </summary>
        /// <param name="value">Next value.</param>
        public void OnNext(T value)
        {
            if (m_isStopped || m_isDisposed)
            {
                return;
            }

            OnNextCore(value);
        }

        /// <summary>
        /// Pushes an error and stops the observer.
        /// </summary>
        /// <param name="exception">Error payload.</param>
        public void OnError(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (m_isStopped || m_isDisposed)
            {
                return;
            }

            m_isStopped = true;
            OnErrorCore(exception);
        }

        /// <summary>
        /// Completes the observer with a result and stops it.
        /// </summary>
        /// <param name="result">Completion result.</param>
        public void OnCompleted(OnityResult result)
        {
            if (m_isStopped || m_isDisposed)
            {
                return;
            }

            m_isStopped = true;
            OnCompletedCore(result);
        }

        /// <summary>
        /// Completes the observer successfully.
        /// </summary>
        public void OnCompleted()
        {
            OnCompleted(OnityResult.Success());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_isStopped = true;
            OnDisposed();
        }

        /// <summary>
        /// Value callback implementation.
        /// </summary>
        /// <param name="value">Next value.</param>
        protected abstract void OnNextCore(T value);

        /// <summary>
        /// Error callback implementation.
        /// </summary>
        /// <param name="exception">Error payload.</param>
        protected virtual void OnErrorCore(Exception exception)
        {
        }

        /// <summary>
        /// Completion callback implementation.
        /// </summary>
        /// <param name="result">Completion result.</param>
        protected virtual void OnCompletedCore(OnityResult result)
        {
        }

        /// <summary>
        /// Called when observer is disposed.
        /// </summary>
        protected virtual void OnDisposed()
        {
        }
    }

    internal sealed class AnonymousOnityObserver<T> : OnityObserver<T>
    {
        private readonly Action<T> m_onNext;
        private readonly Action<Exception> m_onError;
        private readonly Action<OnityResult> m_onCompleted;
        private readonly Action m_onDisposed;

        public AnonymousOnityObserver(
            Action<T> onNext,
            Action<Exception> onError = null,
            Action<OnityResult> onCompleted = null,
            Action onDisposed = null)
        {
            m_onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));
            m_onError = onError;
            m_onCompleted = onCompleted;
            m_onDisposed = onDisposed;
        }

        protected override void OnNextCore(T value)
        {
            m_onNext(value);
        }

        protected override void OnErrorCore(Exception exception)
        {
            m_onError?.Invoke(exception);
        }

        protected override void OnCompletedCore(OnityResult result)
        {
            m_onCompleted?.Invoke(result);
        }

        protected override void OnDisposed()
        {
            m_onDisposed?.Invoke();
        }
    }
}
