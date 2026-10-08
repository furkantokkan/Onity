using System;
using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Covers the coroutine interop against the real player loop: advancing an enumerator from the
    /// PlayerLoop with every supported yield instruction, running it on a MonoBehaviour as a Unity
    /// coroutine, and driving a task from a coroutine.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskCoroutinePlayModeTests
    {
        private const int k_timeoutSeconds = 10;

        private sealed class CoroutineProbe : MonoBehaviour
        {
        }

        private GameObject m_gameObject;

        [TearDown]
        public void TearDown()
        {
            if (m_gameObject != null)
            {
                Object.Destroy(m_gameObject);
            }

            m_gameObject = null;
        }

        private CoroutineProbe CreateProbe()
        {
            m_gameObject = new GameObject("coroutine-probe");
            return m_gameObject.AddComponent<CoroutineProbe>();
        }

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed.TotalSeconds < k_timeoutSeconds)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, "The coroutine interop timed out.");
        }

        private static IEnumerator YieldNullTimes(int count, Action onStep)
        {
            for (int i = 0; i < count; i++)
            {
                onStep();
                yield return null;
            }

            onStep();
        }

        [UnityTest]
        public IEnumerator Enumerator_YieldNull_IsAdvancedAtOnceAndThenOncePerUpdate()
        {
            int steps = 0;

            OnityTask task = YieldNullTimes(3, () => steps++).ToOnityTask();
            Assert.That(steps, Is.EqualTo(1), "The first step must run synchronously.");
            Assert.That(task.IsCompleted, Is.False);
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(steps, Is.EqualTo(4));
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator Enumerator_WaitForSeconds_WaitsAtLeastTheScaledTime()
        {
            Stopwatch timer = Stopwatch.StartNew();

            OnityTask task = WaitForSecondsEnumerator(0.15f).ToOnityTask();
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(timer.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(0.1));
            task.GetAwaiter().GetResult();
        }

        private static IEnumerator WaitForSecondsEnumerator(float seconds)
        {
            yield return new WaitForSeconds(seconds);
        }

        [UnityTest]
        public IEnumerator Enumerator_WaitForSeconds_ZeroSecondsStillTakesAFrame()
        {
            int startFrame = Time.frameCount;

            OnityTask task = WaitForSecondsEnumerator(0f).ToOnityTask();
            Assert.That(task.IsCompleted, Is.False);
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(Time.frameCount, Is.GreaterThan(startFrame));
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator Enumerator_CustomYieldInstruction_WaitsUntilItStopsWaiting()
        {
            bool release = false;

            OnityTask task = WaitUntilEnumerator(() => release).ToOnityTask();
            yield return null;
            yield return null;
            Assert.That(task.IsCompleted, Is.False);
            release = true;
            yield return WaitFor(() => task.IsCompleted);

            task.GetAwaiter().GetResult();
        }

        private static IEnumerator WaitUntilEnumerator(Func<bool> predicate)
        {
            yield return new WaitUntil(predicate);
        }

        [UnityTest]
        public IEnumerator Enumerator_AsyncOperation_WaitsUntilItIsDone()
        {
            OnityTask task = AsyncOperationEnumerator().ToOnityTask();

            yield return WaitFor(() => task.IsCompleted);

            task.GetAwaiter().GetResult();
        }

        private static IEnumerator AsyncOperationEnumerator()
        {
            yield return Resources.LoadAsync<TextAsset>("onity-coroutine-test-missing-asset");
        }

        [UnityTest]
        public IEnumerator Enumerator_NestedEnumerator_RunsToCompletionBeforeTheOuterContinues()
        {
            int order = 0;
            int innerEnd = 0;
            int outerEnd = 0;

            OnityTask task = NestedEnumerator(() => innerEnd = ++order, () => outerEnd = ++order).ToOnityTask();
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(innerEnd, Is.EqualTo(1));
            Assert.That(outerEnd, Is.EqualTo(2));
            task.GetAwaiter().GetResult();
        }

        private static IEnumerator NestedEnumerator(Action innerDone, Action outerDone)
        {
            yield return InnerEnumerator(innerDone);
            outerDone();
        }

        private static IEnumerator InnerEnumerator(Action done)
        {
            yield return null;
            yield return null;
            done();
        }

        [UnityTest]
        public IEnumerator Enumerator_WaitForFixedUpdate_CompletesAtAFixedStep()
        {
            OnityTask task = FixedUpdateEnumerator().ToOnityTask();

            yield return WaitFor(() => task.IsCompleted);

            task.GetAwaiter().GetResult();
        }

        private static IEnumerator FixedUpdateEnumerator()
        {
            yield return new WaitForFixedUpdate();
        }

        // OnityTask.WaitForEndOfFrame rejects the environments in which an end-of-frame wait would
        // never fire: the Editor in batch mode and a null graphics device.
        private static bool EndOfFrameUnsupported =>
            (Application.isEditor && Application.isBatchMode)
            || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        [UnityTest]
        public IEnumerator Enumerator_WaitForEndOfFrame_CompletesWhenARenderingDeviceExists()
        {
            Assume.That(EndOfFrameUnsupported, Is.False,
                "End-of-frame waits need a graphics device and are unavailable in Editor batch mode.");

            OnityTask task = EndOfFrameEnumerator().ToOnityTask();

            yield return WaitFor(() => task.IsCompleted);

            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator Enumerator_WaitForEndOfFrame_WithoutRendering_FaultsWithPlatformNotSupported()
        {
            Assume.That(EndOfFrameUnsupported, Is.True, "End-of-frame waits are supported in this environment.");

            OnityTask task = EndOfFrameEnumerator().ToOnityTask();
            yield return WaitFor(() => task.IsCompleted);

            Assert.Throws<PlatformNotSupportedException>(() => task.GetAwaiter().GetResult());
        }

        private static IEnumerator EndOfFrameEnumerator()
        {
            yield return new WaitForEndOfFrame();
        }

        [UnityTest]
        public IEnumerator Enumerator_UnsupportedYield_LogsAWarningAndContinues()
        {
            LogAssert.Expect(LogType.Warning, new Regex("yield Object is not supported by IEnumerator.ToOnityTask"));

            OnityTask task = UnsupportedYieldEnumerator().ToOnityTask();
            yield return WaitFor(() => task.IsCompleted);

            task.GetAwaiter().GetResult();
        }

        private static IEnumerator UnsupportedYieldEnumerator()
        {
            yield return new object();
        }

        [UnityTest]
        public IEnumerator Enumerator_Cancellation_CancelsTheTaskAndStopsAdvancing()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            int steps = 0;

            OnityTask task = YieldNullTimes(1000, () => steps++).WithCancellation(cancellation.Token);
            yield return null;
            yield return null;
            cancellation.Cancel();
            yield return WaitFor(() => task.IsCompleted);
            int stepsAtCancel = steps;
            yield return null;
            yield return null;

            Assert.That(task.IsCanceled, Is.True);
            Assert.That(steps, Is.EqualTo(stepsAtCancel), "A canceled enumerator must not advance any more.");
        }

        [UnityTest]
        public IEnumerator Enumerator_ThrowingAfterAFrame_FaultsTheTask()
        {
            OnityTask task = ThrowAfterFrameEnumerator(new InvalidOperationException("coroutine failed")).ToOnityTask();

            yield return WaitFor(() => task.IsCompleted);

            Assert.That(task.IsFaulted, Is.True);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        private static IEnumerator ThrowAfterFrameEnumerator(Exception exception)
        {
            yield return null;
            throw exception;
        }

        [UnityTest]
        public IEnumerator GetAwaiter_Enumerator_CanBeAwaitedFromAnAsyncMethod()
        {
            OnityTask run = AwaitEnumeratorAsync();

            yield return WaitFor(() => run.IsCompleted);

            run.GetAwaiter().GetResult();
        }

        private static async OnityTask AwaitEnumeratorAsync()
        {
            await YieldNullTimes(2, () => { });
        }

        [UnityTest]
        public IEnumerator ToOnityTask_Runner_RunsTheEnumeratorAsAUnityCoroutine()
        {
            CoroutineProbe runner = CreateProbe();
            int steps = 0;

            OnityTask task = YieldNullTimes(3, () => steps++).ToOnityTask(runner);
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(steps, Is.EqualTo(4));
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator ToOnityTask_Runner_SupportsEveryYieldInstructionUnityDefines()
        {
            CoroutineProbe runner = CreateProbe();

            OnityTask task = WaitForSecondsThenFixedUpdate().ToOnityTask(runner);
            yield return WaitFor(() => task.IsCompleted);

            task.GetAwaiter().GetResult();
        }

        private static IEnumerator WaitForSecondsThenFixedUpdate()
        {
            yield return new WaitForSeconds(0.05f);
            yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator ToOnityTask_Runner_DestroyedRunner_CancelsTheTask()
        {
            CoroutineProbe runner = CreateProbe();

            OnityTask task = YieldNullTimes(100000, () => { }).ToOnityTask(runner);
            yield return null;
            Object.Destroy(m_gameObject);
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(task.IsCanceled, Is.True);
        }

        [Test]
        public void ToOnityTask_Runner_InactiveRunner_Throws()
        {
            CoroutineProbe runner = CreateProbe();
            m_gameObject.SetActive(false);

            Assert.Throws<InvalidOperationException>(() => YieldNullTimes(1, () => { }).ToOnityTask(runner));
        }

        [Test]
        public void ToOnityTask_Runner_NullRunner_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => YieldNullTimes(1, () => { }).ToOnityTask(null));
        }

        [UnityTest]
        public IEnumerator ToOnityTask_Runner_AlreadyDestroyedRunner_ReturnsACanceledTask()
        {
            CoroutineProbe runner = CreateProbe();
            Object.Destroy(m_gameObject);
            yield return null;

            OnityTask task = YieldNullTimes(1, () => { }).ToOnityTask(runner);

            Assert.That(task.IsCanceled, Is.True);
        }

        [UnityTest]
        public IEnumerator ToCoroutine_RunByUnity_EndsWhenTheTaskCompletes()
        {
            CoroutineProbe runner = CreateProbe();
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            bool finished = false;

            runner.StartCoroutine(RunAndMark(source.Task.ToCoroutine(), () => finished = true));
            yield return null;
            yield return null;
            Assert.That(finished, Is.False);
            source.TrySetResult();
            yield return WaitFor(() => finished);
        }

        private static IEnumerator RunAndMark(IEnumerator coroutine, Action done)
        {
            yield return coroutine;
            done();
        }

        [UnityTest]
        public IEnumerator ToCoroutine_Typed_HandsTheResultToUnityCoroutines()
        {
            CoroutineProbe runner = CreateProbe();
            int result = 0;

            runner.StartCoroutine(DelayedResult(5, 21).ToCoroutine(value => result = value));
            yield return WaitFor(() => result == 21);
        }

        private static async OnityTask<int> DelayedResult(int frames, int value)
        {
            await OnityTask.DelayFrames(frames);
            return value;
        }

        [UnityTest]
        public IEnumerator StartAsyncCoroutine_PassesTheDestroyTokenAndReturnsTheTask()
        {
            CoroutineProbe runner = CreateProbe();
            CancellationToken received = default;
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            OnityTask task = runner.StartAsyncCoroutine(token =>
            {
                received = token;
                return source.Task;
            });

            Assert.That(received.CanBeCanceled, Is.True);
            Assert.That(received.IsCancellationRequested, Is.False);
            Assert.That(task.IsCompleted, Is.False);
            Object.Destroy(m_gameObject);
            yield return null;

            Assert.That(received.IsCancellationRequested, Is.True);
            source.TrySetResult();
            Assert.That(task.IsCompleted, Is.True);
        }
    }
}
