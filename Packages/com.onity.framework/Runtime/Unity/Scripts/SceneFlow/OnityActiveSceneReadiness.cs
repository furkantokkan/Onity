using System.Threading;
using System.Threading.Tasks;
using Onity.DI;
using Onity.Unity.Async;
using Onity.Unity.Contexts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Waits, once per frame, until <see cref="OnitySceneRevealPolicy" /> lets the active scene show. The scene's
    /// <see cref="OnityContext.ReadyTask" /> covers its whole build, including asynchronous build callbacks; the
    /// policy adds settle frames after it and a maximum wait.
    /// </summary>
    /// <remarks>
    /// Owner: the caller that awaits it, usually <see cref="OnityCoveredSceneTransition" />. Stops: the policy's
    /// answer or the token. A scene without a <see cref="SceneContext" /> counts as ready, and a context whose build
    /// fails or is destroyed completes its ready task too, so a failed scene still shows instead of staying covered.
    /// </remarks>
    public sealed class OnityActiveSceneReadiness : IOnitySceneReadiness
    {
        private readonly OnitySceneRevealPolicy m_policy;

        /// <summary>
        /// Initializes a readiness wait with the default <see cref="OnitySceneRevealPolicy" />. The container uses
        /// this constructor.
        /// </summary>
        [Inject]
        public OnityActiveSceneReadiness()
            : this(new OnitySceneRevealPolicy())
        {
        }

        /// <summary>
        /// Initializes a readiness wait.
        /// </summary>
        /// <param name="policy">The reveal policy; null uses the default policy.</param>
        public OnityActiveSceneReadiness(OnitySceneRevealPolicy policy)
        {
            m_policy = policy ?? new OnitySceneRevealPolicy();
        }

        /// <summary>
        /// Gets the reveal policy.
        /// </summary>
        public OnitySceneRevealPolicy Policy => m_policy;

        /// <inheritdoc />
        public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task ready = OnitySceneScopes.TryFind(SceneManager.GetActiveScene(), out SceneContext scope)
                ? scope.ReadyTask
                : Task.CompletedTask;
            float startTime = Time.realtimeSinceStartup;
            int framesSinceReady = 0;

            while (true)
            {
                bool isSceneReady = ready.IsCompleted;
                float secondsWaited = Time.realtimeSinceStartup - startTime;

                if (m_policy.ShouldReveal(isSceneReady, framesSinceReady, secondsWaited))
                {
                    return;
                }

                await OnityTask.NextFrame(cancellationToken);

                if (isSceneReady)
                {
                    framesSinceReady++;
                }
            }
        }
    }
}
