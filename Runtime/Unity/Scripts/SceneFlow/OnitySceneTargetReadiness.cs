using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using Onity.Unity.Contexts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// How the wait for one exact scene ended.
    /// </summary>
    internal enum OnitySceneReadinessOutcome
    {
        /// <summary>
        /// The scene's <see cref="SceneContext" /> built, or the scene has none.
        /// </summary>
        Ready = 0,

        /// <summary>
        /// The build failed or the context was destroyed before it was ready.
        /// </summary>
        Failed = 1,

        /// <summary>
        /// The reveal policy's maximum wait passed first.
        /// </summary>
        TimedOut = 2,

        /// <summary>
        /// The scene unloaded first.
        /// </summary>
        Unloaded = 3
    }

    /// <summary>
    /// The outcome of <see cref="OnitySceneTargetReadiness.WaitAsync" />.
    /// </summary>
    internal readonly struct OnitySceneReadiness
    {
        public OnitySceneReadiness(OnitySceneReadinessOutcome outcome, Exception exception)
        {
            Outcome = outcome;
            Exception = exception;
        }

        public OnitySceneReadinessOutcome Outcome { get; }

        public Exception Exception { get; }
    }

    /// <summary>
    /// Waits for one exact scene, never whichever scene happens to be active, and tells a successful build apart
    /// from the moment the policy merely lets the scene show.
    /// </summary>
    internal static class OnitySceneTargetReadiness
    {
        // Owner: the operation that awaits it. Stops: the reveal policy's answer, an unloaded scene, or the token.
        public static async Task<OnitySceneReadiness> WaitAsync(
            Scene scene,
            OnitySceneRevealPolicy policy,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A scene without a SceneContext is ready as soon as it loaded.
            Task ready = OnitySceneScopes.TryFind(scene, out SceneContext context)
                ? context.ReadyTask
                : Task.CompletedTask;
            float startTime = Time.realtimeSinceStartup;
            int framesSinceReady = 0;

            while (true)
            {
                if (scene.IsValid() == false || scene.isLoaded == false)
                {
                    return new OnitySceneReadiness(OnitySceneReadinessOutcome.Unloaded, null);
                }

                bool isSceneReady = ready.IsCompleted;

                if (policy.ShouldReveal(isSceneReady, framesSinceReady, Time.realtimeSinceStartup - startTime))
                {
                    break;
                }

                await OnityTask.NextFrame(cancellationToken);

                if (isSceneReady)
                {
                    framesSinceReady++;
                }
            }

            if (ready.IsCompleted == false)
            {
                return new OnitySceneReadiness(OnitySceneReadinessOutcome.TimedOut, null);
            }

            if (ready.IsFaulted)
            {
                // Reading the exception observes the faulted build task.
                AggregateException failure = ready.Exception;
                return new OnitySceneReadiness(
                    OnitySceneReadinessOutcome.Failed,
                    failure?.InnerException ?? failure);
            }

            return ready.IsCanceled
                ? new OnitySceneReadiness(OnitySceneReadinessOutcome.Failed, null)
                : new OnitySceneReadiness(OnitySceneReadinessOutcome.Ready, null);
        }
    }
}
