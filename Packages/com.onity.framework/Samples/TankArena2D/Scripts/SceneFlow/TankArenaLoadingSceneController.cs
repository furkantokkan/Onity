using System;
using System.Threading.Tasks;
using Onity.Unity.SceneFlow;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Onity.Samples.TankArena2D.SceneFlow
{
    /// <summary>
    /// Loading scene controller that routes to pending target scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TankArenaLoadingSceneController : MonoBehaviour
    {
        private const string k_defaultProgressFormat = "Loading {0}... {1}%";
        private const string k_progressLabelName = "loading-progress";

        [Header("Profile")]
        [Tooltip("Shared scene-flow profile used for fallback target resolution.")]
        [SerializeField] private OnitySceneFlowProfile m_sceneFlowProfile;

        [Header("Flow")]
        [Tooltip("Fallback target scene when no transition request exists.")]
        [SerializeField] private string m_defaultTargetScene = TankArenaSceneIds.MainMenu;

        [Tooltip("Minimum time loading scene stays visible.")]
        [SerializeField] private float m_minVisibleSeconds = 0.6f;

        [Header("Optional UI Toolkit")]
        [Tooltip("Optional UIDocument used for loading progress.")]
        [SerializeField] private UIDocument m_document;

        [Header("Optional TMP")]
        [Tooltip("Optional TextMeshPro text used for loading progress.")]
        [SerializeField] private TextMeshProUGUI m_progressText;

        private AsyncOperation m_loadingOperation;
        private float m_startedAtRealtime;
        private bool m_isLoading;
        private string m_targetSceneName;
        private Label m_progressLabel;

        private async void Start()
        {
            if (m_isLoading)
            {
                return;
            }

            string fallbackTargetScene = ResolveFallbackTargetScene();
            m_isLoading = true;
            if (OnitySceneTransitionStore.TryConsumePendingOrDefault(
                    fallbackTargetScene,
                    new TankArenaMainMenuEnterData(TankArenaSceneEntrySource.Bootstrap, 0, 0),
                    out OnitySceneTransitionPayload transitionPayload)
                == false)
            {
                m_targetSceneName = fallbackTargetScene;
            }
            else
            {
                m_targetSceneName = transitionPayload.TargetSceneName;
                OnitySceneTransitionStore.SetActiveEnterData(transitionPayload.EnterData);
            }

            m_startedAtRealtime = Time.realtimeSinceStartup;
            BindProgressUi();

            try
            {
                m_loadingOperation = SceneManager.LoadSceneAsync(m_targetSceneName, LoadSceneMode.Single);

                if (m_loadingOperation == null)
                {
                    return;
                }

                m_loadingOperation.allowSceneActivation = false;

                while (m_loadingOperation.isDone == false)
                {
                    UpdateProgressText();

                    bool readyToActivate = m_loadingOperation.progress >= 0.9f;
                    float elapsed = Time.realtimeSinceStartup - m_startedAtRealtime;
                    bool minVisibleTimeReached = elapsed >= Mathf.Max(0f, m_minVisibleSeconds);

                    if (readyToActivate && minVisibleTimeReached)
                    {
                        m_loadingOperation.allowSceneActivation = true;
                    }

                    await Task.Yield();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                m_isLoading = false;
                m_loadingOperation = null;
            }
        }

        private string ResolveFallbackTargetScene()
        {
            if (m_sceneFlowProfile != null
                && m_sceneFlowProfile.TryGetSceneName(
                    OnitySceneFlowStateId.MainMenuHub,
                    out string profileTargetScene))
            {
                return profileTargetScene;
            }

            return m_defaultTargetScene;
        }

        private void BindProgressUi()
        {
            m_progressLabel = null;

            if (m_document != null && m_document.rootVisualElement != null)
            {
                m_progressLabel = m_document.rootVisualElement.Q<Label>(k_progressLabelName);
            }
        }

        private void UpdateProgressText()
        {
            int percent = 0;

            if (m_loadingOperation != null)
            {
                percent = Mathf.Clamp(Mathf.RoundToInt((m_loadingOperation.progress / 0.9f) * 100f), 0, 100);
            }

            string text = string.Format(k_defaultProgressFormat, m_targetSceneName, percent);

            if (m_progressLabel != null)
            {
                m_progressLabel.text = text;
            }

            if (m_progressText != null)
            {
                m_progressText.text = text;
            }
        }
    }
}
