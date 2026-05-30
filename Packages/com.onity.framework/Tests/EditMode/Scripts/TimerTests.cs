using System;
using NUnit.Framework;
using Onity.Core;
using Onity.Reactive;
using Onity.Unity.Reactive;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class TimerTests
    {
        [Test]
        public void CountdownTimer_Tick_CompletesAndInvokesEvents()
        {
            using OnityCountdownTimer timer = new OnityCountdownTimer(1f, false, false);
            int startedCount = 0;
            int stoppedCount = 0;
            int completedCount = 0;

            timer.Started += () => startedCount++;
            timer.Stopped += () => stoppedCount++;
            timer.Completed += () => completedCount++;

            timer.Start();
            timer.Tick(0.25f);
            timer.Tick(0.75f);

            Assert.That(startedCount, Is.EqualTo(1));
            Assert.That(stoppedCount, Is.EqualTo(1));
            Assert.That(completedCount, Is.EqualTo(1));
            Assert.That(timer.IsRunning, Is.False);
            Assert.That(timer.CurrentTimeSeconds, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(timer.Progress01, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void IntervalTimer_Tick_EmitsExpectedIntervalCounts()
        {
            using OnityIntervalTimer timer = new OnityIntervalTimer(0.25f, false, false);
            int eventCallCount = 0;
            int latestTickIndex = 0;

            timer.IntervalElapsed +=
                tickIndex =>
                {
                    eventCallCount++;
                    latestTickIndex = tickIndex;
                };

            timer.Start();
            timer.Tick(0.10f);
            timer.Tick(0.20f);
            timer.Tick(0.50f);

            Assert.That(eventCallCount, Is.EqualTo(3));
            Assert.That(latestTickIndex, Is.EqualTo(3));
            Assert.That(timer.TickCount, Is.EqualTo(3));
            Assert.That(timer.IsRunning, Is.True);
        }

        [Test]
        public void StopwatchTimer_Tick_AccumulatesElapsedTime()
        {
            using OnityStopwatchTimer timer = new OnityStopwatchTimer(false, false);

            timer.Start();
            timer.Tick(0.3f);
            timer.Tick(0.2f);

            Assert.That(timer.CurrentTimeSeconds, Is.EqualTo(0.5f).Within(0.0001f));

            timer.Reset();
            Assert.That(timer.CurrentTimeSeconds, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void UnityObservableTimer_ZeroDelay_EmitsImmediately()
        {
            int emittedCount = 0;
            IDisposable subscription =
                OnityUnityObservable.Timer(0f).Subscribe(_ => emittedCount++);

            Assert.That(emittedCount, Is.EqualTo(1));
            subscription.Dispose();
        }

        [Test]
        public void UnityObservableDelay_ZeroDelay_ReturnsSourceReference()
        {
            Subject<Unit> source = new Subject<Unit>();

            try
            {
                IOnityObservable<Unit> delayed = source.Delay(0f);
                Assert.That(delayed, Is.SameAs(source));
            }
            finally
            {
                source.Dispose();
            }
        }

        [Test]
        public void UnityObservableEveryUpdate_SingleThreadMode_ReturnsSharedUpdateStream()
        {
            IOnityObservable<Unit> defaultStream = OnityUnityObservable.EveryUpdate();
            IOnityObservable<Unit> singleThreadStream =
                OnityUnityObservable.EveryUpdate(OnityUnityThreadMode.SingleThread);

            Assert.That(singleThreadStream, Is.SameAs(defaultStream));
        }

        [Test]
        public void UnityObservableEveryUpdate_JobMultiThread_InvalidWorkItemCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => OnityUnityObservable.EveryUpdate(OnityUnityThreadMode.JobMultiThread, 0));
        }

        [Test]
        public void UnityObservableEveryUpdate_JobMultiThread_ReturnsConfiguredObservable()
        {
            IOnityObservable<Unit> updateStream = OnityUnityObservable.EveryUpdate();
            IOnityObservable<Unit> jobStream =
                OnityUnityObservable.EveryUpdate(OnityUnityThreadMode.JobMultiThread, 8, 2);

            Assert.That(jobStream, Is.Not.Null);
            Assert.That(jobStream, Is.Not.SameAs(updateStream));
        }

        [Test]
        public void UnityObservableEveryUpdate_JobMultiThread_InvalidMinCommands_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => OnityUnityObservable.EveryUpdate(OnityUnityThreadMode.JobMultiThread, 8, 0));
        }

        [Test]
        public void UnityObservableEveryUpdate_BurstJobMultiThread_ReturnsConfiguredObservable()
        {
            IOnityObservable<Unit> updateStream = OnityUnityObservable.EveryUpdate();
            IOnityObservable<Unit> burstJobStream =
                OnityUnityObservable.EveryUpdate(OnityUnityThreadMode.BurstJobMultiThread, 8, 2);

            Assert.That(burstJobStream, Is.Not.Null);
            Assert.That(burstJobStream, Is.Not.SameAs(updateStream));
        }

        [Test]
        public void UnityObservableEveryUpdate_DotsEventDriven_ReturnsConfiguredObservable()
        {
            IOnityObservable<Unit> updateStream = OnityUnityObservable.EveryUpdate();
            IOnityObservable<Unit> dotsDrivenStream =
                OnityUnityObservable.EveryUpdate(OnityUnityThreadMode.DotsEventDriven, 8, 2);

            Assert.That(dotsDrivenStream, Is.Not.Null);
            Assert.That(dotsDrivenStream, Is.Not.SameAs(updateStream));
        }
    }
}
