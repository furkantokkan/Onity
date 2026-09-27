using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>Producer handle for a channel shared by multiple writers.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    public sealed class OnityChannelWriter<T>
    {
        private readonly OnityChannel<T> m_channel;

        internal OnityChannelWriter(OnityChannel<T> channel)
        {
            m_channel = channel;
        }

        /// <summary>Accepts an item immediately if open and capacity/fairness permits.</summary>
        /// <param name="item">Item to accept.</param>
        /// <returns>True when accepted, otherwise false without accepting the item.</returns>
        /// <remarks>Cannot bypass queued writers. Warm buffered writes without pending reader
        /// publication allocate no channel storage. Unbounded growth, pending publication,
        /// bridges and user continuations may allocate.</remarks>
        public bool TryWrite(T item) => m_channel.TryWrite(item);

        /// <summary>Accepts an item or waits in FIFO order for bounded capacity.</summary>
        /// <param name="item">Item retained until acceptance or cancellation/closure.</param>
        /// <param name="cancellationToken">Cancels only an unaccepted write.</param>
        /// <returns>A native task completing at acceptance.</returns>
        /// <remarks>Pre-cancellation wins before acceptance. Committed acceptance wins later
        /// cancellation. Pending waiters allocate; no thread/context hop is added. Closure faults
        /// with OnityChannelClosedException retaining any terminal error as InnerException.</remarks>
        public OnityTask WriteAsync(T item, CancellationToken cancellationToken = default) =>
            m_channel.WriteAsync(item, cancellationToken);

        /// <summary>Closes acceptance once, preserving buffered items for draining.</summary>
        /// <param name="error">Original terminal error, or null for normal completion.</param>
        /// <returns>True for the first completion, otherwise false.</returns>
        /// <remarks>Unaccepted writers fail. Accepted items drain before readers observe end/error.
        /// An OperationCanceledException supplied as an error remains a fault.</remarks>
        public bool TryComplete(Exception error = null) => m_channel.TryComplete(error);
    }
}
