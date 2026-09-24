using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Runtime state machine for SEP scene flow.
    /// </summary>
    public sealed class OnitySceneFlowStateMachine
    {
        private readonly OnitySceneFlowProfile m_profile;

        /// <summary>
        /// Initializes a scene-flow state machine.
        /// </summary>
        /// <param name="profile">Scene-flow profile.</param>
        public OnitySceneFlowStateMachine(OnitySceneFlowProfile profile)
        {
            m_profile = profile ?? throw new ArgumentNullException(nameof(profile));
            CurrentStateId = OnitySceneFlowStateId.Unknown;
            SyncCurrentStateFromActiveScene();
        }

        /// <summary>
        /// Current mapped scene-flow state.
        /// </summary>
        public OnitySceneFlowStateId CurrentStateId { get; private set; }

        /// <summary>
        /// Re-maps current state from active scene name.
        /// </summary>
        public void SyncCurrentStateFromActiveScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            if (activeScene.IsValid() == false)
            {
                CurrentStateId = OnitySceneFlowStateId.Unknown;
                return;
            }

            if (m_profile.TryGetStateId(activeScene.name, out OnitySceneFlowStateId stateId))
            {
                CurrentStateId = stateId;
                return;
            }

            CurrentStateId = OnitySceneFlowStateId.Unknown;
        }

        /// <summary>
        /// Resolves a transition plan for target state.
        /// </summary>
        /// <param name="targetStateId">Target state.</param>
        /// <param name="enterData">Optional target payload.</param>
        /// <returns>Resolved transition plan.</returns>
        public OnitySceneFlowTransitionPlan BuildTransitionPlan(
            OnitySceneFlowStateId targetStateId,
            IOnitySceneEnterData enterData = null)
        {
            if (m_profile.TryGetSceneName(targetStateId, out string targetSceneName) == false)
            {
                throw new InvalidOperationException(
                    $"SceneFlow profile does not contain target scene for state '{targetStateId}'.");
            }

            return BuildTransitionPlanInternal(targetStateId, targetSceneName, enterData);
        }

        /// <summary>
        /// Resolves a transition plan for a concrete scene name already grouped in the profile.
        /// </summary>
        /// <param name="targetSceneName">Concrete target scene name.</param>
        /// <param name="enterData">Optional target payload.</param>
        /// <returns>Resolved transition plan.</returns>
        public OnitySceneFlowTransitionPlan BuildTransitionPlan(
            string targetSceneName,
            IOnitySceneEnterData enterData = null)
        {
            if (string.IsNullOrWhiteSpace(targetSceneName))
            {
                throw new ArgumentException("Target scene name cannot be empty.", nameof(targetSceneName));
            }

            OnitySceneFlowStateId targetStateId =
                m_profile.TryGetStateId(targetSceneName, out OnitySceneFlowStateId resolvedStateId)
                    ? resolvedStateId
                    : OnitySceneFlowStateId.Unknown;

            return BuildTransitionPlanInternal(targetStateId, targetSceneName, enterData);
        }

        private OnitySceneFlowTransitionPlan BuildTransitionPlanInternal(
            OnitySceneFlowStateId targetStateId,
            string targetSceneName,
            IOnitySceneEnterData enterData)
        {
            bool routeViaLoadingScene = false;
            string entrySceneName = targetSceneName;
            OnitySceneFlowStateId entryStateId = targetStateId;

            bool shouldTryLoadingRoute = m_profile.RouteTransitionsThroughLoadingScene
                && targetStateId != OnitySceneFlowStateId.Loading;

            if (shouldTryLoadingRoute
                && m_profile.TryGetSceneName(OnitySceneFlowStateId.Loading, out string loadingSceneName)
                && string.Equals(loadingSceneName, targetSceneName, StringComparison.Ordinal) == false)
            {
                routeViaLoadingScene = true;
                entrySceneName = loadingSceneName;
                entryStateId = OnitySceneFlowStateId.Loading;
            }

            return new OnitySceneFlowTransitionPlan(
                entryStateId,
                targetStateId,
                entrySceneName,
                targetSceneName,
                routeViaLoadingScene,
                enterData);
        }

        /// <summary>
        /// Executes transition to target state.
        /// </summary>
        /// <param name="targetStateId">Target state.</param>
        /// <param name="enterData">Optional target payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public async Task TransitionAsync(
            OnitySceneFlowStateId targetStateId,
            IOnitySceneEnterData enterData = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OnitySceneFlowTransitionPlan transitionPlan =
                BuildTransitionPlan(targetStateId, enterData);

            await ExecuteTransitionPlanAsync(transitionPlan, cancellationToken);
        }

        /// <summary>
        /// Executes transition to a concrete target scene grouped by the profile.
        /// </summary>
        /// <param name="targetSceneName">Target scene name.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public async Task TransitionAsync(
            string targetSceneName,
            IOnitySceneEnterData enterData = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OnitySceneFlowTransitionPlan transitionPlan =
                BuildTransitionPlan(targetSceneName, enterData);

            await ExecuteTransitionPlanAsync(transitionPlan, cancellationToken);
        }

        private async Task ExecuteTransitionPlanAsync(
            OnitySceneFlowTransitionPlan transitionPlan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (transitionPlan.RouteViaLoadingScene)
            {
                OnitySceneTransitionStore.SetNextTarget(
                    transitionPlan.TargetSceneName,
                    transitionPlan.EnterData);
            }
            else
            {
                OnitySceneTransitionStore.SetActiveEnterData(transitionPlan.EnterData);
            }

            CurrentStateId = transitionPlan.EntryStateId;
            await OnitySceneLoader.LoadSingleAsync(
                transitionPlan.EntrySceneName,
                cancellationToken: cancellationToken);
        }
    }
}
