using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityTaskTimeoutPlayModeTests
    {
        private const BindingFlags k_private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type s_updateAnchor = typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate);
        private static readonly Type s_updateMarker = typeof(OnityTaskPlayerLoop)
            .GetNestedType("UpdateMarker", BindingFlags.NonPublic);

        [UnityTest]
        public IEnumerator PausedScaledClock_IncludingPositiveEpsilon_DoesNotExpireUntilTimeAdvances()
        {
            float previousScale = Time.timeScale;
            var scaled = new OnityTaskCompletionSource<int>();
            var tiny = new OnityTaskCompletionSource();
            var unscaled = new OnityTaskCompletionSource<int>();
            var maximum = new OnityTaskCompletionSource();
            try
            {
                Time.timeScale = 0f;
                // Wait for one real frame so the measured scaled clock is already paused.
                yield return null;
                double pausedClock = Time.timeAsDouble;
                var scaledResult = new Observation(scaled.Task.TimeoutWithoutException(0.02f, false));
                var tinyResult = new Observation(tiny.Task.Timeout(float.Epsilon, false));
                var unscaledResult = new Observation(unscaled.Task.Timeout(0.02f));
                var maximumResult = new Observation(maximum.Task.TimeoutWithoutException(float.MaxValue));
                yield return Poll(() => unscaledResult.Count == 1);
                for (int frame = 0; frame < 4; frame++)
                {
                    yield return null;
                    Assert.That(Time.timeAsDouble, Is.EqualTo(pausedClock));
                    Assert.That(tinyResult.Count, Is.Zero, "A strictly positive duration cannot expire on an unchanged clock.");
                    Assert.That(scaledResult.Count, Is.Zero);
                    Assert.That(maximumResult.Count, Is.Zero);
                }
                Assert.That(unscaledResult.Error, Is.TypeOf<TimeoutException>());
                maximum.TrySetResult();
                Assert.That(maximumResult.Count, Is.EqualTo(1));
                Assert.That(maximumResult.Error, Is.Null);
                Assert.That(maximumResult.Timeout, Is.False);
                Time.timeScale = previousScale > 0f ? previousScale : 1f;
                yield return Poll(() => tinyResult.Count == 1 && scaledResult.Count == 1);
                Assert.That(tinyResult.Error, Is.TypeOf<TimeoutException>());
                Assert.That(scaledResult.Error, Is.Null);
                Assert.That(scaledResult.Timeout, Is.True);
                Assert.That(scaledResult.Value, Is.Zero);
            }
            finally
            {
                scaled.TrySetResult(42);
                tiny.TrySetResult();
                unscaled.TrySetResult(42);
                maximum.TrySetResult();
                Time.timeScale = previousScale;
            }
        }

        [UnityTest]
        public IEnumerator PendingProducerOutcomes_AllShapesPreserveFaultsTokensAndWorkerPublicationWithoutContextCapture()
        {
            int main = Thread.CurrentThread.ManagedThreadId;
            bool previousFlow = OnityTask.FlowExecutionContext;
            SynchronizationContext previousContext = SynchronizationContext.Current;
            var local = new AsyncLocal<string>();
            var context = new CountingContext();
            try
            {
                for (int shape = 0; shape < 4; shape++)
                {
                    for (int outcome = 0; outcome < 4; outcome++)
                    {
                        using (var cancellation = new CancellationTokenSource())
                        {
                            var typed = new OnityTaskCompletionSource<int>();
                            var plain = new OnityTaskCompletionSource();
                            Exception fault = outcome == 2 ? (Exception)new OperationCanceledException(cancellation.Token)
                                : new TimeoutException("Producer-owned timeout fault.");
                            Task<int> worker = null;
                            OnityTask.FlowExecutionContext = outcome != 1;
                            local.Value = "caller";
                            Output output;
                            SynchronizationContext.SetSynchronizationContext(context);
                            try
                            {
                                if (outcome == 3)
                                {
                                    using (ExecutionContext.SuppressFlow())
                                    {
                                        output = Wrap(shape, typed.Task, plain.Task, 1000f);
                                        Assert.That(ExecutionContext.IsFlowSuppressed(), Is.True);
                                    }
                                }
                                else
                                {
                                    output = Wrap(shape, typed.Task, plain.Task, 1000f);
                                }
                            }
                            finally
                            {
                                SynchronizationContext.SetSynchronizationContext(previousContext);
                            }
                            var result = new Observation(output, local);
                            try
                            {
                                using (ExecutionContext.SuppressFlow())
                                {
                                    worker = Task.Run(() =>
                                    {
                                        Complete(typed, plain, outcome, fault, cancellation.Token);
                                        return Thread.CurrentThread.ManagedThreadId;
                                    });
                                }
                                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                                Assert.That(result.Count, Is.EqualTo(1));
                                Assert.That(result.Thread, Is.EqualTo(worker.Result));
                                Assert.That(result.Thread, Is.Not.EqualTo(main));
                                Assert.That(result.Ambient, Is.Null);
                                Assert.That(result.Context, Is.Null);
                                Assert.That(context.Posts, Is.Zero);
                                Assert.That(local.Value, Is.EqualTo("caller"));
                                Assert.That(ExecutionContext.IsFlowSuppressed(), Is.False);
                                if (outcome == 0)
                                {
                                    Assert.That(result.Error, Is.Null);
                                    Assert.That(result.Timeout, Is.False);
                                    Assert.That(result.Value, Is.EqualTo(shape % 2 == 0 ? 42 : 0));
                                }
                                else if (outcome == 1)
                                {
                                    Assert.That(result.Error, Is.TypeOf<OperationCanceledException>());
                                    Assert.That(((OperationCanceledException)result.Error).CancellationToken, Is.EqualTo(cancellation.Token));
                                }
                                else
                                {
                                    Assert.That(result.Error, Is.SameAs(fault));
                                    Assert.That(result.Faulted, Is.True);
                                }
                                // The consumed output must not be affected by a later main-thread Stop acknowledgment.
                                yield return null;
                                Assert.That(result.Count, Is.EqualTo(1));
                                var controlSource = new OnityTaskCompletionSource<int>();
                                var control = new Observation(controlSource.Task.TimeoutWithoutException(1000f));
                                controlSource.TrySetResult(7);
                                Assert.That(control.Error, Is.Null);
                                Assert.That(control.Value, Is.EqualTo(7));
                            }
                            finally
                            {
                                typed.TrySetResult(42);
                                plain.TrySetResult();
                                if (worker != null)
                                {
                                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                                }
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
                }
            }
            finally
            {
                local.Value = null;
                OnityTask.FlowExecutionContext = previousFlow;
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        [UnityTest]
        public IEnumerator PendingTaskAndGenuinePreservedNativeInputs_KeepResultAndOutputBridgeSharing()
        {
            for (int family = 0; family < 2; family++)
            {
                var typedGate = new Gate();
                var plainGate = new Gate();
                var typedBcl = new TaskCompletionSource<int>();
                var plainBcl = new TaskCompletionSource<bool>();
                OnityTask<int> typed = family == 0 ? Native(typedGate).Preserve() : new OnityTask<int>(typedBcl.Task);
                OnityTask plain = family == 0 ? NativeUntyped(plainGate).Preserve() : new OnityTask(plainBcl.Task);
                var typedOutput = typed.TimeoutWithoutException(1000f);
                var plainOutput = plain.Timeout(1000f);
                Task<(bool isTimeout, int result)> typedBridge = typedOutput.AsTask();
                Task plainBridge = plainOutput.AsTask();
                try
                {
                    Assert.That(typedOutput.AsTask(), Is.SameAs(typedBridge));
                    Assert.That(plainOutput.AsTask(), Is.SameAs(plainBridge));
                    Task producer = Task.Run(() =>
                    {
                        typedGate.Complete();
                        plainGate.Complete();
                        typedBcl.TrySetResult(42);
                        plainBcl.TrySetResult(true);
                    });
                    Assert.That(producer.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    yield return Poll(() => typedBridge.IsCompleted && plainBridge.IsCompleted);
                    Assert.That(typedBridge.GetAwaiter().GetResult().isTimeout, Is.False);
                    Assert.That(typedBridge.GetAwaiter().GetResult().result, Is.EqualTo(42));
                    Assert.DoesNotThrow(() => plainBridge.GetAwaiter().GetResult());
                    Assert.Throws<InvalidOperationException>(() => typedOutput.GetAwaiter().GetResult());
                    Assert.Throws<InvalidOperationException>(() => plainOutput.GetAwaiter().GetResult());
                }
                finally
                {
                    typedGate.Complete();
                    plainGate.Complete();
                    typedBcl.TrySetResult(42);
                    plainBcl.TrySetResult(true);
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PositivePreclaimedNativeRegistration_FaultsWrapperWithoutStealingFirstConsumer()
        {
            for (int shape = 0; shape < 4; shape++)
            {
                var typedGate = new Gate();
                var plainGate = new Gate();
                OnityTask<int> typed = Native(typedGate);
                OnityTask plain = NativeUntyped(plainGate);
                int firstCalls = 0;
                Exception firstError = null;
                if (shape % 2 == 0)
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
                try
                {
                    Output output = null;
                    Assert.DoesNotThrow(() => output = Wrap(shape, typed, plain, 1000f));
                    var result = new Observation(output);
                    Assert.That(result.Count, Is.EqualTo(1));
                    Assert.That(result.Faulted, Is.True);
                    Assert.That(result.Error, Is.TypeOf<InvalidOperationException>());
                    Assert.That(firstCalls, Is.Zero);
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
                    yield return null;
                    Assert.That(result.Count, Is.EqualTo(1));
                }
                finally
                {
                    typedGate.Complete();
                    plainGate.Complete();
                }
            }
        }

        [UnityTest]
        public IEnumerator PublicUpdateProducerWinsBeforePrivateTimer_EvenWhenDurationHasElapsed()
        {
            var producer = new OnityTaskCompletionSource<int>();
            var result = new Observation(producer.Task.TimeoutWithoutException(float.Epsilon));
            Exception callbackError = null;
            int callbackFrame = -1;
            OnityTask signal = OnityTask.Yield(OnityPlayerLoopTiming.Update);
            signal.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    signal.GetAwaiter().GetResult();
                    callbackFrame = Time.frameCount;
                    producer.TrySetResult(23);
                }
                catch (Exception exception)
                {
                    callbackError = exception;
                }
            });
            try
            {
                yield return Poll(() => result.Count == 1);
                Assert.That(callbackError, Is.Null);
                Assert.That(callbackFrame, Is.GreaterThanOrEqualTo(0));
                Assert.That(result.Frame, Is.EqualTo(callbackFrame));
                Assert.That(result.Timeout, Is.False);
                Assert.That(result.Value, Is.EqualTo(23));
                Assert.That(result.Error, Is.Null);
            }
            finally
            {
                producer.TrySetResult(23);
            }
        }

        [UnityTest]
        public IEnumerator TimersRegisteredDuringCancellationPublicUpdateAndExpiryCallbacks_WaitForNextPass()
        {
            for (int lane = 0; lane < 3; lane++)
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    var outerProducer = new OnityTaskCompletionSource();
                    var childProducer = new OnityTaskCompletionSource<int>();
                    Observation child = null;
                    int registrationFrame = -1;
                    int afterRegistrationFrame = -1;
                    bool pendingAfterPass = false;
                    Exception error = null;
                    Action register = () =>
                    {
                        registrationFrame = Time.frameCount;
                        child = new Observation(childProducer.Task.TimeoutWithoutException(float.Epsilon));
                    };
                    OnityTask trigger = lane == 2 ? outerProducer.Task.Timeout(float.Epsilon)
                        : OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellation.Token);
                    trigger.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        try
                        {
                            try
                            {
                                trigger.GetAwaiter().GetResult();
                            }
                            catch (OperationCanceledException)
                            {
                                if (lane != 0)
                                {
                                    throw;
                                }
                            }
                            catch (TimeoutException)
                            {
                                if (lane != 2)
                                {
                                    throw;
                                }
                            }
                            register();
                        }
                        catch (Exception exception)
                        {
                            error = exception;
                        }
                    });
                    AddAfterProbe(() =>
                    {
                        if (registrationFrame == Time.frameCount && child != null)
                        {
                            afterRegistrationFrame = Time.frameCount;
                            pendingAfterPass = child.Count == 0;
                        }
                    });
                    try
                    {
                        if (lane == 0)
                        {
                            Task worker = Task.Run(() => cancellation.Cancel());
                            Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                        }
                        yield return Poll(() => error != null || (child != null && child.Count == 1));
                        Assert.That(error, Is.Null);
                        Assert.That(afterRegistrationFrame, Is.EqualTo(registrationFrame));
                        Assert.That(pendingAfterPass, Is.True);
                        Assert.That(child.Frame, Is.GreaterThan(registrationFrame));
                        Assert.That(child.Timeout, Is.True);
                        Assert.That(child.Error, Is.Null);
                    }
                    finally
                    {
                        cancellation.Cancel();
                        outerProducer.TrySetResult();
                        childProducer.TrySetResult(42);
                        RemoveProbes();
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator CloseSessionDuringPublicUpdate_CancelsTimersRejectsReentryAndPreventsOldExpiry()
        {
            var producers = new OnityTaskCompletionSource<int>[4];
            var results = new Observation[4];
            for (int i = 0; i < results.Length; i++)
            {
                producers[i] = new OnityTaskCompletionSource<int>();
                results[i] = i < 2 ? new Observation(producers[i].Task.Timeout(float.Epsilon))
                    : new Observation(producers[i].Task.TimeoutWithoutException(float.Epsilon));
            }
            Exception error = null;
            Exception rejected = null;
            var signal = OnityTask.Yield(OnityPlayerLoopTiming.Update);
            signal.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    signal.GetAwaiter().GetResult();
                    CloseOwner();
                    rejected = Catch(() => producers[0].Task.Timeout(1f));
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });
            try
            {
                yield return Poll(() => error != null || results[3].Count == 1);
                Assert.That(error, Is.Null);
                Assert.That(rejected, Is.TypeOf<InvalidOperationException>());
                foreach (var result in results)
                {
                    Assert.That(result.Count, Is.EqualTo(1));
                    Assert.That(result.Error, Is.TypeOf<OperationCanceledException>());
                    Assert.That(result.Canceled, Is.True);
                }
                BeginOwner();
                var freshSource = new OnityTaskCompletionSource<int>();
                var fresh = new Observation(freshSource.Task.TimeoutWithoutException(1000f));
                freshSource.TrySetResult(9);
                yield return null;
                Assert.That(fresh.Value, Is.EqualTo(9));
                Assert.That(fresh.Error, Is.Null);
                foreach (var result in results)
                {
                    Assert.That(result.Count, Is.EqualTo(1));
                }
            }
            finally
            {
                foreach (var producer in producers)
                {
                    producer.TrySetResult(42);
                }
                BeginOwner();
            }
        }

        [UnityTest]
        public IEnumerator FailedRepairBetweenPublicAndPrivatePasses_FaultsTimerAndAllowsFreshSessionControl()
        {
            var source = new OnityTaskCompletionSource<int>();
            var timer = new Observation(source.Task.TimeoutWithoutException(float.Epsilon));
            PlayerLoopSystem removed = default;
            int index = -1;
            Exception repairError = null;
            Exception unexpected = null;
            var signal = OnityTask.Yield(OnityPlayerLoopTiming.Update);
            signal.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    signal.GetAwaiter().GetResult();
                    var loop = PlayerLoop.GetCurrentPlayerLoop();
                    if (!RemoveAnchor(ref loop, out removed, out index))
                    {
                        throw new InvalidOperationException("Test could not remove the public Update anchor.");
                    }
                    PlayerLoop.SetPlayerLoop(loop);
                    repairError = Catch(() => OnityTaskPlayerLoop.Initialize());
                    RestoreAnchor(removed, index);
                    OnityTaskPlayerLoop.Initialize();
                }
                catch (Exception exception)
                {
                    unexpected = exception;
                }
            });
            try
            {
                yield return Poll(() => unexpected != null || timer.Count == 1);
                Assert.That(unexpected, Is.Null);
                Assert.That(repairError, Is.TypeOf<InvalidOperationException>());
                Assert.That(timer.Error, Is.SameAs(repairError));
                Assert.That(timer.Faulted, Is.True);
                var freshProducer = new OnityTaskCompletionSource<int>();
                var fresh = new Observation(freshProducer.Task.TimeoutWithoutException(1000f));
                freshProducer.TrySetResult(11);
                yield return null;
                Assert.That(fresh.Error, Is.Null);
                Assert.That(fresh.Value, Is.EqualTo(11));
                Assert.That(timer.Count, Is.EqualTo(1));
            }
            finally
            {
                source.TrySetResult(42);
                if (index >= 0)
                {
                    RestoreAnchor(removed, index);
                }
                OnityTaskPlayerLoop.Initialize();
            }
        }

        [UnityTest]
        public IEnumerator LegacyRunnerDestroy_DoesNotRetireTimerOwnedByInjectedLoop()
        {
            var source = new OnityTaskCompletionSource<int>();
            var timer = new Observation(source.Task.TimeoutWithoutException(0.01f));
            var legacy = OnityTask.WaitUntil(() => false);
            Type runnerType = typeof(OnityTask).Assembly.GetType("Onity.Unity.Async.OnityTaskRunner", true);
            var runner = (Component)runnerType.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            try
            {
                UnityEngine.Object.DestroyImmediate(runner.gameObject);
                Assert.That(legacy.IsCanceled, Is.True);
                Assert.Throws<OperationCanceledException>(() => legacy.GetAwaiter().GetResult());
                yield return Poll(() => timer.Count == 1);
                Assert.That(timer.Error, Is.Null);
                Assert.That(timer.Timeout, Is.True);
            }
            finally
            {
                source.TrySetResult(42);
            }
        }

        private static Output Wrap(int shape, OnityTask<int> typed, OnityTask plain, float seconds)
        {
            return new Output
            {
                Shape = shape,
                Typed = shape == 0 ? typed.Timeout(seconds) : default,
                Plain = shape == 1 ? plain.Timeout(seconds) : default,
                TypedFlag = shape == 2 ? typed.TimeoutWithoutException(seconds) : default,
                PlainFlag = shape == 3 ? plain.TimeoutWithoutException(seconds) : default
            };
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

        private static IEnumerator Poll(Func<bool> complete)
        {
            for (int frame = 0; frame < 240 && !complete(); frame++)
            {
                yield return null;
            }
            Assert.That(complete(), Is.True, "Timeout PlayerLoop check timed out.");
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

        private static void CloseOwner()
        {
            typeof(OnityTaskPlayerLoop).GetMethod("CloseSession", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
        }

        private static void BeginOwner()
        {
            typeof(OnityTaskPlayerLoop).GetMethod("BeginSession", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { true });
            OnityTaskPlayerLoop.Initialize();
        }

        private static void AddAfterProbe(PlayerLoopSystem.UpdateFunction callback)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Assert.That(InsertAfter(ref loop, s_updateMarker,
                new PlayerLoopSystem { type = typeof(AfterMarker), updateDelegate = callback }), Is.True);
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool InsertAfter(ref PlayerLoopSystem loop, Type adjacent, PlayerLoopSystem node)
        {
            if (loop.subSystemList == null)
            {
                return false;
            }
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == adjacent)
                {
                    var expanded = new PlayerLoopSystem[loop.subSystemList.Length + 1];
                    Array.Copy(loop.subSystemList, 0, expanded, 0, i + 1);
                    expanded[i + 1] = node;
                    Array.Copy(loop.subSystemList, i + 1, expanded, i + 2, loop.subSystemList.Length - i - 1);
                    loop.subSystemList = expanded;
                    return true;
                }
                var child = loop.subSystemList[i];
                if (InsertAfter(ref child, adjacent, node))
                {
                    loop.subSystemList[i] = child;
                    return true;
                }
            }
            return false;
        }

        private static void RemoveProbes()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Filter(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void Filter(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null)
            {
                return;
            }
            var retained = new List<PlayerLoopSystem>();
            foreach (var original in loop.subSystemList)
            {
                if (original.type == typeof(AfterMarker))
                {
                    continue;
                }
                var child = original;
                Filter(ref child);
                retained.Add(child);
            }
            loop.subSystemList = retained.ToArray();
        }

        private static bool RemoveAnchor(ref PlayerLoopSystem loop, out PlayerLoopSystem removed, out int index)
        {
            removed = default;
            index = -1;
            if (loop.subSystemList == null)
            {
                return false;
            }
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.type == typeof(UnityEngine.PlayerLoop.Update) && loop.subSystemList[i].type == s_updateAnchor)
                {
                    removed = loop.subSystemList[i];
                    index = i;
                    var retained = new List<PlayerLoopSystem>(loop.subSystemList);
                    retained.RemoveAt(i);
                    loop.subSystemList = retained.ToArray();
                    return true;
                }
                var child = loop.subSystemList[i];
                if (RemoveAnchor(ref child, out removed, out index))
                {
                    loop.subSystemList[i] = child;
                    return true;
                }
            }
            return false;
        }

        private static void RestoreAnchor(PlayerLoopSystem anchor, int index)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            RestoreInParent(ref loop, anchor, index);
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool RestoreInParent(ref PlayerLoopSystem loop, PlayerLoopSystem anchor, int index)
        {
            if (loop.subSystemList == null)
            {
                return false;
            }
            if (loop.type == typeof(UnityEngine.PlayerLoop.Update))
            {
                foreach (var existing in loop.subSystemList)
                {
                    if (existing.type == anchor.type)
                    {
                        return true;
                    }
                }
                var children = new List<PlayerLoopSystem>(loop.subSystemList);
                children.Insert(Math.Min(index, children.Count), anchor);
                loop.subSystemList = children.ToArray();
                return true;
            }
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var child = loop.subSystemList[i];
                if (RestoreInParent(ref child, anchor, index))
                {
                    loop.subSystemList[i] = child;
                    return true;
                }
            }
            return false;
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

        private sealed class CountingContext : SynchronizationContext
        {
            internal int Posts;
            public override void Post(SendOrPostCallback callback, object state)
            {
                Interlocked.Increment(ref Posts);
            }
        }

        private sealed class AfterMarker
        {
        }

        private sealed class Output
        {
            internal int Shape;
            internal OnityTask<int> Typed;
            internal OnityTask Plain;
            internal OnityTask<(bool isTimeout, int result)> TypedFlag;
            internal OnityTask<bool> PlainFlag;
        }

        private sealed class Observation
        {
            internal int Count;
            internal int Thread;
            internal int Frame = -1;
            internal int Value;
            internal bool Timeout;
            internal bool Faulted;
            internal bool Canceled;
            internal Exception Error;
            internal string Ambient;
            internal SynchronizationContext Context;

            internal Observation(OnityTask<int> task) : this(new Output { Shape = 0, Typed = task })
            {
            }
            internal Observation(OnityTask task) : this(new Output { Shape = 1, Plain = task })
            {
            }
            internal Observation(OnityTask<(bool isTimeout, int result)> task)
                : this(new Output { Shape = 2, TypedFlag = task })
            {
            }
            internal Observation(OnityTask<bool> task) : this(new Output { Shape = 3, PlainFlag = task })
            {
            }

            internal Observation(Output output, AsyncLocal<string> local = null)
            {
                int main = System.Threading.Thread.CurrentThread.ManagedThreadId;
                Action read = () =>
                {
                    Thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    Context = SynchronizationContext.Current;
                    Ambient = local?.Value;
                    if (Thread == main)
                    {
                        Frame = Time.frameCount;
                    }
                    try
                    {
                        if (output.Shape == 0)
                        {
                            Faulted = output.Typed.IsFaulted;
                            Canceled = output.Typed.IsCanceled;
                            Value = output.Typed.GetAwaiter().GetResult();
                        }
                        else if (output.Shape == 1)
                        {
                            Faulted = output.Plain.IsFaulted;
                            Canceled = output.Plain.IsCanceled;
                            output.Plain.GetAwaiter().GetResult();
                        }
                        else if (output.Shape == 2)
                        {
                            Faulted = output.TypedFlag.IsFaulted;
                            Canceled = output.TypedFlag.IsCanceled;
                            var result = output.TypedFlag.GetAwaiter().GetResult();
                            Timeout = result.isTimeout;
                            Value = result.result;
                        }
                        else
                        {
                            Faulted = output.PlainFlag.IsFaulted;
                            Canceled = output.PlainFlag.IsCanceled;
                            Timeout = output.PlainFlag.GetAwaiter().GetResult();
                        }
                    }
                    catch (Exception exception)
                    {
                        Error = exception;
                    }
                    Interlocked.Increment(ref Count);
                };
                if (output.Shape == 0)
                {
                    output.Typed.GetAwaiter().UnsafeOnCompleted(read);
                }
                else if (output.Shape == 1)
                {
                    output.Plain.GetAwaiter().UnsafeOnCompleted(read);
                }
                else if (output.Shape == 2)
                {
                    output.TypedFlag.GetAwaiter().UnsafeOnCompleted(read);
                }
                else
                {
                    output.PlainFlag.GetAwaiter().UnsafeOnCompleted(read);
                }
            }
        }
    }
}
