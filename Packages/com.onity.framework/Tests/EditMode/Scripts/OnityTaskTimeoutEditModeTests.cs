using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    public sealed class OnityTaskTimeoutEditModeTests
    {
        private const BindingFlags k_private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly object s_domainSentinel = new object();

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void InvalidSeconds_ThrowBeforeClaimingNativeInput_EvenWhenCompleted(int shape)
        {
            foreach (float seconds in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var typedGate = new Gate();
                var plainGate = new Gate();
                OnityTask<int> typed = Native(typedGate);
                OnityTask plain = NativeUntyped(plainGate);
                try
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => Wrap(shape, typed, plain, seconds));
                    Assert.Throws<ArgumentOutOfRangeException>(() => Wrap(shape, default, default, seconds));
                    typedGate.Complete();
                    plainGate.Complete();
                    Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo(42));
                    Assert.DoesNotThrow(() => plain.GetAwaiter().GetResult());
                }
                finally
                {
                    typedGate.Complete();
                    plainGate.Complete();
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void CompletedEntry_WinsIncludingZeroAndWorkerEdit_AndOnlyPlainTimeoutKeepsIdentity(int shape)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                for (int outcome = 0; outcome < 5; outcome++)
                {
                    foreach (float seconds in new[] { 0f, float.MaxValue })
                    {
                        var typed = new OnityTaskCompletionSource<int>();
                        var plain = new OnityTaskCompletionSource();
                        Exception fault = outcome == 3 ? (Exception)new OperationCanceledException(cancellation.Token)
                            : new TimeoutException("Producer timeout is an original fault.");
                        Complete(typed, plain, outcome, fault, cancellation.Token);
                        try
                        {
                            Output output = Wrap(shape, typed.Task, plain.Task, seconds);
                            if (shape == 0)
                            {
                                Assert.That(output.Typed.Equals(typed.Task), Is.True);
                            }
                            else if (shape == 1)
                            {
                                Assert.That(output.Plain.Equals(plain.Task), Is.True);
                            }
                            CheckProducer(output, outcome, fault, cancellation.Token);
                            Task<Output> worker = Task.Run(() => Wrap(shape, typed.Task, plain.Task, seconds));
                            Join(worker);
                            CheckProducer(worker.GetAwaiter().GetResult(), outcome, fault, cancellation.Token);
                        }
                        finally
                        {
                            // Each API shape observes one source; consume only the unused terminal source.
                            if (shape % 2 == 0)
                            {
                                Catch(() => plain.Task.GetAwaiter().GetResult());
                            }
                            else
                            {
                                Catch(() => typed.Task.GetAwaiter().GetResult());
                            }
                        }
                    }
                }
                Output empty = Wrap(shape, default, default, 0f);
                Result value = empty.GetResult();
                Assert.That(value.Timeout, Is.False);
                Assert.That(value.Value, Is.Zero);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void PendingZero_TimesOutInWorkerEdit_WithoutCancelingProducer_AndObservesLateFault(int shape)
        {
            foreach (bool unscaled in new[] { false, true })
            {
                var typed = new OnityTaskCompletionSource<int>();
                var plain = new OnityTaskCompletionSource();
                Task<int> typedBridge = typed.Task.AsTask();
                Task plainBridge = plain.Task.AsTask();
                Exception fault = new InvalidOperationException("Late zero-timeout producer fault.");
                try
                {
                    Task<Output> worker = Task.Run(() => Wrap(shape, typed.Task, plain.Task, 0f, unscaled));
                    Join(worker);
                    Output output = worker.GetAwaiter().GetResult();
                    CheckTimeout(output);
                    Assert.That(typed.Task.IsCompleted || plain.Task.IsCompleted, Is.False);
                    typed.TrySetException(fault);
                    plain.TrySetException(fault);
                    Wait(() => typedBridge.IsCompleted && plainBridge.IsCompleted);
                    Assert.That(FaultObserved(shape % 2 == 0 ? (Task)typedBridge : plainBridge), Is.True);
                    // Explicitly observe the unrelated producer, which this shape did not wrap.
                    _ = (shape % 2 == 0 ? plainBridge : (Task)typedBridge).Exception;
                    Assert.Throws<InvalidOperationException>(() => output.GetResult());
                }
                finally
                {
                    typed.TrySetResult(42);
                    plain.TrySetResult();
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void PositivePending_EditAndWorkerRejectBeforeClaim_OriginalNativeConsumerRemainsValid(int shape)
        {
            var typedGate = new Gate();
            var plainGate = new Gate();
            OnityTask<int> typed = Native(typedGate);
            OnityTask plain = NativeUntyped(plainGate);
            try
            {
                Assert.Throws<InvalidOperationException>(() => Wrap(shape, typed, plain, 1f));
                Task<Exception> worker = Task.Run(() => Catch(() => Wrap(shape, typed, plain, 1f)));
                Join(worker);
                Assert.That(worker.Result, Is.TypeOf<InvalidOperationException>());
                typedGate.Complete();
                plainGate.Complete();
                Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo(42));
                Assert.DoesNotThrow(() => plain.GetAwaiter().GetResult());
            }
            finally
            {
                typedGate.Complete();
                plainGate.Complete();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StaleOrPreclaimedNativeInput_ProducesFaultWrapperAndKeepsEarlierConsumer(bool stale)
        {
            for (int shape = 0; shape < 4; shape++)
            {
                var typedGate = new Gate();
                var plainGate = new Gate();
                OnityTask<int> typed = Native(typedGate);
                OnityTask plain = NativeUntyped(plainGate);
                int firstCalls = 0;
                Exception firstError = null;
                try
                {
                    if (stale)
                    {
                        typedGate.Complete();
                        plainGate.Complete();
                        typed.GetAwaiter().GetResult();
                        plain.GetAwaiter().GetResult();
                    }
                    else if (shape % 2 == 0)
                    {
                        typed.GetAwaiter().UnsafeOnCompleted(() =>
                        {
                            firstError = Catch(() => typed.GetAwaiter().GetResult());
                            firstCalls++;
                        });
                    }
                    else
                    {
                        plain.GetAwaiter().UnsafeOnCompleted(() =>
                        {
                            firstError = Catch(() => plain.GetAwaiter().GetResult());
                            firstCalls++;
                        });
                    }
                    // Zero already wins before registration failure; a stale status fails first.
                    Output output = null;
                    Assert.DoesNotThrow(() => output = Wrap(shape, typed, plain, stale ? 1f : 0f));
                    if (stale)
                    {
                        Assert.That(output.IsFaulted, Is.True);
                        Assert.That(Catch(() => output.GetResult()), Is.TypeOf<InvalidOperationException>());
                    }
                    else
                    {
                        CheckTimeout(output);
                        typedGate.Complete();
                        plainGate.Complete();
                        Assert.That(firstCalls, Is.EqualTo(1));
                        Assert.That(firstError, Is.Null);
                        if (shape % 2 == 0)
                        {
                            plain.GetAwaiter().GetResult();
                        }
                        else
                        {
                            typed.GetAwaiter().GetResult();
                        }
                    }
                }
                finally
                {
                    typedGate.Complete();
                    plainGate.Complete();
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EarlyOutputConsumption_ObservesDerivedLateFaultWithBeforeConcurrentAndFutureInputBridge(int timing)
        {
            foreach (int shape in new[] { 0, 2 })
            {
                var source = new DerivedSource<int>();
                Task<int> bridge = timing == 0 ? source.Task.AsTask() : null;
                Exception fault = new InvalidOperationException("Derived timeout loser fault.");
                Output output = Wrap(shape, source.Task, default, 0f);
                CheckTimeout(output);
                try
                {
                    if (timing == 1)
                    {
                        using (var start = new ManualResetEventSlim(false))
                        {
                            Task bridgeWorker = Task.Run(() =>
                            {
                                start.Wait();
                                bridge = source.Task.AsTask();
                            });
                            Task completeWorker = Task.Run(() =>
                            {
                                start.Wait();
                                source.TrySetException(fault);
                            });
                            start.Set();
                            Join(bridgeWorker, completeWorker);
                        }
                    }
                    else
                    {
                        source.TrySetException(fault);
                        bridge = bridge ?? source.Task.AsTask();
                    }
                    Wait(() => bridge.IsCompleted);
                    Assert.That(FaultObserved(bridge), Is.True);
                    Assert.That(source.Task.IsFaulted, Is.True);
                }
                finally
                {
                    source.TrySetResult(42);
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void PendingZero_NativePreservedAndTaskInputs_OutputPreserveAndBridgeFollowConsumerContract(int shape)
        {
            for (int family = 0; family < 3; family++)
            {
                var typedGate = new Gate();
                var plainGate = new Gate();
                var typedTaskSource = new TaskCompletionSource<int>();
                var plainTaskSource = new TaskCompletionSource<bool>();
                OnityTask<int> typed = family == 2 ? new OnityTask<int>(typedTaskSource.Task) : Native(typedGate);
                OnityTask plain = family == 2 ? new OnityTask(plainTaskSource.Task) : NativeUntyped(plainGate);
                if (family == 1)
                {
                    typed = typed.Preserve();
                    plain = plain.Preserve();
                }
                try
                {
                    Output output = Wrap(shape, typed, plain, 0f).Preserve();
                    CheckTimeout(output);
                    CheckTimeout(output);
                    Task first = output.Bridge();
                    Assert.That(output.Bridge(), Is.SameAs(first));
                    Assert.That(first.IsCompleted, Is.True);
                    _ = first.Exception;
                    typedGate.Complete();
                    plainGate.Complete();
                    typedTaskSource.TrySetResult(42);
                    plainTaskSource.TrySetResult(true);
                    if (family == 0)
                    {
                        Assert.That(Catch(() =>
                        {
                            if (shape % 2 == 0)
                            {
                                typed.GetAwaiter().GetResult();
                            }
                            else
                            {
                                plain.GetAwaiter().GetResult();
                            }
                        }), Is.TypeOf<InvalidOperationException>());
                        if (shape % 2 == 0)
                        {
                            plain.GetAwaiter().GetResult();
                        }
                        else
                        {
                            typed.GetAwaiter().GetResult();
                        }
                    }
                    else
                    {
                        Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo(42));
                        Assert.DoesNotThrow(() => plain.GetAwaiter().GetResult());
                    }
                }
                finally
                {
                    typedGate.Complete();
                    plainGate.Complete();
                    typedTaskSource.TrySetResult(42);
                    plainTaskSource.TrySetResult(true);
                }
            }
            var pending = new OnityTaskCompletionSource<int>();
            var untyped = new OnityTaskCompletionSource();
            try
            {
                Output output = Wrap(shape, pending.Task, untyped.Task, 0f);
                Task bridge = output.Bridge();
                // Conversion of this already-complete unpreserved native source releases it.
                Assert.Throws<InvalidOperationException>(() => output.Bridge());
                Assert.That(Catch(() => output.GetResult()), Is.TypeOf<InvalidOperationException>());
                _ = bridge.Exception;
            }
            finally
            {
                pending.TrySetResult(42);
                untyped.TrySetResult();
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ProducerObservation_DoesNotCaptureCreatorContext_AndRestoresSuppression(int mode)
        {
            SynchronizationContext previousContext = SynchronizationContext.Current;
            bool previousFlow = OnityTask.FlowExecutionContext;
            var context = new CountingContext();
            var local = new AsyncLocal<string>();
            try
            {
                OnityTask.FlowExecutionContext = mode != 1;
                for (int shape = 0; shape < 4; shape++)
                {
                    var typed = new TaskCompletionSource<int>();
                    var plain = new TaskCompletionSource<bool>();
                    Exception fault = new InvalidOperationException("Context-free producer observer fault.");
                    local.Value = "creator";
                    SynchronizationContext.SetSynchronizationContext(context);
                    Output output;
                    if (mode == 2)
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            output = Wrap(shape, new OnityTask<int>(typed.Task), new OnityTask(plain.Task), 0f);
                            Assert.That(ExecutionContext.IsFlowSuppressed(), Is.True);
                        }
                    }
                    else
                    {
                        output = Wrap(shape, new OnityTask<int>(typed.Task), new OnityTask(plain.Task), 0f);
                    }
                    Assert.That(ExecutionContext.IsFlowSuppressed(), Is.False);
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                    CheckTimeout(output);
                    Task worker;
                    using (ExecutionContext.SuppressFlow())
                    {
                        worker = Task.Run(() =>
                        {
                            typed.TrySetException(fault);
                            plain.TrySetException(fault);
                        });
                    }
                    Join(worker);
                    Task relevant = shape % 2 == 0 ? (Task)typed.Task : plain.Task;
                    Wait(() => FaultObserved(relevant));
                    Assert.That(context.Posts, Is.Zero);
                    Assert.That(local.Value, Is.EqualTo("creator"));
                    _ = (shape % 2 == 0 ? (Task)plain.Task : typed.Task).Exception;
                }
            }
            finally
            {
                local.Value = null;
                OnityTask.FlowExecutionContext = previousFlow;
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        [Test]
        public void FactoryVersusProducer_PublicRaceHasOneLegalOutcomeAndObservesAnyLoser()
        {
            for (int round = 0; round < 24; round++)
            {
                var typed = new OnityTaskCompletionSource<int>();
                var plain = new OnityTaskCompletionSource();
                Exception fault = new InvalidOperationException("Racing timeout producer fault.");
                int shape = round % 4;
                Output output = null;
                using (var start = new ManualResetEventSlim(false))
                {
                    Task factory = Task.Run(() =>
                    {
                        start.Wait();
                        output = Wrap(shape, typed.Task, plain.Task, 0f);
                    });
                    Task producer = Task.Run(() =>
                    {
                        start.Wait();
                        typed.TrySetException(fault);
                        plain.TrySetException(fault);
                    });
                    start.Set();
                    Join(factory, producer);
                }
                try
                {
                    bool returnedIdentity = shape == 0 ? output.Typed.Equals(typed.Task)
                        : shape == 1 && output.Plain.Equals(plain.Task);
                    Exception error = Catch(() =>
                    {
                        Result value = output.GetResult();
                        Assert.That(value.Timeout, Is.True);
                    });
                    Assert.That(error == null || error is TimeoutException || ReferenceEquals(error, fault), Is.True);
                    Task bridge = shape % 2 == 0 ? (Task)typed.Task.AsTask() : plain.Task.AsTask();
                    if (!returnedIdentity)
                    {
                        Assert.That(FaultObserved(bridge), Is.True);
                    }
                }
                finally
                {
                    // An unchanged completed input uses its original consumer contract.
                    // Observe both newly materialized bridges, including that identity path.
                    _ = typed.Task.AsTask().Exception;
                    _ = plain.Task.AsTask().Exception;
                }
            }
        }

        private static Output Wrap(int shape, OnityTask<int> typed, OnityTask plain,
            float seconds, bool unscaled = true)
        {
            return new Output
            {
                Shape = shape,
                Typed = shape == 0 ? typed.Timeout(seconds, unscaled) : default,
                Plain = shape == 1 ? plain.Timeout(seconds, unscaled) : default,
                TypedFlag = shape == 2 ? typed.TimeoutWithoutException(seconds, unscaled) : default,
                PlainFlag = shape == 3 ? plain.TimeoutWithoutException(seconds, unscaled) : default
            };
        }

        [UnityTest]
        public IEnumerator ActualReloadDisabledPlayEditPlay_AwakeTimersEndAsCancellationAndRetainProducerObservation()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) == 0)
            {
                Assert.Ignore("Requires the verification host's existing disabled domain and scene reload settings.");
            }
            object sentinel = s_domainSentinel;
            Type probeType = Type.GetType("Onity.Tests.PlayMode.OnityTaskTimeoutAwakeProbe, Onity.Tests.PlayMode", true);
            Component probe = null;
            object retainedProbe = null;
            try
            {
                for (int session = 0; session < 2; session++)
                {
                    probe = new GameObject("Onity Timeout Awake Probe").AddComponent(probeType);
                    retainedProbe = probe;
                    yield return new EnterPlayMode(false);
                    Assert.That(ReferenceEquals(sentinel, s_domainSentinel), Is.True);
                    Assert.That(probe != null, Is.True);
                    Assert.That(Read<bool>(probe, "RegisteredInAwake"), Is.True);
                    Assert.That(Read<bool>(probe, "AwakeBeforeEnteredPlay"), Is.True);
                    Assert.That(Read<string>(probe, "Error"), Is.Null);
                    object sessionState = probe;
                    yield return new ExitPlayMode();
                    Assert.That(ReferenceEquals(sentinel, s_domainSentinel), Is.True);
                    probeType.GetMethod("ObserveAfterExit").Invoke(sessionState, null);
                    Assert.That(Read<string>(sessionState, "Error"), Is.Null);
                    Assert.That(Read<int>(sessionState, "CanceledCount"), Is.EqualTo(4));
                    Assert.That(Read<bool>(sessionState, "ClosingRejected"), Is.True);
                    Assert.That(Read<bool>(sessionState, "ProducersWerePending"), Is.True);
                    if (probe != null)
                    {
                        UnityEngine.Object.DestroyImmediate(probe.gameObject);
                    }
                    probe = null;
                }
            }
            finally
            {
                if (!ReferenceEquals(retainedProbe, null))
                {
                    probeType.GetMethod("CompleteProducers").Invoke(retainedProbe, null);
                }
                if (probe != null)
                {
                    UnityEngine.Object.DestroyImmediate(probe.gameObject);
                }
            }
        }

        [UnityTearDown]
        public IEnumerator ExitAfterFailure()
        {
            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }

        private static T Read<T>(object probe, string name)
        {
            Assert.That(ReferenceEquals(probe, null), Is.False);
            return (T)probe.GetType().GetField(name).GetValue(probe);
        }

        private static void CheckTimeout(Output output)
        {
            if (output.Shape < 2)
            {
                Assert.That(output.IsFaulted, Is.True);
                Assert.That(Catch(() => output.GetResult()), Is.TypeOf<TimeoutException>());
            }
            else
            {
                Result result = output.GetResult();
                Assert.That(result.Timeout, Is.True);
                Assert.That(result.Value, Is.Zero);
            }
        }

        private static void CheckProducer(Output output, int outcome, Exception fault, CancellationToken token)
        {
            if (outcome < 2)
            {
                Result value = output.GetResult();
                Assert.That(value.Timeout, Is.False);
                Assert.That(value.Value, Is.EqualTo(output.Shape % 2 == 0 && outcome == 1 ? 42 : 0));
            }
            else if (outcome == 2)
            {
                Assert.That(output.IsCanceled, Is.True);
                Assert.That(((OperationCanceledException)Catch(() => output.GetResult())).CancellationToken, Is.EqualTo(token));
            }
            else
            {
                Assert.That(output.IsFaulted, Is.True);
                Assert.That(Catch(() => output.GetResult()), Is.SameAs(fault));
            }
        }

        private static void Complete(OnityTaskCompletionSource<int> typed, OnityTaskCompletionSource plain,
            int outcome, Exception fault, CancellationToken token)
        {
            if (outcome < 2)
            {
                typed.TrySetResult(outcome == 0 ? 0 : 42);
                plain.TrySetResult();
            }
            else if (outcome == 2)
            {
                typed.TrySetCanceled(token);
                plain.TrySetCanceled(token);
            }
            else
            {
                typeof(OnityTaskCompletionSource<int>).GetMethod("TrySetFault", k_private)
                    .Invoke(typed, new object[] { fault });
                typeof(OnityTaskCompletionSource<bool>).GetMethod("TrySetFault", k_private)
                    .Invoke(plain, new object[] { fault });
            }
        }

        private static bool FaultObserved(Task task)
        {
            object contingent = typeof(Task).GetField("m_contingentProperties", k_private).GetValue(task);
            if (contingent == null)
            {
                return false;
            }
            object holder = contingent.GetType().GetField("m_exceptionsHolder", k_private).GetValue(contingent);
            return holder != null && (bool)holder.GetType().GetField("m_isHandled", k_private).GetValue(holder);
        }

        private static void Join(params Task[] workers)
        {
            Assert.That(Task.WaitAll(workers, TimeSpan.FromSeconds(5)), Is.True, "Timeout worker did not finish.");
        }

        private static void Wait(Func<bool> condition)
        {
            Assert.That(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), Is.True);
        }

        private static Exception Catch(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static async OnityTask<int> Native(Gate gate)
        {
            await gate;
            return 42;
        }

        private static async OnityTask NativeUntyped(Gate gate)
        {
            await gate;
        }

        private sealed class Gate : ICriticalNotifyCompletion
        {
            private Action m_continuation;
            public bool IsCompleted => false;
            public Gate GetAwaiter() => this;
            public void GetResult()
            {
            }
            public void OnCompleted(Action continuation) => m_continuation = continuation;
            public void UnsafeOnCompleted(Action continuation) => m_continuation = continuation;
            public void Complete() => Interlocked.Exchange(ref m_continuation, null)?.Invoke();
        }

        private sealed class DerivedSource<T> : OnityTaskCompletionSource<T>
        {
        }

        private sealed class CountingContext : SynchronizationContext
        {
            internal int Posts;
            public override void Post(SendOrPostCallback callback, object state)
            {
                Interlocked.Increment(ref Posts);
            }
        }

        private struct Result
        {
            internal bool Timeout;
            internal int Value;
        }

        private sealed class Output
        {
            internal int Shape;
            internal OnityTask<int> Typed;
            internal OnityTask Plain;
            internal OnityTask<(bool isTimeout, int result)> TypedFlag;
            internal OnityTask<bool> PlainFlag;
            internal bool IsFaulted => Shape == 0 ? Typed.IsFaulted : Shape == 1 ? Plain.IsFaulted
                : Shape == 2 ? TypedFlag.IsFaulted : PlainFlag.IsFaulted;
            internal bool IsCanceled => Shape == 0 ? Typed.IsCanceled : Shape == 1 ? Plain.IsCanceled
                : Shape == 2 ? TypedFlag.IsCanceled : PlainFlag.IsCanceled;

            internal Result GetResult()
            {
                if (Shape == 0)
                {
                    return new Result { Value = Typed.GetAwaiter().GetResult() };
                }
                if (Shape == 1)
                {
                    Plain.GetAwaiter().GetResult();
                    return default;
                }
                if (Shape == 2)
                {
                    var result = TypedFlag.GetAwaiter().GetResult();
                    return new Result { Timeout = result.isTimeout, Value = result.result };
                }
                return new Result { Timeout = PlainFlag.GetAwaiter().GetResult() };
            }

            internal Output Preserve()
            {
                return new Output
                {
                    Shape = Shape, Typed = Shape == 0 ? Typed.Preserve() : default,
                    Plain = Shape == 1 ? Plain.Preserve() : default,
                    TypedFlag = Shape == 2 ? TypedFlag.Preserve() : default,
                    PlainFlag = Shape == 3 ? PlainFlag.Preserve() : default
                };
            }

            internal Task Bridge()
            {
                if (Shape == 0)
                {
                    return Typed.AsTask();
                }
                if (Shape == 1)
                {
                    return Plain.AsTask();
                }
                return Shape == 2 ? (Task)TypedFlag.AsTask() : PlainFlag.AsTask();
            }
        }
    }
}
