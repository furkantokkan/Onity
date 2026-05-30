using Onity.DI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Samples.GameObjectContextScope
{
    /// <summary>
    /// UI Toolkit presenter that reads and mutates context-scoped state.
    /// </summary>
    public sealed class SampleScopeCounterPresenter : MonoBehaviour
    {
        private const string k_valueLabelName = "scope-value";
        private const string k_incrementButtonName = "scope-increment";

        [Header("UI Toolkit")]
        [Tooltip("UIDocument used for scoped counter UI.")]
        [SerializeField] private UIDocument m_document;

        [Tooltip("Optional prefix to identify this scoped instance in the HUD.")]
        [SerializeField] private string m_scopeTitle = "Scope";

        [Inject]
        private ScopedCounterService m_counterService = null;

        private Label m_valueLabel;
        private Button m_incrementButton;

        private void Awake()
        {
            if (m_document == null)
            {
                m_document = GetComponent<UIDocument>();
            }
        }

        private void OnEnable()
        {
            if (m_document == null || m_document.rootVisualElement == null)
            {
                return;
            }

            VisualElement root = m_document.rootVisualElement;
            m_valueLabel = root.Q<Label>(k_valueLabelName);
            m_incrementButton = root.Q<Button>(k_incrementButtonName);

            if (m_incrementButton != null)
            {
                m_incrementButton.clicked += OnIncrementClicked;
            }

            RefreshLabel();
        }

        private void OnDisable()
        {
            if (m_incrementButton != null)
            {
                m_incrementButton.clicked -= OnIncrementClicked;
            }

            m_incrementButton = null;
            m_valueLabel = null;
        }

        private void OnIncrementClicked()
        {
            m_counterService.Increment();
            RefreshLabel();
        }

        private void RefreshLabel()
        {
            if (m_valueLabel == null)
            {
                return;
            }

            m_valueLabel.text = $"{m_scopeTitle}: {m_counterService.Value}";
        }
    }
}
