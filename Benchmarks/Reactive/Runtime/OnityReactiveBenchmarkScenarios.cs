using System;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Scenario table of the reactive comparison. Every library runs the same value sequence; the
    /// expected sink totals come from <see cref="OnityReactiveBenchmarkGolden"/>, which uses no library.
    /// </summary>
    internal static class OnityReactiveBenchmarkScenarios
    {
        internal const string SubjectOnNext1 = "SubjectOnNext1";
        internal const string SubjectOnNext8 = "SubjectOnNext8";
        internal const string SubjectSubscribeDispose = "SubjectSubscribeDispose";
        internal const string PropertySetChanged = "PropertySetChanged";
        internal const string PropertySetSame = "PropertySetSame";
        internal const string PropertySubscribeDispose = "PropertySubscribeDispose";
        internal const string WhereSelectOnNext = "WhereSelectOnNext";
        internal const string ChainSubscribeDispose = "ChainSubscribeDispose";
        internal const string CombineLatestOnNext = "CombineLatestOnNext";

        /// <summary>Self-test runs use the default operation count divided by this value.</summary>
        internal const int SelfTestDivisor = 1000;

        internal static readonly OnityReactiveBenchmarkScenario[] All =
        {
            new OnityReactiveBenchmarkScenario(SubjectOnNext1,
                "subject + 1 subscriber; timed: subject.OnNext(i)", 200000),
            new OnityReactiveBenchmarkScenario(SubjectOnNext8,
                "subject + 8 subscribers; timed: subject.OnNext(i)", 50000),
            new OnityReactiveBenchmarkScenario(SubjectSubscribeDispose,
                "subject + 1 resident subscriber; timed: Subscribe(cachedAction) then Dispose()", 20000),
            new OnityReactiveBenchmarkScenario(PropertySetChanged,
                "property = 0 + 1 subscriber; timed: Value = i + 1 (always a new value)", 200000),
            new OnityReactiveBenchmarkScenario(PropertySetSame,
                "property = 42 + 1 subscriber; timed: Value = 42 (no notification)", 200000),
            new OnityReactiveBenchmarkScenario(PropertySubscribeDispose,
                "property = 7; timed: Subscribe(cachedAction) (receives 7) then Dispose()", 20000),
            new OnityReactiveBenchmarkScenario(WhereSelectOnNext,
                "subject.Where(x => (x & 1) == 0).Select(x => x * 2).Subscribe(sink); timed: subject.OnNext(i)", 200000),
            new OnityReactiveBenchmarkScenario(ChainSubscribeDispose,
                "subject, cached predicate/selector/sink; timed: Where().Select().Subscribe() then Dispose()", 10000),
            new OnityReactiveBenchmarkScenario(CombineLatestOnNext,
                "a.CombineLatest(b, (x, y) => x + y).Subscribe(sink), both primed; timed: a.OnNext(i) / b.OnNext(i) alternately", 100000)
        };

        /// <summary>Returns the scenario with <paramref name="id"/> (ordinal, case-insensitive) or null.</summary>
        internal static OnityReactiveBenchmarkScenario Find(string id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return All[i];
                }
            }

            return null;
        }
    }

    /// <summary>One row of the scenario table.</summary>
    internal sealed class OnityReactiveBenchmarkScenario
    {
        internal OnityReactiveBenchmarkScenario(string id, string description, int operations)
        {
            Id = id;
            Description = description;
            Operations = operations;
        }

        /// <summary>Stable scenario id used on the command line and in reports.</summary>
        internal string Id { get; }

        /// <summary>Untimed setup and timed operation in words.</summary>
        internal string Description { get; }

        /// <summary>Timed operations per sample in a measurement run.</summary>
        internal int Operations { get; }

        /// <summary>Timed operations per sample in a self-test run.</summary>
        internal int SelfTestOperations => Math.Max(1, Operations / OnityReactiveBenchmarkScenarios.SelfTestDivisor);
    }

    /// <summary>What a scenario's sink must have received, computed without any reactive library.</summary>
    internal readonly struct OnityReactiveBenchmarkExpectation
    {
        internal OnityReactiveBenchmarkExpectation(long checksum, long notifications, long probeChecksum, long probeNotifications)
        {
            Checksum = checksum;
            Notifications = notifications;
            ProbeChecksum = probeChecksum;
            ProbeNotifications = probeNotifications;
        }

        /// <summary>Sum of the values received during the timed loop.</summary>
        internal long Checksum { get; }

        /// <summary>Number of values received during the timed loop.</summary>
        internal long Notifications { get; }

        /// <summary>Sum received for the untimed probe pushed after the timed loop.</summary>
        internal long ProbeChecksum { get; }

        /// <summary>Number of values received for the untimed probe.</summary>
        internal long ProbeNotifications { get; }
    }

    /// <summary>
    /// Library-independent model of every scenario. The probe is one value pushed after the timed loop
    /// (outside timing); it proves the graph is still connected (or, after subscribe/dispose loops,
    /// that every transient subscription was really removed).
    /// </summary>
    internal static class OnityReactiveBenchmarkGolden
    {
        /// <summary>Probe value: even (passes Where), negative (differs from every timed value).</summary>
        internal const int ProbeValue = -2;

        /// <summary>Initial and timed value of <c>PropertySetSame</c>.</summary>
        internal const int SameValue = 42;

        /// <summary>Initial value of <c>PropertySubscribeDispose</c>.</summary>
        internal const int SubscribeValue = 7;

        /// <summary>Initial value of <c>PropertySetChanged</c>.</summary>
        internal const int ChangedInitialValue = 0;

        /// <summary>Untimed priming value of the first CombineLatest source.</summary>
        internal const int PrimeFirst = 1;

        /// <summary>Untimed priming value of the second CombineLatest source.</summary>
        internal const int PrimeSecond = 2;

        /// <summary>Subscriber count of <c>SubjectOnNext8</c>.</summary>
        internal const int ManySubscribers = 8;

        /// <summary>Computes the expected sink totals for <paramref name="operations"/> timed operations.</summary>
        internal static OnityReactiveBenchmarkExpectation Compute(string scenarioId, int operations)
        {
            long sum = 0;
            long count = 0;
            switch (scenarioId)
            {
                case OnityReactiveBenchmarkScenarios.SubjectOnNext1:
                    for (int i = 0; i < operations; i++)
                    {
                        sum += i;
                        count++;
                    }

                    return new OnityReactiveBenchmarkExpectation(sum, count, ProbeValue, 1);
                case OnityReactiveBenchmarkScenarios.SubjectOnNext8:
                    for (int i = 0; i < operations; i++)
                    {
                        sum += (long)i * ManySubscribers;
                        count += ManySubscribers;
                    }

                    return new OnityReactiveBenchmarkExpectation(sum, count, (long)ProbeValue * ManySubscribers, ManySubscribers);
                case OnityReactiveBenchmarkScenarios.SubjectSubscribeDispose:
                    // Nothing is pushed in the timed loop; the probe reaches the resident subscriber only.
                    return new OnityReactiveBenchmarkExpectation(0, 0, ProbeValue, 1);
                case OnityReactiveBenchmarkScenarios.PropertySetChanged:
                    for (int i = 0; i < operations; i++)
                    {
                        sum += i + 1;
                        count++;
                    }

                    return new OnityReactiveBenchmarkExpectation(sum, count, ProbeValue, 1);
                case OnityReactiveBenchmarkScenarios.PropertySetSame:
                    return new OnityReactiveBenchmarkExpectation(0, 0, ProbeValue, 1);
                case OnityReactiveBenchmarkScenarios.PropertySubscribeDispose:
                    for (int i = 0; i < operations; i++)
                    {
                        sum += SubscribeValue;
                        count++;
                    }

                    // No resident subscriber: the probe must reach nobody.
                    return new OnityReactiveBenchmarkExpectation(sum, count, 0, 0);
                case OnityReactiveBenchmarkScenarios.WhereSelectOnNext:
                    for (int i = 0; i < operations; i++)
                    {
                        if ((i & 1) == 0)
                        {
                            sum += i * 2L;
                            count++;
                        }
                    }

                    return new OnityReactiveBenchmarkExpectation(sum, count, ProbeValue * 2L, 1);
                case OnityReactiveBenchmarkScenarios.ChainSubscribeDispose:
                    return new OnityReactiveBenchmarkExpectation(0, 0, 0, 0);
                case OnityReactiveBenchmarkScenarios.CombineLatestOnNext:
                    long latestFirst = PrimeFirst;
                    long latestSecond = PrimeSecond;
                    for (int i = 0; i < operations; i++)
                    {
                        if ((i & 1) == 0)
                        {
                            latestFirst = i;
                        }
                        else
                        {
                            latestSecond = i;
                        }

                        sum += latestFirst + latestSecond;
                        count++;
                    }

                    // The probe goes to the first source.
                    return new OnityReactiveBenchmarkExpectation(sum, count, ProbeValue + latestSecond, 1);
                default:
                    throw new ArgumentException("Unknown reactive benchmark scenario: " + scenarioId, nameof(scenarioId));
            }
        }
    }
}
