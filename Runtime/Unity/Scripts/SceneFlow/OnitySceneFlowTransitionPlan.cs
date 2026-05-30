namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Resolved transition plan for moving between SEP scene-flow groups.
    /// </summary>
    public readonly struct OnitySceneFlowTransitionPlan
    {
        /// <summary>
        /// Initializes a transition plan.
        /// </summary>
        /// <param name="entryStateId">State entered first.</param>
        /// <param name="targetStateId">Final target state.</param>
        /// <param name="entrySceneName">Scene loaded first.</param>
        /// <param name="targetSceneName">Final target scene.</param>
        /// <param name="routeViaLoadingScene">True when plan goes through loading scene.</param>
        /// <param name="enterData">Payload passed to final target scene.</param>
        public OnitySceneFlowTransitionPlan(
            OnitySceneFlowStateId entryStateId,
            OnitySceneFlowStateId targetStateId,
            string entrySceneName,
            string targetSceneName,
            bool routeViaLoadingScene,
            IOnitySceneEnterData enterData)
        {
            EntryStateId = entryStateId;
            TargetStateId = targetStateId;
            EntrySceneName = entrySceneName;
            TargetSceneName = targetSceneName;
            RouteViaLoadingScene = routeViaLoadingScene;
            EnterData = enterData;
        }

        /// <summary>
        /// Scene-flow group entered immediately by the transition.
        /// </summary>
        public OnitySceneFlowStateId EntryStateId { get; }

        /// <summary>
        /// Final target scene-flow group.
        /// </summary>
        public OnitySceneFlowStateId TargetStateId { get; }

        /// <summary>
        /// Scene loaded immediately by the transition.
        /// </summary>
        public string EntrySceneName { get; }

        /// <summary>
        /// Final target scene.
        /// </summary>
        public string TargetSceneName { get; }

        /// <summary>
        /// True when transition routes through loading scene.
        /// </summary>
        public bool RouteViaLoadingScene { get; }

        /// <summary>
        /// Optional typed payload forwarded to target scene.
        /// </summary>
        public IOnitySceneEnterData EnterData { get; }
    }
}
