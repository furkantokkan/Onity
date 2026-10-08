using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Completion result payload for Onity reactive streams.
    /// </summary>
    public readonly struct OnityResult
    {
        private readonly Exception m_exception;

        /// <summary>
        /// True when stream completed successfully.
        /// </summary>
        public bool IsSuccess => m_exception == null;

        /// <summary>
        /// True when stream completed with failure.
        /// </summary>
        public bool IsFailure => m_exception != null;

        /// <summary>
        /// Failure exception when <see cref="IsFailure" /> is true.
        /// </summary>
        public Exception Exception => m_exception;

        private OnityResult(Exception exception)
        {
            m_exception = exception;
        }

        /// <summary>
        /// Creates a successful completion result.
        /// </summary>
        /// <returns>Success result.</returns>
        public static OnityResult Success()
        {
            return new OnityResult(null);
        }

        /// <summary>
        /// Creates a failed completion result.
        /// </summary>
        /// <param name="exception">Failure exception.</param>
        /// <returns>Failure result.</returns>
        public static OnityResult Failure(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            return new OnityResult(exception);
        }
    }
}
