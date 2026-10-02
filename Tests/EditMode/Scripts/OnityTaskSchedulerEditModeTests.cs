using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="OnityTaskScheduler"/> through the paths that publish to it: faults of
    /// <c>async OnityTaskVoid</c> methods, <c>Forget</c> of .NET-task-backed and tracked tasks, and
    /// the <c>Forget</c> overload with a main-thread flag. The event, the cancellation filter, the
    /// log type and the main-thread dispatch are each checked.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskSchedulerEditModeTests
    {
        private const int k_timeoutSeconds = 10;

        private readonly object m_gate = new object();
        private readonly List<Exception> m_received = new List<Exception>();
        private readonly List<int> m_receivedThreads = new List<int>();
        private bool m_previousPropagate;
        private bool m_previousDispatch;
        private LogType m_previousLogType;
        private bool m_previousTracking;

        [SetUp]
        public void SaveSettings()
        {
            m_previousPropagate = OnityTaskScheduler.PropagateOperationCanceledException;
            m_previousDispatch = OnityTaskScheduler.DispatchUnityMainThread;
            m_previousLogType = OnityTaskScheduler.UnobservedExceptionWriteLogType;
            m_previousTracking = OnityTaskTracker.IsEnabled;
            m_received.Clear();
            m_receivedThreads.Clear();
        }

        [TearDown]
        public void RestoreSettings()
        {
            OnityTaskScheduler.UnobservedTaskException -= Record;
            OnityTaskScheduler.PropagateOperationCanceledException = m_previousPropagate;
            OnityTaskScheduler.DispatchUnityMainThread = m_previousDispatch;
            OnityTaskScheduler.UnobservedExceptionWriteLogType = m_previousLogType;
            OnityTaskTracker.IsEnabled = m_previousTracking;
            OnityTaskTracker.ClearAll();
        }

        private void Subscribe()
        {
            OnityTaskScheduler.UnobservedTaskException += Record;
        }

        private void Record(Exception exception)
        {
            lock (m_gate)
            {
                m_received.Add(exception);
                m_receivedThreads.Add(Thread.CurrentThread.ManagedThreadId);
            }
        }

        private int ReceivedCount
        {
            get
            {
                lock (m_gate)
                {
                    return m_received.Count;
                }
            }
        }

        private static async OnityTaskVoid FailAsync(Exception exception)
        {
            await OnityTask.CompletedTask;
            throw exception;
        }

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed.TotalSeconds < k_timeoutSeconds)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, "The scheduler publication timed out.");
        }

        [Test]
        public void Defaults_MatchUniTask()
        {
            Assert.That(OnityTaskScheduler.PropagateOperationCanceledException, Is.False);
            Assert.That(OnityTaskScheduler.UnobservedExceptionWriteLogType, Is.EqualTo(LogType.Exception));
            Assert.That(OnityTaskScheduler.DispatchUnityMainThread, Is.True);
        }

        [Test]
        public void VoidMethod_Fault_RaisesTheEventWithTheException()
        {
            Subscribe();
            InvalidOperationException failure = new InvalidOperationException("void fault");

            FailAsync(failure).Forget();
            Assert.That(m_received, Is.EqualTo(new[] { failure }));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void VoidMethod_FaultWithoutSubscriber_LogsTheException()
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: logged by default"));

            FailAsync(new InvalidOperationException("logged by default")).Forget();
        }

        [Test]
        public void VoidMethod_Cancellation_IsDroppedByDefault()
        {
            Subscribe();

            FailAsync(new OperationCanceledException()).Forget();
            FailAsync(new TaskCanceledException()).Forget();
            Assert.That(ReceivedCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void VoidMethod_Cancellation_IsPublishedWhenPropagating()
        {
            Subscribe();
            OnityTaskScheduler.PropagateOperationCanceledException = true;
            OperationCanceledException canceled = new OperationCanceledException("canceled");

            FailAsync(canceled).Forget();
            FailAsync(new TaskCanceledException()).Forget();
            Assert.That(m_received.Count, Is.EqualTo(2));
            Assert.That(m_received[0], Is.SameAs(canceled));
            Assert.That(m_received[1], Is.InstanceOf<TaskCanceledException>());
        }

        [Test]
        public void VoidMethod_CancellationWithoutSubscriber_IsLoggedOnlyWhenPropagating()
        {
            OnityTaskScheduler.PropagateOperationCanceledException = true;
            LogAssert.Expect(LogType.Exception, new Regex("OperationCanceledException: propagated"));

            FailAsync(new OperationCanceledException("propagated")).Forget();
        }

        [TestCase(LogType.Error)]
        [TestCase(LogType.Assert)]
        [TestCase(LogType.Warning)]
        [TestCase(LogType.Log)]
        public void WriteLogType_UsesTheMatchingUnityLogCall(LogType logType)
        {
            OnityTaskScheduler.UnobservedExceptionWriteLogType = logType;
            LogAssert.Expect(
                logType,
                new Regex("Unobserved OnityTask exception: .*InvalidOperationException: typed log"));

            FailAsync(new InvalidOperationException("typed log")).Forget();
        }

        [Test]
        public void Subscribers_ThrowingSubscriberDoesNotStopTheOthers()
        {
            Action<Exception> throwing = _ => throw new InvalidOperationException("subscriber failed");
            OnityTaskScheduler.UnobservedTaskException += throwing;
            Subscribe();
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: subscriber failed"));
                InvalidOperationException failure = new InvalidOperationException("published");

                FailAsync(failure).Forget();
                Assert.That(m_received, Is.EqualTo(new[] { failure }));
            }
            finally
            {
                OnityTaskScheduler.UnobservedTaskException -= throwing;
            }
        }

        [Test]
        public void TaskForget_WithoutHandler_PublishesFaultsAndDropsCancellation()
        {
            Subscribe();
            InvalidOperationException failure = new InvalidOperationException("task fault");
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Task.FromException(failure).Forget();
            Task.FromCanceled(cancellation.Token).Forget();
            Task.FromException<int>(failure).Forget();
            Task.FromCanceled<int>(cancellation.Token).Forget();

            Assert.That(m_received, Is.EqualTo(new[] { failure, failure }));
        }

        [Test]
        public void TaskForget_WithHandler_BypassesTheScheduler()
        {
            Subscribe();
            Exception handled = null;
            InvalidOperationException failure = new InvalidOperationException("handled");

            Task.FromException(failure).Forget(exception => handled = exception);

            Assert.That(handled, Is.SameAs(failure));
            Assert.That(ReceivedCount, Is.Zero);
        }

        [Test]
        public void OnityTaskForget_DotNetTaskBackedTask_PublishesToTheScheduler()
        {
            Subscribe();
            InvalidOperationException failure = new InvalidOperationException("backed fault");

            OnityTask.FromException(failure).Forget();
            OnityTask.FromCanceled(new CancellationToken(true)).Forget();

            Assert.That(m_received, Is.EqualTo(new[] { failure }));
        }

        [UnityTest]
        public IEnumerator OnityTaskForget_SingleConsumerTaskWithTrackingEnabled_PublishesToTheScheduler()
        {
            Subscribe();
            OnityTaskTracker.IsEnabled = true;
            OnityTaskTracker.ClearAll();
            InvalidOperationException failure = new InvalidOperationException("tracked fault");
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();

            source.Task.Forget();
            source.TrySetException(failure);
            yield return WaitFor(() => ReceivedCount == 1);

            Assert.That(m_received, Is.EqualTo(new[] { failure }));
        }

        [Test]
        public void ForgetWithHandler_Fault_PassesTheExceptionToTheHandler()
        {
            Subscribe();
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            Exception handled = null;
            InvalidOperationException failure = new InvalidOperationException("handler fault");

            source.Task.Forget(exception => handled = exception, true);
            Assert.That(handled, Is.Null);
            source.TrySetException(failure);

            Assert.That(handled, Is.SameAs(failure));
            Assert.That(ReceivedCount, Is.Zero, "A handled fault must not reach the scheduler.");
        }

        [Test]
        public void ForgetWithHandler_Cancellation_IsPassedToTheHandler()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            Exception handled = null;

            source.Task.Forget(exception => handled = exception, false);
            source.TrySetCanceled(cancellation.Token);

            Assert.That(handled, Is.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void ForgetWithHandler_Success_DoesNotCallTheHandler()
        {
            Subscribe();
            int calls = 0;

            OnityTask.CompletedTask.Forget(_ => calls++, true);
            OnityTask.FromResult(1).Forget(_ => calls++, false);

            Assert.That(calls, Is.Zero);
            Assert.That(ReceivedCount, Is.Zero);
        }

        [Test]
        public void ForgetWithHandler_NullHandler_FallsBackToTheSchedulerPath()
        {
            Subscribe();
            InvalidOperationException failure = new InvalidOperationException("no handler");

            OnityTask.FromException(failure).Forget(null, true);
            OnityTask.FromException<int>(failure).Forget(null, false);

            Assert.That(m_received, Is.EqualTo(new[] { failure, failure }));
        }

        [Test]
        public void ForgetWithHandler_HandlerThrows_PublishesTheHandlerFailure()
        {
            Subscribe();
            InvalidOperationException handlerFailure = new InvalidOperationException("handler failed");
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            source.Task.Forget(_ => throw handlerFailure, true);
            source.TrySetException(new InvalidOperationException("original"));

            Assert.That(m_received, Is.EqualTo(new[] { handlerFailure }));
        }

        [Test]
        public void Publish_FromWorkerWithoutDispatch_RunsSubscribersOnTheWorkerThread()
        {
            Subscribe();
            OnityTaskScheduler.DispatchUnityMainThread = false;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int workerThread = 0;

            Task.Run(() =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                FailAsync(new InvalidOperationException("from worker")).Forget();
            }).Wait();

            Assert.That(ReceivedCount, Is.EqualTo(1));
            Assert.That(m_receivedThreads[0], Is.EqualTo(workerThread));
            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator Publish_FromWorkerWithDispatch_RunsSubscribersOnTheMainThread()
        {
            Subscribe();
            OnityTaskScheduler.DispatchUnityMainThread = true;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int workerThread = 0;

            Task worker = Task.Run(() =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                FailAsync(new InvalidOperationException("dispatched")).Forget();
            });
            yield return WaitFor(() => worker.IsCompleted && ReceivedCount == 1);

            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
            Assert.That(m_receivedThreads[0], Is.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator ForgetWithHandler_FromWorkerWithMainThreadFlag_CallsTheHandlerOnTheMainThread()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            int handlerThread = 0;
            int workerThread = 0;

            source.Task.Forget(_ => Volatile.Write(ref handlerThread, Thread.CurrentThread.ManagedThreadId), true);
            Task worker = Task.Run(() =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                source.TrySetException(new InvalidOperationException("late"));
            });
            yield return WaitFor(() => worker.IsCompleted && Volatile.Read(ref handlerThread) != 0);

            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
            Assert.That(handlerThread, Is.EqualTo(mainThread));
        }

        [Test]
        public void ForgetWithHandler_FromWorkerWithoutMainThreadFlag_CallsTheHandlerOnTheWorkerThread()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            int handlerThread = 0;
            int workerThread = 0;

            source.Task.Forget(_ => handlerThread = Thread.CurrentThread.ManagedThreadId, false);
            Task.Run(() =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                source.TrySetException(new InvalidOperationException("late"));
            }).Wait();

            Assert.That(handlerThread, Is.EqualTo(workerThread));
        }
    }
}
