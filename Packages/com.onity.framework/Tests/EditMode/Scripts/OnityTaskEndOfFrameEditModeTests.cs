using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    public sealed class OnityTaskEndOfFrameEditModeTests
    {
        private static readonly object s_domainSentinel = new object();

        [Test]
        public void EditAndWorkerExecution_RejectBeforePrecancellation()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<InvalidOperationException>(() => OnityTask.WaitForEndOfFrame());
                Assert.Throws<InvalidOperationException>(() => OnityTask.WaitForEndOfFrame(cancellation.Token));
                Task<Exception> worker = Task.Run(() =>
                {
                    try
                    {
                        OnityTask.WaitForEndOfFrame(cancellation.Token);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                });
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(worker.Result, Is.TypeOf<InvalidOperationException>());
            }
        }

        [UnityTest]
        public IEnumerator ReloadDisabledAwake_ValidatesRenderingAndReopensSessionAcrossActualTransitions()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) == 0)
            {
                Assert.Ignore("Requires the verification host's existing disabled domain and scene reload settings.");
            }
            object sentinel = s_domainSentinel;
            Type probeType = Type.GetType("Onity.Tests.PlayMode.OnityTaskEndOfFrameAwakeProbe, Onity.Tests.PlayMode", true);
            Component probe = null;
            try
            {
                for (int session = 0; session < 2; session++)
                {
                    probe = new GameObject("Onity EOF Awake Guard Probe").AddComponent(probeType);
                    yield return new EnterPlayMode(false);
                    Assert.That(ReferenceEquals(sentinel, s_domainSentinel), Is.True);
                    Assert.That(probe != null, Is.True);
                    Assert.That(Read<string>(probe, "Error"), Is.Null);
                    Assert.That(Read<bool>(probe, "AwakeBeforeEnteredPlay"), Is.True);
                    Assert.That(Read<bool>(probe, "ValidatedInAwake"), Is.True);
                    object sessionState = probe;
                    yield return new ExitPlayMode();
                    Assert.That(ReferenceEquals(sentinel, s_domainSentinel), Is.True);
                    probeType.GetMethod("CheckAfterExit").Invoke(sessionState, null);
                    Assert.That(Read<string>(sessionState, "Error"), Is.Null);
                    Assert.That(Read<bool>(sessionState, "EditRejected"), Is.True);
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

        private static T Read<T>(object probe, string name)
        {
            return (T)probe.GetType().GetField(name).GetValue(probe);
        }
    }
}
