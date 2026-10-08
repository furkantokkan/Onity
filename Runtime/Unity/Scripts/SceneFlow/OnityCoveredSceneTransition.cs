using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Runs one scene change at a time, optionally behind a screen cover: the cover shows, the change runs, the
    /// active scene is waited on until it is ready, and the cover hides, so the load and the new scene's build frames
    /// never show.
    /// </summary>
    /// <remarks>
    /// Owner: a scope that outlives every scene change, usually the <c>ProjectContext</c> (bind it
    /// <c>AsSingle</c>), because a change unloads the scene that asks for it. Stop points: each change ends after
    /// its cover hides; <see cref="Dispose" /> cancels the lifetime token that the cover, the change and the
    /// readiness wait observe. The cover and the scene change are independent: compose them, for example with an
    /// <see cref="OnityScenePreloader" /> start, inside the change delegate.
    /// </remarks>
    public sealed class OnityCoveredSceneTransition : IDisposable
    {
        private static readonly Task<bool> s_notStarted = Task.FromResult(false);

        private readonly IOnitySceneCover m_defaultCover;
        private readonly IOnitySceneReadiness m_readiness;
        private readonly CancellationTokenSource m_lifetime = new CancellationTokenSource();
        private bool m_isRunning;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a transition.
        /// </summary>
        /// <param name="defaultCover">The cover used when a change asks for the default cover.</param>
        /// <param name="readiness">Waits until the scene a change leaves active may be shown.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public OnityCoveredSceneTransition(IOnitySceneCover defaultCover, IOnitySceneReadiness readiness)
        {
            m_defaultCover = defaultCover ?? throw new ArgumentNullException(nameof(defaultCover));
            m_readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        }

        /// <summary>
        /// Gets the cover used when <see cref="RunAsync(Func{CancellationToken, Task}, bool, CancellationToken)" />
        /// asks for one.
        /// </summary>
        public IOnitySceneCover DefaultCover => m_defaultCover;

        /// <summary>
        /// Gets whether a change runs, from its start until its cover has hidden.
        /// </summary>
        public bool IsRunning => m_isRunning;

        /// <summary>
        /// Runs <paramref name="sceneChange" />, behind the default cover when <paramref name="showCover" /> is true.
        /// See <see cref="RunAsync(Func{CancellationToken, Task}, IOnitySceneCover, CancellationToken)" />.
        /// </summary>
        /// <param name="sceneChange">The scene change; it receives this transition's lifetime token.</param>
        /// <param name="showCover">True to show the default cover; false to run the change with no cover.</param>
        /// <param name="cancellationToken">Ends the caller's wait only, never a started change.</param>
        /// <returns>True when the change ran; false when it was ignored.</returns>
        public Task<bool> RunAsync(
            Func<CancellationToken, Task> sceneChange,
            bool showCover,
            CancellationToken cancellationToken)
        {
            return RunAsync(sceneChange, showCover ? m_defaultCover : null, cancellationToken);
        }

        /// <summary>
        /// Runs <paramref name="sceneChange" /> behind <paramref name="cover" />: the cover shows, the change runs,
        /// the active scene is waited on until it may show, and the cover hides. With a null cover the same flow runs
        /// with no cover.
        /// </summary>
        /// <param name="sceneChange">
        /// The scene change. It receives this transition's lifetime token, not
        /// <paramref name="cancellationToken" />, because the scene that asks is usually the one the change unloads.
        /// </param>
        /// <param name="cover">The cover for this change, or null for no cover.</param>
        /// <param name="cancellationToken">
        /// Ends the caller's wait only: the returned task is canceled while a started change, its readiness wait and
        /// its cover hide still complete.
        /// </param>
        /// <returns>
        /// True once a started change ended and its cover hid. False at once, with nothing run, when another change
        /// is running or the transition is disposed. A change that throws fails the task with its exception after the
        /// cover has hidden over the old scene again.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="sceneChange" /> is null.</exception>
        public Task<bool> RunAsync(
            Func<CancellationToken, Task> sceneChange,
            IOnitySceneCover cover,
            CancellationToken cancellationToken)
        {
            if (sceneChange == null)
            {
                throw new ArgumentNullException(nameof(sceneChange));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<bool>(cancellationToken);
            }

            if (m_isDisposed || m_isRunning)
            {
                return s_notStarted;
            }

            m_isRunning = true;
            Task<bool> run = RunOwnedAsync(sceneChange, cover, m_lifetime.Token);

            if (cancellationToken.CanBeCanceled == false)
            {
                return run;
            }

            return run.AsOnityTask().AttachExternalCancellation(cancellationToken).AsTask();
        }

        /// <summary>
        /// Cancels the lifetime token that a running change, its cover and its readiness wait observe, and ignores
        /// every later request.
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
        }

        // Owner: this transition; the returned task reports it to the caller. Stops: the cover hid, a failed change
        // after its cover hid again, or the lifetime token.
        private async Task<bool> RunOwnedAsync(
            Func<CancellationToken, Task> sceneChange,
            IOnitySceneCover cover,
            CancellationToken token)
        {
            try
            {
                if (cover != null)
                {
                    await cover.ShowAsync(token);
                }

                try
                {
                    Task change = sceneChange(token)
                        ?? throw new InvalidOperationException("The scene change returned no task.");
                    await change;
                }
                catch (Exception) when (token.IsCancellationRequested == false)
                {
                    // The old scene still shows: reveal it so its caller can offer the action again.
                    if (cover != null)
                    {
                        await cover.HideAsync(token);
                    }

                    throw;
                }

                await m_readiness.WaitUntilReadyAsync(token);

                if (cover != null)
                {
                    await cover.HideAsync(token);
                }

                return true;
            }
            finally
            {
                m_isRunning = false;
            }
        }
    }
}
