using System;
using Onity.DI;
#if ONITY_ENTITIES
using Onity.DOTS;
#endif
using Onity.Messaging;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Samples.TankArena2D.SceneFlow
{
    /// <summary>
    /// Gameplay scene shell controller for restart and main-menu transitions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TankArenaGameSceneController : MonoBehaviour
    {
        private const string k_restartButtonName = "restart-run-button";
        private const string k_mainMenuButtonName = "main-menu-button";

        [Header("Profile")]
        [Tooltip("Shared scene-flow profile. When assigned, profile-driven state machine is used.")]
        [SerializeField] private OnitySceneFlowProfile m_sceneFlowProfile;

        [Header("Flow")]
        [Tooltip("Main menu scene loaded when exiting gameplay.")]
        [SerializeField] private string m_mainMenuScene = TankArenaSceneIds.MainMenu;

        [Tooltip("Loading scene used during transitions.")]
        [SerializeField] private string m_loadingScene = TankArenaSceneIds.Loading;

        [Header("Optional UI Toolkit")]
        [Tooltip("Optional UIDocument used for shell buttons.")]
        [SerializeField] private UIDocument m_document;

        private IPublisher<TankArenaRestartRequestedMessage> m_restartPublisher;
        private ITankArenaGameStateService m_gameStateService;
        private TankArenaEnemySpawner m_enemySpawner;
        private Button m_restartButton;
        private Button m_mainMenuButton;
        private bool m_isTransitioning;
        private bool m_hasAppliedEnterData;
        private TankArenaGameplayEnterData m_sceneEnterData;

        private void Start()
        {
            CaptureSceneEnterData();
            ApplySceneEnterDataIfNeeded();
        }

        private void OnEnable()
        {
            BindButtons();

            if (m_restartButton != null)
            {
                m_restartButton.clicked += OnRestartRequested;
            }

            if (m_mainMenuButton != null)
            {
                m_mainMenuButton.clicked += OnMainMenuRequested;
            }
        }

        private void OnDisable()
        {
            if (m_restartButton != null)
            {
                m_restartButton.clicked -= OnRestartRequested;
            }

            if (m_mainMenuButton != null)
            {
                m_mainMenuButton.clicked -= OnMainMenuRequested;
            }
        }

        private void Update()
        {
            if (m_isTransitioning)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                OnRestartRequested();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnMainMenuRequested();
            }
        }

        [Inject]
        private void Construct(
            IPublisher<TankArenaRestartRequestedMessage> restartPublisher,
            ITankArenaGameStateService gameStateService,
            TankArenaEnemySpawner enemySpawner)
        {
            m_restartPublisher = restartPublisher;
            m_gameStateService = gameStateService;
            m_enemySpawner = enemySpawner;
            ApplySceneEnterDataIfNeeded();
        }

        private void BindButtons()
        {
            m_restartButton = null;
            m_mainMenuButton = null;

            if (m_document == null || m_document.rootVisualElement == null)
            {
                return;
            }

            VisualElement root = m_document.rootVisualElement;
            m_restartButton = root.Q<Button>(k_restartButtonName);
            m_mainMenuButton = root.Q<Button>(k_mainMenuButtonName);
        }

        private void OnRestartRequested()
        {
            if (m_gameStateService == null)
            {
                return;
            }

            if (m_gameStateService.IsGameOver.Value == false)
            {
                return;
            }

            m_restartPublisher?.Publish(new TankArenaRestartRequestedMessage());
        }

        private async void OnMainMenuRequested()
        {
            if (m_isTransitioning)
            {
                return;
            }

            m_isTransitioning = true;

            try
            {
                int lastScore = m_gameStateService != null ? m_gameStateService.Score.Value : 0;
                int lastWave = m_gameStateService != null ? m_gameStateService.CurrentWave.Value : 0;
                TankArenaMainMenuEnterData enterData = new TankArenaMainMenuEnterData(
                    TankArenaSceneEntrySource.Gameplay,
                    lastScore,
                    lastWave);

                if (m_sceneFlowProfile != null)
                {
                    await TankArenaSceneFlowLoader.TransitionAsync(
                        m_sceneFlowProfile,
                        OnitySceneFlowStateId.MainMenuHub,
                        enterData);
                    return;
                }

                await TankArenaSceneFlowLoader.TransitionViaLoadingSceneAsync(
                    m_loadingScene,
                    m_mainMenuScene,
                    enterData);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                m_isTransitioning = false;
            }
        }

        private void ApplySceneEnterDataIfNeeded()
        {
            if (m_hasAppliedEnterData)
            {
                return;
            }

            CaptureSceneEnterData();

            if (m_sceneEnterData == null)
            {
                m_hasAppliedEnterData = true;
                return;
            }

            if (m_enemySpawner == null)
            {
                return;
            }

            m_enemySpawner?.ApplySessionConfig(
                m_sceneEnterData.SessionSeed,
                m_sceneEnterData.StartingWave,
                m_sceneEnterData.EnemyBonusPerWave);
#if ONITY_ENTITIES
            OnityDotsSessionBridge.TrySetSessionState(
                m_sceneEnterData.SessionSeed,
                m_sceneEnterData.StartingWave,
                m_sceneEnterData.EnemyBonusPerWave,
                0,
                1);
#endif
            m_hasAppliedEnterData = true;
            m_sceneEnterData = null;
        }

        private void CaptureSceneEnterData()
        {
            if (m_sceneEnterData != null)
            {
                return;
            }

            OnitySceneTransitionStore.TryConsumeActiveEnterData(out m_sceneEnterData);
        }
    }
}
