using System;

namespace Onity.Messaging
{
    /// <summary>
    /// Thrown when a messaging-core operation cannot complete, such as an invalid
    /// channel state or a failed message delivery.
    /// </summary>
    public sealed class OnityMessagingException : Exception
    {
        /// <summary>
        /// Initializes a new messaging exception with a descriptive message.
        /// </summary>
        /// <param name="message">Human-readable error description.</param>
        public OnityMessagingException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new messaging exception with a descriptive message and the
        /// underlying cause.
        /// </summary>
        /// <param name="message">Human-readable error description.</param>
        /// <param name="innerException">Exception that triggered this one.</param>
        public OnityMessagingException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
