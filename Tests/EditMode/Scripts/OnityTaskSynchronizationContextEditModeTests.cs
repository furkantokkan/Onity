using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the thread switches added beside the thread-pool switch: the task-pool switch, the
    /// SynchronizationContext switch and its return scopes, and the state and async-delegate forms of
    /// <c>RunOnThreadPool</c>.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskSynchronizationContextEditModeTests
    {
        private const int k_timeoutSeconds = 10;

        private sealed class RecordingContext : SynchronizationContext
        {
            private readonly Queue<KeyValuePair<SendOrPostCallback, object>> m_posted =
                new Queue<KeyValuePair<SendOrPostCallback, object>>();

            public int PostCount
            {
                get
                {
                    lock (m_posted)
                    {
                        return m_posted.Count;
                    }
                }
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                lock (m_posted)
                {
                    m_posted.Enqueue(new KeyValuePair<SendOrPostCallback, object>(d, state));
                }
            }

            public void RunAll()
            {
                SynchronizationContext previous = Current;
                SetSynchronizationContext(this);
                try
                {
                    while (true)
                    {
                        KeyValuePair<SendOrPostCallback, object> work;
                        lock (m_posted)
                        {
                            if (m_posted.Count == 0)
                            {
                                return;
                            }

                            work = m_posted.Dequeue();
                        }

                        work.Key(work.Value);
                    }
                }
                finally
                {
                    SetSynchronizationContext(previous);
                }
            }
        }

        private sealed class DedicatedThreadContext : SynchronizationContext, IDisposable
        {
            private readonly BlockingCollection<KeyValuePair<SendOrPostCallback, object>> m_queue =
                new BlockingCollection<KeyValuePair<SendOrPostCallback, object>>();

            private readonly Thread m_thread;

            public DedicatedThreadContext()
            {
                m_thread = new Thread(RunLoop) { IsBackground = true, Name = "OnityTaskContextTestThread" };
                m_thread.Start();
            }

            public int ThreadId => m_thread.ManagedThreadId;

            public override void Post(SendOrPostCallback d, object state)
            {
                m_queue.Add(new KeyValuePair<SendOrPostCallback, object>(d, state));
            }

            public void Dispose()
            {
                m_queue.CompleteAdding();
            }

            private void RunLoop()
            {
                SetSynchronizationContext(this);
                foreach (KeyValuePair<SendOrPostCallback, object> work in m_queue.GetConsumingEnumerable())
                {
                    work.Key(work.Value);
                }
            }
        }

        private static void WaitUntilCompleted(OnityTask task)
        {
            Assert.That(SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(k_timeoutSeconds)), Is.True,
                "The thread-pool work timed out.");
        }

        private static void WaitUntilCompleted<T>(OnityTask<T> task)
        {
            Assert.That(SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(k_timeoutSeconds)), Is.True,
                "The thread-pool work timed out.");
        }

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed.TotalSeconds < k_timeoutSeconds)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, "The operation timed out.");
        }

        [Test]
        public void SwitchToSynchronizationContext_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => OnityTask.SwitchToSynchronizationContext(null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.ReturnToSynchronizationContext(null));
        }

        [Test]
        public void SwitchToSynchronizationContext_Awaiter_PostsEveryContinuationAndNeverCompletesInline()
        {
            RecordingContext context = new RecordingContext();
            OnitySynchronizationContextSwitchAwaiter awaiter =
                OnityTask.SwitchToSynchronizationContext(context).GetAwaiter();
            int calls = 0;

            Assert.That(awaiter.IsCompleted, Is.False);
            awaiter.UnsafeOnCompleted(() => calls++);
            awaiter.OnCompleted(() => calls++);
            Assert.That(context.PostCount, Is.EqualTo(2));
            Assert.That(calls, Is.Zero);
            context.RunAll();

            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void SwitchToSynchronizationContext_OnTheSameContext_StillPosts()
        {
            RecordingContext context = new RecordingContext();
            SynchronizationContext previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Assert.That(OnityTask.SwitchToSynchronizationContext(context).GetAwaiter().IsCompleted, Is.False);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Test]
        public void SwitchToSynchronizationContext_Awaiter_ObservesCancellationWhenTheAwaitCompletes()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            RecordingContext context = new RecordingContext();
            OnitySynchronizationContextSwitchAwaiter awaiter =
                OnityTask.SwitchToSynchronizationContext(context, cancellation.Token).GetAwaiter();

            Assert.DoesNotThrow(() => awaiter.GetResult());
            awaiter.UnsafeOnCompleted(() => { });
            cancellation.Cancel();

            Assert.That(
                Assert.Catch<OperationCanceledException>(() => awaiter.GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void SwitchToSynchronizationContext_NullContinuation_Throws()
        {
            OnitySynchronizationContextSwitchAwaiter awaiter =
                OnityTask.SwitchToSynchronizationContext(new RecordingContext()).GetAwaiter();

            Assert.Throws<ArgumentNullException>(() => awaiter.OnCompleted(null));
            Assert.Throws<ArgumentNullException>(() => awaiter.UnsafeOnCompleted(null));
        }

        [Test]
        public void ReturnToSynchronizationContext_Scope_PostsEvenOnTheSameContext()
        {
            RecordingContext context = new RecordingContext();
            SynchronizationContext previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                OnityReturnToSynchronizationContextAwaiter awaiter =
                    OnityTask.ReturnToSynchronizationContext(context).DisposeAsync();
                int calls = 0;

                Assert.That(awaiter.IsCompleted, Is.False);
                awaiter.UnsafeOnCompleted(() => calls++);
                Assert.That(context.PostCount, Is.EqualTo(1));
                context.RunAll();

                Assert.That(calls, Is.EqualTo(1));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Test]
        public void ReturnToCurrentSynchronizationContext_SameContext_CompletesInlineUnlessAskedToPost()
        {
            RecordingContext context = new RecordingContext();
            SynchronizationContext previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Assert.That(
                    OnityTask.ReturnToCurrentSynchronizationContext().DisposeAsync().IsCompleted,
                    Is.True);
                Assert.That(
                    OnityTask.ReturnToCurrentSynchronizationContext(false).DisposeAsync().IsCompleted,
                    Is.False);

                // The scope captures the context it is created on, so it has to be created before the
                // current context changes in order to be closed on another one.
                OnityReturnToSynchronizationContext scope = OnityTask.ReturnToCurrentSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(new RecordingContext());
                Assert.That(
                    scope.DisposeAsync().IsCompleted,
                    Is.False,
                    "A scope closed on another context must post.");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Test]
        public void ReturnToCurrentSynchronizationContext_WithoutACurrentContext_ClosesInline()
        {
            SynchronizationContext previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                OnityReturnToSynchronizationContextAwaiter awaiter =
                    OnityTask.ReturnToCurrentSynchronizationContext(false).DisposeAsync();
                int calls = 0;

                Assert.That(awaiter.IsCompleted, Is.True);
                awaiter.UnsafeOnCompleted(() => calls++);

                Assert.That(calls, Is.EqualTo(1), "There is no context to post to, so the continuation runs inline.");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Test]
        public void ReturnToSynchronizationContext_Awaiter_ObservesCancellationAndRejectsNullContinuations()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityReturnToSynchronizationContextAwaiter awaiter =
                OnityTask.ReturnToSynchronizationContext(new RecordingContext(), cancellation.Token).DisposeAsync();

            Assert.That(awaiter.GetAwaiter().IsCompleted, Is.False);
            Assert.Throws<ArgumentNullException>(() => awaiter.OnCompleted(null));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => awaiter.GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void ReturnToSynchronizationContext_Awaiter_RunsTheContinuationOnTheContext()
        {
            using DedicatedThreadContext context = new DedicatedThreadContext();
            using ManualResetEventSlim ran = new ManualResetEventSlim();
            SynchronizationContext observedContext = null;
            int observedThread = 0;

            OnityTask.ReturnToSynchronizationContext(context).DisposeAsync().UnsafeOnCompleted(() =>
            {
                observedContext = SynchronizationContext.Current;
                observedThread = Thread.CurrentThread.ManagedThreadId;
                ran.Set();
            });

            Assert.That(ran.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)), Is.True);
            Assert.That(observedThread, Is.EqualTo(context.ThreadId));
            Assert.That(observedContext, Is.SameAs(context));
        }

        [Test]
        public async Task AwaitUsing_ResumesOnTheContextThreadWhenTheScopeCloses()
        {
            using DedicatedThreadContext context = new DedicatedThreadContext();
            int insideScope;

            await using (OnityTask.ReturnToSynchronizationContext(context))
            {
                await OnityTask.SwitchToThreadPool();
                insideScope = Thread.CurrentThread.ManagedThreadId;
            }

            Assert.That(insideScope, Is.Not.EqualTo(context.ThreadId));
            Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(context.ThreadId),
                "Closing the scope must resume on the context's thread.");
        }

        [Test]
        public async Task SwitchToSynchronizationContext_CanBeAwaited()
        {
            using DedicatedThreadContext context = new DedicatedThreadContext();

            await OnityTask.SwitchToSynchronizationContext(context);

            Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(context.ThreadId));
        }

        [Test]
        public void SwitchToTaskPool_Awaiter_QueuesEveryContinuationToTheTaskScheduler()
        {
            OnityTaskPoolSwitchAwaiter awaiter = OnityTask.SwitchToTaskPool().GetAwaiter();
            using ManualResetEventSlim ran = new ManualResetEventSlim();
            int callerThread = Thread.CurrentThread.ManagedThreadId;
            int continuationThread = 0;
            bool onPool = false;

            Assert.That(awaiter.IsCompleted, Is.False);
            awaiter.UnsafeOnCompleted(() =>
            {
                continuationThread = Thread.CurrentThread.ManagedThreadId;
                onPool = Thread.CurrentThread.IsThreadPoolThread;
                ran.Set();
            });

            Assert.That(ran.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)), Is.True);
            Assert.That(continuationThread, Is.Not.EqualTo(callerThread));
            Assert.That(onPool, Is.True);
            Assert.DoesNotThrow(() => awaiter.GetResult());
        }

        [Test]
        public void SwitchToTaskPool_Awaiter_NullContinuationThrows()
        {
            OnityTaskPoolSwitchAwaiter awaiter = OnityTask.SwitchToTaskPool().GetAwaiter();

            Assert.Throws<ArgumentNullException>(() => awaiter.OnCompleted(null));
            Assert.Throws<ArgumentNullException>(() => awaiter.UnsafeOnCompleted(null));
        }

        [Test]
        public async Task SwitchToTaskPool_CanBeAwaited()
        {
            await OnityTask.SwitchToTaskPool();

            Assert.That(Thread.CurrentThread.IsThreadPoolThread, Is.True);
        }

        [Test]
        public void RunOnThreadPool_NullDelegates_ThrowSynchronously()
        {
            Assert.Throws<ArgumentNullException>(() => OnityTask.RunOnThreadPool((Action<object>)null, "state"));
            Assert.Throws<ArgumentNullException>(() => OnityTask.RunOnThreadPool((Func<OnityTask>)null));
            Assert.Throws<ArgumentNullException>(
                () => OnityTask.RunOnThreadPool((Func<object, OnityTask>)null, "state"));
            Assert.Throws<ArgumentNullException>(() => OnityTask.RunOnThreadPool((Func<OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => OnityTask.RunOnThreadPool((Func<object, int>)null, "state"));
            Assert.Throws<ArgumentNullException>(
                () => OnityTask.RunOnThreadPool((Func<object, OnityTask<int>>)null, "state"));
        }

        [Test]
        public void RunOnThreadPool_ActionWithState_RunsOnAWorkerWithTheState()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int workerThread = 0;
            object received = null;

            OnityTask task = OnityTask.RunOnThreadPool(
                state =>
                {
                    received = state;
                    workerThread = Thread.CurrentThread.ManagedThreadId;
                },
                "state",
                false);
            WaitUntilCompleted(task);
            task.GetAwaiter().GetResult();

            Assert.That(received, Is.EqualTo("state"));
            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
        }

        [Test]
        public void RunOnThreadPool_AsyncDelegate_CompletesAfterTheReturnedTask()
        {
            OnityTaskCompletionSource inner = new OnityTaskCompletionSource();
            using ManualResetEventSlim started = new ManualResetEventSlim();
            int workerThread = 0;
            int mainThread = Thread.CurrentThread.ManagedThreadId;

            OnityTask task = OnityTask.RunOnThreadPool(
                () =>
                {
                    workerThread = Thread.CurrentThread.ManagedThreadId;
                    started.Set();
                    return inner.Task;
                },
                false);
            Assert.That(started.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)), Is.True);
            Assert.That(task.IsCompleted, Is.False, "The task must wait for the task the delegate returned.");
            inner.TrySetResult();
            WaitUntilCompleted(task);

            task.GetAwaiter().GetResult();
            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
        }

        [Test]
        public void RunOnThreadPool_AsyncDelegateWithState_PassesTheState()
        {
            string received = null;

            OnityTask task = OnityTask.RunOnThreadPool(
                (Func<object, OnityTask>)(state =>
                {
                    received = (string)state;
                    return OnityTask.CompletedTask;
                }),
                "value",
                false);
            WaitUntilCompleted(task);
            task.GetAwaiter().GetResult();

            Assert.That(received, Is.EqualTo("value"));
        }

        [Test]
        public void RunOnThreadPool_TypedAsyncDelegate_ReturnsTheResult()
        {
            OnityTask<int> task = OnityTask.RunOnThreadPool(() => OnityTask.FromResult(12), false);
            WaitUntilCompleted(task);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(12));
        }

        [Test]
        public void RunOnThreadPool_FunctionWithState_ReturnsTheResult()
        {
            OnityTask<int> task = OnityTask.RunOnThreadPool(state => ((string)state).Length, "four", false);
            WaitUntilCompleted(task);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(4));
        }

        [Test]
        public void RunOnThreadPool_TypedAsyncDelegateWithState_ReturnsTheResult()
        {
            OnityTask<string> task = OnityTask.RunOnThreadPool(
                state => OnityTask.FromResult("echo " + state),
                "x",
                false);
            WaitUntilCompleted(task);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo("echo x"));
        }

        [Test]
        public void RunOnThreadPool_DelegateFaults_PropagateTheException()
        {
            InvalidOperationException failure = new InvalidOperationException("worker failed");

            OnityTask action = OnityTask.RunOnThreadPool((Action<object>)(_ => throw failure), "state", false);
            OnityTask asyncAction = OnityTask.RunOnThreadPool((Func<OnityTask>)(() => throw failure), false);
            OnityTask<int> function = OnityTask.RunOnThreadPool((Func<object, int>)(_ => throw failure), "state", false);
            WaitUntilCompleted(action);
            WaitUntilCompleted(asyncAction);
            WaitUntilCompleted(function);

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => action.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => asyncAction.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => function.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void RunOnThreadPool_PreCanceled_DoesNotInvokeTheDelegates()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int calls = 0;

            OnityTask action = OnityTask.RunOnThreadPool(_ => { calls++; }, "state", false, cancellation.Token);
            OnityTask asyncAction = OnityTask.RunOnThreadPool(
                () =>
                {
                    calls++;
                    return OnityTask.CompletedTask;
                },
                false,
                cancellation.Token);
            OnityTask<int> typed = OnityTask.RunOnThreadPool(
                () =>
                {
                    calls++;
                    return OnityTask.FromResult(1);
                },
                false,
                cancellation.Token);

            Assert.That(action.IsCanceled, Is.True);
            Assert.That(asyncAction.IsCanceled, Is.True);
            Assert.That(typed.IsCanceled, Is.True);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void RunOnThreadPool_CanceledWhileTheDelegateRuns_CompletesAsCanceled()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTaskCompletionSource inner = new OnityTaskCompletionSource();
            using ManualResetEventSlim started = new ManualResetEventSlim();

            OnityTask task = OnityTask.RunOnThreadPool(
                () =>
                {
                    started.Set();
                    return inner.Task;
                },
                false,
                cancellation.Token);
            Assert.That(started.Wait(TimeSpan.FromSeconds(k_timeoutSeconds)), Is.True);
            cancellation.Cancel();
            inner.TrySetResult();
            WaitUntilCompleted(task);

            Assert.That(task.IsCanceled, Is.True);
        }

        [UnityTest]
        public IEnumerator RunOnThreadPool_AsyncDelegate_ReturnsToTheMainThreadWhenAsked()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int workerThread = 0;
            int completionThread = 0;

            OnityTask<int> task = OnityTask.RunOnThreadPool(
                () =>
                {
                    workerThread = Thread.CurrentThread.ManagedThreadId;
                    return OnityTask.FromResult(3);
                });
            task.GetAwaiter().UnsafeOnCompleted(() => completionThread = Thread.CurrentThread.ManagedThreadId);
            yield return WaitFor(() => task.IsCompleted && Volatile.Read(ref completionThread) != 0);

            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
            Assert.That(completionThread, Is.EqualTo(mainThread), "The return must publish on the main thread.");
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(3));
        }
    }
}
