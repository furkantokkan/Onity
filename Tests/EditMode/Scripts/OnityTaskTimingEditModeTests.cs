using System;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Tests.EditMode
{
    /// <summary>Phase T surface checks that need no Play session.</summary>
    public sealed class OnityTaskTimingEditModeTests
    {
        [Test]
        public void TimingValues_AreAppendedAndStable()
        {
            Assert.That(Enum.GetValues(typeof(OnityPlayerLoopTiming)).Length, Is.EqualTo(19));
            Assert.That((int)OnityPlayerLoopTiming.Update, Is.EqualTo(0));
            Assert.That((int)OnityPlayerLoopTiming.FixedUpdate, Is.EqualTo(1));
            Assert.That((int)OnityPlayerLoopTiming.LateUpdate, Is.EqualTo(2));
            Assert.That((int)OnityPlayerLoopTiming.Initialization, Is.EqualTo(3));
            Assert.That((int)OnityPlayerLoopTiming.FixedUpdateBegin, Is.EqualTo(7));
            Assert.That((int)OnityPlayerLoopTiming.UpdateBegin, Is.EqualTo(11));
            Assert.That((int)OnityPlayerLoopTiming.LastTimeUpdate, Is.EqualTo(18));
        }

        [Test]
        public void PublicEntryPoints_RejectNullsAndUndefinedTimings_BeforeTheSessionCheck()
        {
            var item = new NeverItem();
            Assert.Throws<ArgumentNullException>(() => OnityTaskPlayerLoop.AddAction(OnityPlayerLoopTiming.Update, null));
            Assert.Throws<ArgumentNullException>(() => OnityTaskPlayerLoop.AddContinuation(OnityPlayerLoopTiming.Update, null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.Post(null));
            Assert.Throws<ArgumentNullException>(() => OnityTaskPlayerLoop.Initialize((OnityPlayerLoopTiming[])null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.WaitForEndOfFrame((MonoBehaviour)null));
            Assert.Throws<ArgumentNullException>(
                () => OnityTask.WaitForEndOfFrame(null, CancellationToken.None, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTaskPlayerLoop.AddAction((OnityPlayerLoopTiming)99, item));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTaskPlayerLoop.IsInjected((OnityPlayerLoopTiming)19));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTaskPlayerLoop.Initialize((OnityPlayerLoopTiming)(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.SwitchToMainThread((OnityPlayerLoopTiming)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.ReturnToMainThread((OnityPlayerLoopTiming)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.DelayFrame(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.Yield((OnityPlayerLoopTiming)99));
        }

        [Test]
        public void TimingWaits_RequireAPlaySession()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                Assert.Throws<InvalidOperationException>(() => OnityTask.Yield(OnityPlayerLoopTiming.PreUpdate));
                Assert.Throws<InvalidOperationException>(() => OnityTask.Yield(cancellation.Token));
                Assert.Throws<InvalidOperationException>(() => OnityTask.Yield(cancellation.Token, true));
                Assert.Throws<InvalidOperationException>(() => OnityTask.NextFrame(OnityPlayerLoopTiming.PostLateUpdate));
                Assert.Throws<InvalidOperationException>(() => OnityTask.DelayFrame(0));
                Assert.Throws<InvalidOperationException>(() => OnityTask.WaitForFixedUpdate());
                Assert.Throws<InvalidOperationException>(() => OnityTask.Post(() => { }));
                Assert.Throws<InvalidOperationException>(
                    () => OnityTaskPlayerLoop.AddAction(OnityPlayerLoopTiming.EarlyUpdate, new NeverItem()));
                Assert.Throws<InvalidOperationException>(() => OnityTaskPlayerLoop.InitializeAll());
            }
        }

        [Test]
        public void MainThreadSwitches_CompleteSynchronouslyOnTheMainThread()
        {
            Assert.That(OnityTaskPlayerLoop.IsMainThread, Is.True);
            OnityTimingSwitchAwaiter timed = OnityTask.SwitchToMainThread(OnityPlayerLoopTiming.PreUpdate).GetAwaiter();
            Assert.That(timed.IsCompleted, Is.True);
            timed.GetResult();
            OnityReturnToMainThreadAwaiter untimed = OnityTask.ReturnToMainThread().DisposeAsync();
            Assert.That(untimed.IsCompleted, Is.True);
            untimed.GetResult();
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                OnityTimingSwitchAwaiter canceled = OnityTask.SwitchToMainThread(
                    OnityPlayerLoopTiming.Update, cancellation.Token).GetAwaiter();
                Assert.Throws<OperationCanceledException>(() => canceled.GetResult());
            }

            Assert.Throws<ArgumentNullException>(() => timed.OnCompleted(null));
        }

        private sealed class NeverItem : IOnityPlayerLoopItem
        {
            public bool MoveNext()
            {
                return false;
            }
        }
    }
}
