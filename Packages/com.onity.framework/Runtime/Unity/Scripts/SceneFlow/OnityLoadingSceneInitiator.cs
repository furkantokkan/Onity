using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Consumes one pending scene-flow target and reports its load progress.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnityLoadingSceneInitiator : OnitySceneInitiator
    {
        [Tooltip("Scene-flow profile used to select a fallback target.")]
        [SerializeField] private OnitySceneFlowProfile m_profile;

        [Tooltip("View that displays normalized scene loading progress.")]
        [SerializeField] private OnityLoadingView m_view;

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
            await OnitySceneLoader.LoadSingleAsync(
                payload.TargetSceneName,
                m_view != null ? m_view.SetProgress : null,
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
