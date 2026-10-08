using NUnit.Framework;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnitySceneFlowLoadingViewTests
    {
        [Test]
        public void SetProgress_ClampsAndUpdatesDefaultTextAndSlider()
        {
            GameObject viewObject = new GameObject(nameof(OnitySceneFlowLoadingViewTests));

            try
            {
                UIDocument document = viewObject.AddComponent<UIDocument>();
                OnityLoadingView view = viewObject.AddComponent<OnityLoadingView>();

                view.SetProgress(0.42f);

                Label label = document.rootVisualElement.Q<Label>("onity-loading__status");
                Slider slider = document.rootVisualElement.Q<Slider>("onity-loading__progress");

                Assert.That(label, Is.Not.Null);
                Assert.That(label.text, Is.EqualTo("Loading... 42%"));
                Assert.That(slider, Is.Not.Null);
                Assert.That(slider.value, Is.EqualTo(0.42f).Within(0.001f));
                Assert.That(slider.focusable, Is.False);
                Assert.That(slider.pickingMode, Is.EqualTo(PickingMode.Ignore));

                view.SetProgress(2f);

                Assert.That(label.text, Is.EqualTo("Loading... 100%"));
                Assert.That(slider.value, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
            }
        }
    }
}
