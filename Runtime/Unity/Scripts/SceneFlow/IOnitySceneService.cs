using System.Threading;
using Onity.Reactive;
using Onity.Unity.Async;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Loads and unloads scenes for the whole application: one operation at a time, each one complete only when
    /// its exact destination scene is ready, its cover has lifted and the scenes it replaced have unloaded.
    /// </summary>
    /// <remarks>
    /// Bind one service on the <c>ProjectContext</c> with
    /// <see cref="OnitySceneServiceBindingExtensions.BindSceneService" />; scene scopes resolve the same instance
    /// from their parent. The service owns every operation it starts, so the scene that asks may unload without
    /// stopping it: a caller's token ends only that caller's wait.
    /// </remarks>
    public interface IOnitySceneService
    {
        /// <summary>
        /// Gets the scene that is active now, read from <see cref="SceneManager" /> on every call, so scene changes
        /// made outside the service are reported too.
        /// </summary>
        Scene ActiveScene { get; }

        /// <summary>
        /// Gets whether an operation runs, from its start until its result is published.
        /// </summary>
        bool IsBusy { get; }

        /// <summary>
        /// Gets the running operation's step and progress, published only when they change.
        /// <see cref="OnitySceneOperationState.Idle" /> when no operation runs.
        /// </summary>
        IReadOnlyReactiveProperty<OnitySceneOperationState> State { get; }

        /// <summary>
        /// Publishes each started operation's result once, after the service is idle again, so a subscriber can
        /// start the next operation at once. Requests that start nothing (<see cref="OnitySceneResultStatus.Busy" />,
        /// <see cref="OnitySceneResultStatus.InvalidTarget" />, <see cref="OnitySceneResultStatus.NotLoaded" />,
        /// <see cref="OnitySceneResultStatus.Disposed" />) are only returned to their caller.
        /// </summary>
        IOnityObservable<OnitySceneResult> Completed { get; }

        /// <summary>
        /// Loads the scene <paramref name="request" /> names.
        /// </summary>
        /// <param name="request">The scene, load mode, enter data and cover.</param>
        /// <param name="cancellationToken">
        /// Ends the caller's wait only: the returned task is canceled while a started operation still completes. An
        /// already canceled token starts nothing.
        /// </param>
        /// <returns>The operation's result. Requests that cannot start return at once with their reason.</returns>
        OnityTask<OnitySceneResult> LoadAsync(
            OnitySceneRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Unloads exactly <paramref name="scene" />, never another loaded instance with the same name.
        /// </summary>
        /// <param name="scene">The loaded scene to unload.</param>
        /// <param name="cancellationToken">
        /// Ends the caller's wait only; an already canceled token starts nothing.
        /// </param>
        /// <returns>
        /// The operation's result; <see cref="OnitySceneResultStatus.NotLoaded" /> for a scene that is not loaded.
        /// </returns>
        OnityTask<OnitySceneResult> UnloadAsync(Scene scene, CancellationToken cancellationToken = default);
    }
}
