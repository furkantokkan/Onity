using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// A screen cover that hides a scene change: a fade, an image, an iris wipe, a loading screen or a custom cover.
    /// </summary>
    /// <remarks>
    /// <see cref="OnityCoveredSceneTransition" /> shows a cover before a scene change and hides it once the new scene
    /// is ready. The cover owns its animation; a token passed to <see cref="ShowAsync" /> or
    /// <see cref="HideAsync" /> stops that animation where it is.
    /// </remarks>
    public interface IOnitySceneCover
    {
        /// <summary>
        /// Gets whether any part of the cover shows. A visible cover blocks presses on the panels below it.
        /// </summary>
        bool IsVisible { get; }

        /// <summary>
        /// Shows the cover and completes once it fully covers the screen.
        /// </summary>
        /// <param name="cancellationToken">Stops the animation where it is; the returned task is then canceled.</param>
        /// <returns>A task that completes when nothing behind the cover shows.</returns>
        Task ShowAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Hides the cover and completes once it is fully hidden and lets presses through again.
        /// </summary>
        /// <param name="cancellationToken">Stops the animation where it is; the returned task is then canceled.</param>
        /// <returns>A task that completes when the cover no longer shows.</returns>
        Task HideAsync(CancellationToken cancellationToken);
    }
}
