using System;

namespace Onity.Benchmarks
{
    /// <summary>
    /// One library's implementation of one scenario. <see cref="Setup"/>, <see cref="Probe"/> and
    /// <see cref="Teardown"/> run outside the timed region; <see cref="Run"/> holds the timed loop and
    /// calls only the library's public API. Delegates handed to the library are created once.
    /// </summary>
    internal abstract class OnityReactiveBenchmarkWorkload
    {
        protected OnityReactiveBenchmarkWorkload()
        {
            Sink = new OnityReactiveBenchmarkSink();
        }

        /// <summary>Receives every value the workload's subscribers observe.</summary>
        internal OnityReactiveBenchmarkSink Sink { get; }

        /// <summary>Creates the sources and subscriptions of one sample (untimed).</summary>
        internal abstract void Setup();

        /// <summary>The timed loop: <paramref name="operations"/> scenario operations.</summary>
        internal abstract void Run(int operations);

        /// <summary>Pushes one untimed value through the scenario's source after the timed loop.</summary>
        internal abstract void Probe(int value);

        /// <summary>Disposes everything <see cref="Setup"/> created (untimed).</summary>
        internal abstract void Teardown();
    }

    /// <summary>
    /// Sums and counts received values. <see cref="OnNext"/> is created once and is the single cached
    /// <see cref="Action{T}"/> every subscription of a workload uses.
    /// </summary>
    internal sealed class OnityReactiveBenchmarkSink
    {
        private long m_sum;
        private long m_count;

        internal OnityReactiveBenchmarkSink()
        {
            OnNext = Receive;
        }

        /// <summary>The cached callback passed to each library's <c>Subscribe(Action&lt;int&gt;)</c>.</summary>
        internal Action<int> OnNext { get; }

        /// <summary>Sum of the received values since the last <see cref="Reset"/>.</summary>
        internal long Sum => m_sum;

        /// <summary>Number of received values since the last <see cref="Reset"/>.</summary>
        internal long Count => m_count;

        /// <summary>Clears the totals; called after the untimed setup.</summary>
        internal void Reset()
        {
            m_sum = 0;
            m_count = 0;
        }

        private void Receive(int value)
        {
            m_sum += value;
            m_count++;
        }
    }
}
