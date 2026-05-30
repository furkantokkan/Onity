using System;
using Onity.DI;
#if ONITY_ENTITIES
using Onity.DOTS;
#endif
using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// UI Toolkit presenter for sample HUD interactions.
    /// </summary>
    public sealed class SampleHudController : MonoBehaviour
    {
        private const string k_healthLabelName = "health-value";
        private const string k_dotsDamageLabelName = "dots-damage-value";
        private const string k_damageButtonName = "damage-button";
        private const string k_spawnButtonName = "spawn-button";
        private const string k_dotsResetButtonName = "dots-reset-button";

        [Header("UI Toolkit")]
        [Tooltip("UIDocument that hosts the sample HUD.")]
        [SerializeField] private UIDocument m_document;

        [Header("Scene References")]
        [Tooltip("Damage emitter invoked from HUD button.")]
        [SerializeField] private SampleDamageEmitter m_damageEmitter;

        [Tooltip("Projectile spawner invoked from HUD button.")]
        [SerializeField] private SampleProjectileSpawner m_projectileSpawner;

        [Inject]
        private IPlayerStateService m_playerStateService = null;

        private IDisposable m_healthSubscription;
        private Label m_healthLabel;
        private Label m_dotsDamageLabel;
        private Button m_damageButton;
        private Button m_spawnButton;
        private Button m_dotsResetButton;
        private int m_lastDotsDamageValue = int.MinValue;

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
            m_healthLabel = root.Q<Label>(k_healthLabelName);
            m_dotsDamageLabel = root.Q<Label>(k_dotsDamageLabelName);
            m_damageButton = root.Q<Button>(k_damageButtonName);
            m_spawnButton = root.Q<Button>(k_spawnButtonName);
            m_dotsResetButton = root.Q<Button>(k_dotsResetButtonName);

            if (m_damageButton != null)
            {
                m_damageButton.clicked += OnDamageButtonClicked;
            }

            if (m_spawnButton != null)
            {
                m_spawnButton.clicked += OnSpawnButtonClicked;
            }

            if (m_dotsResetButton != null)
            {
                m_dotsResetButton.clicked += OnDotsResetButtonClicked;
            }

#if ONITY_ENTITIES
            if (m_dotsResetButton != null)
            {
                m_dotsResetButton.SetEnabled(true);
            }
#else
            if (m_dotsResetButton != null)
            {
                m_dotsResetButton.SetEnabled(false);
            }
#endif

            m_lastDotsDamageValue = int.MinValue;
            RefreshDotsDamageLabel(true);
            m_healthSubscription = m_playerStateService.Health.Subscribe(OnHealthChanged, true);
        }

        private void OnDisable()
        {
            if (m_damageButton != null)
            {
                m_damageButton.clicked -= OnDamageButtonClicked;
            }

            if (m_spawnButton != null)
            {
                m_spawnButton.clicked -= OnSpawnButtonClicked;
            }

            if (m_dotsResetButton != null)
            {
                m_dotsResetButton.clicked -= OnDotsResetButtonClicked;
            }

            m_damageButton = null;
            m_spawnButton = null;
            m_dotsResetButton = null;
            m_healthLabel = null;
            m_dotsDamageLabel = null;

            m_healthSubscription?.Dispose();
            m_healthSubscription = null;
        }

        private void Update()
        {
            RefreshDotsDamageLabel(false);
        }

        private void OnDamageButtonClicked()
        {
            m_damageEmitter?.PublishDamage();
        }

        private void OnSpawnButtonClicked()
        {
            m_projectileSpawner?.SpawnProjectile();
        }

        private void OnDotsResetButtonClicked()
        {
#if ONITY_ENTITIES
            if (OnityDotsIntEventBridge.TryResetAccumulator())
            {
                m_lastDotsDamageValue = int.MinValue;
                RefreshDotsDamageLabel(true);
            }
#endif
        }

        private void OnHealthChanged(int healthValue)
        {
            if (m_healthLabel == null)
            {
                return;
            }

            m_healthLabel.text = healthValue.ToString();
        }

        private void RefreshDotsDamageLabel(bool forceRefresh)
        {
            if (m_dotsDamageLabel == null)
            {
                return;
            }

#if ONITY_ENTITIES
            if (OnityDotsIntEventBridge.TryGetAccumulatedValue(out int dotsDamageValue) == false)
            {
                return;
            }

            if (forceRefresh == false && m_lastDotsDamageValue == dotsDamageValue)
            {
                return;
            }

            m_lastDotsDamageValue = dotsDamageValue;
            m_dotsDamageLabel.text = dotsDamageValue.ToString();
#else
            if (forceRefresh == false && m_lastDotsDamageValue == -1)
            {
                return;
            }

            m_lastDotsDamageValue = -1;
            m_dotsDamageLabel.text = "N/A";
#endif
        }
    }
}
