using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Covers <see cref="OnityTaskScheduler"/> against the real player loop: faults published after
    /// a frame wait and from worker threads reach subscribers on Unity's main thread, and the
    /// <c>Forget</c> overload with a main-thread flag resumes there before calling its handler.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskSchedulerPlayModeTests
    {
        private const int k_timeoutSeconds = 10;

        private readonly object m_gate = new object();
        private readonly List<Exception> m_received = new List<Exception>();
        private readonly List<int> m_receivedThreads = new List<int>();
        private bool m_previousPropagate;
        private bool m_previousDispatch;

        [SetUp]
        public void SaveSettings()
        {
            m_previousPropagate = OnityTaskScheduler.PropagateOperationCanceledException;
            m_previousDispatch = OnityTaskScheduler.DispatchUnityMainThread;
            m_received.Clear();
            m_receivedThreads.Clear();
            OnityTaskScheduler.UnobservedTaskException += Record;
        }

        [TearDown]
        public void RestoreSettings()
        {
            OnityTaskScheduler.UnobservedTaskException -= Record;
            OnityTaskScheduler.PropagateOperationCanceledException = m_previousPropagate;
            OnityTaskScheduler.DispatchUnityMainThread = m_previousDispatch;
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

        private static async OnityTaskVoid FailAfterFrameAsync(Exception exception)
        {
            await OnityTask.NextFrame();
            throw exception;
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

        [UnityTest]
        public IEnumerator VoidMethodFaultingAfterAFrame_PublishesOnTheMainThread()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            InvalidOperationException failure = new InvalidOperationException("after frame");

            FailAfterFrameAsync(failure).Forget();
            Assert.That(ReceivedCount, Is.Zero, "The method must suspend on the frame wait first.");
            yield return WaitFor(() => ReceivedCount == 1);

            Assert.That(m_received[0], Is.SameAs(failure));
            Assert.That(m_receivedThreads[0], Is.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator VoidMethodCancelingAfterAFrame_IsDroppedUnlessPropagating()
        {
            FailAfterFrameAsync(new OperationCanceledException()).Forget();
            yield return WaitFrames(3);
            Assert.That(ReceivedCount, Is.Zero);

            OnityTaskScheduler.PropagateOperationCanceledException = true;
            FailAfterFrameAsync(new OperationCanceledException()).Forget();
            yield return WaitFor(() => ReceivedCount == 1);

            Assert.That(m_received[0], Is.InstanceOf<OperationCanceledException>());
        }

        [UnityTest]
        public IEnumerator Publish_FromWorkerWithDispatch_RunsSubscribersOnTheMainThreadDuringUpdate()
        {
            OnityTaskScheduler.DispatchUnityMainThread = true;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int workerThread = 0;

            Task worker = Task.Run(() =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                FailAsync(new InvalidOperationException("from worker")).Forget();
            });
            yield return WaitFor(() => worker.IsCompleted && ReceivedCount == 1);

            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
            Assert.That(m_receivedThreads[0], Is.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator Publish_FromWorkerWithoutDispatch_RunsSubscribersOnTheWorkerThread()
        {
            OnityTaskScheduler.DispatchUnityMainThread = false;
            int workerThread = 0;

            Task worker = Task.Run(() =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                FailAsync(new InvalidOperationException("worker only")).Forget();
            });
            yield return WaitFor(() => worker.IsCompleted);

            Assert.That(ReceivedCount, Is.EqualTo(1));
            Assert.That(m_receivedThreads[0], Is.EqualTo(workerThread));
        }

        [UnityTest]
        public IEnumerator ForgetWithHandler_FaultingAfterAFrame_CallsTheHandlerOnTheMainThread()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int handlerThread = 0;
            Exception handled = null;
            InvalidOperationException failure = new InvalidOperationException("handled after frame");

            FaultAfterFrameAsync(failure).Forget(
                exception =>
                {
                    handled = exception;
                    handlerThread = Thread.CurrentThread.ManagedThreadId;
                },
                true);
            yield return WaitFor(() => handled != null);

            Assert.That(handled, Is.SameAs(failure));
            Assert.That(handlerThread, Is.EqualTo(mainThread));
            Assert.That(ReceivedCount, Is.Zero, "A handled fault must not reach the scheduler.");
        }

        [UnityTest]
        public IEnumerator ForgetWithHandler_WorkerFaultWithMainThreadFlag_ResumesOnTheMainThreadFirst()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            int handlerThread = 0;
            int workerThread = 0;

            source.Task.Forget(_ => Volatile.Write(ref handlerThread, Thread.CurrentThread.ManagedThreadId), true);
            Task worker = Task.Run(() =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                source.TrySetException(new InvalidOperationException("worker fault"));
            });
            yield return WaitFor(() => worker.IsCompleted && Volatile.Read(ref handlerThread) != 0);

            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
            Assert.That(handlerThread, Is.EqualTo(mainThread));
        }

        private static async OnityTask FaultAfterFrameAsync(Exception exception)
        {
            await OnityTask.NextFrame();
            throw exception;
        }

        private static IEnumerator WaitFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }
    }
}
