using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using Onity.Unity.SceneFlow;

namespace Onity.Samples.TankArena2D.SceneFlow
{
    /// <summary>
    /// Shared helpers for scene loading operations.
    /// </summary>
    public static class TankArenaSceneFlowLoader
    {
        /// <summary>
        /// Transitions to a target state through shared profile-driven state machine.
        /// </summary>
        /// <param name="sceneFlowProfile">Scene-flow profile.</param>
        /// <param name="targetStateId">Target state id.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static async Task TransitionAsync(
            OnitySceneFlowProfile sceneFlowProfile,
            OnitySceneFlowStateId targetStateId,
            IOnitySceneEnterData enterData = null,
            CancellationToken cancellationToken = default)
        {
            await OnitySceneFlow.TransitionAsync(
                sceneFlowProfile,
                targetStateId,
                enterData,
                cancellationToken);
        }

        /// <summary>
        /// Transitions to a concrete grouped scene name using the shared profile-driven state machine.
        /// </summary>
        /// <param name="sceneFlowProfile">Scene-flow profile.</param>
        /// <param name="targetSceneName">Concrete grouped scene name.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static async Task TransitionAsync(
            OnitySceneFlowProfile sceneFlowProfile,
            string targetSceneName,
            IOnitySceneEnterData enterData = null,
            CancellationToken cancellationToken = default)
        {
            await OnitySceneFlow.TransitionAsync(
                sceneFlowProfile,
                targetSceneName,
                enterData,
                cancellationToken);
        }

        /// <summary>
        /// Resolves transition plan without loading scenes.
        /// </summary>
        /// <param name="sceneFlowProfile">Scene-flow profile.</param>
        /// <param name="targetStateId">Target state id.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <returns>Resolved transition plan.</returns>
        public static OnitySceneFlowTransitionPlan BuildTransitionPlan(
            OnitySceneFlowProfile sceneFlowProfile,
            OnitySceneFlowStateId targetStateId,
            IOnitySceneEnterData enterData = null)
        {
            return OnitySceneFlow.BuildTransitionPlan(
                sceneFlowProfile,
                targetStateId,
                enterData);
        }

        /// <summary>
        /// Resolves transition plan for a concrete grouped scene without loading scenes.
        /// </summary>
        /// <param name="sceneFlowProfile">Scene-flow profile.</param>
        /// <param name="targetSceneName">Concrete grouped scene name.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <returns>Resolved transition plan.</returns>
        public static OnitySceneFlowTransitionPlan BuildTransitionPlan(
            OnitySceneFlowProfile sceneFlowProfile,
            string targetSceneName,
            IOnitySceneEnterData enterData = null)
        {
            return OnitySceneFlow.BuildTransitionPlan(
                sceneFlowProfile,
                targetSceneName,
                enterData);
        }

        /// <summary>
        /// Transitions directly to a target state without routing through LoadingScene.
        /// This is used by Tank Arena bootstrap so the app opens Main Menu first.
        /// </summary>
        /// <param name="sceneFlowProfile">Scene-flow profile.</param>
        /// <param name="targetStateId">Target state id.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static async Task TransitionDirectAsync(
            OnitySceneFlowProfile sceneFlowProfile,
            OnitySceneFlowStateId targetStateId,
            IOnitySceneEnterData enterData = null,
            CancellationToken cancellationToken = default)
        {
            if (sceneFlowProfile == null)
            {
                throw new ArgumentNullException(nameof(sceneFlowProfile));
            }

            if (sceneFlowProfile.TryGetSceneName(targetStateId, out string targetSceneName) == false)
            {
                throw new InvalidOperationException(
                    $"SceneFlow profile does not contain target scene for state '{targetStateId}'.");
            }

            await LoadSingleWithEnterDataAsync(targetSceneName, enterData, cancellationToken);
        }

        /// <summary>
        /// Requests transition via LoadingScene then loads the loading scene.
        /// </summary>
        /// <param name="loadingSceneName">Loading scene name.</param>
        /// <param name="targetSceneName">Target scene name loaded by loading scene.</param>
        /// <param name="enterData">Optional typed target entry payload.</param>
        /// <returns>Completion task.</returns>
        public static async Task TransitionViaLoadingSceneAsync(
            string loadingSceneName,
            string targetSceneName,
            IOnitySceneEnterData enterData = null)
        {
            await OnitySceneFlow.TransitionViaLoadingSceneAsync(
                loadingSceneName,
                targetSceneName,
                enterData);
        }

        /// <summary>
        /// Loads a scene directly and applies typed enter data without a loading-scene handoff.
        /// </summary>
        /// <param name="sceneName">Scene name.</param>
        /// <param name="enterData">Optional typed target entry payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static async Task LoadSingleWithEnterDataAsync(
            string sceneName,
            IOnitySceneEnterData enterData = null,
            CancellationToken cancellationToken = default)
        {
            OnitySceneTransitionStore.SetActiveEnterData(enterData);
            await OnitySceneLoader.LoadSingleAsync(sceneName, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Loads one scene in Single mode and waits for completion.
        /// </summary>
        /// <param name="sceneName">Scene name.</param>
        public static async Task LoadSingleAsync(string sceneName)
        {
            await OnitySceneFlow.LoadSingleAsync(sceneName);
        }
    }
}
