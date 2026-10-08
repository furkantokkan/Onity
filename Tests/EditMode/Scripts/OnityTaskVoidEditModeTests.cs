using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers <c>async OnityTaskVoid</c> methods: synchronous start and completion, suspension and
    /// resumption, one-time fault reporting through the Forget path, cancellation handling, runner
    /// reuse, and the <c>OnityTask.Void</c>, <c>Action</c> and <c>UnityAction</c> factories.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskVoidEditModeTests
    {
        private bool m_previousFlow;

        [SetUp]
        public void SaveSettings()
        {
            m_previousFlow = OnityTask.FlowExecutionContext;
        }

        [TearDown]
        public void RestoreSettings()
        {
            OnityTask.FlowExecutionContext = m_previousFlow;
        }

        [Test]
        public void SynchronousMethod_RunsToCompletionBeforeReturning()
        {
            List<string> steps = new List<string>();

            SyncAsync(steps);
            steps.Add("returned");

            Assert.That(steps, Is.EqualTo(new[] { "body", "returned" }));
        }

        [Test]
        public void SuspendedMethod_StartsSynchronouslyThenCompletesOnResume()
        {
            ManualAwaitable gate = new ManualAwaitable();
            List<string> steps = new List<string>();

            WaitThenAsync(gate, steps);
            steps.Add("returned");

            Assert.That(steps, Is.EqualTo(new[] { "before", "returned" }));

            gate.Complete();

            Assert.That(steps, Is.EqualTo(new[] { "before", "returned", "after" }));
        }

        [Test]
        public void SuspendedMethod_ResumesThroughMultipleAwaits()
        {
            ManualAwaitable first = new ManualAwaitable();
            ManualAwaitable second = new ManualAwaitable();
            List<string> steps = new List<string>();

            TwoAwaitsAsync(first, second, steps);
            first.Complete();
            Assert.That(steps, Is.EqualTo(new[] { "start", "afterFirst" }));

            second.Complete();
            Assert.That(steps, Is.EqualTo(new[] { "start", "afterFirst", "afterSecond" }));
        }

        [Test]
        public void SafeAwaiter_UsesTheSameBuilderPath()
        {
            SafeManualAwaitable gate = new SafeManualAwaitable();
            List<string> steps = new List<string>();

            SafeAwaitAsync(gate, steps);
            Assert.That(steps, Is.EqualTo(new[] { "before" }));

            gate.Complete();

            Assert.That(steps, Is.EqualTo(new[] { "before", "after" }));
        }

        [Test]
        public void AwaitingAnOnityTask_ResumesWhenThatTaskCompletes()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            List<string> steps = new List<string>();

            AwaitOnityTaskAsync(source.Task, steps);
            Assert.That(steps, Is.EqualTo(new[] { "before" }));

            source.TrySetResult();

            Assert.That(steps, Is.EqualTo(new[] { "before", "after" }));
        }

        [Test]
        public void SynchronousFault_IsLoggedOnce()
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: sync fault"));

            ThrowSyncAsync(new InvalidOperationException("sync fault"));
        }

        [Test]
        public void FaultAfterSuspension_IsLoggedOnceAndRunnerIsReleased()
        {
            ManualAwaitable gate = new ManualAwaitable();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: late fault"));

            ThrowAfterAsync(gate, new InvalidOperationException("late fault"));
            gate.Complete();

            // A second cycle that completes cleanly proves the faulted cycle neither threw again nor
            // left a stale consumer behind.
            ManualAwaitable next = new ManualAwaitable();
            List<string> steps = new List<string>();
            WaitThenAsync(next, steps);
            next.Complete();
            Assert.That(steps, Is.EqualTo(new[] { "before", "after" }));
        }

        [Test]
        public void FaultAfterSuspension_ReachesTheLogOnceNotTwice()
        {
            ManualAwaitable gate = new ManualAwaitable();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: single report"));

            ThrowAfterAsync(gate, new InvalidOperationException("single report"));
            gate.Complete();

            // LogAssert fails the test on teardown when an unexpected second exception log was written.
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SynchronousCancellation_IsNotLogged()
        {
            ThrowSyncAsync(new OperationCanceledException());

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CancellationAfterSuspension_IsNotLogged()
        {
            ManualAwaitable gate = new ManualAwaitable();
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                ThrowIfCanceledAfterAsync(gate, cts.Token);
                cts.Cancel();
                gate.Complete();
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TaskCanceledException_IsNotLogged()
        {
            ManualAwaitable gate = new ManualAwaitable();

            ThrowAfterAsync(gate, new System.Threading.Tasks.TaskCanceledException());
            gate.Complete();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Forget_IsANoOp()
        {
            ManualAwaitable gate = new ManualAwaitable();
            List<string> steps = new List<string>();

            OnityTaskVoid result = WaitThenAsync(gate, steps);
            Assert.DoesNotThrow(() => result.Forget());
            Assert.DoesNotThrow(() => result.Forget());
            Assert.That(steps, Is.EqualTo(new[] { "before" }));

            gate.Complete();
            Assert.That(steps, Is.EqualTo(new[] { "before", "after" }));
        }

        [Test]
        public void RepeatedCycles_ReuseTheRunnerWithoutInterference()
        {
            List<string> steps = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                ManualAwaitable gate = new ManualAwaitable();
                steps.Clear();
                WaitThenAsync(gate, steps);
                gate.Complete();
                Assert.That(steps, Is.EqualTo(new[] { "before", "after" }), "cycle " + i);
            }
        }

        [Test]
        public void ConcurrentSuspendedMethods_CompleteIndependently()
        {
            ManualAwaitable firstGate = new ManualAwaitable();
            ManualAwaitable secondGate = new ManualAwaitable();
            List<string> firstSteps = new List<string>();
            List<string> secondSteps = new List<string>();

            WaitThenAsync(firstGate, firstSteps);
            WaitThenAsync(secondGate, secondSteps);

            secondGate.Complete();
            Assert.That(firstSteps, Is.EqualTo(new[] { "before" }));
            Assert.That(secondSteps, Is.EqualTo(new[] { "before", "after" }));

            firstGate.Complete();
            Assert.That(firstSteps, Is.EqualTo(new[] { "before", "after" }));
        }

        [Test]
        public void FlowExecutionContextOn_StillRunsAndFlowsAsyncLocal()
        {
            OnityTask.FlowExecutionContext = true;
            AsyncLocal<string> local = new AsyncLocal<string>();
            string observed = null;
            ManualAwaitable gate = new ManualAwaitable();

            local.Value = "flowed";
            ObserveLocalAsync(gate, local, value => observed = value);
            local.Value = "changed";
            gate.Complete();

            Assert.That(observed, Is.EqualTo("flowed"));
        }

        [Test]
        public void Void_StartsTheMethod()
        {
            int calls = 0;

            OnityTask.Void(() => CountAsync(() => calls++));

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Void_WithToken_PassesTheToken()
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                CancellationToken received = default;

                OnityTask.Void(token => CaptureTokenAsync(token, value => received = value), cts.Token);

                Assert.That(received, Is.EqualTo(cts.Token));
            }
        }

        [Test]
        public void Void_WithState_PassesTheState()
        {
            string received = null;

            OnityTask.Void(state => CaptureStateAsync(state, value => received = value), "state");

            Assert.That(received, Is.EqualTo("state"));
        }

        [Test]
        public void Action_StartsTheMethodOnEveryInvoke()
        {
            int calls = 0;
            Action action = OnityTask.Action(() => CountAsync(() => calls++));

            Assert.That(calls, Is.EqualTo(0), "Creating the delegate must not start the method.");
            action();
            action();

            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void Action_WithTokenAndState_PassesArguments()
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                CancellationToken receivedToken = default;
                string receivedState = null;

                OnityTask.Action(token => CaptureTokenAsync(token, value => receivedToken = value), cts.Token)();
                OnityTask.Action("value", state => CaptureStateAsync(state, value => receivedState = value))();

                Assert.That(receivedToken, Is.EqualTo(cts.Token));
                Assert.That(receivedState, Is.EqualTo("value"));
            }
        }

        [Test]
        public void UnityAction_StartsTheMethodOnEveryInvoke()
        {
            int calls = 0;
            UnityAction action = OnityTask.UnityAction(() => CountAsync(() => calls++));

            Assert.That(calls, Is.EqualTo(0));
            action();
            action();

            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void UnityAction_WithTokenAndState_PassesArguments()
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                CancellationToken receivedToken = default;
                string receivedState = null;

                OnityTask.UnityAction(token => CaptureTokenAsync(token, value => receivedToken = value), cts.Token)();
                OnityTask.UnityAction("value", state => CaptureStateAsync(state, value => receivedState = value))();

                Assert.That(receivedToken, Is.EqualTo(cts.Token));
                Assert.That(receivedState, Is.EqualTo("value"));
            }
        }

        [Test]
        public void UnityAction_Generic_ForwardsArguments()
        {
            int one = 0;
            int sum = 0;
            int sum3 = 0;
            int sum4 = 0;

            OnityTask.UnityAction<int>(arg => RecordAsync(arg, value => one = value))(5);
            OnityTask.UnityAction<int, int>((a, b) => RecordAsync(a + b, value => sum = value))(1, 2);
            OnityTask.UnityAction<int, int, int>((a, b, c) => RecordAsync(a + b + c, value => sum3 = value))(1, 2, 3);
            OnityTask.UnityAction<int, int, int, int>(
                (a, b, c, d) => RecordAsync(a + b + c + d, value => sum4 = value))(1, 2, 3, 4);

            Assert.That(one, Is.EqualTo(5));
            Assert.That(sum, Is.EqualTo(3));
            Assert.That(sum3, Is.EqualTo(6));
            Assert.That(sum4, Is.EqualTo(10));
        }

        [Test]
        public void UnityAction_GenericWithToken_ForwardsArgumentsAndToken()
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                int one = 0;
                int sum = 0;
                int sum3 = 0;
                int sum4 = 0;
                int tokenMatches = 0;

                OnityTask.UnityAction<int>(
                    (arg, token) => RecordAsync(arg, value => one = value, token == cts.Token, () => tokenMatches++),
                    cts.Token)(5);
                OnityTask.UnityAction<int, int>(
                    (a, b, token) => RecordAsync(a + b + (token == cts.Token ? 100 : 0), value => sum = value),
                    cts.Token)(1, 2);
                OnityTask.UnityAction<int, int, int>(
                    (a, b, c, token) => RecordAsync(a + b + c + (token == cts.Token ? 100 : 0), value => sum3 = value),
                    cts.Token)(1, 2, 3);
                OnityTask.UnityAction<int, int, int, int>(
                    (a, b, c, d, token) => RecordAsync(a + b + c + d + (token == cts.Token ? 100 : 0), value => sum4 = value),
                    cts.Token)(1, 2, 3, 4);

                Assert.That(one, Is.EqualTo(5));
                Assert.That(tokenMatches, Is.EqualTo(1));
                Assert.That(sum, Is.EqualTo(103));
                Assert.That(sum3, Is.EqualTo(106));
                Assert.That(sum4, Is.EqualTo(110));
            }
        }

        [Test]
        public void Action_FaultInTheMethod_IsLoggedNotThrown()
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: delegate fault"));
            Action action = OnityTask.Action(() => ThrowSyncAsync(new InvalidOperationException("delegate fault")));

            Assert.DoesNotThrow(() => action());
        }

        private static async OnityTaskVoid SyncAsync(List<string> steps)
        {
            steps.Add("body");
            await OnityTask.CompletedTask;
        }

        private static async OnityTaskVoid WaitThenAsync(ManualAwaitable gate, List<string> steps)
        {
            steps.Add("before");
            await gate;
            steps.Add("after");
        }

        private static async OnityTaskVoid TwoAwaitsAsync(ManualAwaitable first, ManualAwaitable second, List<string> steps)
        {
            steps.Add("start");
            await first;
            steps.Add("afterFirst");
            await second;
            steps.Add("afterSecond");
        }

        private static async OnityTaskVoid SafeAwaitAsync(SafeManualAwaitable gate, List<string> steps)
        {
            steps.Add("before");
            await gate;
            steps.Add("after");
        }

        private static async OnityTaskVoid AwaitOnityTaskAsync(OnityTask task, List<string> steps)
        {
            steps.Add("before");
            await task;
            steps.Add("after");
        }

        private static async OnityTaskVoid ThrowSyncAsync(Exception exception)
        {
            await OnityTask.CompletedTask;
            throw exception;
        }

        private static async OnityTaskVoid ThrowAfterAsync(ManualAwaitable gate, Exception exception)
        {
            await gate;
            throw exception;
        }

        private static async OnityTaskVoid ThrowIfCanceledAfterAsync(ManualAwaitable gate, CancellationToken token)
        {
            await gate;
            token.ThrowIfCancellationRequested();
        }

        private static async OnityTaskVoid ObserveLocalAsync(
            ManualAwaitable gate,
            AsyncLocal<string> local,
            Action<string> observe)
        {
            await gate;
            observe(local.Value);
        }

        private static async OnityTaskVoid CountAsync(Action count)
        {
            count();
            await OnityTask.CompletedTask;
        }

        private static async OnityTaskVoid CaptureTokenAsync(CancellationToken token, Action<CancellationToken> capture)
        {
            capture(token);
            await OnityTask.CompletedTask;
        }

        private static async OnityTaskVoid CaptureStateAsync(string state, Action<string> capture)
        {
            capture(state);
            await OnityTask.CompletedTask;
        }

        private static async OnityTaskVoid RecordAsync(int value, Action<int> record)
        {
            record(value);
            await OnityTask.CompletedTask;
        }

        private static async OnityTaskVoid RecordAsync(int value, Action<int> record, bool tokenMatched, Action onMatch)
        {
            record(value);
            if (tokenMatched)
            {
                onMatch();
            }

            await OnityTask.CompletedTask;
        }

        private sealed class ManualAwaitable : ICriticalNotifyCompletion
        {
            private Action m_continuation;

            public bool IsCompleted => false;

            public ManualAwaitable GetAwaiter()
            {
                return this;
            }

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void Complete()
            {
                Action continuation = Interlocked.Exchange(ref m_continuation, null);
                if (continuation == null)
                {
                    throw new InvalidOperationException("No continuation was registered.");
                }

                continuation();
            }
        }

        private sealed class SafeManualAwaitable : INotifyCompletion
        {
            private Action m_continuation;

            public bool IsCompleted => false;

            public SafeManualAwaitable GetAwaiter()
            {
                return this;
            }

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                m_continuation = continuation;
            }

            public void Complete()
            {
                Action continuation = Interlocked.Exchange(ref m_continuation, null);
                if (continuation == null)
                {
                    throw new InvalidOperationException("No continuation was registered.");
                }

                continuation();
            }
        }
    }
}
