using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Consumes one pending scene-flow target and reports its load progress.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnityLoadingSceneInitiator : OnitySceneInitiator
    {
        private const float k_defaultMinimumVisibleDurationSeconds = 0.35f;

        [Tooltip("Scene-flow profile used to select a fallback target.")]
        [SerializeField] private OnitySceneFlowProfile m_profile;

        [Tooltip("View that displays normalized scene loading progress.")]
        [SerializeField] private OnityLoadingView m_view;

        [Tooltip("Minimum unscaled time that the Loading scene remains visible before target activation.")]
        [SerializeField, Min(0f)]
        private float m_minimumVisibleDurationSeconds =
            k_defaultMinimumVisibleDurationSeconds;

        /// <inheritdoc />
        protected override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (m_profile == null)
            {
                throw new InvalidOperationException(
                    "Loading scene initiator requires an OnitySceneFlowProfile.");
            }

            string fallbackSceneName = ResolveFallbackSceneName();

            if (OnitySceneTransitionStore.TryConsumePendingOrDefault(
                    fallbackSceneName,
                    null,
                    out OnitySceneTransitionPayload payload) == false)
            {
                throw new InvalidOperationException(
                    "Loading scene could not resolve a pending or fallback target.");
            }

            m_view?.SetProgress(0f);
            OnitySceneTransitionStore.SetActiveEnterData(payload.EnterData);

            Task minimumVisibleDurationTask =
                WaitForMinimumVisibleDurationAsync(cancellationToken);
            AsyncOperation operation = await OnitySceneLoader.LoadAsync(
                payload.TargetSceneName,
                LoadSceneMode.Single,
                false,
                m_view != null ? m_view.SetProgress : null,
                cancellationToken);

            await minimumVisibleDurationTask;
            await OnitySceneLoader.ActivateAsync(
                operation,
                m_view != null ? m_view.SetProgress : null,
                cancellationToken);
        }

        private async Task WaitForMinimumVisibleDurationAsync(
            CancellationToken cancellationToken)
        {
            await OnityAsync.NextFrameAsync(cancellationToken);
            await OnityAsync.DelayAsync(
                m_minimumVisibleDurationSeconds,
                true,
                cancellationToken);
        }

        private string ResolveFallbackSceneName()
        {
            if (m_profile.TryGetSceneName(
                    OnitySceneFlowStateId.MainMenuHub,
                    out string sceneName))
            {
                return sceneName;
            }

            if (m_profile.TryGetSceneName(OnitySceneFlowStateId.Hub, out sceneName))
            {
                return sceneName;
            }

            if (m_profile.TryGetSceneName(OnitySceneFlowStateId.Gameplay, out sceneName))
            {
                return sceneName;
            }

            return string.Empty;
        }
    }
}
