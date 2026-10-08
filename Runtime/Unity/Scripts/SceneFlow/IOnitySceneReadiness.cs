using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Waits until the scene a change left active can be shown. <see cref="OnityCoveredSceneTransition" /> waits on
    /// it before it hides its cover; tests replace it to run a transition without loading scenes.
    /// </summary>
    public interface IOnitySceneReadiness
    {
        /// <summary>
        /// Completes once the active scene may be shown.
        /// </summary>
        /// <param name="cancellationToken">Ends the wait; the returned task is then canceled.</param>
        /// <returns>A task that completes when the active scene may be revealed.</returns>
        Task WaitUntilReadyAsync(CancellationToken cancellationToken);
    }
}
