using System;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Scenario table of the messaging comparison. The rows, operation counts and the golden model were
    /// fixed before any measurement. Both libraries run the same message sequence; the expected totals come
    /// from <see cref="OnityMessagingBenchmarkGolden"/>, which uses no messaging library.
    /// </summary>
    internal static class OnityMessagingBenchmarkScenarios
    {
        internal const string PublishNoSubscribers = "PublishNoSubscribers";
        internal const string Publish1 = "Publish1";
        internal const string Publish8 = "Publish8";
        internal const string SubscribeDispose = "SubscribeDispose";
        internal const string SubscribeDispose64 = "SubscribeDispose64";
        internal const string KeyedPublish = "KeyedPublish";
        internal const string KeyedSubscribeDispose = "KeyedSubscribeDispose";
        internal const string AsyncPublish1 = "AsyncPublish1";
        internal const string AsyncPublish8 = "AsyncPublish8";

        /// <summary>Self-test runs use the default operation count divided by this value.</summary>
        internal const int SelfTestDivisor = 1000;

        internal static readonly OnityMessagingBenchmarkScenario[] All =
        {
            new OnityMessagingBenchmarkScenario(PublishNoSubscribers,
                "channel or broker that never had a subscriber; timed: Publish(new Msg(i))", 200000),
            new OnityMessagingBenchmarkScenario(Publish1,
                "1 subscriber; timed: Publish(new Msg(i))", 200000),
            new OnityMessagingBenchmarkScenario(Publish8,
                "8 subscribers; timed: Publish(new Msg(i))", 50000),
            new OnityMessagingBenchmarkScenario(SubscribeDispose,
                "1 resident subscriber; timed: Subscribe(cached) then Dispose() of that subscription", 20000),
            new OnityMessagingBenchmarkScenario(SubscribeDispose64,
                "64 resident subscribers; timed: Subscribe(cached) then Dispose() of that subscription", 20000),
            new OnityMessagingBenchmarkScenario(KeyedPublish,
                "keys 0..15, 1 subscriber each; timed: Publish(i & 15, new Msg(i))", 200000),
            new OnityMessagingBenchmarkScenario(KeyedSubscribeDispose,
                "keys 0..15, 1 resident subscriber each; timed: Subscribe(i & 15, cached) then Dispose()", 20000),
            new OnityMessagingBenchmarkScenario(AsyncPublish1,
                "1 async handler; timed: PublishAsync(new Msg(i), CancellationToken.None) consumed synchronously", 100000),
            new OnityMessagingBenchmarkScenario(AsyncPublish8,
                "8 async handlers; timed: PublishAsync(new Msg(i), CancellationToken.None) consumed synchronously", 25000)
        };

        /// <summary>Returns the scenario with <paramref name="id"/> (ordinal, case-insensitive) or null.</summary>
        internal static OnityMessagingBenchmarkScenario Find(string id)
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
    internal sealed class OnityMessagingBenchmarkScenario
    {
        internal OnityMessagingBenchmarkScenario(string id, string description, int operations)
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
        internal int SelfTestOperations => Math.Max(1, Operations / OnityMessagingBenchmarkScenarios.SelfTestDivisor);
    }

    /// <summary>What a scenario's sink must have received, computed without any messaging library.</summary>
    internal readonly struct OnityMessagingBenchmarkExpectation
    {
        internal OnityMessagingBenchmarkExpectation(long checksum, long notifications, long probeChecksum, long probeNotifications)
        {
            Checksum = checksum;
            Notifications = notifications;
            ProbeChecksum = probeChecksum;
            ProbeNotifications = probeNotifications;
        }

        /// <summary>Sum of the payloads received during the timed loop.</summary>
        internal long Checksum { get; }

        /// <summary>Number of messages received during the timed loop.</summary>
        internal long Notifications { get; }

        /// <summary>Sum received for the untimed probe published after the timed loop.</summary>
        internal long ProbeChecksum { get; }

        /// <summary>Number of handler calls the untimed probe caused.</summary>
        internal long ProbeNotifications { get; }
    }

    /// <summary>
    /// Library-independent model of every scenario. The probe is one message published after the timed
    /// loop (outside timing; keyed rows publish it to key <see cref="ProbeKey"/>). It proves that the
    /// graph is still connected and, after a subscribe/dispose loop, that every transient subscription was
    /// really removed: only resident subscribers may receive it.
    /// </summary>
    internal static class OnityMessagingBenchmarkGolden
    {
        /// <summary>Probe payload: negative, so it differs from every timed payload.</summary>
        internal const int ProbeValue = -2;

        /// <summary>Key the keyed rows publish the probe to.</summary>
        internal const int ProbeKey = 0;

        /// <summary>Subscriber count of <c>Publish8</c> and <c>AsyncPublish8</c>.</summary>
        internal const int ManySubscribers = 8;

        /// <summary>Resident subscriber count of <c>SubscribeDispose64</c>.</summary>
        internal const int ManyResidents = 64;

        /// <summary>Number of keys of the keyed rows (keys 0..15).</summary>
        internal const int KeyCount = 16;

        /// <summary>Mask that maps operation i to its key: <c>i &amp; 15</c>.</summary>
        internal const int KeyMask = KeyCount - 1;

        /// <summary>Computes the expected sink totals for <paramref name="operations"/> timed operations.</summary>
        internal static OnityMessagingBenchmarkExpectation Compute(string scenarioId, int operations)
        {
            switch (scenarioId)
            {
                case OnityMessagingBenchmarkScenarios.PublishNoSubscribers:
                    // Nobody subscribed: neither the timed messages nor the probe reach a handler.
                    return new OnityMessagingBenchmarkExpectation(0, 0, 0, 0);
                case OnityMessagingBenchmarkScenarios.Publish1:
                case OnityMessagingBenchmarkScenarios.AsyncPublish1:
                    return Broadcast(operations, 1);
                case OnityMessagingBenchmarkScenarios.Publish8:
                case OnityMessagingBenchmarkScenarios.AsyncPublish8:
                    return Broadcast(operations, ManySubscribers);
                case OnityMessagingBenchmarkScenarios.SubscribeDispose:
                    // Nothing is published in the timed loop; the probe reaches the resident subscriber only.
                    return new OnityMessagingBenchmarkExpectation(0, 0, ProbeValue, 1);
                case OnityMessagingBenchmarkScenarios.SubscribeDispose64:
                    return new OnityMessagingBenchmarkExpectation(0, 0, (long)ProbeValue * ManyResidents, ManyResidents);
                case OnityMessagingBenchmarkScenarios.KeyedPublish:
                    return Keyed(operations);
                case OnityMessagingBenchmarkScenarios.KeyedSubscribeDispose:
                    // Nothing is published in the timed loop; the probe reaches the resident of key 0 only.
                    return new OnityMessagingBenchmarkExpectation(0, 0, ProbeValue, 1);
                default:
                    throw new ArgumentException("Unknown messaging benchmark scenario: " + scenarioId, nameof(scenarioId));
            }
        }

        // Every timed message reaches each of the subscribers; so does the probe.
        private static OnityMessagingBenchmarkExpectation Broadcast(int operations, int subscribers)
        {
            long sum = 0;
            long count = 0;
            for (int i = 0; i < operations; i++)
            {
                sum += (long)i * subscribers;
                count += subscribers;
            }

            return new OnityMessagingBenchmarkExpectation(sum, count, (long)ProbeValue * subscribers, subscribers);
        }

        // Keys 0..15 hold one subscriber each; message i goes to key i & 15 and reaches only that key's
        // subscribers. The probe goes to key 0.
        private static OnityMessagingBenchmarkExpectation Keyed(int operations)
        {
            int[] subscribersPerKey = new int[KeyCount];
            for (int key = 0; key < KeyCount; key++)
            {
                subscribersPerKey[key] = 1;
            }

            long sum = 0;
            long count = 0;
            for (int i = 0; i < operations; i++)
            {
                int subscribers = subscribersPerKey[i & KeyMask];
                sum += (long)i * subscribers;
                count += subscribers;
            }

            int probeSubscribers = subscribersPerKey[ProbeKey];
            return new OnityMessagingBenchmarkExpectation(sum, count, (long)ProbeValue * probeSubscribers, probeSubscribers);
        }
    }
}
