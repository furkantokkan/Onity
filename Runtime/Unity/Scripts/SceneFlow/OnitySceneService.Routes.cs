using System;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    public sealed partial class OnitySceneService
    {
        // The Loading scene's initiator takes the change in its Start, which runs within a frame of the load.
        private const int k_handoffFrames = 2;

        private const string k_disposedMessage = "The scene service was disposed during the operation.";

        // The handoff's deferred target load. It observes the lifetime token only until Unity starts the load.
        internal async Task<IOnitySceneLoad> LoadDeferredTargetAsync(OnitySceneOperation operation)
        {
            operation.Token.ThrowIfCancellationRequested();
            return await m_backend.LoadAsync(
                operation.TargetKey,
                LoadSceneMode.Single,
                true,
                operation.OnTargetCreated,
                operation.ReportTargetProgress,
                operation.Token);
        }

        // The handoff covers the Loading scene before the target replaces it. An operation that already ended
        // (disposed or failed) activates its target uncovered rather than leave a cover up that nothing lifts.
        internal Task CoverBeforeActivationAsync(OnitySceneOperation operation)
        {
            return ReferenceEquals(m_current, operation) ? ShowCoverAsync(operation) : Task.CompletedTask;
        }

        // Owner: this service, which holds the operation in m_current until Finish. Stops: Finish with the
        // operation's result. It never faults: every outcome becomes a result.
        private async Task RunLoadAsync(OnitySceneOperation operation)
        {
            OnitySceneResult result;

            try
            {
                result = await RunLoadRouteAsync(operation);
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                result = new OnitySceneResult(
                    OnitySceneResultStatus.Disposed,
                    operation.Id,
                    operation.Target,
                    k_disposedMessage);
            }
            catch (Exception exception)
            {
                // The scene the change left still shows: lift the cover over it.
                await RevealAfterFailureAsync(operation);
                result = new OnitySceneResult(
                    OnitySceneResultStatus.LoadFailed,
                    operation.Id,
                    operation.Target,
                    exception.Message,
                    exception);
            }

            Finish(operation, result);
        }

        // Owner and stop points: as RunLoadAsync.
        private async Task RunUnloadAsync(OnitySceneOperation operation, Scene scene)
        {
            OnitySceneResult result;

            try
            {
                Publish(operation, OnitySceneOperationPhase.Unloading, 0f);
                await m_backend.UnloadAsync(scene, operation.Token);
                result = new OnitySceneResult(OnitySceneResultStatus.Succeeded, operation.Id, scene);
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                result = new OnitySceneResult(OnitySceneResultStatus.Disposed, operation.Id, scene, k_disposedMessage);
            }
            catch (Exception exception)
            {
                result = new OnitySceneResult(
                    OnitySceneResultStatus.LoadFailed,
                    operation.Id,
                    scene,
                    exception.Message,
                    exception);
            }

            Finish(operation, result);
        }

        private async Task<OnitySceneResult> RunLoadRouteAsync(OnitySceneOperation operation)
        {
            OnitySceneRequest request = operation.Request;
            await ShowCoverAsync(operation);

            if (request.PreferPreparedScene && m_preloader != null)
            {
                Publish(operation, OnitySceneOperationPhase.Loading, 0f);

                // The prepared scene becomes the active scene and the scene it replaces unloads.
                if (await m_preloader.TryStartAsync(request.Scene, request.EnterData, operation.Token))
                {
                    return await RevealTargetAsync(operation, SceneManager.GetActiveScene());
                }
            }

            if (operation.LoadingKey != null)
            {
                return await LoadThroughLoadingSceneAsync(operation);
            }

            Publish(operation, OnitySceneOperationPhase.Loading, 0f);
            IOnitySceneLoad load = await m_backend.LoadAsync(
                operation.TargetKey,
                request.Mode,
                false,
                operation.OnTargetCreated,
                operation.ReportTargetProgress,
                operation.Token);

            if (request.Mode == LoadSceneMode.Additive && request.SetActive)
            {
                SceneManager.SetActiveScene(load.Scene);
            }

            return await RevealTargetAsync(operation, load.Scene);
        }

        // The Loading scene shows the target's progress; this operation still owns the target and waits for it.
        private async Task<OnitySceneResult> LoadThroughLoadingSceneAsync(OnitySceneOperation operation)
        {
            Publish(operation, OnitySceneOperationPhase.Loading, 0f);
            IOnitySceneLoad loading = await m_backend.LoadAsync(
                operation.LoadingKey,
                LoadSceneMode.Single,
                false,
                operation.OnLoadingSceneCreated,
                null,
                operation.Token);

            // The Loading scene's own readiness only decides when it may show.
            await OnitySceneTargetReadiness.WaitAsync(loading.Scene, m_revealPolicy, operation.Token);
            await WaitForHandoffAsync(operation, loading.Scene);
            await HideCoverAsync(operation);
            operation.MarkLoadingSceneRevealed();

            await operation.TargetActivated.AsOnityTask().AttachExternalCancellation(operation.Token);
            return await RevealTargetAsync(operation, operation.Target);
        }

        private async Task<OnitySceneResult> RevealTargetAsync(OnitySceneOperation operation, Scene target)
        {
            operation.Target = target;
            Publish(operation, OnitySceneOperationPhase.Initializing, 0f);
            OnitySceneReadiness readiness =
                await OnitySceneTargetReadiness.WaitAsync(target, m_revealPolicy, operation.Token);

            // A failed or timed-out scene still lifts its cover; only the result tells them apart.
            await HideCoverAsync(operation);
            return ToResult(operation, target, readiness);
        }

        private OnitySceneResult ToResult(OnitySceneOperation operation, Scene target, OnitySceneReadiness readiness)
        {
            switch (readiness.Outcome)
            {
                case OnitySceneReadinessOutcome.Ready:
                    return new OnitySceneResult(OnitySceneResultStatus.Succeeded, operation.Id, target);

                case OnitySceneReadinessOutcome.TimedOut:
                    return new OnitySceneResult(
                        OnitySceneResultStatus.ReadinessTimedOut,
                        operation.Id,
                        target,
                        $"Scene '{operation.Label}' was not ready within {m_revealPolicy.MaxWaitSeconds} seconds.");

                case OnitySceneReadinessOutcome.Unloaded:
                    return new OnitySceneResult(
                        OnitySceneResultStatus.ReadinessFailed,
                        operation.Id,
                        target,
                        $"Scene '{operation.Label}' unloaded before it was ready.");

                default:
                    return new OnitySceneResult(
                        OnitySceneResultStatus.ReadinessFailed,
                        operation.Id,
                        target,
                        readiness.Exception == null
                            ? $"Scene '{operation.Label}' lost its context before it was ready."
                            : $"Scene '{operation.Label}' failed to build: {readiness.Exception.Message}",
                        readiness.Exception);
            }
        }

        private static async Task WaitForHandoffAsync(OnitySceneOperation operation, Scene loadingScene)
        {
            for (int frame = 0; operation.IsHandoffTaken == false && frame < k_handoffFrames; frame++)
            {
                await OnityTask.NextFrame(operation.Token);
            }

            if (operation.IsHandoffTaken == false)
            {
                throw new InvalidOperationException(
                    $"Loading scene '{loadingScene.name}' did not take the scene change; it needs an "
                    + $"{nameof(OnityLoadingSceneInitiator)}.");
            }
        }

        private async Task ShowCoverAsync(OnitySceneOperation operation)
        {
            if (operation.Cover == null || operation.IsCoverShown)
            {
                return;
            }

            Publish(operation, OnitySceneOperationPhase.Covering, 0f);

            // Marked before the await, so a failure while it shows still lifts it.
            operation.IsCoverShown = true;
            await operation.Cover.ShowAsync(operation.Token);
        }

        private async Task HideCoverAsync(OnitySceneOperation operation)
        {
            if (operation.Cover == null || operation.IsCoverShown == false)
            {
                return;
            }

            Publish(operation, OnitySceneOperationPhase.Revealing, 0f);
            operation.IsCoverShown = false;
            await operation.Cover.HideAsync(operation.Token);
        }

        private async Task RevealAfterFailureAsync(OnitySceneOperation operation)
        {
            if (operation.Token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await HideCoverAsync(operation);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
