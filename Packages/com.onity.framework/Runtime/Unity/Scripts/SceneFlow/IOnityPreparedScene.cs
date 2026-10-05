using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// A scene that built itself hidden, behind the scene that shows, and waits to be started by
    /// <see cref="OnityScenePreloader" />. Bind it in the prepared scene's <c>SceneContext</c>.
    /// </summary>
    /// <remarks>
    /// The scene learns that it is being prepared from <see cref="OnityScenePreloader.TryGetPrepareData{T}" />,
    /// called with its own scene while it installs. While prepared it keeps itself hidden (cameras, audio listeners
    /// and lights off, UI document roots <c>display: none</c>) and shows itself in <see cref="Activate" />.
    /// </remarks>
    public interface IOnityPreparedScene
    {
        /// <summary>
        /// Gets whether the scene finished building hidden and waits to be started. False for a scene that did not
        /// prepare, or after it started.
        /// </summary>
        bool IsPrepared { get; }

        /// <summary>
        /// Decides whether this prepared scene can start for <paramref name="startData" />, for example whether it
        /// built the level the start asks for.
        /// </summary>
        /// <param name="startData">The start request; may be null.</param>
        /// <returns>True when the scene can start for the request.</returns>
        bool CanStart(IOnitySceneEnterData startData);

        /// <summary>
        /// Applies <paramref name="startData" /> while the scene is still hidden, so it shows complete in
        /// <see cref="Activate" />. Owner: the preloader, which awaits it. A failure leaves the scene unstarted and
        /// the preloader unloads it.
        /// </summary>
        /// <param name="startData">The start request; may be null.</param>
        /// <param name="cancellationToken">The preloader's lifetime token.</param>
        /// <returns>A task that completes when the scene can show.</returns>
        Task PrepareStartAsync(IOnitySceneEnterData startData, CancellationToken cancellationToken);

        /// <summary>
        /// Shows the scene and starts it. Called once, after <see cref="PrepareStartAsync" />, in the frame the scene
        /// becomes the active scene and the scene it replaces is hidden.
        /// </summary>
        void Activate();
    }
}
