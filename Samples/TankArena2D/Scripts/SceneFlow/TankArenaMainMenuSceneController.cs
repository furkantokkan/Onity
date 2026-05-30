using System;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Onity.Samples.TankArena2D.SceneFlow
{
    /// <summary>
    /// Main menu controller for starting gameplay through LoadingScene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TankArenaMainMenuSceneController : MonoBehaviour
    {
        private const string k_startButtonName = "start-button";
        private const string k_quitButtonName = "quit-button";
        private const float k_fallbackPanelWidth = 420f;
        private const float k_fallbackPanelHeight = 200f;

        private static Texture2D s_backgroundTexture;
        private static Texture2D s_panelTexture;
        private static GUIStyle s_titleStyle;
        private static GUIStyle s_subtitleStyle;
        private static GUIStyle s_buttonStyle;
        private static GUIStyle s_panelStyle;

        [Header("Profile")]
        [Tooltip("Shared scene-flow profile. When assigned, profile-driven state machine is used.")]
        [SerializeField] private OnitySceneFlowProfile m_sceneFlowProfile;

        [Header("Flow")]
        [Tooltip("Gameplay scene loaded when start is requested.")]
        [SerializeField] private string m_gameplayScene = TankArenaSceneIds.Gameplay;

        [Tooltip("Loading scene used during transitions.")]
        [SerializeField] private string m_loadingScene = TankArenaSceneIds.Loading;

        [Header("Optional UI Toolkit")]
        [Tooltip("Optional UIDocument used for menu buttons.")]
        [SerializeField] private UIDocument m_document;

        private TankArenaMainMenuEnterData m_lastEnterData;
        private Button m_startButton;
        private Button m_quitButton;
        private bool m_isTransitioning;
        private bool m_useFallbackMenu;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureMainMenuController()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            if (activeScene.IsValid() == false)
            {
                return;
            }

            if (string.Equals(activeScene.name, TankArenaSceneIds.MainMenu, StringComparison.Ordinal) == false)
            {
                return;
            }

            if (FindObjectOfType<TankArenaMainMenuSceneController>() != null)
            {
                return;
            }

            GameObject root = new GameObject("MainMenuFlow");
            root.AddComponent<TankArenaMainMenuSceneController>();
        }

        private void Start()
        {
            if (OnitySceneTransitionStore.TryConsumeActiveEnterData(out TankArenaMainMenuEnterData enterData))
            {
                m_lastEnterData = enterData;
            }
        }

        private void OnEnable()
        {
            BindButtons();
            m_useFallbackMenu = m_startButton == null;

            if (m_startButton != null)
            {
                m_startButton.clicked += OnStartClicked;
            }

            if (m_quitButton != null)
            {
                m_quitButton.clicked += OnQuitClicked;
            }
        }

        private void OnDisable()
        {
            if (m_startButton != null)
            {
                m_startButton.clicked -= OnStartClicked;
            }

            if (m_quitButton != null)
            {
                m_quitButton.clicked -= OnQuitClicked;
            }
        }

        private void Update()
        {
            if (m_isTransitioning)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OnStartClicked();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnQuitClicked();
            }
        }

        private void OnGUI()
        {
            if (m_useFallbackMenu == false || m_isTransitioning)
            {
                return;
            }

            EnsureFallbackGuiResources();

            Rect fullscreenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.DrawTexture(fullscreenRect, s_backgroundTexture);

            Rect panelRect = new Rect(
                (Screen.width - k_fallbackPanelWidth) * 0.5f,
                (Screen.height - k_fallbackPanelHeight) * 0.5f,
                k_fallbackPanelWidth,
                k_fallbackPanelHeight);

            GUI.Box(panelRect, GUIContent.none, s_panelStyle);

            Rect titleRect = new Rect(panelRect.x + 20f, panelRect.y + 18f, panelRect.width - 40f, 38f);
            Rect subtitleRect = new Rect(panelRect.x + 20f, panelRect.y + 58f, panelRect.width - 40f, 22f);
            Rect startRect = new Rect(panelRect.x + 20f, panelRect.y + 96f, panelRect.width - 40f, 36f);
            Rect quitRect = new Rect(panelRect.x + 20f, panelRect.y + 140f, panelRect.width - 40f, 30f);

            GUI.Label(titleRect, "Tank Arena 2D", s_titleStyle);
            GUI.Label(subtitleRect, "Onity Sample", s_subtitleStyle);

            if (GUI.Button(startRect, "Start", s_buttonStyle))
            {
                OnStartClicked();
            }

            if (GUI.Button(quitRect, "Quit", s_buttonStyle))
            {
                OnQuitClicked();
            }
        }

        private void BindButtons()
        {
            m_startButton = null;
            m_quitButton = null;

            if (m_document == null || m_document.rootVisualElement == null)
            {
                return;
            }

            VisualElement root = m_document.rootVisualElement;
            m_startButton = root.Q<Button>(k_startButtonName);
            m_quitButton = root.Q<Button>(k_quitButtonName);
        }

        private static void EnsureFallbackGuiResources()
        {
            if (s_backgroundTexture == null)
            {
                s_backgroundTexture = CreateTexture(new Color(0.06f, 0.1f, 0.16f, 1f));
            }

            if (s_panelTexture == null)
            {
                s_panelTexture = CreateTexture(new Color(0.12f, 0.2f, 0.3f, 0.96f));
            }

            if (s_panelStyle == null)
            {
                s_panelStyle = new GUIStyle(GUI.skin.box)
                {
                    normal =
                    {
                        background = s_panelTexture
                    }
                };
            }

            if (s_titleStyle == null)
            {
                s_titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 32,
                    fontStyle = FontStyle.Bold,
                    normal =
                    {
                        textColor = new Color(0.92f, 0.96f, 1f, 1f)
                    }
                };
            }

            if (s_subtitleStyle == null)
            {
                s_subtitleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                    normal =
                    {
                        textColor = new Color(0.69f, 0.82f, 0.95f, 1f)
                    }
                };
            }

            if (s_buttonStyle == null)
            {
                s_buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 15,
                    fontStyle = FontStyle.Bold
                };
            }
        }

        private static Texture2D CreateTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private async void OnStartClicked()
        {
            if (m_isTransitioning)
            {
                return;
            }

            m_isTransitioning = true;

            try
            {
                int sessionSeed = unchecked((int)DateTime.UtcNow.Ticks);
                int startingWave = 1;
                int enemyBonusPerWave = 0;

                if (m_lastEnterData != null)
                {
                    enemyBonusPerWave = Mathf.Max(0, m_lastEnterData.LastWave / 3);
                }

                TankArenaGameplayEnterData enterData = new TankArenaGameplayEnterData(
                    sessionSeed,
                    startingWave,
                    enemyBonusPerWave);

                if (m_sceneFlowProfile != null)
                {
                    await TankArenaSceneFlowLoader.TransitionAsync(
                        m_sceneFlowProfile,
                        OnitySceneFlowStateId.Gameplay,
                        enterData);
                    return;
                }

                await TankArenaSceneFlowLoader.TransitionViaLoadingSceneAsync(
                    m_loadingScene,
                    m_gameplayScene,
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

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
