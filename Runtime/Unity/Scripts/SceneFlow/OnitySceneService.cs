using System;
using System.Threading;
using Onity.Reactive;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// The default <see cref="IOnitySceneService" />: loads scenes from the Build Settings by their exact path,
    /// shows the request's cover, routes Single requests through the profile's Loading scene, starts a scene the
    /// <see cref="OnityScenePreloader" /> keeps prepared when asked, waits until the destination scene's
    /// <c>SceneContext</c> is ready, lifts the cover and then reports the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owner: the <c>ProjectContext</c> scope, through
    /// <see cref="OnitySceneServiceBindingExtensions.BindSceneService" />, which disposes it with the scope. One
    /// operation runs at a time: a request while one runs returns <see cref="OnitySceneResultStatus.Busy" />.
    /// Stop points: an operation ends when its result is published; <see cref="Dispose" /> rejects later requests
    /// and cancels the lifetime token every cover, load wait and readiness wait observes, while a scene activation
    /// that Unity already holds still completes.
    /// </para>
    /// <para>
    /// The service keeps no list of loaded scenes: <see cref="ActiveScene" /> and every check read
    /// <see cref="SceneManager" /> directly, so scenes loaded or unloaded around the service never leave it stale.
    /// Scene loads made around the service are not serialized with its operations.
    /// </para>
    /// </remarks>
    public sealed partial class OnitySceneService : IOnitySceneService, IDisposable
    {
        private readonly IOnitySceneBackend m_backend;
        private readonly OnitySceneFlowProfile m_profile;
        private readonly IOnitySceneCover m_defaultCover;
        private readonly OnityScenePreloader m_preloader;
        private readonly OnitySceneRevealPolicy m_revealPolicy;
        private readonly CancellationTokenSource m_lifetime = new CancellationTokenSource();
        private readonly ReactiveProperty<OnitySceneOperationState> m_state =
            new ReactiveProperty<OnitySceneOperationState>();
        private readonly Subject<OnitySceneResult> m_completed = new Subject<OnitySceneResult>();

        private OnitySceneOperation m_current;
        private int m_lastOperationId;

        // Publications in progress; disposal waits for them before it ends the subscriptions.
        private int m_publishDepth;
        private bool m_isDisposed;
        private bool m_areNotificationsDisposed;

        /// <summary>
        /// Initializes a service that loads scenes from the Build Settings. Bind it with
        /// <see cref="OnitySceneServiceBindingExtensions.BindSceneService" /> rather than constructing it.
        /// </summary>
        /// <param name="profile">
        /// Routes Single requests through its Loading scene when it asks to; null loads every scene directly.
        /// </param>
        /// <param name="defaultCover">The cover of requests that ask for the default; null for none.</param>
        /// <param name="preloader">
        /// The preloader whose prepared scenes <see cref="OnitySceneRequest.WithPreparedScene" /> requests start;
        /// null loads those scenes normally.
        /// </param>
        /// <param name="revealPolicy">When a loaded scene may show; null uses the default policy.</param>
        public OnitySceneService(
            OnitySceneFlowProfile profile = null,
            IOnitySceneCover defaultCover = null,
            OnityScenePreloader preloader = null,
            OnitySceneRevealPolicy revealPolicy = null)
            : this(new OnitySceneManagerBackend(), profile, defaultCover, preloader, revealPolicy)
        {
        }

        internal OnitySceneService(
            IOnitySceneBackend backend,
            OnitySceneFlowProfile profile,
            IOnitySceneCover defaultCover,
            OnityScenePreloader preloader,
            OnitySceneRevealPolicy revealPolicy)
        {
            m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
            m_profile = profile;
            m_defaultCover = defaultCover;
            m_preloader = preloader;
            m_revealPolicy = revealPolicy ?? new OnitySceneRevealPolicy();
        }

        /// <inheritdoc />
        public Scene ActiveScene => SceneManager.GetActiveScene();

        /// <inheritdoc />
        public bool IsBusy => m_current != null;

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<OnitySceneOperationState> State => m_state;

        /// <inheritdoc />
        public IOnityObservable<OnitySceneResult> Completed => m_completed;

        /// <summary>
        /// Gets the cover of requests that ask for the default; null when the service has none.
        /// </summary>
        public IOnitySceneCover DefaultCover => m_defaultCover;

        /// <inheritdoc />
        public OnityTask<OnitySceneResult> LoadAsync(
            OnitySceneRequest request,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<OnitySceneResult>.FromCanceled(cancellationToken);
            }

            if (m_isDisposed)
            {
                return Reject(OnitySceneResultStatus.Disposed, "The scene service is disposed.");
            }

            // Validation changes nothing, so a rejected request leaves cover, enter data and scenes untouched.
            if (TryPlan(request, out string targetKey, out string loadingKey, out string error) == false)
            {
                return Reject(OnitySceneResultStatus.InvalidTarget, error);
            }

            if (m_current != null)
            {
                return RejectBusy();
            }

            OnitySceneOperation operation = Admit(
                request.Scene,
                request,
                targetKey,
                loadingKey,
                SelectCover(request));
            RunLoadAsync(operation).Forget(Debug.LogException);
            return WaitForCaller(operation, cancellationToken);
        }

        /// <inheritdoc />
        public OnityTask<OnitySceneResult> UnloadAsync(Scene scene, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<OnitySceneResult>.FromCanceled(cancellationToken);
            }

            if (m_isDisposed)
            {
                return Reject(OnitySceneResultStatus.Disposed, "The scene service is disposed.");
            }

            if (scene.IsValid() == false || scene.isLoaded == false)
            {
                return Reject(OnitySceneResultStatus.NotLoaded, "The scene to unload is not loaded.");
            }

            if (SceneManager.loadedSceneCount <= 1)
            {
                return Reject(
                    OnitySceneResultStatus.InvalidTarget,
                    $"Scene '{scene.name}' is the only loaded scene; Unity cannot unload it.");
            }

            if (m_current != null)
            {
                return RejectBusy();
            }

            OnitySceneOperation operation = Admit(scene.name, default, null, null, null);
            operation.Target = scene;
            RunUnloadAsync(operation, scene).Forget(Debug.LogException);
            return WaitForCaller(operation, cancellationToken);
        }

        /// <summary>
        /// Rejects later requests with <see cref="OnitySceneResultStatus.Disposed" />, cancels the lifetime token the
        /// running operation observes (it then completes with <see cref="OnitySceneResultStatus.Disposed" />) and
        /// ends the <see cref="State" /> and <see cref="Completed" /> subscriptions. A deferred scene activation the
        /// operation started still completes. Prepared scenes stay with their preloader.
        /// </summary>
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_lifetime.Cancel();
            m_lifetime.Dispose();

            // Called from a State or Completed subscriber: the subscriptions end once that publication returns.
            if (m_publishDepth == 0)
            {
                DisposeNotifications();
            }
        }

        // The current operation's load progress; allocation-free, published only when it changes.
        internal void ReportProgress(OnitySceneOperation operation, float progress)
        {
            Publish(operation, OnitySceneOperationPhase.Loading, progress);
        }

        private bool TryPlan(
            OnitySceneRequest request,
            out string targetKey,
            out string loadingKey,
            out string error)
        {
            targetKey = null;
            loadingKey = null;

            if (request.Mode != LoadSceneMode.Single && request.Mode != LoadSceneMode.Additive)
            {
                error = $"Load mode '{request.Mode}' is not supported.";
                return false;
            }

            if (request.Mode == LoadSceneMode.Additive && request.PreferPreparedScene)
            {
                error = "Only a Single request can start a prepared scene: a prepared start replaces the active scene.";
                return false;
            }

            if (m_backend.TryResolve(request.Scene, out targetKey, out error) == false)
            {
                return false;
            }

            if (request.Mode == LoadSceneMode.Single
                && m_profile != null
                && m_profile.RouteTransitionsThroughLoadingScene
                && m_profile.TryGetSceneName(OnitySceneFlowStateId.Loading, out string loadingScene))
            {
                if (m_backend.TryResolve(loadingScene, out string resolvedLoadingKey, out string loadingError) == false)
                {
                    targetKey = null;
                    error = $"The profile routes through its Loading scene, which cannot load: {loadingError}";
                    return false;
                }

                // A request for the Loading scene itself loads it directly.
                if (string.Equals(resolvedLoadingKey, targetKey, StringComparison.OrdinalIgnoreCase) == false)
                {
                    loadingKey = resolvedLoadingKey;
                }
            }

            error = null;
            return true;
        }

        private IOnitySceneCover SelectCover(OnitySceneRequest request)
        {
            switch (request.CoverMode)
            {
                case OnitySceneCoverMode.Default:
                    return m_defaultCover;

                case OnitySceneCoverMode.Custom:
                    return request.Cover;

                default:
                    return null;
            }
        }

        private OnitySceneOperation Admit(
            string label,
            OnitySceneRequest request,
            string targetKey,
            string loadingKey,
            IOnitySceneCover cover)
        {
            m_lastOperationId = m_lastOperationId == int.MaxValue ? 1 : m_lastOperationId + 1;
            OnitySceneOperation operation = new OnitySceneOperation(
                this,
                m_lastOperationId,
                label,
                request,
                targetKey,
                loadingKey,
                cover,
                m_lifetime.Token);
            m_current = operation;
            return operation;
        }

        // Ends an operation: its records go first, then the service is idle, then its result is published, so a
        // subscriber can start the next operation at once and nothing here touches that newer operation.
        private void Finish(OnitySceneOperation operation, OnitySceneResult result)
        {
            operation.ReleaseRecords();

            if (ReferenceEquals(m_current, operation))
            {
                m_current = null;
            }

            if (m_isDisposed == false)
            {
                m_publishDepth++;

                try
                {
                    // Both sources report a throwing subscriber to OnityObservableExceptionHandler and go on.
                    m_state.Value = OnitySceneOperationState.Idle;
                    m_completed.OnNext(result);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    EndPublication();
                }
            }

            operation.Completion.TrySetResult(result);
        }

        private void Publish(OnitySceneOperation operation, OnitySceneOperationPhase phase, float progress)
        {
            // A finished operation never publishes over a newer one.
            if (m_isDisposed || ReferenceEquals(m_current, operation) == false)
            {
                return;
            }

            m_publishDepth++;

            try
            {
                m_state.Value = new OnitySceneOperationState(operation.Id, phase, operation.Label, progress);
            }
            finally
            {
                EndPublication();
            }
        }

        private void EndPublication()
        {
            m_publishDepth--;

            if (m_isDisposed && m_publishDepth == 0)
            {
                DisposeNotifications();
            }
        }

        private void DisposeNotifications()
        {
            if (m_areNotificationsDisposed)
            {
                return;
            }

            m_areNotificationsDisposed = true;
            m_state.Dispose();
            m_completed.Dispose();
        }

        private OnityTask<OnitySceneResult> RejectBusy()
        {
            return Reject(
                OnitySceneResultStatus.Busy,
                $"Operation {m_current.Id} ('{m_current.Label}') is running.");
        }

        private static OnityTask<OnitySceneResult> Reject(OnitySceneResultStatus status, string message)
        {
            return OnityTask<OnitySceneResult>.FromResult(new OnitySceneResult(status, 0, default, message));
        }

        // The caller's token ends only its own wait; the operation keeps the service's lifetime token.
        private static OnityTask<OnitySceneResult> WaitForCaller(
            OnitySceneOperation operation,
            CancellationToken cancellationToken)
        {
            OnityTask<OnitySceneResult> task = operation.Completion.Task;
            return cancellationToken.CanBeCanceled ? task.AttachExternalCancellation(cancellationToken) : task;
        }
    }
}
