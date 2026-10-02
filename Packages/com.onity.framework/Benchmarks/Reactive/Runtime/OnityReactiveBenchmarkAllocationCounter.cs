using System;
using System.Globalization;

namespace Onity.Benchmarks
{
    /// <summary>
    /// <c>GC.GetAllocatedBytesForCurrentThread()</c> with a 64 KiB positive control and an empty control.
    /// When the positive control does not read at least 64 KiB, or the empty control is not zero, the
    /// allocation pass is skipped and reported unavailable. On IL2CPP the counter is not called at all:
    /// the DI harness observed crashes there, and the other benchmark counters skip it for that reason.
    /// </summary>
    internal sealed class OnityReactiveBenchmarkAllocationCounter
    {
        internal const string CounterName = "GC.GetAllocatedBytesForCurrentThread";

        private const int k_positiveControlBytes = 65536;

        private OnityReactiveBenchmarkAllocationCounter(bool available, long positiveBytes, long emptyBytes, string detail)
        {
            IsAvailable = available;
            PositiveControlBytes = positiveBytes;
            EmptyControlBytes = emptyBytes;
            Detail = detail;
        }

        /// <summary>True when both controls passed on this thread.</summary>
        internal bool IsAvailable { get; }

        /// <summary>Bytes read around the 64 KiB positive control.</summary>
        internal long PositiveControlBytes { get; }

        /// <summary>Bytes read for the empty control.</summary>
        internal long EmptyControlBytes { get; }

        /// <summary>Why the counter is or is not available.</summary>
        internal string Detail { get; }

        /// <summary>Runs both controls on the calling thread.</summary>
        internal static OnityReactiveBenchmarkAllocationCounter Calibrate()
        {
#if ENABLE_IL2CPP
            return new OnityReactiveBenchmarkAllocationCounter(false, 0, 0,
                "Unavailable on IL2CPP: the counter is not called because the DI harness observed IL2CPP crashes with it.");
#else
            try
            {
                GC.GetAllocatedBytesForCurrentThread();
                long before = GC.GetAllocatedBytesForCurrentThread();
                byte[] control = new byte[k_positiveControlBytes];
                long after = GC.GetAllocatedBytesForCurrentThread();
                GC.KeepAlive(control);
                long positiveBytes = after - before;
                before = GC.GetAllocatedBytesForCurrentThread();
                after = GC.GetAllocatedBytesForCurrentThread();
                long emptyBytes = after - before;
                bool available = positiveBytes >= k_positiveControlBytes && emptyBytes == 0;
                string detail = available
                    ? "Positive control (64 KiB array) and empty control passed."
                    : "Unavailable: the positive control read " + Format(positiveBytes) + " bytes for a 64 KiB array and the empty control read "
                        + Format(emptyBytes) + " bytes.";
                return new OnityReactiveBenchmarkAllocationCounter(available, positiveBytes, emptyBytes, detail);
            }
            catch (Exception exception)
            {
                return new OnityReactiveBenchmarkAllocationCounter(false, 0, 0,
                    "Unavailable: the counter threw " + exception.GetType().Name + ".");
            }
#endif
        }

        /// <summary>Reads the counter; only called when <see cref="IsAvailable"/> is true.</summary>
        internal long Read()
        {
#if ENABLE_IL2CPP
            return 0;
#else
            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }

        /// <summary>Copies the calibration into the report.</summary>
        internal OnityReactiveBenchmarkAllocationInfo ToReport()
        {
            return new OnityReactiveBenchmarkAllocationInfo
            {
                counter = CounterName,
                available = IsAvailable,
                positiveControlBytes = PositiveControlBytes,
                emptyControlBytes = EmptyControlBytes,
                detail = Detail
            };
        }

        private static string Format(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
