using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    public sealed class OnityTaskPlayerLoopEditModeTests
    {
        private static readonly object s_domainSentinel = new object();

        [Test]
        public void ArgumentsValidateBeforeEditExecution_AndPrecanceledZeroStillRequiresPlay()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.Yield((OnityPlayerLoopTiming)99));
                Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.NextFrame((OnityPlayerLoopTiming)99, cancellation.Token));
                Assert.Throws<ArgumentOutOfRangeException>(() => OnityTask.DelayFrames(-1, OnityPlayerLoopTiming.Update, cancellation.Token));
                Assert.Throws<InvalidOperationException>(() => OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellation.Token));
                Assert.Throws<InvalidOperationException>(() => OnityTask.NextFrame(OnityPlayerLoopTiming.Update, cancellation.Token));
                Assert.Throws<InvalidOperationException>(() => OnityTask.DelayFrames(0, OnityPlayerLoopTiming.Update, cancellation.Token));
                Assert.Throws<InvalidOperationException>(() => OnityTask.DelayFrames(0, OnityPlayerLoopTiming.Update, default));
                Assert.Throws<InvalidOperationException>(() => OnityTaskPlayerLoop.Initialize());
            }
        }

        [Test]
        public void WorkerArgumentsValidateFirst_AndValidCallsRejectEvenWhenPrecanceled()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Task<Exception[]> worker = Task.Run(() => new[]
                {
                    Catch(() => OnityTask.Yield((OnityPlayerLoopTiming)99, cancellation.Token)),
                    Catch(() => OnityTask.DelayFrames(-1, OnityPlayerLoopTiming.Update, cancellation.Token)),
                    Catch(() => OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellation.Token)),
                    Catch(() => OnityTask.NextFrame(OnityPlayerLoopTiming.FixedUpdate, cancellation.Token)),
                    Catch(() => OnityTask.DelayFrames(0, OnityPlayerLoopTiming.LateUpdate, default)),
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
        }

        [UnityTest]
        public IEnumerator ActualReloadDisabledPlayEditPlay_PreservesAwakeAndRetiresObservedAndUnawaitedWaits()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) == 0)
            {
                Assert.Ignore("Requires the verification host's existing disabled domain and scene reload settings.");
            }
            object identity = s_domainSentinel;
            Type probeType = Type.GetType("Onity.Tests.PlayMode.OnityTaskPlayerLoopAwakeProbe, Onity.Tests.PlayMode", true);
            Component probe = null;
            try
            {
                for (int session = 0; session < 2; session++)
                {
                    probe = new GameObject("Onity Timing Awake Probe").AddComponent(probeType);
                    yield return new EnterPlayMode(false);
                    Assert.That(ReferenceEquals(identity, s_domainSentinel), Is.True, "Domain reloaded on entry.");
                    for (int frame = 0; frame < 240 && !Read<bool>(probe, "ShortCompleted"); frame++)
                    {
                        yield return null;
                    }
                    Assert.That(Read<string>(probe, "Error"), Is.Null);
                    Assert.That(Read<bool>(probe, "RegisteredInAwake"), Is.True);
                    Assert.That(Read<bool>(probe, "AwakeBeforeEnteredPlay"), Is.True);
                    Assert.That(Read<bool>(probe, "NodesPresentInAwake"), Is.True);
                    Assert.That(Read<bool>(probe, "ShortCompleted"), Is.True);
                    // Unity may destroy the scene object on exit; callbacks retain this managed state.
                    object completedSession = probe;
                    yield return new ExitPlayMode();
                    Assert.That(ReferenceEquals(identity, s_domainSentinel), Is.True, "Domain reloaded on exit.");
                    probeType.GetMethod("ObserveUnawaitedAfterExit").Invoke(completedSession, null);
                    Assert.That(ReadManaged<string>(completedSession, "Error"), Is.Null);
                    Assert.That(ReadManaged<bool>(completedSession, "LongCanceled"), Is.True);
                    Assert.That(ReadManaged<bool>(completedSession, "UnawaitedCanceled"), Is.True);
                    Assert.That((int)probeType.GetMethod("CountOwned").Invoke(null,
                        new object[] { PlayerLoop.GetCurrentPlayerLoop() }), Is.Zero);
                    if (probe != null)
                    {
                        UnityEngine.Object.DestroyImmediate(probe.gameObject);
                    }
                    probe = null;
                }
            }
            finally
            {
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

        private static T Read<T>(Component probe, string name)
        {
            Assert.That(probe != null, Is.True, "Lifecycle probe disappeared across a reload-disabled transition.");
            return (T)probe.GetType().GetField(name).GetValue(probe);
        }

        private static T ReadManaged<T>(object probe, string name)
        {
            Assert.That(ReferenceEquals(probe, null), Is.False, "Managed session state was lost.");
            return (T)probe.GetType().GetField(name).GetValue(probe);
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
    }
}
