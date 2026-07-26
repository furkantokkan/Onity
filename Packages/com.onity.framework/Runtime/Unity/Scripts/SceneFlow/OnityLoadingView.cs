using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Binds a loading label and non-interactive slider from a runtime UI document.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class OnityLoadingView : MonoBehaviour
    {
        private const string k_rootName = "onity-loading";
        private const string k_statusName = "onity-loading__status";
        private const string k_progressName = "onity-loading__progress";

        private Label m_statusLabel;
        private Slider m_progressSlider;

        private void OnEnable()
        {
            BindView();
            SetProgress(0f);
        }

        /// <summary>
        /// Updates the loading text and slider with normalized progress.
        /// </summary>
        /// <param name="progress">Normalized progress between zero and one.</param>
        public void SetProgress(float progress)
        {
            if (m_statusLabel == null || m_progressSlider == null)
            {
                BindView();
            }

            float value = Mathf.Clamp01(progress);
            m_progressSlider?.SetValueWithoutNotify(value);

            if (m_statusLabel != null)
            {
                m_statusLabel.text = $"Loading... {Mathf.RoundToInt(value * 100f)}%";
            }
        }

        private void BindView()
        {
            UIDocument document = GetComponent<UIDocument>();
            VisualElement root = document.rootVisualElement;
            m_statusLabel = root.Q<Label>(k_statusName);
            m_progressSlider = root.Q<Slider>(k_progressName);

            if (m_statusLabel == null || m_progressSlider == null)
            {
                CreateDefaultView(root);
            }

            m_progressSlider.focusable = false;
            m_progressSlider.pickingMode = PickingMode.Ignore;
        }

        private void CreateDefaultView(VisualElement documentRoot)
        {
            VisualElement container = new VisualElement
            {
                name = k_rootName
            };
            container.AddToClassList(k_rootName);

            m_statusLabel = new Label("Loading... 0%")
            {
                name = k_statusName
            };
            m_statusLabel.AddToClassList(k_statusName);

            m_progressSlider = new Slider
            {
                name = k_progressName,
                lowValue = 0f,
                highValue = 1f,
                value = 0f
            };
            m_progressSlider.AddToClassList(k_progressName);

            container.Add(m_statusLabel);
            container.Add(m_progressSlider);
            documentRoot.Add(container);
        }
    }
}
