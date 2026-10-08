using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Verifies the shared cover animation of <see cref="OnitySceneCoverView" /> with a test cover that only
    /// implements <c>ApplyProgress</c>: progress runs 0 to 1 and back on unscaled time, a caller's token stops it
    /// where it is, a newer animation replaces a running one, disabling it stops, clears and hides it, a missing
    /// element is reported, and a running animation allocates nothing per frame.
    /// </summary>
    [TestFixture]
    public sealed class OnitySceneCoverViewPlayModeTests
    {
        private const float k_timeoutSeconds = 10f;
        private const string k_elementName = "probe-cover";

        private GameObject m_object;
        private PanelSettings m_panelSettings;
        private float m_originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            m_originalTimeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = m_originalTimeScale;

            if (m_object != null)
            {
                Object.Destroy(m_object);
            }

            if (m_panelSettings != null)
            {
                Object.Destroy(m_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator ShowThenHide_DrivesProgressBothWaysOnUnscaledTime()
        {
            ProbeCoverView view = CreateView(true);
            Assert.That(view.Recorded(view.RecordedCount - 1), Is.EqualTo(0f), "binding clears the cover");
            Time.timeScale = 0f;

            int start = view.RecordedCount;
            Task show = view.ShowAsync(CancellationToken.None);
            yield return WaitForTask(show);

            Assert.That(view.RecordedCount - start, Is.GreaterThan(2), "the show runs over several frames");
            AssertMonotonic(view, start, view.RecordedCount, true);
            Assert.That(view.Progress, Is.EqualTo(1f));
            Assert.That(view.IsVisible, Is.True);

            start = view.RecordedCount;
            Task hide = view.HideAsync(CancellationToken.None);
            yield return WaitForTask(hide);

            AssertMonotonic(view, start, view.RecordedCount, false);
            Assert.That(view.Progress, Is.EqualTo(0f));
            Assert.That(view.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator CallerCancellation_StopsTheAnimationWhereItIs()
        {
            ProbeCoverView view = CreateView(true);
            SetField(view, "m_showSeconds", 5f);
            using CancellationTokenSource caller = new CancellationTokenSource();

            Task show = view.ShowAsync(caller.Token);

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            caller.Cancel();
            yield return WaitUntil(() => show.IsCompleted);
            float stoppedAt = view.Progress;

            yield return null;
            yield return null;

            Assert.That(show.IsCanceled, Is.True);
            Assert.That(stoppedAt, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(view.Progress, Is.EqualTo(stoppedAt), "it stays where it stopped");
            Assert.That(view.IsVisible, Is.True, "a stopped cover still covers");
        }

        [UnityTest]
        public IEnumerator NewerAnimation_ReplacesTheRunningOne()
        {
            ProbeCoverView view = CreateView(true);
            SetField(view, "m_showSeconds", 5f);

            Task show = view.ShowAsync(CancellationToken.None);
            yield return null;
            yield return null;
            Task hide = view.HideAsync(CancellationToken.None);
            yield return WaitUntil(() => show.IsCompleted && hide.IsCompleted);

            Assert.That(show.IsCanceled, Is.True, "the replaced show ends");
            Assert.That(hide.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(view.Progress, Is.EqualTo(0f));
            Assert.That(view.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator Disable_StopsClearsAndHides()
        {
            ProbeCoverView view = CreateView(true);
            SetField(view, "m_showSeconds", 5f);

            Task show = view.ShowAsync(CancellationToken.None);
            yield return null;
            yield return null;

            view.enabled = false;
            yield return WaitUntil(() => show.IsCompleted);

            Assert.That(show.IsCanceled, Is.True);
            Assert.That(view.Progress, Is.EqualTo(0f));
            Assert.That(view.IsVisible, Is.False);
            Assert.That(view.UnbindCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator MissingElement_IsReportedAndCoversNothing()
        {
            LogAssert.Expect(LogType.Error, new Regex(k_elementName));
            ProbeCoverView view = CreateView(false);

            Task show = view.ShowAsync(CancellationToken.None);
            yield return null;

            Assert.That(show.Status, Is.EqualTo(TaskStatus.RanToCompletion), "a cover without an element completes");
            Assert.That(view.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator RunningAnimation_AllocatesNothingPerFrame()
        {
            const int frameCount = 30;
            ProbeCoverView view = CreateView(true);

            // Warm the pools, the JIT and the async runner with one full show and hide.
            yield return WaitForTask(view.ShowAsync(CancellationToken.None));
            yield return WaitForTask(view.HideAsync(CancellationToken.None));

            long idleStart = GC.GetAllocatedBytesForCurrentThread();

            for (int frame = 0; frame < frameCount; frame++)
            {
                yield return null;
            }

            long idleBytes = GC.GetAllocatedBytesForCurrentThread() - idleStart;

            SetField(view, "m_showSeconds", 30f);
            Task show = view.ShowAsync(CancellationToken.None);
            yield return null;
            yield return null;
            int recordedBefore = view.RecordedCount;
            long busyStart = GC.GetAllocatedBytesForCurrentThread();

            for (int frame = 0; frame < frameCount; frame++)
            {
                yield return null;
            }

            long busyBytes = GC.GetAllocatedBytesForCurrentThread() - busyStart;
            int framesAnimated = view.RecordedCount - recordedBefore;
            view.enabled = false;
            yield return WaitUntil(() => show.IsCompleted);

            Assert.That(framesAnimated, Is.GreaterThanOrEqualTo(frameCount - 1), "the animation ran every frame");
            Assert.That(busyBytes - idleBytes, Is.LessThanOrEqualTo(64),
                $"animating allocated {busyBytes} bytes over {frameCount} frames against {idleBytes} idle");
        }

        private ProbeCoverView CreateView(bool withElement)
        {
            m_panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            m_object = new GameObject(nameof(OnitySceneCoverViewPlayModeTests));
            m_object.SetActive(false);
            UIDocument document = m_object.AddComponent<UIDocument>();
            document.panelSettings = m_panelSettings;
            m_object.SetActive(true);

            if (withElement)
            {
                document.rootVisualElement.Add(new VisualElement { name = k_elementName });
            }

            return m_object.AddComponent<ProbeCoverView>();
        }

        private static void AssertMonotonic(ProbeCoverView view, int start, int end, bool rising)
        {
            for (int index = start + 1; index < end; index++)
            {
                float previous = view.Recorded(index - 1);
                float current = view.Recorded(index);

                if (rising)
                {
                    Assert.That(current, Is.GreaterThanOrEqualTo(previous), $"step {index}");
                }
                else
                {
                    Assert.That(current, Is.LessThanOrEqualTo(previous), $"step {index}");
                }
            }
        }

        private static void SetField(object target, string fieldName, object value)
        {
            for (Type current = target.GetType(); current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"Field {fieldName} was not found.");
        }

        private static IEnumerator WaitForTask(Task task)
        {
            yield return WaitUntil(() => task.IsCompleted);
            Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
        }

        private static IEnumerator WaitUntil(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + k_timeoutSeconds;

            while (condition() == false && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, "timed out");
        }

        // A cover that only records the progress it is asked to draw, in a preallocated ring.
        private sealed class ProbeCoverView : OnitySceneCoverView
        {
            private readonly float[] m_recorded = new float[4096];

            public int RecordedCount { get; private set; }

            public int UnbindCount { get; private set; }

            protected override string ElementName => k_elementName;

            public float Recorded(int index)
            {
                return m_recorded[index % m_recorded.Length];
            }

            protected override void ApplyProgress(float progress)
            {
                m_recorded[RecordedCount % m_recorded.Length] = progress;
                RecordedCount++;
            }

            protected override void OnUnbind(VisualElement element)
            {
                UnbindCount++;
            }
        }
    }
}
