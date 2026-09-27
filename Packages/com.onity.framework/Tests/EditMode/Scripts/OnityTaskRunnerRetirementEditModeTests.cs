using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    public sealed class OnityTaskRunnerRetirementEditModeTests
    {
        private static readonly object s_domainSentinel = new object();
        private static readonly FieldInfo s_instance = typeof(OnityTask).Assembly
            .GetType("Onity.Unity.Async.OnityTaskRunner", true)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void EditRunnerDestroy_RetiresUnawaitedAllQueues_AndManualReplacementStillWorks()
        {
            OnityTask[] waits =
            {
                OnityTask.NextFrame(), OnityTask.DelayFrames(int.MaxValue),
                OnityTask.NextFixedFrame(), OnityTask.NextLateFrame(),
                OnityTask.Delay(1000f), OnityTask.WaitUntil(() => false), OnityTask.WaitWhile(() => true)
            };
            GameObject owner = CurrentRunner();
            try
            {
                foreach (var task in waits)
                {
                    Assert.That(task.IsCompleted, Is.False);
                }
                UnityEngine.Object.DestroyImmediate(owner);
                foreach (var task in waits)
                {
                    Assert.That(task.IsCanceled, Is.True);
                    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                }
                var replacement = OnityTask.WaitUntil(() => false);
                Assert.That(CurrentRunner(), Is.Not.Null);
                Assert.That(CurrentRunner(), Is.Not.SameAs(owner));
                UnityEngine.Object.DestroyImmediate(CurrentRunner());
                Assert.Throws<OperationCanceledException>(() => replacement.GetAwaiter().GetResult());
            }
            finally
            {
                DestroyCurrent();
            }
        }

        [UnityTest]
        public IEnumerator ActualReloadDisabledPlayEditPlay_PreservesAwakeAndClosesObservedAndUnawaitedLegacyWaits()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) == 0)
            {
                Assert.Ignore("Requires the verification host's existing disabled domain and scene reload settings.");
            }
            object sentinel = s_domainSentinel;
            Type probeType = Type.GetType("Onity.Tests.PlayMode.OnityTaskRunnerRetirementAwakeProbe, Onity.Tests.PlayMode", true);
            Component probe = null;
            try
            {
                for (int session = 0; session < 2; session++)
                {
                    probe = new GameObject("Onity Legacy Retirement Awake Probe").AddComponent(probeType);
                    yield return new EnterPlayMode(false);
                    Assert.That(ReferenceEquals(sentinel, s_domainSentinel), Is.True, "Domain reloaded on entry.");
                    for (int frame = 0; frame < 240 && !Read<bool>(probe, "ShortCompleted"); frame++)
                    {
                        yield return null;
                    }
                    Assert.That(probe != null, Is.True, "Awake probe was lost during Play.");
                    Assert.That(Read<string>(probe, "Error"), Is.Null);
                    Assert.That(Read<bool>(probe, "RegisteredInAwake"), Is.True);
                    Assert.That(Read<bool>(probe, "AwakeBeforeEnteredPlay"), Is.True);
                    Assert.That(Read<bool>(probe, "ShortCompleted"), Is.True);
                    object completedSession = probe;
                    yield return new ExitPlayMode();
                    Assert.That(ReferenceEquals(sentinel, s_domainSentinel), Is.True, "Domain reloaded on exit.");
                    probeType.GetMethod("ObserveUnawaitedAfterExit").Invoke(completedSession, null);
                    Assert.That(Read<string>(completedSession, "Error"), Is.Null);
                    Assert.That(Read<int>(completedSession, "CanceledCount"), Is.EqualTo(2));
                    Assert.That(Read<bool>(completedSession, "UnawaitedCanceled"), Is.True);
                    Assert.That(Read<bool>(completedSession, "ClosingRejected"), Is.True);
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
            DestroyCurrent();
        }

        private static T Read<T>(object probe, string name)
        {
            Assert.That(ReferenceEquals(probe, null), Is.False);
            return (T)probe.GetType().GetField(name).GetValue(probe);
        }

        private static GameObject CurrentRunner()
        {
            var runner = (Component)s_instance.GetValue(null);
            return runner != null ? runner.gameObject : null;
        }

        private static void DestroyCurrent()
        {
            GameObject owner = CurrentRunner();
            if (owner != null)
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
    }
}
