using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// One operation of <see cref="OnitySceneService" />: its request, the scenes it reaches, the records it keeps
    /// in <see cref="OnitySceneTransitionStore" /> and, on the Loading-scene route, the handoff the Loading scene
    /// takes. The service owns it from admission until its result is published.
    /// </summary>
    internal sealed class OnitySceneOperation : IOnityLoadingSceneHandoff
    {
        private readonly OnitySceneService m_service;
        private readonly TaskCompletionSource<bool> m_targetActivated =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_loadingSceneRevealed =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private Action<float> m_viewProgress;

        public OnitySceneOperation(
            OnitySceneService service,
            int id,
            string label,
            OnitySceneRequest request,
            string targetKey,
            string loadingKey,
            IOnitySceneCover cover,
            CancellationToken token)
        {
            m_service = service;
            Id = id;
            Label = label;
            Request = request;
            TargetKey = targetKey;
            LoadingKey = loadingKey;
            Cover = cover;
            Token = token;
            OnTargetCreated = RegisterTarget;
            OnLoadingSceneCreated = RegisterLoadingScene;
            ReportTargetProgress = ReportProgress;
        }

        public int Id { get; }

        // The scene as requested, published in the service's state.
        public string Label { get; }

        public OnitySceneRequest Request { get; }

        public string TargetKey { get; }

        // Null unless the change routes through the Loading scene.
        public string LoadingKey { get; }

        // Null for no cover.
        public IOnitySceneCover Cover { get; }

        // The service's lifetime token; a caller's token never reaches the operation.
        public CancellationToken Token { get; }

        public OnityTaskCompletionSource<OnitySceneResult> Completion { get; } =
            new OnityTaskCompletionSource<OnitySceneResult>();

        public Scene Target { get; set; }

        public bool IsCoverShown { get; set; }

        public bool IsHandoffTaken { get; private set; }

        public Task TargetActivated => m_targetActivated.Task;

        public Action<Scene> OnTargetCreated { get; }

        public Action<Scene> OnLoadingSceneCreated { get; }

        public Action<float> ReportTargetProgress { get; }

        public void MarkLoadingSceneRevealed()
        {
            m_loadingSceneRevealed.TrySetResult(true);
        }

        // Removes everything this operation registered, so no later scene or operation can read it.
        public void ReleaseRecords()
        {
            OnitySceneTransitionStore.RemoveSceneEnterData(Target);
            OnitySceneTransitionStore.ClearActiveEnterData(Request.EnterData);
            OnitySceneTransitionStore.ClearLoadingSceneHandoff(this);

            // A handoff still waiting for the Loading scene's reveal goes on to activate its target.
            m_loadingSceneRevealed.TrySetResult(true);
        }

        // Owner: the Loading scene's initiator awaits it; the service awaits TargetActivated. Stops: the target is
        // active, or its load failed before it started.
        async Task IOnityLoadingSceneHandoff.LoadTargetAsync(
            Action<float> onProgress,
            Task activationGate,
            CancellationToken cancellationToken)
        {
            IsHandoffTaken = true;
            m_viewProgress = onProgress;

            try
            {
                IOnitySceneLoad load = await m_service.LoadDeferredTargetAsync(this);

                try
                {
                    await activationGate;
                }
                catch (Exception)
                {
                    // The Loading scene ended early, for example destroyed: the target still activates.
                }

                try
                {
                    // The service lifts its cover over the Loading scene first, so the two never overlap.
                    await m_loadingSceneRevealed.Task;
                    await m_service.CoverBeforeActivationAsync(this);
                }
                finally
                {
                    // Unity holds every later scene operation behind a deferred activation that never happens.
                    await load.ActivateAsync(ReportTargetProgress);
                }

                m_targetActivated.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                m_targetActivated.TrySetCanceled();
                throw;
            }
            catch (Exception exception)
            {
                m_targetActivated.TrySetException(exception);
                throw;
            }
        }

        private void RegisterTarget(Scene scene)
        {
            Target = scene;
            IOnitySceneEnterData enterData = Request.EnterData;

            if (enterData == null)
            {
                return;
            }

            OnitySceneTransitionStore.SetSceneEnterData(scene, enterData);

            // Scenes written against the shared slot receive it too; ReleaseRecords clears it when still unread.
            OnitySceneTransitionStore.SetActiveEnterData(enterData);
        }

        private void RegisterLoadingScene(Scene scene)
        {
            OnitySceneTransitionStore.SetLoadingSceneHandoff(scene, this);
        }

        private void ReportProgress(float progress)
        {
            m_viewProgress?.Invoke(progress);
            m_service.ReportProgress(this, progress);
        }
    }
}
