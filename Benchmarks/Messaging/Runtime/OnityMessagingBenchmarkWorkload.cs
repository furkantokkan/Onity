using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Benchmarks
{
    /// <summary>
    /// The one message type of the messaging comparison: a readonly struct with one <see cref="int"/> payload.
    /// Both libraries publish exactly this type.
    /// </summary>
    internal readonly struct OnityMessagingBenchmarkMessage
    {
        /// <summary>The payload every handler adds to its sum.</summary>
        internal readonly int Value;

        internal OnityMessagingBenchmarkMessage(int value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// One library's implementation of one scenario. <see cref="Setup"/>, <see cref="Probe"/> and
    /// <see cref="Teardown"/> run outside the timed region; <see cref="Run"/> holds the timed loop and
    /// calls only the library's public interfaces. Handler delegates handed to the library are created
    /// once per workload, outside timing.
    /// </summary>
    internal abstract class OnityMessagingBenchmarkWorkload
    {
        protected OnityMessagingBenchmarkWorkload()
        {
            Sink = new OnityMessagingBenchmarkSink();
        }

        /// <summary>Receives every message the workload's handlers observe.</summary>
        internal OnityMessagingBenchmarkSink Sink { get; }

        /// <summary>Creates the brokers and subscriptions of one sample (untimed).</summary>
        internal abstract void Setup();

        /// <summary>The timed loop: <paramref name="operations"/> scenario operations.</summary>
        internal abstract void Run(int operations);

        /// <summary>Publishes one untimed probe message after the timed loop.</summary>
        internal abstract void Probe(int value);

        /// <summary>Disposes everything <see cref="Setup"/> created (untimed).</summary>
        internal abstract void Teardown();

        /// <summary>
        /// The failure an asynchronous publish raises when its task (a <see cref="ValueTask"/>, or the
        /// <c>UniTask</c> of MessagePipe's Unity package) is not already completed successfully. Both libraries
        /// use it, so the timed loops stay identical.
        /// </summary>
        internal static InvalidOperationException CreateIncompletePublishException(int operation)
        {
            return new InvalidOperationException("PublishAsync of operation " + operation.ToString(CultureInfo.InvariantCulture)
                + " returned a task that was not completed successfully; every handler completes synchronously, so the run fails.");
        }
    }

    /// <summary>
    /// Sums and counts received messages. Every handler of both libraries runs one of these method bodies:
    /// add <c>message.Value</c> to a <see cref="long"/> sum and increment a count, then return nothing or an
    /// already completed task. Each library's workload creates its delegates from these methods once.
    /// </summary>
    internal sealed class OnityMessagingBenchmarkSink
    {
        private long m_sum;
        private long m_count;

        /// <summary>Sum of the received payloads since the last <see cref="Reset"/>.</summary>
        internal long Sum => m_sum;

        /// <summary>Number of received messages since the last <see cref="Reset"/>.</summary>
        internal long Count => m_count;

        /// <summary>Clears the totals; called after the untimed setup.</summary>
        internal void Reset()
        {
            m_sum = 0;
            m_count = 0;
        }

        /// <summary>Synchronous handler body.</summary>
        internal void Receive(OnityMessagingBenchmarkMessage message)
        {
            m_sum += message.Value;
            m_count++;
        }

        /// <summary>Asynchronous handler body: the same work, then an already completed <see cref="ValueTask"/>.</summary>
        internal ValueTask ReceiveAsync(OnityMessagingBenchmarkMessage message, CancellationToken cancellationToken)
        {
            m_sum += message.Value;
            m_count++;
            return default;
        }
#if ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK

        /// <summary>
        /// Asynchronous handler body for MessagePipe's Unity package, whose async API uses UniTask: the same
        /// work, then <c>default(UniTask)</c>, an already completed <c>UniTask</c>.
        /// </summary>
        internal Cysharp.Threading.Tasks.UniTask ReceiveUniTaskAsync(OnityMessagingBenchmarkMessage message, CancellationToken cancellationToken)
        {
            m_sum += message.Value;
            m_count++;
            return default;
        }
#endif
    }
}
