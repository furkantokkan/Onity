using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Exception type for failures raised inside the Onity reactive core.
    /// </summary>
    public sealed class OnityReactiveException : Exception
    {
        /// <summary>
        /// Initializes a new exception instance.
        /// </summary>
        /// <param name="message">Exception message.</param>
        public OnityReactiveException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new exception instance that wraps an inner exception.
        /// </summary>
        /// <param name="message">Exception message.</param>
        /// <param name="innerException">Underlying exception.</param>
        public OnityReactiveException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
