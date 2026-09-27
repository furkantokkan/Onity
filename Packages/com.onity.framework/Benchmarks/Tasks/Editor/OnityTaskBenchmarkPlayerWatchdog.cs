namespace Onity.Editor.Benchmarks
{
    internal enum PlayerBenchmarkTimeout
    {
        None,
        Startup,
        Measurement
    }

    // Pure deadline logic, separate from process polling and trace-file I/O.
    internal sealed class OnityTaskBenchmarkPlayerWatchdog
    {
        private const long k_startupTimeoutMilliseconds = 60000;
        private const long k_measurementTimeoutMilliseconds = 900000;

        private long m_benchmarkEntryMilliseconds = -1;

        internal bool HasEnteredBenchmark => m_benchmarkEntryMilliseconds >= 0;

        internal PlayerBenchmarkTimeout Check(long elapsedMilliseconds, bool hasEntryMarker)
        {
            if (!HasEnteredBenchmark && hasEntryMarker)
            {
                m_benchmarkEntryMilliseconds = elapsedMilliseconds;
            }

            if (!HasEnteredBenchmark)
            {
                return elapsedMilliseconds >= k_startupTimeoutMilliseconds
                    ? PlayerBenchmarkTimeout.Startup
                    : PlayerBenchmarkTimeout.None;
            }

            return elapsedMilliseconds - m_benchmarkEntryMilliseconds >= k_measurementTimeoutMilliseconds
                ? PlayerBenchmarkTimeout.Measurement
                : PlayerBenchmarkTimeout.None;
        }

        internal static bool HasEntryMarker(string trace)
        {
            return trace != null && trace.IndexOf("\tbenchmark-entry\t", System.StringComparison.Ordinal) >= 0;
        }
    }
}
