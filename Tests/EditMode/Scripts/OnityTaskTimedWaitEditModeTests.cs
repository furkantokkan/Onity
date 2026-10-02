using System;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.Networking;

namespace Onity.Tests.EditMode
{
    /// <summary>Phase T timed waits (A4): argument validation and the Play-only contract.</summary>
    public sealed class OnityTaskTimedWaitEditModeTests
    {
        [Test]
        public void TimedWaits_ValidateArgumentsBeforeTheSessionCheck()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.Delay(TimeSpan.FromTicks(-1), OnityDelayType.Realtime));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.Delay(TimeSpan.Zero, (OnityDelayType)3));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.WaitForSeconds(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.WaitForSeconds(-1));
            Assert.Throws<ArgumentNullException>(() => OnityTask.WaitUntil(null, OnityPlayerLoopTiming.Update));
            Assert.Throws<ArgumentNullException>(() => OnityTask.WaitWhile(null, OnityPlayerLoopTiming.Update));
            Assert.Throws<ArgumentNullException>(() => OnityTask.WaitUntil(1, null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.WaitWhile(1, null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.WaitUntilValueChanged<object, int>(null, value => 0));
            Assert.Throws<ArgumentNullException>(() => OnityTask.WaitUntilValueChanged<object, int>(new object(), null));
        }

        [Test]
        public void TimedWaits_RequireAPlaySession()
        {
            Assert.Throws<InvalidOperationException>(() => OnityTask.Delay(TimeSpan.FromSeconds(1), OnityDelayType.Realtime));
            Assert.Throws<InvalidOperationException>(() => OnityTask.Delay(TimeSpan.Zero, true));
            Assert.Throws<InvalidOperationException>(() => OnityTask.WaitUntil(() => true, OnityPlayerLoopTiming.Update));
            Assert.Throws<InvalidOperationException>(() => OnityTask.WaitUntilCanceled(CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => OnityTask.WaitUntilValueChanged(new object(), value => 0));
            Assert.Throws<InvalidOperationException>(() => OnityPlayerLoopTimer.StartNew(TimeSpan.Zero, false,
                OnityDelayType.Realtime, OnityPlayerLoopTiming.Update, CancellationToken.None, state => { }, null));
        }

        [Test]
        public void TimersAndTimeouts_ValidateTheirArguments()
        {
            Assert.Throws<ArgumentNullException>(() => OnityPlayerLoopTimer.Create(TimeSpan.Zero, false,
                OnityDelayType.DeltaTime, OnityPlayerLoopTiming.Update, CancellationToken.None, null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityPlayerLoopTimer.Create(TimeSpan.FromTicks(-1), false,
                OnityDelayType.DeltaTime, OnityPlayerLoopTiming.Update, CancellationToken.None, state => { }, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityPlayerLoopTimer.Create(TimeSpan.Zero, false,
                OnityDelayType.DeltaTime, (OnityPlayerLoopTiming)19, CancellationToken.None, state => { }, null));
            OnityPlayerLoopTimer timer = OnityPlayerLoopTimer.Create(TimeSpan.Zero, true, OnityDelayType.Realtime,
                OnityPlayerLoopTiming.Update, CancellationToken.None, state => { }, null);
            Assert.That(timer.IsRunning, Is.False);
            timer.Dispose();
            Assert.Throws<ObjectDisposedException>(() => timer.Restart());

            Assert.Throws<ArgumentNullException>(() => ((CancellationTokenSource)null).CancelAfterSlim(
                TimeSpan.Zero, OnityDelayType.DeltaTime));
            using (var source = new CancellationTokenSource())
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => source.CancelAfterSlim(-1));
                source.CancelAfterSlim(TimeSpan.Zero, OnityDelayType.Realtime);
                Assert.That(source.IsCancellationRequested, Is.True, "A zero delay cancels now.");
            }

            Assert.Throws<ArgumentNullException>(() => new OnityTimeoutController((CancellationTokenSource)null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OnityTimeoutController((OnityDelayType)7));
            using (var controller = new OnityTimeoutController(OnityDelayType.DeltaTime))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => controller.Timeout(-1));
                Assert.That(controller.Timeout(0).IsCancellationRequested, Is.True);
                Assert.That(controller.IsTimeout(), Is.True);
            }

            var pending = new OnityTaskCompletionSource();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => pending.Task.Timeout(TimeSpan.FromTicks(-1), OnityDelayType.DeltaTime));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => pending.Task.Timeout(TimeSpan.Zero, (OnityDelayType)3));
            Assert.Throws<InvalidOperationException>(
                () => pending.Task.Timeout(TimeSpan.FromSeconds(1), OnityDelayType.Realtime));
        }

        [Test]
        public void WebRequestException_ExposesTheUniTaskMembers()
        {
            using (var request = new UnityWebRequest("http://localhost/onity"))
            {
                var exception = new OnityUnityWebRequestException(request);
                Assert.That(exception.UnityWebRequest, Is.SameAs(request));
                Assert.That(exception.IsNetworkError, Is.EqualTo(request.result == UnityWebRequest.Result.ConnectionError));
                Assert.That(exception.IsHttpError, Is.EqualTo(request.result == UnityWebRequest.Result.ProtocolError));
                Assert.That(exception.Error, Is.EqualTo(request.error));
                Assert.That(exception.Text, Is.Null);
            }
        }

        [Test]
        public void SourceOnCompleted_OnACompletedTaskRunsInline()
        {
            object received = null;
            var state = new object();
            OnityTask.CompletedTask.GetAwaiter().SourceOnCompleted(value => received = value, state);
            Assert.That(received, Is.SameAs(state));
            OnityTask<int>.FromResult(1).GetAwaiter().SourceOnCompleted(value => received = null, state);
            Assert.That(received, Is.Null);
        }
    }
}
