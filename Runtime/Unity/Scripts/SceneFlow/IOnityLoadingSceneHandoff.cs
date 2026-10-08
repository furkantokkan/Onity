using System;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// A scene change that <see cref="OnitySceneService" /> routes through the Loading scene. The service registers
    /// it for the exact Loading scene it loads, and that scene's <see cref="OnityLoadingSceneInitiator" /> takes it
    /// with <see cref="OnitySceneTransitionStore.TryTakeLoadingSceneHandoff" /> instead of the uncorrelated pending
    /// target, so the service stays the owner until the final target is ready.
    /// </summary>
    internal interface IOnityLoadingSceneHandoff
    {
        /// <summary>
        /// Loads the operation's target behind the Loading scene, reports its progress, waits for
        /// <paramref name="activationGate" />, covers the screen and activates the target. The target always
        /// activates once its load started, also when the gate fails or the Loading scene is destroyed.
        /// </summary>
        /// <param name="onProgress">The Loading scene's progress view; may be null.</param>
        /// <param name="activationGate">Completes when the Loading scene may make way, for example after its minimum visible time.</param>
        /// <param name="cancellationToken">The Loading scene's token; it ends no load or activation the service owns.</param>
        /// <returns>A task that completes when the target is active, or faults when it could not load.</returns>
        Task LoadTargetAsync(Action<float> onProgress, Task activationGate, CancellationToken cancellationToken);
    }
}
