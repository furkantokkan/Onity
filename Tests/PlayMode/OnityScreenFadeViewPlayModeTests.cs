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
    /// Verifies <see cref="OnityScreenFadeView" /> on a document built from <c>OnityScreenFade.uxml</c>: it is hidden
    /// when idle, covers and clears on unscaled time, stops, clears and hides when disabled mid-fade, and covers with
    /// its image when one is set or with the USS color otherwise.
    /// </summary>
    [TestFixture]
    public sealed class OnityScreenFadeViewPlayModeTests
    {
        private const float k_timeoutSeconds = 10f;

        private GameObject m_object;
        private PanelSettings m_panelSettings;
        private Texture2D m_texture;
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
            DestroyIfSet(m_object);
            DestroyIfSet(m_panelSettings);
            DestroyIfSet(m_texture);
        }

        private static void DestroyIfSet(Object target)
        {
            if (target != null)
            {
                Object.Destroy(target);
            }
        }

        [UnityTest]
        public IEnumerator ShowThenHide_OnUnscaledTime_CoversAndClears()
        {
            OnityScreenFadeView view = CreateView();
            VisualElement fade = FadeElement(view);
            Assert.That(view.IsVisible, Is.False, "idle, the fade is hidden");
            Assert.That(fade.style.display.value, Is.EqualTo(DisplayStyle.None));
            Time.timeScale = 0f;

            Task show = view.ShowAsync(CancellationToken.None);
            Assert.That(view.IsVisible, Is.True, "shown at once, so it blocks presses for the whole fade");
            Assert.That(fade.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            int frames = 0;

            // Bounded by real time: batch-mode frames can be far shorter than the fade.
            double deadline = Time.realtimeSinceStartupAsDouble + k_timeoutSeconds;

            while (show.IsCompleted == false && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                frames++;
            }

            Assert.That(show.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(frames, Is.GreaterThan(1), "the fade runs over frames while time is paused");
            Assert.That(view.Progress, Is.EqualTo(1f));
            Assert.That(fade.style.opacity.value, Is.EqualTo(1f));

            Task hide = view.HideAsync(CancellationToken.None);
            yield return WaitForTask(hide);

            Assert.That(view.Progress, Is.EqualTo(0f));
            Assert.That(fade.style.opacity.value, Is.EqualTo(0f));
            Assert.That(view.IsVisible, Is.False);
            Assert.That(fade.style.display.value, Is.EqualTo(DisplayStyle.None), "clear, it lets presses through");
        }

        [UnityTest]
        public IEnumerator Disable_MidFade_StopsClearsAndHides()
        {
            OnityScreenFadeView view = CreateView();
            VisualElement fade = FadeElement(view);
            SetField(view, "m_showSeconds", 5f);

            Task show = view.ShowAsync(CancellationToken.None);

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(view.Progress, Is.GreaterThan(0f).And.LessThan(1f));

            view.enabled = false;
            yield return WaitUntil(() => show.IsCompleted);

            Assert.That(show.IsCanceled, Is.True, "the awaiting task ends");
            Assert.That(view.Progress, Is.EqualTo(0f));
            Assert.That(view.IsVisible, Is.False);
            Assert.That(fade.style.display.value, Is.EqualTo(DisplayStyle.None));

            float progressAfterStop = view.Progress;
            yield return null;
            Assert.That(view.Progress, Is.EqualTo(progressAfterStop), "nothing draws after the stop");
        }

        [UnityTest]
        public IEnumerator Image_CoversAsTheBackground()
        {
            OnityScreenFadeView view = CreateView();
            VisualElement fade = FadeElement(view);
            m_texture = new Texture2D(4, 4);
            SetField(view, "m_image", m_texture);
            SetField(view, "m_imageScaleMode", ScaleMode.ScaleAndCrop);

            // The image applies when the view binds its element.
            view.enabled = false;
            view.enabled = true;

            Assert.That(view.Image, Is.SameAs(m_texture));
            Assert.That(fade.style.backgroundImage.value.texture, Is.SameAs(m_texture));
            Assert.That(fade.style.backgroundSize.value.sizeType, Is.EqualTo(BackgroundSizeType.Cover));

            Task show = view.ShowAsync(CancellationToken.None);
            yield return WaitForTask(show);
            Assert.That(view.Progress, Is.EqualTo(1f));
            Assert.That(fade.style.opacity.value, Is.EqualTo(1f));

            view.enabled = false;
            Assert.That(fade.style.backgroundImage.keyword, Is.EqualTo(StyleKeyword.Null), "unbinding drops the image");
        }

        [UnityTest]
        public IEnumerator NoImage_CoversWithTheUssColor()
        {
            OnityScreenFadeView view = CreateView();
            VisualElement fade = FadeElement(view);

            Task show = view.ShowAsync(CancellationToken.None);
            yield return WaitForTask(show);
            yield return null;

            Assert.That(view.Image, Is.Null);
            Assert.That(fade.style.backgroundImage.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(fade.resolvedStyle.backgroundColor, Is.EqualTo(Color.black), "OnityScreenFade.uss covers black");
        }

        private OnityScreenFadeView CreateView()
        {
            m_panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            m_panelSettings.sortingOrder = 1000;
            m_object = new GameObject(nameof(OnityScreenFadeViewPlayModeTests));
            m_object.SetActive(false);
            UIDocument document = m_object.AddComponent<UIDocument>();
            document.panelSettings = m_panelSettings;
            document.visualTreeAsset = LoadTemplate("OnityScreenFade");
            m_object.SetActive(true);

            // Added once the document has built its tree, so the view binds the authored element.
            return m_object.AddComponent<OnityScreenFadeView>();
        }

        private static VisualElement FadeElement(OnityScreenFadeView view)
        {
            VisualElement fade = view.GetComponent<UIDocument>().rootVisualElement.Q(OnityScreenFadeView.FadeElementName);
            Assert.That(fade, Is.Not.Null, "OnityScreenFade.uxml defines the fade element");
            return fade;
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
            FieldInfo field = FindField(target.GetType(), fieldName);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

                if (field != null)
                {
                    return field;
                }
            }

            return null;
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
