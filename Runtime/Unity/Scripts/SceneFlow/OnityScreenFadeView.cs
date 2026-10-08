using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// A fade cover: the <c>onity-screen-fade</c> element of its <see cref="UIDocument" /> fades in and out. The cover
    /// is the element's USS background color (black in <c>OnityScreenFade.uss</c>), or the optional image.
    /// </summary>
    /// <remarks>
    /// Build the document from <c>OnityScreenFade.uxml</c>. To change the image per scene change, give each image its
    /// own fade view and pass the one you want to <see cref="OnityCoveredSceneTransition" />.
    /// </remarks>
    public sealed class OnityScreenFadeView : OnitySceneCoverView
    {
        /// <summary>
        /// Name of the cover element in <c>OnityScreenFade.uxml</c>.
        /// </summary>
        public const string FadeElementName = "onity-screen-fade";

        [Header("Fade Image")]
        [Tooltip("Optional image shown as the cover. Leave empty to cover with the element's USS background color.")]
        [SerializeField] private Texture2D m_image;

        [Tooltip("How the image fills the screen.")]
        [SerializeField] private ScaleMode m_imageScaleMode = ScaleMode.ScaleAndCrop;

        /// <summary>
        /// Gets the image shown as the cover, or null when the USS background color covers.
        /// </summary>
        public Texture2D Image => m_image;

        /// <inheritdoc />
        protected override string ElementName => FadeElementName;

        /// <inheritdoc />
        protected override void OnBind(VisualElement element)
        {
            if (m_image == null)
            {
                return;
            }

            BackgroundPosition position = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition(m_imageScaleMode);
            element.style.backgroundImage = new StyleBackground(m_image);
            element.style.backgroundPositionX = position;
            element.style.backgroundPositionY = position;
            element.style.backgroundRepeat = BackgroundPropertyHelper.ConvertScaleModeToBackgroundRepeat(m_imageScaleMode);
            element.style.backgroundSize = BackgroundPropertyHelper.ConvertScaleModeToBackgroundSize(m_imageScaleMode);
        }

        /// <inheritdoc />
        protected override void OnUnbind(VisualElement element)
        {
            if (m_image == null)
            {
                return;
            }

            element.style.backgroundImage = StyleKeyword.Null;
            element.style.backgroundPositionX = StyleKeyword.Null;
            element.style.backgroundPositionY = StyleKeyword.Null;
            element.style.backgroundRepeat = StyleKeyword.Null;
            element.style.backgroundSize = StyleKeyword.Null;
        }

        /// <inheritdoc />
        protected override void ApplyProgress(float progress)
        {
            Element.style.opacity = progress;
        }
    }
}
