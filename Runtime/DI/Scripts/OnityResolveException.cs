using System;

namespace Onity.DI
{
    /// <summary>
    /// Exception thrown when a dependency cannot be resolved.
    /// </summary>
    public sealed class OnityResolveException : Exception
    {
        /// <summary>
        /// Initializes a new exception instance.
        /// </summary>
        /// <param name="message">Exception message.</param>
        public OnityResolveException(string message)
            : base(message)
        {
        }
    }
}
