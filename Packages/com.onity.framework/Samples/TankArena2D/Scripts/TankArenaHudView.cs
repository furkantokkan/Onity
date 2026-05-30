using System;
using Onity.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// UI Toolkit view component for Tank Arena HUD.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class TankArenaHudView : MonoBehaviour
    {
        private const string k_scoreLabelName = "score-value";
        private const string k_healthLabelName = "health-value";
        private const string k_waveLabelName = "wave-value";
        private const string k_enemyCountLabelName = "enemy-count-value";
        private const string k_timerLabelName = "timer-value";
        private const string k_dotsLabelName = "dots-value";
        private const string k_statusLabelName = "status-value";
        private const string k_restartButtonName = "restart-button";

        [Header("UI Toolkit")]
        [Tooltip("UIDocument that hosts Tank Arena HUD.")]
        [SerializeField] private UIDocument m_document;

        private Label m_scoreLabel;
        private Label m_healthLabel;
        private Label m_waveLabel;
        private Label m_enemyCountLabel;
        private Label m_timerLabel;
        private Label m_dotsLabel;
        private Label m_statusLabel;
        private Button m_restartButton;

        /// <summary>
        /// Raised when restart button is clicked.
        /// </summary>
        public event Action<Unit> RestartRequested;

        private void Awake()
        {
            if (m_document == null)
            {
                m_document = GetComponent<UIDocument>();
            }
        }

        private void OnEnable()
        {
            TryBindUi();

            if (m_restartButton != null)
            {
                m_restartButton.clicked += OnRestartButtonClicked;
            }
        }

        private void OnDisable()
        {
            if (m_restartButton != null)
            {
                m_restartButton.clicked -= OnRestartButtonClicked;
            }
        }

        /// <summary>
        /// Sets score label text.
        /// </summary>
        /// <param name="text">Formatted score text.</param>
        public void SetScore(string text)
        {
            if (m_scoreLabel != null)
            {
                m_scoreLabel.text = text;
            }
        }

        /// <summary>
        /// Sets health label text.
        /// </summary>
        /// <param name="text">Formatted health text.</param>
        public void SetHealth(string text)
        {
            if (m_healthLabel != null)
            {
                m_healthLabel.text = text;
            }
        }

        /// <summary>
        /// Sets wave label text.
        /// </summary>
        /// <param name="text">Formatted wave text.</param>
        public void SetWave(string text)
        {
            if (m_waveLabel != null)
            {
                m_waveLabel.text = text;
            }
        }

        /// <summary>
        /// Sets active enemy count label text.
        /// </summary>
        /// <param name="text">Formatted enemy count text.</param>
        public void SetEnemyCount(string text)
        {
            if (m_enemyCountLabel != null)
            {
                m_enemyCountLabel.text = text;
            }
        }

        /// <summary>
        /// Sets elapsed timer label text.
        /// </summary>
        /// <param name="text">Formatted timer text.</param>
        public void SetTimer(string text)
        {
            if (m_timerLabel != null)
            {
                m_timerLabel.text = text;
            }
        }

        /// <summary>
        /// Sets DOTS accumulator label text.
        /// </summary>
        /// <param name="text">Formatted DOTS text.</param>
        public void SetDots(string text)
        {
            if (m_dotsLabel != null)
            {
                m_dotsLabel.text = text;
            }
        }

        /// <summary>
        /// Sets gameplay status label text.
        /// </summary>
        /// <param name="text">Formatted status text.</param>
        public void SetStatus(string text)
        {
            if (m_statusLabel != null)
            {
                m_statusLabel.text = text;
            }
        }

        /// <summary>
        /// Shows or hides restart button.
        /// </summary>
        /// <param name="visible">Visibility value.</param>
        public void SetRestartButtonVisible(bool visible)
        {
            if (m_restartButton == null)
            {
                return;
            }

            m_restartButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void TryBindUi()
        {
            if (m_document == null || m_document.rootVisualElement == null)
            {
                return;
            }

            VisualElement root = m_document.rootVisualElement;
            m_scoreLabel = root.Q<Label>(k_scoreLabelName);
            m_healthLabel = root.Q<Label>(k_healthLabelName);
            m_waveLabel = root.Q<Label>(k_waveLabelName);
            m_enemyCountLabel = root.Q<Label>(k_enemyCountLabelName);
            m_timerLabel = root.Q<Label>(k_timerLabelName);
            m_dotsLabel = root.Q<Label>(k_dotsLabelName);
            m_statusLabel = root.Q<Label>(k_statusLabelName);
            m_restartButton = root.Q<Button>(k_restartButtonName);
        }

        private void OnRestartButtonClicked()
        {
            RestartRequested?.Invoke(Unit.Default);
        }
    }
}
