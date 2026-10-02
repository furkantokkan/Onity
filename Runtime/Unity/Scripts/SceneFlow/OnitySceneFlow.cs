using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Shared scene flow helpers for SEP-style transitions.
    /// </summary>
    public static class OnitySceneFlow
    {
        /// <summary>
        /// Transitions to target state using a scene-flow profile.
        /// </summary>
        /// <param name="profile">Scene-flow profile.</param>
        /// <param name="targetStateId">Target state id.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static async Task TransitionAsync(
            OnitySceneFlowProfile profile,
            OnitySceneFlowStateId targetStateId,
            IOnitySceneEnterData enterData = null,
            CancellationToken cancellationToken = default)
        {
            OnitySceneFlowStateMachine stateMachine = new OnitySceneFlowStateMachine(profile);
            await stateMachine.TransitionAsync(targetStateId, enterData, cancellationToken);
        }

        /// <summary>
        /// Transitions to a concrete scene name grouped in the supplied profile.
        /// This supports multiple menu or gameplay scenes under the same profile.
        /// </summary>
        /// <param name="profile">Scene-flow profile.</param>
        /// <param name="targetSceneName">Concrete grouped scene name.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static async Task TransitionAsync(
            OnitySceneFlowProfile profile,
            string targetSceneName,
            IOnitySceneEnterData enterData = null,
            CancellationToken cancellationToken = default)
        {
            OnitySceneFlowStateMachine stateMachine = new OnitySceneFlowStateMachine(profile);
            await stateMachine.TransitionAsync(targetSceneName, enterData, cancellationToken);
        }

        /// <summary>
        /// Builds transition plan for target state without loading scenes.
        /// </summary>
        /// <param name="profile">Scene-flow profile.</param>
        /// <param name="targetStateId">Target state id.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <returns>Resolved transition plan.</returns>
        public static OnitySceneFlowTransitionPlan BuildTransitionPlan(
            OnitySceneFlowProfile profile,
            OnitySceneFlowStateId targetStateId,
            IOnitySceneEnterData enterData = null)
        {
            OnitySceneFlowStateMachine stateMachine = new OnitySceneFlowStateMachine(profile);
            return stateMachine.BuildTransitionPlan(targetStateId, enterData);
        }

        /// <summary>
        /// Builds transition plan for a concrete grouped scene without loading scenes.
        /// </summary>
        /// <param name="profile">Scene-flow profile.</param>
        /// <param name="targetSceneName">Concrete grouped scene name.</param>
        /// <param name="enterData">Optional typed target payload.</param>
        /// <returns>Resolved transition plan.</returns>
        public static OnitySceneFlowTransitionPlan BuildTransitionPlan(
            OnitySceneFlowProfile profile,
            string targetSceneName,
            IOnitySceneEnterData enterData = null)
        {
            OnitySceneFlowStateMachine stateMachine = new OnitySceneFlowStateMachine(profile);
            return stateMachine.BuildTransitionPlan(targetSceneName, enterData);
        }

        /// <summary>
        /// Requests transition via loading scene then loads loading scene.
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
            OnitySceneTransitionStore.SetNextTarget(targetSceneName, enterData);
            await OnitySceneLoader.LoadSingleAsync(loadingSceneName);
        }

        /// <summary>
        /// Loads one scene in single mode and waits for completion.
        /// </summary>
        /// <param name="sceneName">Scene name.</param>
        /// <returns>Completion task.</returns>
        public static async Task LoadSingleAsync(string sceneName)
        {
            await OnitySceneLoader.LoadSingleAsync(sceneName);
        }
    }
}
