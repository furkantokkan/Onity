using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.SceneFlow;
using UnityEngine;

namespace Onity.Samples.TankArena2D.SceneFlow
{
    /// <summary>
    /// Entry controller for Bootstrap scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TankArenaBootstrapSceneController : OnitySceneInitiator
    {
        [Header("Profile")]
        [Tooltip("Shared scene-flow profile. When assigned, profile-driven state machine is used.")]
        [SerializeField] private OnitySceneFlowProfile m_sceneFlowProfile;

        [Header("Flow")]
        [Tooltip("Initial scene loaded from bootstrap. Usually MainMenuHub.")]
        [SerializeField] private string m_initialTargetScene = TankArenaSceneIds.MainMenu;

        [Tooltip("Loading scene used during transitions.")]
        [SerializeField] private string m_loadingScene = TankArenaSceneIds.Loading;

        [Tooltip(
            "When enabled, bootstrap opens Main Menu directly and reserves LoadingScene for later gameplay transitions.")]
        [SerializeField] private bool m_skipLoadingSceneOnStartup = true;

        /// <inheritdoc />
        protected override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TankArenaMainMenuEnterData enterData = new TankArenaMainMenuEnterData(
                TankArenaSceneEntrySource.Bootstrap,
                0,
                0);

            if (m_skipLoadingSceneOnStartup)
            {
                if (m_sceneFlowProfile != null)
                {
                    await TankArenaSceneFlowLoader.TransitionDirectAsync(
                        m_sceneFlowProfile,
                        OnitySceneFlowStateId.MainMenuHub,
                        enterData,
                        cancellationToken);
                    return;
                }

                await TankArenaSceneFlowLoader.LoadSingleWithEnterDataAsync(
                    m_initialTargetScene,
                    enterData,
                    cancellationToken);
                return;
            }

            if (m_sceneFlowProfile != null)
            {
                await TankArenaSceneFlowLoader.TransitionAsync(
                    m_sceneFlowProfile,
                    OnitySceneFlowStateId.MainMenuHub,
                    enterData,
                    cancellationToken);
                return;
            }

            await TankArenaSceneFlowLoader.TransitionViaLoadingSceneAsync(
                m_loadingScene,
                m_initialTargetScene,
                enterData);
        }
    }
}
