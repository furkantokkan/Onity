using System;

namespace Onity.Unity.Async
{
    /// <summary>Reports an operation rejected by a completed channel.</summary>
    public sealed class OnityChannelClosedException : InvalidOperationException
    {
        /// <summary>Creates an exception for normal channel closure.</summary>
        public OnityChannelClosedException() : base("The channel is closed.")
        {
        }

        /// <summary>Creates an exception retaining the channel's terminal error.</summary>
        /// <param name="innerException">Original terminal error, or null for normal closure.</param>
        public OnityChannelClosedException(Exception innerException)
            : base("The channel is closed.", innerException)
        {
        }

        /// <summary>Creates an exception with a custom message.</summary>
        /// <param name="message">Error message.</param>
        public OnityChannelClosedException(string message) : base(message)
        {
        }

        /// <summary>Creates an exception with a custom message that retains the channel's terminal error.</summary>
        /// <param name="message">Error message.</param>
        /// <param name="innerException">Original terminal error, or null.</param>
        public OnityChannelClosedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
