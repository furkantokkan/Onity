namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Canonical SEP scene-flow groups for Onity-driven game loops.
    /// Bootstrap and Loading are singleton groups; MainMenuHub and Gameplay can contain multiple scenes.
    /// </summary>
    public enum OnitySceneFlowStateId
    {
        /// <summary>
        /// Unknown or unmapped scene state.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Initial bootstrap state.
        /// </summary>
        Bootstrap = 1,

        /// <summary>
        /// Transitional loading state.
        /// </summary>
        Loading = 2,

        /// <summary>
        /// Main menu hub state.
        /// </summary>
        MainMenuHub = 3,

        /// <summary>
        /// Gameplay state.
        /// </summary>
        Gameplay = 4
    }
}
