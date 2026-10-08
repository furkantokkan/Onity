using System;
using System.Collections;
using System.IO;
using System.Reflection;
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
    /// Verifies <see cref="OnityIrisCoverView" /> on a document built from <c>OnityIrisCover.uxml</c>: at runtime the
    /// preview background is cleared and the view draws the cover itself, the iris closes and opens on unscaled time,
    /// nothing redraws while it is idle, and disabling it mid-wipe stops, clears, hides and unbinds it.
    /// </summary>
    [TestFixture]
    public sealed class OnityIrisCoverViewPlayModeTests
    {
        private const float k_timeoutSeconds = 10f;

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
        public IEnumerator Bind_ClearsThePreviewBackgroundAndHides()
        {
            OnityIrisCoverView view = CreateView();
            VisualElement iris = IrisElement(view);
            yield return null;

            Assert.That(iris.style.backgroundColor.value, Is.EqualTo(Color.clear), "the view draws the cover itself");
            Assert.That(iris.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(view.IsVisible, Is.False);
            Assert.That(view.Color, Is.EqualTo(Color.black));
            Assert.That(view.Center, Is.EqualTo(new Vector2(0.5f, 0.5f)));
        }

        [UnityTest]
        public IEnumerator CloseThenOpen_OnUnscaledTime_CoversAndClears()
        {
            OnityIrisCoverView view = CreateView();
            VisualElement iris = IrisElement(view);
            view.Center = new Vector2(0.25f, 0.75f);
            Time.timeScale = 0f;

            Task close = view.ShowAsync(CancellationToken.None);
            Assert.That(view.IsVisible, Is.True);
            float previous = 0f;
            int frames = 0;

            // Bounded by real time: batch-mode frames can be far shorter than the wipe.
            double deadline = Time.realtimeSinceStartupAsDouble + k_timeoutSeconds;

            while (close.IsCompleted == false && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                frames++;
                Assert.That(view.Progress, Is.GreaterThanOrEqualTo(previous), "the iris only closes while closing");
                previous = view.Progress;
            }

            Assert.That(close.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(frames, Is.GreaterThan(1));
            Assert.That(view.Progress, Is.EqualTo(1f));
            Assert.That(view.Center, Is.EqualTo(new Vector2(0.25f, 0.75f)));

            Task open = view.HideAsync(CancellationToken.None);
            yield return WaitForTask(open);

            Assert.That(view.Progress, Is.EqualTo(0f));
            Assert.That(view.IsVisible, Is.False);
            Assert.That(iris.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator Idle_RequestsNoRedraw()
        {
            OnityIrisCoverView view = CreateView();
            VisualElement iris = IrisElement(view);
            yield return WaitForTask(view.ShowAsync(CancellationToken.None));
            yield return WaitForTask(view.HideAsync(CancellationToken.None));

            int draws = 0;
            Action<MeshGenerationContext> countDraw = _ => draws++;
            iris.generateVisualContent += countDraw;

            for (int frame = 0; frame < 10; frame++)
            {
                yield return null;
            }

            iris.generateVisualContent -= countDraw;
            Assert.That(draws, Is.EqualTo(0), "a clear iris is hidden and asks for no repaint");
        }

        [UnityTest]
        public IEnumerator Disable_MidWipe_StopsClearsHidesAndUnbinds()
        {
            OnityIrisCoverView view = CreateView();
            VisualElement iris = IrisElement(view);
            SetField(view, "m_showSeconds", 5f);

            Task close = view.ShowAsync(CancellationToken.None);

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(view.Progress, Is.GreaterThan(0f).And.LessThan(1f));

            view.enabled = false;
            yield return WaitUntil(() => close.IsCompleted);

            Assert.That(close.IsCanceled, Is.True);
            Assert.That(view.Progress, Is.EqualTo(0f));
            Assert.That(view.IsVisible, Is.False);
            Assert.That(iris.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(iris.style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Null),
                "unbinding restores the authored background");
        }

        private OnityIrisCoverView CreateView()
        {
            m_panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            m_panelSettings.sortingOrder = 1000;
            m_object = new GameObject(nameof(OnityIrisCoverViewPlayModeTests));
            m_object.SetActive(false);
            UIDocument document = m_object.AddComponent<UIDocument>();
            document.panelSettings = m_panelSettings;
            document.visualTreeAsset = LoadTemplate("OnityIrisCover");
            m_object.SetActive(true);

            // Added once the document has built its tree, so the view binds the authored element.
            return m_object.AddComponent<OnityIrisCoverView>();
        }

        private static VisualElement IrisElement(OnityIrisCoverView view)
        {
            VisualElement iris = view.GetComponent<UIDocument>().rootVisualElement.Q(OnityIrisCoverView.IrisElementName);
            Assert.That(iris, Is.Not.Null, "OnityIrisCover.uxml defines the iris element");
            return iris;
        }

        private static VisualTreeAsset LoadTemplate(string templateName)
        {
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"{templateName} t:VisualTreeAsset");

            for (int index = 0; index < guids.Length; index++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[index]);

                if (Path.GetFileNameWithoutExtension(path) == templateName)
                {
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                }
            }
#endif
            Assert.Fail($"The {templateName}.uxml template was not found.");
            return null;
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
    }
}
