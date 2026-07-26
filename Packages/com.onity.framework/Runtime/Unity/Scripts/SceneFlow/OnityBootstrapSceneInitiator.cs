using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Contexts;
using UnityEngine;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Waits for the project context build lifecycle before leaving the bootstrap scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnityBootstrapSceneInitiator : OnitySceneInitiator
    {
        [Tooltip("Scene-flow profile used to select the first player-facing scene.")]
        [SerializeField] private OnitySceneFlowProfile m_profile;

        /// <inheritdoc />
        protected override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (m_profile == null)
            {
                throw new InvalidOperationException(
                    "Bootstrap scene initiator requires an OnitySceneFlowProfile.");
            }

            await WaitForProjectContextReadyAsync(cancellationToken);
            OnitySceneFlowStateId targetStateId = ResolveTargetStateId();
            await OnitySceneFlow.TransitionAsync(
                m_profile,
                targetStateId,
                cancellationToken: cancellationToken);
        }

        private OnitySceneFlowStateId ResolveTargetStateId()
        {
            if (m_profile.TryGetSceneName(OnitySceneFlowStateId.MainMenuHub, out _))
            {
                return OnitySceneFlowStateId.MainMenuHub;
            }

            if (m_profile.TryGetSceneName(OnitySceneFlowStateId.Hub, out _))
            {
                return OnitySceneFlowStateId.Hub;
            }

            if (m_profile.TryGetSceneName(OnitySceneFlowStateId.Gameplay, out _))
            {
                return OnitySceneFlowStateId.Gameplay;
            }

            throw new InvalidOperationException(
                "Scene-flow profile requires a Menu, Hub, or Gameplay target after Bootstrap.");
        }

        private static async Task WaitForProjectContextReadyAsync(
            CancellationToken cancellationToken)
        {
            while (ProjectContext.Instance == null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            Task readyTask = ProjectContext.Instance.ReadyTask;

            while (readyTask.IsCompleted == false)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            await readyTask;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
