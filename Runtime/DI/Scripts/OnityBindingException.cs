using System;

namespace Onity.DI
{
    /// <summary>
    /// Exception thrown when a binding configuration is invalid.
    /// </summary>
    public sealed class OnityBindingException : Exception
    {
        /// <summary>
        /// Initializes a new exception instance.
        /// </summary>
        /// <param name="message">Exception message.</param>
        public OnityBindingException(string message)
            : base(message)
        {
        }
    }
}
