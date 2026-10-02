using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityTaskCancellationEditModeTests
    {
        private const BindingFlags k_private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void NoncancelableAttachment_ReturnsOriginalWithoutClaimingPendingNativeInput()
        {
            var gate = new Gate();
            var plainGate = new Gate();
            var typed = Native(gate);
            var plain = NativeUntyped(plainGate);
            try
            {
                Assert.That(typed.AttachExternalCancellation(default), Is.EqualTo(typed));
                Assert.That(plain.AttachExternalCancellation(default), Is.EqualTo(plain));
                Assert.That(default(OnityTask<int>).AttachExternalCancellation(default), Is.EqualTo(default(OnityTask<int>)));
                Assert.That(default(OnityTask).SuppressCancellationThrow().GetAwaiter().GetResult(), Is.False);
                Assert.That(default(OnityTask<int>).SuppressCancellationThrow().GetAwaiter().GetResult(), Is.EqualTo((false, 0)));
                gate.Complete();
                plainGate.Complete();
                Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo(42));
                Assert.DoesNotThrow(() => plain.GetAwaiter().GetResult());
            }
            finally
            {
                gate.Complete();
                plainGate.Complete();
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CompletedEntry_WinsAgainstPrecanceledExternalToken(int outcome)
        {
            using (var producerCancellation = new CancellationTokenSource())
            using (var externalCancellation = new CancellationTokenSource())
            {
                producerCancellation.Cancel();
                externalCancellation.Cancel();
                var typed = new OnityTaskCompletionSource<int>();
                var plain = new OnityTaskCompletionSource();
                var fault = new InvalidOperationException("completed producer");
                Complete(typed, plain, outcome, fault, producerCancellation.Token);
                OnityTask<int> input = typed.Task;
                OnityTask plainInput = plain.Task;
                var attached = input.AttachExternalCancellation(externalCancellation.Token);
                var plainAttached = plainInput.AttachExternalCancellation(externalCancellation.Token);
                Assert.That(attached, Is.EqualTo(input));
                Assert.That(plainAttached, Is.EqualTo(plainInput));
                CheckOutcome(attached, plainAttached, outcome, fault, producerCancellation.Token);
                Assert.That(default(OnityTask<int>).AttachExternalCancellation(externalCancellation.Token)
                    .GetAwaiter().GetResult(), Is.Zero);
                Assert.DoesNotThrow(() => default(OnityTask).AttachExternalCancellation(externalCancellation.Token)
                    .GetAwaiter().GetResult());
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void PendingPrecancellation_WinsWithoutCancelingProducer_AndObservesLateOutcome(int outcome)
        {
            using (var producerCancellation = new CancellationTokenSource())
            using (var externalCancellation = new CancellationTokenSource())
            {
                producerCancellation.Cancel();
                externalCancellation.Cancel();
                var typed = new OnityTaskCompletionSource<int>();
                var plain = new OnityTaskCompletionSource();
                var attached = typed.Task.AttachExternalCancellation(externalCancellation.Token);
                var plainAttached = plain.Task.AttachExternalCancellation(externalCancellation.Token);
                try
                {
                    Assert.That(typed.Task.IsCompleted, Is.False);
                    Assert.That(plain.Task.IsCompleted, Is.False);
                    CheckOutcome(attached, plainAttached, 2, null, externalCancellation.Token);
                    Complete(typed, plain, outcome, new InvalidOperationException("late producer"), producerCancellation.Token);
                    Assert.That(typed.Task.IsCanceled, Is.EqualTo(outcome == 2));
                    if (outcome == 1)
                    {
                        Assert.That(FaultObserved(typed.Task.AsTask()), Is.True);
                        Assert.That(FaultObserved(plain.Task.AsTask()), Is.True);
                    }
                }
                finally
                {
                    typed.TrySetResult(0);
                    plain.TrySetResult();
                }
            }
        }

        [Test]
        public void ProducerFirstCancellation_RetainsProducerTokenAfterExternalCancellation()
        {
            using (var producerCancellation = new CancellationTokenSource())
            using (var externalCancellation = new CancellationTokenSource())
            {
                producerCancellation.Cancel();
                var typed = new OnityTaskCompletionSource<int>();
                var plain = new OnityTaskCompletionSource();
                var attached = typed.Task.AttachExternalCancellation(externalCancellation.Token);
                var plainAttached = plain.Task.AttachExternalCancellation(externalCancellation.Token);
                try
                {
                    typed.TrySetCanceled(producerCancellation.Token);
                    plain.TrySetCanceled(producerCancellation.Token);
                    externalCancellation.Cancel();
                    CheckOutcome(attached, plainAttached, 2, null, producerCancellation.Token);
                }
                finally
                {
                    typed.TrySetResult(0);
                    plain.TrySetResult();
                }
            }
        }

        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(false, 3)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(true, 3)]
        public void Suppression_MapsOnlyActualCancellation_AndPreservesSuccessAndFaultStatus(bool completed, int outcome)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var typed = new OnityTaskCompletionSource<int>();
                var plain = new OnityTaskCompletionSource();
                Exception fault = outcome == 3 ? new OperationCanceledException(cancellation.Token)
                    : new InvalidOperationException("suppression fault");
                if (completed)
                {
                    CompleteSuppressionInputs(typed, plain, outcome, fault, cancellation.Token);
                }
                var suppressed = typed.Task.SuppressCancellationThrow();
                var plainSuppressed = plain.Task.SuppressCancellationThrow();
                try
                {
                    if (!completed)
                    {
                        Assert.That(suppressed.IsCompleted, Is.False);
                        CompleteSuppressionInputs(typed, plain, outcome, fault, cancellation.Token);
                    }
                    if (outcome <= 1)
                    {
                        Assert.That(suppressed.IsCompletedSuccessfully, Is.True);
                        Assert.That(plainSuppressed.IsCompletedSuccessfully, Is.True);
                        Assert.That(suppressed.GetAwaiter().GetResult(), Is.EqualTo((outcome == 1, outcome == 1 ? 0 : 42)));
                        Assert.That(plainSuppressed.GetAwaiter().GetResult(), Is.EqualTo(outcome == 1));
                    }
                    else
                    {
                        Assert.That(suppressed.IsFaulted, Is.True);
                        Assert.That(plainSuppressed.IsFaulted, Is.True);
                        Assert.That(Assert.Catch(() => suppressed.GetAwaiter().GetResult()), Is.SameAs(fault));
                        Assert.That(Assert.Catch(() => plainSuppressed.GetAwaiter().GetResult()), Is.SameAs(fault));
                    }
                }
                finally
                {
                    typed.TrySetResult(0);
                    plain.TrySetResult();
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TaskBackedFaultedOce_RemainsFaultedThroughBothExtensions(bool completed)
        {
            using (var external = new CancellationTokenSource())
            {
                var fault = new OperationCanceledException("fault rather than cancellation");
                var typedProducer = new TaskCompletionSource<int>();
                var plainProducer = new TaskCompletionSource<bool>();
                if (completed)
                {
                    typedProducer.TrySetException(fault);
                    plainProducer.TrySetException(fault);
                }
                var typed = OnityTask<int>.FromTask(typedProducer.Task);
                var plain = OnityTask.FromTask(plainProducer.Task);
                var attached = typed.AttachExternalCancellation(external.Token);
                var plainAttached = plain.AttachExternalCancellation(external.Token);
                var suppressed = typed.SuppressCancellationThrow();
                var plainSuppressed = plain.SuppressCancellationThrow();
                try
                {
                    typedProducer.TrySetException(fault);
                    plainProducer.TrySetException(fault);
                    Wait(() => attached.IsCompleted && plainAttached.IsCompleted
                        && suppressed.IsCompleted && plainSuppressed.IsCompleted);
                    Assert.That(attached.IsFaulted && plainAttached.IsFaulted
                        && suppressed.IsFaulted && plainSuppressed.IsFaulted, Is.True);
                    Assert.That(Assert.Catch(() => attached.GetAwaiter().GetResult()), Is.SameAs(fault));
                    Assert.That(Assert.Catch(() => plainAttached.GetAwaiter().GetResult()), Is.SameAs(fault));
                    Assert.That(Assert.Catch(() => suppressed.GetAwaiter().GetResult()), Is.SameAs(fault));
                    Assert.That(Assert.Catch(() => plainSuppressed.GetAwaiter().GetResult()), Is.SameAs(fault));
                    external.Cancel();
                }
                finally
                {
                    typedProducer.TrySetResult(0);
                    plainProducer.TrySetResult(false);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeFaultedOce_AttachmentPreservesFaultStatusAndException(bool completed)
        {
            using (var external = new CancellationTokenSource())
            {
                var typed = new OnityTaskCompletionSource<int>();
                var plain = new OnityTaskCompletionSource();
                var fault = new OperationCanceledException("native faulted OCE");
                if (completed)
                {
                    SetFault(typed, fault);
                    SetFault((OnityTaskCompletionSource<bool>)plain, fault);
                }
                var output = typed.Task.AttachExternalCancellation(external.Token);
                var plainOutput = plain.Task.AttachExternalCancellation(external.Token);
                try
                {
                    SetFault(typed, fault);
                    SetFault((OnityTaskCompletionSource<bool>)plain, fault);
                    CheckOutcome(output, plainOutput, 1, fault, default);
                    external.Cancel();
                }
                finally
                {
                    typed.TrySetResult(0);
                    plain.TrySetResult();
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EarlyExternalConsumption_ObservesDerivedLateFaultAndExistingConcurrentFutureBridge(int bridgeTiming)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var producer = new DerivedSource<int>();
                Task<int> bridge = bridgeTiming == 0 ? producer.Task.AsTask() : null;
                var attached = producer.Task.AttachExternalCancellation(cancellation.Token);
                cancellation.Cancel();
                CheckCanceled(attached, cancellation.Token);
                Task worker = null;
                try
                {
                    var next = new OnityTaskCompletionSource<int>();
                    var nextAttached = next.Task.AttachExternalCancellation(cancellation.Token);
                    CheckCanceled(nextAttached, cancellation.Token);
                    next.TrySetResult(9);
                    if (bridgeTiming == 1)
                    {
                        worker = Task.Run(() =>
                        {
                            bridge = producer.Task.AsTask();
                        });
                    }
                    producer.TrySetException(new InvalidOperationException("derived late fault"));
                    if (worker != null)
                    {
                        Join(worker);
                    }
                    bridge = bridge ?? producer.Task.AsTask();
                    Assert.That(FaultObserved(bridge), Is.True);
                }
                finally
                {
                    producer.TrySetResult(0);
                    if (worker != null)
                    {
                        Join(worker);
                    }
                    if (bridge != null && bridge.IsFaulted)
                    {
                        GC.KeepAlive(bridge.Exception);
                    }
                }
            }
        }

        [Test]
        public void NativeAndPreservedInputs_ConsumeOnce_AndOutputBridgeAndPreserveShareResults()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var gate = new Gate();
                var native = Native(gate);
                var attached = native.AttachExternalCancellation(cancellation.Token);
                var bridge = attached.AsTask();
                try
                {
                    Assert.That(attached.AsTask(), Is.SameAs(bridge));
                    gate.Complete();
                    Join(bridge);
                    Assert.That(bridge.GetAwaiter().GetResult(), Is.EqualTo(42));
                    Assert.Throws<InvalidOperationException>(() => attached.GetAwaiter().GetResult());
                    Assert.Throws<InvalidOperationException>(() => native.GetAwaiter().GetResult());
                }
                finally
                {
                    gate.Complete();
                }
                var plainGate = new Gate();
                var plain = NativeUntyped(plainGate);
                var suppressed = plain.SuppressCancellationThrow();
                try
                {
                    plainGate.Complete();
                    Assert.That(suppressed.GetAwaiter().GetResult(), Is.False);
                    Assert.Throws<InvalidOperationException>(() => suppressed.GetAwaiter().GetResult());
                    Assert.Throws<InvalidOperationException>(() => plain.GetAwaiter().GetResult());
                }
                finally
                {
                    plainGate.Complete();
                }
                var preservedGate = new Gate();
                var preserved = Native(preservedGate).Preserve();
                var result = preserved.SuppressCancellationThrow().Preserve();
                try
                {
                    preservedGate.Complete();
                    Assert.That(result.GetAwaiter().GetResult(), Is.EqualTo((false, 42)));
                    Assert.That(result.GetAwaiter().GetResult(), Is.EqualTo((false, 42)));
                    Assert.That(preserved.GetAwaiter().GetResult(), Is.EqualTo(42));
                }
                finally
                {
                    preservedGate.Complete();
                }
            }
        }

        [Test]
        public void ConcurrentProducerAndExternalCancellation_PublishExactlyOneMatchingOutcome()
        {
            for (int repeat = 0; repeat < 24; repeat++)
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    var producer = new OnityTaskCompletionSource<int>();
                    var output = producer.Task.AttachExternalCancellation(cancellation.Token);
                    var observation = new Observation<int>();
                    Observe(output, observation);
                    Task complete = Task.Run(() => producer.TrySetResult(42));
                    Task cancel = Task.Run(() => cancellation.Cancel());
                    try
                    {
                        Join(complete, cancel);
                        Wait(() => Volatile.Read(ref observation.calls) != 0);
                        Assert.That(observation.calls, Is.EqualTo(1));
                        if (observation.error == null)
                        {
                            Assert.That(observation.value, Is.EqualTo(42));
                        }
                        else
                        {
                            Assert.That(observation.error, Is.TypeOf<OperationCanceledException>());
                            Assert.That(((OperationCanceledException)observation.error).CancellationToken,
                                Is.EqualTo(cancellation.Token));
                        }
                    }
                    finally
                    {
                        producer.TrySetResult(0);
                        Join(complete, cancel);
                    }
                }
            }
        }

        [Test]
        public void CancellationDuringFactoryRegistration_IsBoundedAndStillObservesProducer()
        {
            // Public scheduling races cover the boundary; they do not prove a private assignment interleaving.
            for (int repeat = 0; repeat < 24; repeat++)
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    var typed = new OnityTaskCompletionSource<int>();
                    var plain = new OnityTaskCompletionSource();
                    Task cancel = Task.Run(() => cancellation.Cancel());
                    try
                    {
                        var output = typed.Task.AttachExternalCancellation(cancellation.Token);
                        var plainOutput = plain.Task.AttachExternalCancellation(cancellation.Token);
                        Join(cancel);
                        Wait(() => output.IsCompleted && plainOutput.IsCompleted);
                        CheckOutcome(output, plainOutput, 2, null, cancellation.Token);
                        typed.TrySetException(new InvalidOperationException("registration race typed loser"));
                        plain.TrySetException(new InvalidOperationException("registration race plain loser"));
                        Assert.That(FaultObserved(typed.Task.AsTask()), Is.True);
                        Assert.That(FaultObserved(plain.Task.AsTask()), Is.True);
                    }
                    finally
                    {
                        typed.TrySetResult(0);
                        plain.TrySetResult();
                        Join(cancel);
                    }
                }
            }
        }

        [Test]
        public void ActiveCancelConsumer_CanWaitForProducerAndReenterWithoutRegistrationDisposalDeadlock()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var producer = new OnityTaskCompletionSource<int>();
                var nestedProducer = new OnityTaskCompletionSource<int>();
                var output = producer.Task.AttachExternalCancellation(cancellation.Token);
                int calls = 0;
                bool producerFinishedInsideCallback = false;
                Exception callbackError = null;
                Task producerWorker = null;
                output.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        try
                        {
                            output.GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        var nested = nestedProducer.Task.AttachExternalCancellation(cancellation.Token);
                        try
                        {
                            nested.GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        producerWorker = Task.Run(() => producer.TrySetException(
                            new InvalidOperationException("producer during active cancel callback")));
                        producerFinishedInsideCallback = producerWorker.Wait(TimeSpan.FromSeconds(2));
                    }
                    catch (Exception exception)
                    {
                        callbackError = exception;
                    }
                    finally
                    {
                        Interlocked.Increment(ref calls);
                    }
                });
                Task cancelWorker = Task.Run(() => cancellation.Cancel());
                try
                {
                    Join(cancelWorker);
                    if (producerWorker != null)
                    {
                        Join(producerWorker);
                    }
                    Assert.That(callbackError, Is.Null);
                    Assert.That(producerFinishedInsideCallback, Is.True,
                        "Producer cleanup blocked waiting for the active cancellation consumer.");
                    Assert.That(calls, Is.EqualTo(1));
                    cancellation.Cancel();
                    nestedProducer.TrySetResult(9);
                    Assert.That(FaultObserved(producer.Task.AsTask()), Is.True);
                }
                finally
                {
                    producer.TrySetResult(0);
                    nestedProducer.TrySetResult(0);
                    Join(cancelWorker);
                    if (producerWorker != null)
                    {
                        Join(producerWorker);
                    }
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TokenObserver_DoesNotCaptureCallerContext_AndRestoresFlowSuppression(int mode)
        {
            bool previousFlow = OnityTask.FlowExecutionContext;
            SynchronizationContext previousContext = SynchronizationContext.Current;
            var context = new CountingContext();
            var local = new AsyncLocal<string>();
            using (var cancellation = new CancellationTokenSource())
            {
                var producer = new OnityTaskCompletionSource<int>();
                var observation = new Observation<int>();
                string observedLocal = null;
                Task worker = null;
                try
                {
                    OnityTask.FlowExecutionContext = mode != 1;
                    local.Value = "caller";
                    SynchronizationContext.SetSynchronizationContext(context);
                    OnityTask<int> output;
                    if (mode == 2)
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            output = producer.Task.AttachExternalCancellation(cancellation.Token);
                            Assert.That(ExecutionContext.IsFlowSuppressed(), Is.True);
                        }
                    }
                    else
                    {
                        output = producer.Task.AttachExternalCancellation(cancellation.Token);
                    }
                    Assert.That(ExecutionContext.IsFlowSuppressed(), Is.False);
                    output.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        observedLocal = local.Value;
                        Read(output, observation);
                    });
                    worker = Task.Run(() =>
                    {
                        string previous = local.Value;
                        try
                        {
                            local.Value = "cancel-worker";
                            cancellation.Cancel();
                        }
                        finally
                        {
                            local.Value = previous;
                        }
                    });
                    Join(worker);
                    Assert.That(observedLocal, Is.EqualTo("cancel-worker"));
                    Assert.That(local.Value, Is.EqualTo("caller"));
                    Assert.That(observation.error, Is.TypeOf<OperationCanceledException>());
                    Assert.That(context.posts, Is.Zero);
                }
                finally
                {
                    producer.TrySetResult(0);
                    if (worker != null)
                    {
                        Join(worker);
                    }
                    local.Value = null;
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                    OnityTask.FlowExecutionContext = previousFlow;
                }
            }
        }

        [Test]
        public void TaskProducerObservers_CompleteBothExtensionsWithoutPostingCreatorContext()
        {
            SynchronizationContext previous = SynchronizationContext.Current;
            var context = new CountingContext();
            var typed = new TaskCompletionSource<int>();
            var plain = new TaskCompletionSource<bool>();
            using (var cancellation = new CancellationTokenSource())
            {
                OnityTask<int> attached;
                OnityTask plainAttached;
                OnityTask<(bool isCanceled, int result)> suppressed;
                OnityTask<bool> plainSuppressed;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(context);
                    attached = OnityTask<int>.FromTask(typed.Task).AttachExternalCancellation(cancellation.Token);
                    plainAttached = OnityTask.FromTask(plain.Task).AttachExternalCancellation(cancellation.Token);
                    suppressed = OnityTask<int>.FromTask(typed.Task).SuppressCancellationThrow();
                    plainSuppressed = OnityTask.FromTask(plain.Task).SuppressCancellationThrow();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
                Task worker = Task.Run(() =>
                {
                    typed.TrySetResult(42);
                    plain.TrySetResult(true);
                });
                try
                {
                    Join(worker);
                    Wait(() => attached.IsCompleted && plainAttached.IsCompleted
                        && suppressed.IsCompleted && plainSuppressed.IsCompleted);
                    Assert.That(attached.GetAwaiter().GetResult(), Is.EqualTo(42));
                    plainAttached.GetAwaiter().GetResult();
                    Assert.That(suppressed.GetAwaiter().GetResult(), Is.EqualTo((false, 42)));
                    Assert.That(plainSuppressed.GetAwaiter().GetResult(), Is.False);
                    Assert.That(context.posts, Is.Zero);
                }
                finally
                {
                    typed.TrySetResult(0);
                    plain.TrySetResult(false);
                    Join(worker);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StaleOrPreclaimedInput_ProducesFaultedWrappersWithoutStealingValidConsumer(bool stale)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var typedGate = new Gate();
                var plainGate = new Gate();
                var typed = Native(typedGate);
                var plain = NativeUntyped(plainGate);
                var firstTyped = new Observation<int>();
                int plainCalls = 0;
                Exception plainError = null;
                if (stale)
                {
                    typedGate.Complete();
                    plainGate.Complete();
                    ResetSource(typed, typeof(OnityTask<int>));
                    ResetSource(plain, typeof(OnityTask));
                }
                else
                {
                    Observe(typed, firstTyped);
                    plain.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        try
                        {
                            plain.GetAwaiter().GetResult();
                        }
                        catch (Exception exception)
                        {
                            plainError = exception;
                        }
                        finally
                        {
                            Interlocked.Increment(ref plainCalls);
                        }
                    });
                }
                try
                {
                    OnityTask<int> attached = default;
                    OnityTask plainAttached = default;
                    OnityTask<(bool isCanceled, int result)> suppressed = default;
                    OnityTask<bool> plainSuppressed = default;
                    Assert.DoesNotThrow(() => attached = typed.AttachExternalCancellation(cancellation.Token));
                    Assert.DoesNotThrow(() => plainAttached = plain.AttachExternalCancellation(cancellation.Token));
                    Assert.DoesNotThrow(() => suppressed = typed.SuppressCancellationThrow());
                    Assert.DoesNotThrow(() => plainSuppressed = plain.SuppressCancellationThrow());
                    Assert.That(attached.IsFaulted && plainAttached.IsFaulted
                        && suppressed.IsFaulted && plainSuppressed.IsFaulted, Is.True);
                    Assert.Throws<InvalidOperationException>(() => attached.GetAwaiter().GetResult());
                    Assert.Throws<InvalidOperationException>(() => plainAttached.GetAwaiter().GetResult());
                    Assert.Throws<InvalidOperationException>(() => suppressed.GetAwaiter().GetResult());
                    Assert.Throws<InvalidOperationException>(() => plainSuppressed.GetAwaiter().GetResult());
                    if (!stale)
                    {
                        typedGate.Complete();
                        plainGate.Complete();
                        Assert.That(firstTyped.calls, Is.EqualTo(1));
                        Assert.That(firstTyped.error, Is.Null);
                        Assert.That(firstTyped.value, Is.EqualTo(42));
                        Assert.That(plainCalls, Is.EqualTo(1));
                        Assert.That(plainError, Is.Null);
                    }
                }
                finally
                {
                    typedGate.Complete();
                    plainGate.Complete();
                }
            }
        }

        private static void ResetSource(object task, Type taskType)
        {
            object source = taskType.GetField("m_state", k_private).GetValue(task);
            source.GetType().BaseType.GetMethod("Reset", k_private)
                .Invoke(source, new object[] { CancellationToken.None });
        }

        private static void Complete(OnityTaskCompletionSource<int> typed, OnityTaskCompletionSource plain,
            int outcome, Exception fault, CancellationToken token)
        {
            if (outcome == 0)
            {
                typed.TrySetResult(42);
                plain.TrySetResult();
            }
            else if (outcome == 1)
            {
                typed.TrySetException(fault);
                plain.TrySetException(fault);
            }
            else
            {
                typed.TrySetCanceled(token);
                plain.TrySetCanceled(token);
            }
        }

        private static void CompleteSuppressionInputs(OnityTaskCompletionSource<int> typed,
            OnityTaskCompletionSource plain, int outcome, Exception fault, CancellationToken token)
        {
            if (outcome < 2)
            {
                Complete(typed, plain, outcome == 0 ? 0 : 2, fault, token);
            }
            else
            {
                SetFault(typed, fault);
                SetFault((OnityTaskCompletionSource<bool>)plain, fault);
            }
        }

        private static void CheckOutcome(OnityTask<int> typed, OnityTask plain, int outcome,
            Exception fault, CancellationToken token)
        {
            if (outcome == 0)
            {
                Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo(42));
                Assert.DoesNotThrow(() => plain.GetAwaiter().GetResult());
            }
            else if (outcome == 1)
            {
                Assert.That(typed.IsFaulted && plain.IsFaulted, Is.True);
                Assert.That(Assert.Catch(() => typed.GetAwaiter().GetResult()), Is.SameAs(fault));
                Assert.That(Assert.Catch(() => plain.GetAwaiter().GetResult()), Is.SameAs(fault));
            }
            else
            {
                CheckCanceled(typed, token);
                Assert.That(plain.IsCanceled, Is.True);
                Assert.That(Assert.Throws<OperationCanceledException>(() => plain.GetAwaiter().GetResult())
                    .CancellationToken, Is.EqualTo(token));
            }
        }

        private static void CheckCanceled(OnityTask<int> task, CancellationToken token)
        {
            Assert.That(task.IsCanceled, Is.True);
            Assert.That(Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult())
                .CancellationToken, Is.EqualTo(token));
        }

        private static void Observe<T>(OnityTask<T> task, Observation<T> observation)
        {
            task.GetAwaiter().UnsafeOnCompleted(() => Read(task, observation));
        }

        private static void Read<T>(OnityTask<T> task, Observation<T> observation)
        {
            try
            {
                observation.value = task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                observation.error = exception;
            }
            finally
            {
                Interlocked.Increment(ref observation.calls);
            }
        }

        private static void Join(params Task[] tasks)
        {
            Assert.That(Task.WaitAll(tasks, TimeSpan.FromSeconds(5)), Is.True, "Owned cancellation worker timed out.");
        }

        private static void Wait(Func<bool> completed)
        {
            Assert.That(SpinWait.SpinUntil(completed, TimeSpan.FromSeconds(5)), Is.True, "Cancellation output timed out.");
        }

        private static bool FaultObserved(Task task)
        {
            object contingent = typeof(Task).GetField("m_contingentProperties", k_private).GetValue(task);
            object holder = contingent.GetType().GetField("m_exceptionsHolder", k_private).GetValue(contingent);
            return (bool)holder.GetType().GetField("m_isHandled", k_private).GetValue(holder);
        }

        private static void SetFault<T>(OnityTaskCompletionSource<T> source, Exception fault)
        {
            typeof(OnityTaskCompletionSource<T>).GetMethod("TrySetFault", k_private)
                .Invoke(source, new object[] { fault });
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

        private sealed class Observation<T>
        {
            public T value;
            public Exception error;
            public int calls;
        }

        private sealed class DerivedSource<T> : OnityTaskCompletionSource<T>
        {
        }

        private sealed class CountingContext : SynchronizationContext
        {
            public int posts;
            public override void Post(SendOrPostCallback callback, object state) => Interlocked.Increment(ref posts);
        }
    }
}
