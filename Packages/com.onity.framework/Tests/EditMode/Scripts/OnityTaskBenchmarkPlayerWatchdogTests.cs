#if ONITY_TASK_BENCHMARKS
using System;
using System.Reflection;
using NUnit.Framework;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers Player startup and measurement deadlines without launching Unity or a process.
    /// Reflection preserves the existing test assembly's benchmark-independent references.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskBenchmarkPlayerWatchdogTests
    {
        private Type m_watchdogType;
        private object m_watchdog;

        [SetUp]
        public void CreateWatchdog()
        {
            m_watchdogType = Assembly.Load("Onity.TaskBenchmarks.Editor").GetType(
                "Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerWatchdog", true);
            m_watchdog = Activator.CreateInstance(m_watchdogType, true);
        }

        [Test]
        public void MissingEntry_TimesOutAtSixtySeconds()
        {
            Assert.That(Check(59999, false), Is.EqualTo("None"));
            Assert.That(Check(60000, false), Is.EqualTo("Startup"));
        }

        [Test]
        public void Entry_StartsIndependentFifteenMinuteDeadline()
        {
            Assert.That(Check(59000, true), Is.EqualTo("None"));
            Assert.That(Check(60000, false), Is.EqualTo("None"));
            Assert.That(Check(958999, false), Is.EqualTo("None"));
            Assert.That(Check(959000, false), Is.EqualTo("Measurement"));
        }

        [Test]
        public void RepeatedEntryObservation_DoesNotExtendMeasurementDeadline()
        {
            Assert.That(Check(1000, true), Is.EqualTo("None"));
            Assert.That(Check(500000, true), Is.EqualTo("None"));
            Assert.That(Check(901000, true), Is.EqualTo("Measurement"));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("timestamp\tafter-scene-load\t\n", false)]
        [TestCase("timestamp\tbenchmark-failed\tbenchmark-entry\n", false)]
        [TestCase("timestamp\tbenchmark-entry", false)]
        [TestCase("timestamp\tbenchmark-entry\tprimary\n", true)]
        public void Handshake_RequiresExactDelimitedEntryMarker(string trace, bool expected)
        {
            MethodInfo method = m_watchdogType.GetMethod("HasEntryMarker", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method.Invoke(null, new object[] { trace }), Is.EqualTo(expected));
        }

        private string Check(long elapsedMilliseconds, bool hasEntryMarker)
        {
            MethodInfo method = m_watchdogType.GetMethod("Check", BindingFlags.Instance | BindingFlags.NonPublic);
            return method.Invoke(m_watchdog, new object[] { elapsedMilliseconds, hasEntryMarker }).ToString();
        }
    }
}
#endif
