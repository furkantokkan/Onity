using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using Unity.Entities;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    public sealed partial class OnityTaskPlayerLoopPlayModeTests
    {
        private static readonly OnityPlayerLoopTiming[] s_eagerTimings =
        {
            OnityPlayerLoopTiming.Update, OnityPlayerLoopTiming.FixedUpdate, OnityPlayerLoopTiming.LateUpdate
        };

        [UnityTest]
        public IEnumerator YieldPhaseOrdering_BeforeDrainUsesSameOccurrence_AfterAndReentrantUseNext()
        {
            // The probes bracket the script-callback anchors of the three eager timings; the appended
            // timings are covered by OnityTaskTimingPlayModeTests.
            foreach (OnityPlayerLoopTiming timing in s_eagerTimings)
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    OnityTaskPlayerLoop.Initialize();
                    int occurrence = 0;
                    int beforeCompleted = 0;
                    int afterCompleted = 0;
                    int reentrantCompleted = 0;
                    bool beforeRegistered = false;
                    bool afterRegistered = false;
                    bool firstAfterSawBeforeCompletion = false;
                    Exception error = null;
                    AddProbe(Anchor(timing), typeof(BeforeMarker), () =>
                    {
                        occurrence++;
                        if (beforeRegistered)
                        {
                            return;
                        }
                        beforeRegistered = true;
                        try
                        {
                            var wait = OnityTask.Yield(timing, cancellation.Token);
                            Observe(wait, () =>
                            {
                                beforeCompleted = occurrence;
                                var nested = OnityTask.Yield(timing, cancellation.Token);
                                Observe(nested, () => reentrantCompleted = occurrence, exception => error = exception);
                            }, exception => error = exception);
                        }
                        catch (Exception exception)
                        {
                            error = exception;
                        }
                    }, false);
                    AddProbe(Marker(timing), typeof(AfterMarker), () =>
                    {
                        if (afterRegistered)
                        {
                            return;
                        }
                        afterRegistered = true;
                        firstAfterSawBeforeCompletion = beforeCompleted == occurrence && reentrantCompleted == 0;
                        try
                        {
                            var wait = OnityTask.Yield(timing, cancellation.Token);
                            Observe(wait, () => afterCompleted = occurrence, exception => error = exception);
                        }
                        catch (Exception exception)
                        {
                            error = exception;
                        }
                    }, true);
                    try
                    {
                        yield return Wait(() => error != null || (afterCompleted != 0 && reentrantCompleted != 0));
                        Assert.That(error, Is.Null);
                        Assert.That(firstAfterSawBeforeCompletion, Is.True, timing.ToString());
                        Assert.That(beforeCompleted, Is.EqualTo(1));
                        Assert.That(afterCompleted, Is.EqualTo(2));
                        Assert.That(reentrantCompleted, Is.EqualTo(2));
                    }
                    finally
                    {
                        cancellation.Cancel();
                        RemoveTestNodes();
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator NextFrameAndDelayFrames_CountRenderedFramesDespiteMultipleFixedTicks()
        {
            float previousScale = Time.timeScale;
            float previousFixed = Time.fixedDeltaTime;
            float previousCapture = Time.captureDeltaTime;
            int previousFrame = -1;
            int ticks = 0;
            int maximumTicks = 0;
            int[] nextFrames = { -1, -1, -1 };
            int[] delayFrames = { -1, -1, -1 };
            Exception error = null;
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    Time.timeScale = 1f;
                    Time.fixedDeltaTime = 0.005f;
                    Time.captureDeltaTime = 0.04f;
                    OnityTaskPlayerLoop.Initialize();
                    AddProbe(Anchor(OnityPlayerLoopTiming.FixedUpdate), typeof(FixedTickMarker), () =>
                    {
                        if (previousFrame != Time.frameCount)
                        {
                            previousFrame = Time.frameCount;
                            ticks = 0;
                        }
                        ticks++;
                        maximumTicks = Math.Max(maximumTicks, ticks);
                    }, false);
                    int registeredFrame = Time.frameCount;
                    for (int i = 0; i < 3; i++)
                    {
                        int index = i;
                        var next = OnityTask.NextFrame((OnityPlayerLoopTiming)i, cancellation.Token);
                        var delayed = OnityTask.DelayFrames(2, (OnityPlayerLoopTiming)i, cancellation.Token);
                        Observe(next, () => nextFrames[index] = Time.frameCount, exception => error = exception);
                        Observe(delayed, () => delayFrames[index] = Time.frameCount, exception => error = exception);
                    }
                    yield return Wait(() => error != null || (delayFrames[0] >= 0 && delayFrames[1] >= 0 && delayFrames[2] >= 0));
                    Assert.That(error, Is.Null);
                    Assert.That(maximumTicks, Is.GreaterThanOrEqualTo(2), "No actual same-rendered-frame fixed-tick evidence.");
                    for (int i = 0; i < 3; i++)
                    {
                        Assert.That(nextFrames[i] - registeredFrame, Is.GreaterThanOrEqualTo(1));
                        Assert.That(delayFrames[i] - registeredFrame, Is.GreaterThanOrEqualTo(2));
                    }
                }
                finally
                {
                    cancellation.Cancel();
                    RemoveTestNodes();
                    Time.timeScale = previousScale;
                    Time.fixedDeltaTime = previousFixed;
                    Time.captureDeltaTime = previousCapture;
                }
            }
        }

        [UnityTest]
        public IEnumerator PausedFixedWorkerCancellation_PublishesOnMain_AndNewCanceledUpdateWaitNeedsNextPass()
        {
            float previousScale = Time.timeScale;
            int main = Thread.CurrentThread.ManagedThreadId;
            int outerFrame = -1;
            int nestedFrame = -1;
            int callbackThread = 0;
            bool afterChecked = false;
            bool samePassViolation = false;
            Exception error = null;
            Task worker = null;
            using (var cancellation = new CancellationTokenSource())
            using (var nestedCancellation = new CancellationTokenSource())
            {
                try
                {
                    Time.timeScale = 0f;
                    OnityTaskPlayerLoop.Initialize();
                    var wait = OnityTask.DelayFrames(1000, OnityPlayerLoopTiming.FixedUpdate, cancellation.Token);
                    wait.GetAwaiter().UnsafeOnCompleted(() =>
                    {
                        try
                        {
                            ReadCancellation(wait, cancellation.Token);
                            outerFrame = Time.frameCount;
                            callbackThread = Thread.CurrentThread.ManagedThreadId;
                            var nested = OnityTask.Yield(OnityPlayerLoopTiming.Update, nestedCancellation.Token);
                            nested.GetAwaiter().UnsafeOnCompleted(() =>
                            {
                                try
                                {
                                    ReadCancellation(nested, nestedCancellation.Token);
                                    nestedFrame = Time.frameCount;
                                }
                                catch (Exception exception)
                                {
                                    error = exception;
                                }
                            });
                            nestedCancellation.Cancel();
                        }
                        catch (Exception exception)
                        {
                            error = exception;
                        }
                    });
                    AddProbe(Marker(OnityPlayerLoopTiming.Update), typeof(AfterMarker), () =>
                    {
                        if (outerFrame >= 0 && !afterChecked)
                        {
                            afterChecked = true;
                            samePassViolation = nestedFrame >= 0;
                        }
                    }, true);
                    worker = Task.Run(() => cancellation.Cancel());
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(wait.IsCompleted, Is.False, "Worker cancellation published outside the injected main-thread drain.");
                    yield return Wait(() => error != null || nestedFrame >= 0);
                    Assert.That(error, Is.Null);
                    Assert.That(callbackThread, Is.EqualTo(main));
                    Assert.That(afterChecked, Is.True);
                    Assert.That(samePassViolation, Is.False);
                    Assert.That(nestedFrame, Is.GreaterThan(outerFrame));
                }
                finally
                {
                    cancellation.Cancel();
                    nestedCancellation.Cancel();
                    if (worker != null)
                    {
                        Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    }
                    RemoveTestNodes();
                    Time.timeScale = previousScale;
                }
            }
        }

        [UnityTest]
        public IEnumerator FastPathsAndWorkers_RespectPrecedenceWithoutReinstallingRemovedNodes()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                OnityTaskPlayerLoop.Initialize();
                RemoveOwnedNodes();
                try
                {
                    Assert.That(OnityTask.DelayFrames(0, OnityPlayerLoopTiming.Update, default).IsCompletedSuccessfully, Is.True);
                    var canceled = OnityTask.DelayFrames(0, OnityPlayerLoopTiming.Update, cancellation.Token);
                    ReadCancellation(canceled, cancellation.Token);
                    ReadCancellation(OnityTask.Yield(OnityPlayerLoopTiming.FixedUpdate, cancellation.Token), cancellation.Token);
                    Assert.That(CountOwned(PlayerLoop.GetCurrentPlayerLoop()), Is.Zero);
                    Task<Exception[]> worker = Task.Run(() => new[]
                    {
                        Catch(() => OnityTask.Yield((OnityPlayerLoopTiming)99)),
                        Catch(() => OnityTask.DelayFrames(-1, OnityPlayerLoopTiming.Update, default)),
                        Catch(() => OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellation.Token)),
                        Catch(() => OnityTask.NextFrame(OnityPlayerLoopTiming.LateUpdate, default)),
                        Catch(() => OnityTask.DelayFrames(0, OnityPlayerLoopTiming.FixedUpdate, default)),
                        Catch(() => OnityTaskPlayerLoop.Initialize())
                    });
                    Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    var failures = worker.GetAwaiter().GetResult();
                    Assert.That(failures[0], Is.TypeOf<ArgumentOutOfRangeException>());
                    Assert.That(failures[1], Is.TypeOf<ArgumentOutOfRangeException>());
                    for (int i = 2; i < failures.Length; i++)
                    {
                        Assert.That(failures[i], Is.TypeOf<InvalidOperationException>());
                    }
                }
                finally
                {
                    OnityTaskPlayerLoop.Initialize();
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator InitializeRepairsCurrentLoopIdempotently_WithoutLosingPendingTargetsOrForeignNodes()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                OnityTaskPlayerLoop.Initialize();
                int registeredFrame = Time.frameCount;
                int completedFrame = -1;
                int foreignCalls = 0;
                Exception error = null;
                var pending = OnityTask.DelayFrames(3, OnityPlayerLoopTiming.Update, cancellation.Token);
                Observe(pending, () => completedFrame = Time.frameCount, exception => error = exception);
                try
                {
                    RemoveOwnedNodes();
                    AddProbe(Anchor(OnityPlayerLoopTiming.Update), typeof(ForeignMarker), () => foreignCalls++, true);
                    OnityTaskPlayerLoop.Initialize();
                    OnityTaskPlayerLoop.Initialize();
                    Assert.That(CountEager(PlayerLoop.GetCurrentPlayerLoop()), Is.EqualTo(3));
                    Assert.That(Count(PlayerLoop.GetCurrentPlayerLoop(), typeof(ForeignMarker)), Is.EqualTo(1));
                    yield return Wait(() => error != null || completedFrame >= 0);
                    Assert.That(error, Is.Null);
                    Assert.That(completedFrame - registeredFrame, Is.GreaterThanOrEqualTo(3));
                    Assert.That(foreignCalls, Is.GreaterThan(0));
                }
                finally
                {
                    cancellation.Cancel();
                    RemoveTestNodes();
                    OnityTaskPlayerLoop.Initialize();
                }
            }
        }

        [UnityTest]
        public IEnumerator FailedRepair_FaultsPendingAndRejectsReentry_ThenRetriesInSameSession()
        {
            OnityTaskPlayerLoop.Initialize();
            var pending = OnityTask.DelayFrames(1000, OnityPlayerLoopTiming.LateUpdate, default);
            Exception pendingError = null;
            Exception reentryError = null;
            pending.GetAwaiter().UnsafeOnCompleted(() =>
            {
                pendingError = Catch(() => pending.GetAwaiter().GetResult());
                reentryError = Catch(() => OnityTask.Yield(OnityPlayerLoopTiming.Update));
            });
            PlayerLoopSystem anchor = default;
            int anchorIndex = -1;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Assert.That(RemoveAnchor(ref loop, typeof(UnityEngine.PlayerLoop.Update),
                Anchor(OnityPlayerLoopTiming.Update), out anchor, out anchorIndex), Is.True);
            PlayerLoop.SetPlayerLoop(loop);
            try
            {
                Assert.Throws<InvalidOperationException>(() => OnityTaskPlayerLoop.Initialize());
                Assert.That(pendingError, Is.TypeOf<InvalidOperationException>());
                Assert.That(reentryError, Is.TypeOf<InvalidOperationException>());
                Assert.That(CountOwned(PlayerLoop.GetCurrentPlayerLoop()), Is.Zero);
                Assert.Throws<InvalidOperationException>(() => OnityTask.Yield(OnityPlayerLoopTiming.Update));
                RestoreAnchor(anchor, anchorIndex);
                OnityTaskPlayerLoop.Initialize();
                var fresh = OnityTask.Yield(OnityPlayerLoopTiming.Update);
                bool completed = false;
                Exception freshError = null;
                Observe(fresh, () => completed = true, exception => freshError = exception);
                yield return Wait(() => completed || freshError != null);
                Assert.That(freshError, Is.Null);
                Assert.That(completed, Is.True);
            }
            finally
            {
                RestoreAnchor(anchor, anchorIndex);
                OnityTaskPlayerLoop.Initialize();
            }
        }

        [UnityTest]
        public IEnumerator FailedRepairDuringPublication_RetiresOldEpochAndDefersFreshRegistration()
        {
            OnityTaskPlayerLoop.Initialize();
            Exception retiredError = null;
            Exception repairError = null;
            Exception unexpected = null;
            int triggerFrame = -1;
            int freshFrame = -1;
            PlayerLoopSystem removed = default;
            int removedIndex = -1;
            var waits = new[] { OnityTask.Yield(OnityPlayerLoopTiming.Update), OnityTask.Yield(OnityPlayerLoopTiming.Update) };
            foreach (var wait in waits)
            {
                Observe(wait, () =>
                {
                    if (triggerFrame >= 0)
                    {
                        unexpected = new InvalidOperationException("Old epoch published success.");
                        return;
                    }
                    try
                    {
                        triggerFrame = Time.frameCount;
                        var loop = PlayerLoop.GetCurrentPlayerLoop();
                        if (!RemoveAnchor(ref loop, typeof(UnityEngine.PlayerLoop.Update), Anchor(OnityPlayerLoopTiming.Update),
                            out removed, out removedIndex))
                        {
                            throw new InvalidOperationException("Test could not remove the Update anchor.");
                        }
                        PlayerLoop.SetPlayerLoop(loop);
                        repairError = Catch(() => OnityTaskPlayerLoop.Initialize());
                        RestoreAnchor(removed, removedIndex);
                        OnityTaskPlayerLoop.Initialize();
                        var fresh = OnityTask.Yield(OnityPlayerLoopTiming.Update);
                        Observe(fresh, () => freshFrame = Time.frameCount, exception => unexpected = exception);
                    }
                    catch (Exception exception)
                    {
                        unexpected = exception;
                    }
                }, exception => retiredError = exception);
            }
            try
            {
                yield return Wait(() => unexpected != null || freshFrame >= 0);
                Assert.That(unexpected, Is.Null);
                Assert.That(repairError, Is.TypeOf<InvalidOperationException>());
                Assert.That(retiredError, Is.TypeOf<InvalidOperationException>());
                Assert.That(freshFrame, Is.GreaterThan(triggerFrame));
            }
            finally
            {
                if (removedIndex >= 0)
                {
                    RestoreAnchor(removed, removedIndex);
                }
                OnityTaskPlayerLoop.Initialize();
            }
        }

        [UnityTest]
        public IEnumerator NativeReuseAndBridges_KeepConsumerAndStaleTokenRules()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                // A cancelable explicit wait is a pooled single-consumer source.
                OnityTask first = OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellation.Token);
                bool completed = false;
                Exception error = null;
                Observe(first, () => completed = true, exception => error = exception);
                yield return Wait(() => completed || error != null);
                Assert.That(error, Is.Null);
                OnityTask second = OnityTask.NextFrame(OnityPlayerLoopTiming.LateUpdate, cancellation.Token);
                Task bridge = second.AsTask();
                Assert.That(second.AsTask(), Is.SameAs(bridge));
                Assert.Throws<InvalidOperationException>(() => first.GetAwaiter().GetResult());
                yield return Wait(() => bridge.IsCompleted);
                bridge.GetAwaiter().GetResult();
                Assert.Throws<InvalidOperationException>(() => second.GetAwaiter().GetResult());
            }

            // A token-less explicit wait is stateless (D2b): one shared bridge and repeatable reads.
            OnityTask stateless = OnityTask.NextFrame(OnityPlayerLoopTiming.LateUpdate, default);
            Task statelessBridge = stateless.AsTask();
            Assert.That(stateless.AsTask(), Is.SameAs(statelessBridge));
            yield return Wait(() => statelessBridge.IsCompleted);
            statelessBridge.GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => stateless.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => stateless.GetAwaiter().GetResult());
            var preserved = OnityTask.Yield(OnityPlayerLoopTiming.FixedUpdate).Preserve();
            Task firstBridge = preserved.AsTask();
            Task secondBridge = preserved.AsTask();
            yield return Wait(() => firstBridge.IsCompleted && secondBridge.IsCompleted);
            firstBridge.GetAwaiter().GetResult();
            secondBridge.GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => preserved.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => preserved.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator LegacyRunnerDestruction_DoesNotRetireInjectedPendingWait()
        {
            // A legacy delay creates the legacy runner; default frame waits no longer do.
            OnityTask legacy = OnityTask.Delay(0.001f);
            yield return Wait(() => legacy.IsCompleted);
            legacy.GetAwaiter().GetResult();
            var pending = OnityTask.DelayFrames(3, OnityPlayerLoopTiming.Update, default);
            Task bridge = pending.AsTask();
            GameObject runner = null;
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate.name == "OnityTaskRunner")
                {
                    runner = candidate;
                    break;
                }
            }
            Assert.That(runner, Is.Not.Null);
            UnityEngine.Object.DestroyImmediate(runner);
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(CountEager(PlayerLoop.GetCurrentPlayerLoop()), Is.EqualTo(3));
            yield return Wait(() => bridge.IsCompleted);
            Assert.That(bridge.IsCanceled || bridge.IsFaulted, Is.False);
            bridge.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator EntitiesAppend_PreservesNodes_AndSimulationYieldTargetsNextUpdate()
        {
            OnityTaskPlayerLoop.Initialize();
            using (var world = new World("Onity Timing Verification World"))
            {
                var group = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
                var system = world.GetOrCreateSystemManaged<TimingProbeSystem>();
                group.AddSystemToUpdateList(system);
                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
                try
                {
                    Assert.That(CountEager(PlayerLoop.GetCurrentPlayerLoop()), Is.EqualTo(3));
                    Assert.That(ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(world), Is.True);
                    yield return Wait(() => system.CompletedFrame >= 0 || system.Error != null);
                    Assert.That(system.Error, Is.Null);
                    Assert.That(system.CompletedFrame, Is.GreaterThan(system.RegisteredFrame));
                }
                finally
                {
                    ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
                }
            }
        }

        [DisableAutoCreation]
        public partial class TimingProbeSystem : SystemBase
        {
            public int RegisteredFrame = -1;
            public int CompletedFrame = -1;
            public Exception Error;

            protected override void OnUpdate()
            {
                if (RegisteredFrame >= 0)
                {
                    return;
                }
                try
                {
                    RegisteredFrame = UnityEngine.Time.frameCount;
                    var task = OnityTask.Yield(OnityPlayerLoopTiming.Update);
                    Observe(task, () => CompletedFrame = UnityEngine.Time.frameCount, exception => Error = exception);
                }
                catch (Exception exception)
                {
                    Error = exception;
                }
            }
        }

        private static void Observe(OnityTask task, Action complete, Action<Exception> fail)
        {
            task.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    task.GetAwaiter().GetResult();
                    complete();
                }
                catch (Exception exception)
                {
                    fail(exception);
                }
            });
        }

        private static void ReadCancellation(OnityTask task, CancellationToken token)
        {
            try
            {
                task.GetAwaiter().GetResult();
                throw new InvalidOperationException("Expected timing cancellation.");
            }
            catch (OperationCanceledException exception)
            {
                if (exception.CancellationToken != token)
                {
                    throw new InvalidOperationException("Timing cancellation token mismatch.");
                }
            }
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

        private static IEnumerator Wait(Func<bool> completed)
        {
            // Bounded by real time, not by frames: in Editor batch mode a frame can take about 0.15 ms, so
            // 240 frames may end before two fixed steps (2 x fixedDeltaTime of game time) have run.
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!completed() && timer.Elapsed.TotalSeconds < 10d)
            {
                yield return null;
            }
            Assert.That(completed(), Is.True, "Injected timing operation timed out.");
        }

        private static Type Anchor(OnityPlayerLoopTiming timing)
        {
            return timing == OnityPlayerLoopTiming.Update ? typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate)
                : timing == OnityPlayerLoopTiming.FixedUpdate ? typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate)
                : typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate);
        }

        private static Type Marker(OnityPlayerLoopTiming timing)
        {
            string name = timing == OnityPlayerLoopTiming.Update ? "UpdateMarker"
                : timing == OnityPlayerLoopTiming.FixedUpdate ? "FixedUpdateMarker" : "LateUpdateMarker";
            return typeof(OnityTaskPlayerLoop).GetNestedType(name, BindingFlags.NonPublic);
        }

        private static void AddProbe(Type adjacent, Type marker, PlayerLoopSystem.UpdateFunction callback, bool after)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Assert.That(Insert(ref loop, adjacent, new PlayerLoopSystem { type = marker, updateDelegate = callback }, after), Is.True);
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool Insert(ref PlayerLoopSystem loop, Type adjacent, PlayerLoopSystem node, bool after)
        {
            var children = loop.subSystemList;
            if (children == null)
            {
                return false;
            }
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == adjacent)
                {
                    int index = i + (after ? 1 : 0);
                    var expanded = new PlayerLoopSystem[children.Length + 1];
                    Array.Copy(children, 0, expanded, 0, index);
                    expanded[index] = node;
                    Array.Copy(children, index, expanded, index + 1, children.Length - index);
                    loop.subSystemList = expanded;
                    return true;
                }
                var child = children[i];
                if (Insert(ref child, adjacent, node, after))
                {
                    children[i] = child;
                    return true;
                }
            }
            return false;
        }

        private static int Count(PlayerLoopSystem loop, Type marker)
        {
            int count = loop.type == marker ? 1 : 0;
            if (loop.subSystemList != null)
            {
                foreach (var child in loop.subSystemList)
                {
                    count += Count(child, marker);
                }
            }
            return count;
        }

        private static int CountOwned(PlayerLoopSystem loop) => OnityTaskPlayerLoopAwakeProbe.CountOwned(loop);

        /// <summary>Counts the three eager nodes only; appended timings install on first use.</summary>
        private static int CountEager(PlayerLoopSystem loop)
        {
            int count = 0;
            foreach (OnityPlayerLoopTiming timing in s_eagerTimings)
            {
                count += Count(loop, Marker(timing));
            }

            return count;
        }

        private static void RemoveOwnedNodes()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Filter(ref loop, type => type != null && type.DeclaringType == typeof(OnityTaskPlayerLoop));
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void RemoveTestNodes()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Filter(ref loop, type => type == typeof(BeforeMarker) || type == typeof(AfterMarker)
                || type == typeof(FixedTickMarker) || type == typeof(ForeignMarker));
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void Filter(ref PlayerLoopSystem loop, Func<Type, bool> remove)
        {
            if (loop.subSystemList == null)
            {
                return;
            }
            var retained = new List<PlayerLoopSystem>();
            foreach (var original in loop.subSystemList)
            {
                if (remove(original.type))
                {
                    continue;
                }
                var child = original;
                Filter(ref child, remove);
                retained.Add(child);
            }
            loop.subSystemList = retained.ToArray();
        }

        private static bool RemoveAnchor(ref PlayerLoopSystem loop, Type parent, Type anchor,
            out PlayerLoopSystem removed, out int index)
        {
            removed = default;
            index = -1;
            if (loop.subSystemList == null)
            {
                return false;
            }
            if (loop.type == parent)
            {
                for (int i = 0; i < loop.subSystemList.Length; i++)
                {
                    if (loop.subSystemList[i].type == anchor)
                    {
                        removed = loop.subSystemList[i];
                        index = i;
                        var retained = new List<PlayerLoopSystem>(loop.subSystemList);
                        retained.RemoveAt(i);
                        loop.subSystemList = retained.ToArray();
                        return true;
                    }
                }
            }
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var child = loop.subSystemList[i];
                if (RemoveAnchor(ref child, parent, anchor, out removed, out index))
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
            if (Count(loop, anchor.type) == 0)
            {
                Assert.That(RestoreInParent(ref loop, typeof(UnityEngine.PlayerLoop.Update), anchor, index), Is.True);
                PlayerLoop.SetPlayerLoop(loop);
            }
        }

        private static bool RestoreInParent(ref PlayerLoopSystem loop, Type parent, PlayerLoopSystem anchor, int index)
        {
            if (loop.subSystemList == null)
            {
                return false;
            }
            if (loop.type == parent)
            {
                var children = new List<PlayerLoopSystem>(loop.subSystemList);
                children.Insert(Math.Min(index, children.Count), anchor);
                loop.subSystemList = children.ToArray();
                return true;
            }
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var child = loop.subSystemList[i];
                if (RestoreInParent(ref child, parent, anchor, index))
                {
                    loop.subSystemList[i] = child;
                    return true;
                }
            }
            return false;
        }

        private sealed class BeforeMarker
        {
        }

        private sealed class AfterMarker
        {
        }

        private sealed class FixedTickMarker
        {
        }

        private sealed class ForeignMarker
        {
        }
    }
}
