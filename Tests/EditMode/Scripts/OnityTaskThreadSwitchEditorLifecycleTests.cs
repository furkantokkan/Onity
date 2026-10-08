using System;
using System.Collections;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Editor-controlled harness for the thread-switch session rules. Each test enters and exits
    /// Play Mode, so it stores its state in static fields that survive Play Mode exit and restores
    /// the enter Play Mode options from <see cref="SessionState"/>, which survives domain reloads.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskThreadSwitchEditorLifecycleTests
    {
        private const int k_timeoutIterations = 600;
        private const int k_settleIterations = 5;
        private const string k_savedKey = "Onity.Tests.ThreadSwitch.SavedEnterPlayModeOptions";
        private const string k_savedEnabledKey = "Onity.Tests.ThreadSwitch.EnterPlayModeOptionsEnabled";
        private const string k_savedOptionsKey = "Onity.Tests.ThreadSwitch.EnterPlayModeOptions";

        private static OnityTaskThreadSwitch s_capturedSwitch;
        private static int s_staleRuns;
        private static int s_freshRuns;

        [SetUp]
        public void SaveEnterPlayModeOptions()
        {
            if (SessionState.GetBool(k_savedKey, false))
            {
                return;
            }

            SessionState.SetBool(k_savedEnabledKey, EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(k_savedOptionsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(k_savedKey, true);
        }

        [TearDown]
        public void RestoreEnterPlayModeOptions()
        {
            if (!SessionState.GetBool(k_savedKey, false))
            {
                return;
            }

            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(k_savedEnabledKey, false);
            EditorSettings.enterPlayModeOptions =
                (EnterPlayModeOptions)SessionState.GetInt(k_savedOptionsKey, (int)EnterPlayModeOptions.None);
            SessionState.EraseBool(k_savedKey);
            SessionState.EraseBool(k_savedEnabledKey);
            SessionState.EraseInt(k_savedOptionsKey);
        }

        [UnityTest]
        public IEnumerator PlayModeExit_DiscardsPlayModeContinuations_WithDomainReloadEnabled()
        {
            ConfigureDomainReload(false);
            yield return new EnterPlayMode();
            yield return Settle();

            Assert.That(Application.isPlaying, Is.True);
            s_capturedSwitch = OnityTask.SwitchToMainThread();
            s_staleRuns = 0;
            s_freshRuns = 0;

            yield return new ExitPlayMode();
            yield return Settle();

            Assert.That(Application.isPlaying, Is.False);
            s_capturedSwitch.GetAwaiter().UnsafeOnCompleted(() => s_staleRuns++);
            OnityTask.SwitchToMainThread().GetAwaiter().UnsafeOnCompleted(() => s_freshRuns++);

            yield return WaitForFlag(() => s_freshRuns > 0);
            yield return Settle();

            Assert.That(s_staleRuns, Is.EqualTo(0),
                "A continuation from the finished Play Mode session ran in Edit Mode.");
        }

        [UnityTest]
        public IEnumerator PlayModeExit_DiscardsPlayModeContinuations_WithDomainReloadDisabled()
        {
            ConfigureDomainReload(true);
            yield return new EnterPlayMode();
            yield return Settle();

            Assert.That(Application.isPlaying, Is.True);
            s_capturedSwitch = OnityTask.SwitchToMainThread();
            s_staleRuns = 0;
            s_freshRuns = 0;

            yield return new ExitPlayMode();
            yield return Settle();

            Assert.That(Application.isPlaying, Is.False);
            s_capturedSwitch.GetAwaiter().UnsafeOnCompleted(() => s_staleRuns++);
            OnityTask.SwitchToMainThread().GetAwaiter().UnsafeOnCompleted(() => s_freshRuns++);

            yield return WaitForFlag(() => s_freshRuns > 0);
            yield return Settle();

            Assert.That(s_staleRuns, Is.EqualTo(0),
                "A continuation from the finished Play Mode session ran in Edit Mode.");
        }

        [UnityTest]
        public IEnumerator PlayModeEntry_DiscardsEditModeContinuations_WithDomainReloadDisabled()
        {
            ConfigureDomainReload(true);
            Assert.That(Application.isPlaying, Is.False);
            s_capturedSwitch = OnityTask.SwitchToMainThread();
            s_staleRuns = 0;
            s_freshRuns = 0;

            yield return new EnterPlayMode();
            yield return Settle();

            Assert.That(Application.isPlaying, Is.True);
            s_capturedSwitch.GetAwaiter().UnsafeOnCompleted(() => s_staleRuns++);
            OnityTask.SwitchToMainThread().GetAwaiter().UnsafeOnCompleted(() => s_freshRuns++);

            yield return WaitForFlag(() => s_freshRuns > 0);
            yield return Settle();

            Assert.That(s_staleRuns, Is.EqualTo(0),
                "A continuation from the previous Edit Mode session ran in Play Mode.");

            yield return new ExitPlayMode();
            yield return Settle();
        }

        [UnityTest]
        public IEnumerator PlayModeSessions_DoNotShareContinuations_WithDomainReloadDisabled()
        {
            ConfigureDomainReload(true);
            yield return new EnterPlayMode();
            yield return Settle();

            Assert.That(Application.isPlaying, Is.True);
            s_capturedSwitch = OnityTask.SwitchToMainThread();
            s_staleRuns = 0;
            s_freshRuns = 0;

            yield return new ExitPlayMode();
            yield return Settle();
            yield return new EnterPlayMode();
            yield return Settle();

            Assert.That(Application.isPlaying, Is.True);
            s_capturedSwitch.GetAwaiter().UnsafeOnCompleted(() => s_staleRuns++);
            OnityTask.SwitchToMainThread().GetAwaiter().UnsafeOnCompleted(() => s_freshRuns++);

            yield return WaitForFlag(() => s_freshRuns > 0);
            yield return Settle();

            Assert.That(s_staleRuns, Is.EqualTo(0),
                "A continuation from the previous Play Mode session ran in the next session.");

            yield return new ExitPlayMode();
            yield return Settle();
        }

        private static void ConfigureDomainReload(bool disableDomainReload)
        {
            if (disableDomainReload)
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            }
            else
            {
                EditorSettings.enterPlayModeOptionsEnabled = false;
            }
        }

        private static IEnumerator Settle()
        {
            for (int i = 0; i < k_settleIterations; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitForFlag(Func<bool> predicate)
        {
            int remainingIterations = k_timeoutIterations;
            while (predicate() == false && remainingIterations > 0)
            {
                remainingIterations--;
                yield return null;
            }

            Assert.That(predicate(), Is.True, "Operation did not complete before the test timeout.");
        }
    }
}
