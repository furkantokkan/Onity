using System;
using System.Text;
#if ONITY_ENTITIES
using Onity.DOTS;
#endif
using Onity.DI;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// UI Toolkit HUD presenter for Roll-a-Ball sample state.
    /// </summary>
    public sealed class RollABallHudController : MonoBehaviour
    {
        private const string k_scoreValueLabelName = "score-value";
        private const string k_collectedValueLabelName = "collected-value";
        private const string k_activePickupsValueLabelName = "active-pickups-value";
        private const string k_dotsValueLabelName = "dots-score-value";
        private const string k_respawnButtonName = "respawn-button";
        private const string k_resetScoreButtonName = "reset-score-button";
        private const string k_resetDotsButtonName = "reset-dots-button";
        private const string k_fallbackHudObjectName = "RollABallHudTMPFallback";

        private static readonly Color k_fallbackHudColor = new Color(0.93f, 0.96f, 1f, 1f);

        [Header("UI Toolkit")]
        [Tooltip("UIDocument used for this sample HUD.")]
        [SerializeField] private UIDocument m_document;

        [Header("Fallback")]
        [Tooltip("Uses TextMeshPro fallback HUD when UI Toolkit labels are missing or empty.")]
        [SerializeField] private bool m_enableTextMeshProFallback = true;

        [Header("Scene References")]
        [Tooltip("Pickup spawner used for manual respawn button action.")]
        [SerializeField] private RollABallPickupSpawner m_pickupSpawner;

        [Inject]
        private IRollABallScoreService m_scoreService = null;

        private IDisposable m_scoreSubscription;
        private IDisposable m_collectedSubscription;

        private Label m_scoreValueLabel;
        private Label m_collectedValueLabel;
        private Label m_activePickupsValueLabel;
        private Label m_dotsValueLabel;

        private Button m_respawnButton;
        private Button m_resetScoreButton;
        private Button m_resetDotsButton;

        private GameObject m_fallbackHudRoot;
        private TextMeshProUGUI m_fallbackHudText;
        private bool m_usingTextMeshProFallback;
        private readonly StringBuilder m_fallbackTextBuilder = new StringBuilder(256);

        private int m_currentScore;
        private int m_currentCollectedCount;
        private int m_currentActivePickupCount;
        private int m_currentDotsScore = -1;

        private int m_lastActivePickupCount = int.MinValue;
        private int m_lastDotsScore = int.MinValue;
        private int m_lastFallbackTextHash = int.MinValue;

        private void Awake()
        {
            if (m_document == null)
            {
                m_document = GetComponent<UIDocument>();
            }
        }

        private void OnEnable()
        {
            m_usingTextMeshProFallback = false;
            bool uiToolkitBound = TryBindUiToolkit();

            if (m_enableTextMeshProFallback)
            {
                EnsureTextMeshProFallbackHud();
                m_usingTextMeshProFallback = m_fallbackHudText != null;

                if (m_usingTextMeshProFallback && m_document != null && m_document.rootVisualElement != null)
                {
                    m_document.rootVisualElement.style.display = DisplayStyle.None;
                }
            }
            else
            {
                DestroyTextMeshProFallbackHud();

                if (uiToolkitBound == false)
                {
                    return;
                }
            }

#if ONITY_ENTITIES
            if (m_resetDotsButton != null)
            {
                m_resetDotsButton.SetEnabled(true);
            }
#else
            if (m_resetDotsButton != null)
            {
                m_resetDotsButton.SetEnabled(false);
            }
#endif

            m_lastActivePickupCount = int.MinValue;
            m_lastDotsScore = int.MinValue;
            m_lastFallbackTextHash = int.MinValue;

            RefreshActivePickupLabel(true);
            RefreshDotsScoreLabel(true);

            m_scoreSubscription = m_scoreService.Score.Subscribe(OnScoreChanged, true);
            m_collectedSubscription = m_scoreService.CollectedCount.Subscribe(OnCollectedChanged, true);
            RefreshFallbackHudText(true);
        }

        private void OnDisable()
        {
            if (m_document != null && m_document.rootVisualElement != null)
            {
                m_document.rootVisualElement.style.display = DisplayStyle.Flex;
            }

            if (m_respawnButton != null)
            {
                m_respawnButton.clicked -= OnRespawnButtonClicked;
            }

            if (m_resetScoreButton != null)
            {
                m_resetScoreButton.clicked -= OnResetScoreButtonClicked;
            }

            if (m_resetDotsButton != null)
            {
                m_resetDotsButton.clicked -= OnResetDotsButtonClicked;
            }

            m_respawnButton = null;
            m_resetScoreButton = null;
            m_resetDotsButton = null;
            m_scoreValueLabel = null;
            m_collectedValueLabel = null;
            m_activePickupsValueLabel = null;
            m_dotsValueLabel = null;

            m_scoreSubscription?.Dispose();
            m_collectedSubscription?.Dispose();
            m_scoreSubscription = null;
            m_collectedSubscription = null;

            DestroyTextMeshProFallbackHud();
            m_usingTextMeshProFallback = false;
            m_lastFallbackTextHash = int.MinValue;
        }

        private void Update()
        {
            RefreshActivePickupLabel(false);
            RefreshDotsScoreLabel(false);
            HandleFallbackHotkeys();
            RefreshFallbackHudText(false);
        }

        private void OnRespawnButtonClicked()
        {
            m_pickupSpawner?.RespawnAll();
            m_lastActivePickupCount = int.MinValue;
            RefreshActivePickupLabel(true);
            RefreshFallbackHudText(true);
        }

        private void OnResetScoreButtonClicked()
        {
            m_scoreService?.Reset();
            RefreshFallbackHudText(true);
        }

        private void OnResetDotsButtonClicked()
        {
#if ONITY_ENTITIES
            if (OnityDotsIntEventBridge.TryResetAccumulator())
            {
                m_lastDotsScore = int.MinValue;
                RefreshDotsScoreLabel(true);
                RefreshFallbackHudText(true);
            }
#endif
        }

        private void OnScoreChanged(int score)
        {
            m_currentScore = score;

            if (m_scoreValueLabel == null)
            {
                RefreshFallbackHudText(true);
                return;
            }

            m_scoreValueLabel.text = score.ToString();
            RefreshFallbackHudText(true);
        }

        private void OnCollectedChanged(int collectedCount)
        {
            m_currentCollectedCount = collectedCount;

            if (m_collectedValueLabel == null)
            {
                RefreshFallbackHudText(true);
                return;
            }

            m_collectedValueLabel.text = collectedCount.ToString();
            RefreshFallbackHudText(true);
        }

        private void RefreshActivePickupLabel(bool forceRefresh)
        {
            int activePickupCount = m_pickupSpawner != null ? m_pickupSpawner.ActivePickupCount : 0;
            m_currentActivePickupCount = activePickupCount;

            if (forceRefresh == false && m_lastActivePickupCount == activePickupCount)
            {
                return;
            }

            m_lastActivePickupCount = activePickupCount;

            if (m_activePickupsValueLabel != null)
            {
                m_activePickupsValueLabel.text = activePickupCount.ToString();
            }

            RefreshFallbackHudText(true);
        }

        private void RefreshDotsScoreLabel(bool forceRefresh)
        {
#if ONITY_ENTITIES
            if (OnityDotsIntEventBridge.TryGetAccumulatedValue(out int dotsScore) == false)
            {
                return;
            }

            m_currentDotsScore = dotsScore;

            if (forceRefresh == false && m_lastDotsScore == dotsScore)
            {
                return;
            }

            m_lastDotsScore = dotsScore;

            if (m_dotsValueLabel != null)
            {
                m_dotsValueLabel.text = dotsScore.ToString();
            }

            RefreshFallbackHudText(true);
#else
            m_currentDotsScore = -1;

            if (forceRefresh == false && m_lastDotsScore == -1)
            {
                return;
            }

            m_lastDotsScore = -1;

            if (m_dotsValueLabel != null)
            {
                m_dotsValueLabel.text = "N/A";
            }

            RefreshFallbackHudText(true);
#endif
        }

        private bool TryBindUiToolkit()
        {
            if (m_document == null || m_document.rootVisualElement == null)
            {
                return false;
            }

            VisualElement root = m_document.rootVisualElement;
            m_scoreValueLabel = root.Q<Label>(k_scoreValueLabelName);
            m_collectedValueLabel = root.Q<Label>(k_collectedValueLabelName);
            m_activePickupsValueLabel = root.Q<Label>(k_activePickupsValueLabelName);
            m_dotsValueLabel = root.Q<Label>(k_dotsValueLabelName);
            m_respawnButton = root.Q<Button>(k_respawnButtonName);
            m_resetScoreButton = root.Q<Button>(k_resetScoreButtonName);
            m_resetDotsButton = root.Q<Button>(k_resetDotsButtonName);

            bool hasCoreLabels =
                m_scoreValueLabel != null &&
                m_collectedValueLabel != null &&
                m_activePickupsValueLabel != null &&
                m_dotsValueLabel != null;

            if (hasCoreLabels == false)
            {
                return false;
            }

            if (m_respawnButton != null)
            {
                m_respawnButton.clicked += OnRespawnButtonClicked;
            }

            if (m_resetScoreButton != null)
            {
                m_resetScoreButton.clicked += OnResetScoreButtonClicked;
            }

            if (m_resetDotsButton != null)
            {
                m_resetDotsButton.clicked += OnResetDotsButtonClicked;
            }

            return true;
        }

        private void EnsureTextMeshProFallbackHud()
        {
            if (m_fallbackHudRoot != null && m_fallbackHudText != null)
            {
                return;
            }

            m_fallbackHudRoot = new GameObject(k_fallbackHudObjectName);
            m_fallbackHudRoot.transform.SetParent(transform, false);

            Canvas canvas = m_fallbackHudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2500;

            GameObject textRoot = new GameObject("ScoreText");
            textRoot.transform.SetParent(m_fallbackHudRoot.transform, false);

            RectTransform textRect = textRoot.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(18f, -18f);
            textRect.sizeDelta = new Vector2(520f, 240f);

            m_fallbackHudText = textRoot.AddComponent<TextMeshProUGUI>();
            m_fallbackHudText.alignment = TextAlignmentOptions.TopLeft;
            m_fallbackHudText.fontSize = 24f;
            m_fallbackHudText.color = k_fallbackHudColor;
            m_fallbackHudText.enableWordWrapping = false;
            m_fallbackHudText.raycastTarget = false;
            m_fallbackHudText.text = string.Empty;

            if (TMP_Settings.defaultFontAsset != null)
            {
                m_fallbackHudText.font = TMP_Settings.defaultFontAsset;
            }
        }

        private void DestroyTextMeshProFallbackHud()
        {
            if (m_fallbackHudRoot != null)
            {
                Destroy(m_fallbackHudRoot);
            }

            m_fallbackHudRoot = null;
            m_fallbackHudText = null;
        }

        private void HandleFallbackHotkeys()
        {
            if (m_usingTextMeshProFallback == false)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                OnRespawnButtonClicked();
            }

            if (Input.GetKeyDown(KeyCode.K))
            {
                OnResetScoreButtonClicked();
            }

#if ONITY_ENTITIES
            if (Input.GetKeyDown(KeyCode.L))
            {
                OnResetDotsButtonClicked();
            }
#endif
        }

        private void RefreshFallbackHudText(bool forceRefresh)
        {
            if (m_usingTextMeshProFallback == false || m_fallbackHudText == null)
            {
                return;
            }

            int valueHash = m_currentScore;
            valueHash = (valueHash * 397) ^ m_currentCollectedCount;
            valueHash = (valueHash * 397) ^ m_currentActivePickupCount;
            valueHash = (valueHash * 397) ^ m_currentDotsScore;

            if (forceRefresh == false && valueHash == m_lastFallbackTextHash)
            {
                return;
            }

            m_lastFallbackTextHash = valueHash;
            m_fallbackTextBuilder.Clear();
            m_fallbackTextBuilder.AppendLine("Onity Roll-A-Ball");
            m_fallbackTextBuilder.AppendLine("Move: WASD / Arrows");
            m_fallbackTextBuilder.AppendLine("Respawn: R   Reset Score: K");
#if ONITY_ENTITIES
            m_fallbackTextBuilder.AppendLine("Reset DOTS: L");
#endif
            m_fallbackTextBuilder.Append("Score: ").AppendLine(m_currentScore.ToString());
            m_fallbackTextBuilder.Append("Collected: ").AppendLine(m_currentCollectedCount.ToString());
            m_fallbackTextBuilder.Append("Active Pickups: ").AppendLine(m_currentActivePickupCount.ToString());
            m_fallbackTextBuilder.Append("DOTS Score: ");

            if (m_currentDotsScore >= 0)
            {
                m_fallbackTextBuilder.Append(m_currentDotsScore);
            }
            else
            {
                m_fallbackTextBuilder.Append("N/A");
            }

            m_fallbackHudText.text = m_fallbackTextBuilder.ToString();
        }
    }
}
