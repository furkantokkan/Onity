using System;
using System.Reflection;
using Onity.Unity.Async;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Command-line options and public-API retention controls shared by the primary, builderlifecycle and
    /// throughput suites. Uses only public Onity APIs: <see cref="OnityTask.RunnerPoolCapacity"/> and, when
    /// the runtime declares it, a public static int <c>OnityTask.SourcePoolCapacity</c> property found by
    /// reflection.
    /// </summary>
    internal static class OnityTaskBenchmarkOptions
    {
        /// <summary>Marks a harness self-test: tiny sample counts, never used for ratios.</summary>
        internal const string SelfTestArgument = "-onityTaskBenchmarkSelfTest";

        /// <summary>Selects the Onity retention policy: default or matched.</summary>
        internal const string RetentionArgument = "-onityTaskBenchmarkRetention";

        private static PropertyInfo s_sourcePoolCapacity;
        private static bool s_sourcePoolCapacityProbed;

        /// <summary>True when the process was started with <see cref="SelfTestArgument"/>.</summary>
        internal static bool IsSelfTest()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], SelfTestArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads <see cref="RetentionArgument"/>. Absent means default. A trailing flag or a value other than
        /// default or matched throws, so a run can never be silently mislabelled.
        /// </summary>
        /// <returns>True for matched.</returns>
        internal static bool ReadMatchedRetention()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], RetentionArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (i == args.Length - 1)
                {
                    throw new ArgumentException(RetentionArgument + " requires a value (default or matched).");
                }
                if (string.Equals(args[i + 1], "matched", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (string.Equals(args[i + 1], "default", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                throw new ArgumentException(RetentionArgument + " must be default or matched.");
            }

            return false;
        }

        /// <summary>True when the runtime declares a public static read/write int OnityTask.SourcePoolCapacity.</summary>
        internal static bool SourcePoolCapacityAvailable => GetSourcePoolCapacityProperty() != null;

        /// <summary>The current source-pool capacity, or -1 when the property does not exist.</summary>
        internal static int ReadSourcePoolCapacity()
        {
            PropertyInfo property = GetSourcePoolCapacityProperty();
            return property == null ? -1 : (int)property.GetValue(null);
        }

        /// <summary>Sets the source-pool capacity when the property exists; otherwise does nothing.</summary>
        /// <param name="capacity">New capacity.</param>
        internal static void WriteSourcePoolCapacity(int capacity)
        {
            GetSourcePoolCapacityProperty()?.SetValue(null, capacity);
        }

        private static PropertyInfo GetSourcePoolCapacityProperty()
        {
            if (!s_sourcePoolCapacityProbed)
            {
                PropertyInfo property = typeof(OnityTask).GetProperty("SourcePoolCapacity", BindingFlags.Static | BindingFlags.Public);
                s_sourcePoolCapacity = property != null && property.PropertyType == typeof(int)
                    && property.GetGetMethod() != null && property.GetSetMethod() != null ? property : null;
                s_sourcePoolCapacityProbed = true;
            }

            return s_sourcePoolCapacity;
        }
    }

    /// <summary>
    /// Raises Onity's retention caps to a cohort and restores the values that were in force before. Only
    /// public APIs are touched.
    /// </summary>
    internal sealed class OnityTaskRetentionScope
    {
        private readonly int m_oldRunnerCapacity;
        private readonly int m_oldSourceCapacity;
        private bool m_active;

        private OnityTaskRetentionScope(int cohort)
        {
            m_oldRunnerCapacity = OnityTask.RunnerPoolCapacity;
            m_oldSourceCapacity = OnityTaskBenchmarkOptions.ReadSourcePoolCapacity();
            OnityTask.RunnerPoolCapacity = Math.Max(m_oldRunnerCapacity, cohort);
            if (m_oldSourceCapacity >= 0)
            {
                OnityTaskBenchmarkOptions.WriteSourcePoolCapacity(Math.Max(m_oldSourceCapacity, cohort));
            }

            RunnerPoolCapacity = OnityTask.RunnerPoolCapacity;
            SourcePoolCapacity = OnityTaskBenchmarkOptions.ReadSourcePoolCapacity();
            m_active = true;
        }

        /// <summary>Runner capacity in force inside the scope.</summary>
        internal int RunnerPoolCapacity { get; }

        /// <summary>Source capacity in force inside the scope, or -1 when the runtime has no such property.</summary>
        internal int SourcePoolCapacity { get; }

        /// <summary>True when the source pool was raised as well.</summary>
        internal bool SourcePoolMatched => SourcePoolCapacity >= 0;

        /// <summary>Raises both caps to at least <paramref name="cohort"/>.</summary>
        /// <param name="cohort">Concurrent operations of the arm.</param>
        internal static OnityTaskRetentionScope Raise(int cohort)
        {
            return new OnityTaskRetentionScope(cohort);
        }

        /// <summary>Restores the caps that were in force when the scope was raised. Idempotent.</summary>
        internal void Restore()
        {
            if (!m_active)
            {
                return;
            }

            m_active = false;
            OnityTask.RunnerPoolCapacity = m_oldRunnerCapacity;
            if (m_oldSourceCapacity >= 0)
            {
                OnityTaskBenchmarkOptions.WriteSourcePoolCapacity(m_oldSourceCapacity);
            }
        }
    }
}
