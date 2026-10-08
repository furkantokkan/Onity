using System;
using System.Runtime.ExceptionServices;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Outcome of one input of <see cref="OnityTask.WhenEach{T}(OnityTask{T}[])"/>: its result, or the
    /// exception that faulted or canceled it.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    public readonly struct OnityWhenEachResult<T>
    {
        /// <summary>Creates the outcome of an input that completed successfully.</summary>
        /// <param name="result">The input's result.</param>
        public OnityWhenEachResult(T result)
        {
            Result = result;
            Exception = null;
        }

        /// <summary>Creates the outcome of an input that faulted or was canceled.</summary>
        /// <param name="exception">
        /// The input's fault, or an <see cref="OperationCanceledException"/> for a canceled input.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
        public OnityWhenEachResult(Exception exception)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
            Result = default;
        }

        /// <summary>Gets the result of a successful input, or the default value of a failed one.</summary>
        public T Result { get; }

        /// <summary>
        /// Gets the fault of a failed input, or an <see cref="OperationCanceledException"/> carrying the
        /// token of a canceled input; null for a successful input.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>Gets whether the input completed successfully.</summary>
        public bool IsCompletedSuccessfully => Exception == null;

        /// <summary>Gets whether the input faulted or was canceled.</summary>
        public bool IsFaulted => Exception != null;

        /// <summary>Rethrows the input's exception with its original stack trace when the input failed.</summary>
        public void TryThrow()
        {
            if (Exception != null)
            {
                ExceptionDispatchInfo.Capture(Exception).Throw();
            }
        }

        /// <summary>Returns the result, or rethrows the input's exception when the input failed.</summary>
        /// <returns>The result of a successful input.</returns>
        public T GetResult()
        {
            TryThrow();
            return Result;
        }

        /// <summary>Formats the result, or the failure as <c>Exception{message}</c>.</summary>
        /// <returns>The formatted outcome.</returns>
        public override string ToString()
        {
            if (Exception != null)
            {
                return "Exception{" + Exception.Message + "}";
            }

            return Result == null ? string.Empty : Result.ToString();
        }
    }
}
