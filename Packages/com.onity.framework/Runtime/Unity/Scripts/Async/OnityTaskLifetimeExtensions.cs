using System;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Destroy-bound cancellation helpers for Unity objects. Every member requires Unity's main
    /// thread. A MonoBehaviour uses Unity's native <c>destroyCancellationToken</c>; other
    /// components and GameObjects share one lazily added hidden trigger component per GameObject.
    /// </summary>
    public static class OnityTaskLifetimeExtensions
    {
        private static readonly Action<object> s_cancelSource = CancelSource;

        /// <summary>
        /// Gets a token canceled when the MonoBehaviour is destroyed. Main thread only.
        /// </summary>
        /// <param name="monoBehaviour">Owner behaviour.</param>
        /// <returns>Unity's native destroy token; already canceled for a destroyed owner.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="monoBehaviour" /> is null.</exception>
        public static CancellationToken GetCancellationTokenOnDestroy(this MonoBehaviour monoBehaviour)
        {
            if (ReferenceEquals(monoBehaviour, null))
            {
                throw new ArgumentNullException(nameof(monoBehaviour));
            }

            if (monoBehaviour == null)
            {
                return new CancellationToken(true);
            }

            return monoBehaviour.destroyCancellationToken;
        }

        /// <summary>
        /// Gets a token canceled when the GameObject is destroyed. Main thread only. The token is
        /// cached by one hidden trigger component, so repeated calls return the same token.
        /// </summary>
        /// <param name="gameObject">Owner GameObject.</param>
        /// <returns>Destroy token; already canceled for a destroyed owner.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject" /> is null.</exception>
        public static CancellationToken GetCancellationTokenOnDestroy(this GameObject gameObject)
        {
            if (ReferenceEquals(gameObject, null))
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            if (gameObject == null)
            {
                return new CancellationToken(true);
            }

            return OnityAsyncDestroyTrigger.GetOrAdd(gameObject).CancellationToken;
        }

        /// <summary>
        /// Gets a token canceled when the component's GameObject is destroyed. Main thread only.
        /// A MonoBehaviour uses its native destroy token.
        /// </summary>
        /// <param name="component">Owner component.</param>
        /// <returns>Destroy token; already canceled for a destroyed owner.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component" /> is null.</exception>
        public static CancellationToken GetCancellationTokenOnDestroy(this Component component)
        {
            if (ReferenceEquals(component, null))
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (component == null)
            {
                return new CancellationToken(true);
            }

            if (component is MonoBehaviour monoBehaviour)
            {
                return monoBehaviour.destroyCancellationToken;
            }

            return OnityAsyncDestroyTrigger.GetOrAdd(component.gameObject).CancellationToken;
        }

        /// <summary>
        /// Cancels the source when the component is destroyed. Main thread only. The source is
        /// canceled immediately when the owner is already destroyed; a source disposed before the
        /// destroy is ignored (deviation from UniTask, which throws from OnDestroy). The destroy
        /// trigger of a non-MonoBehaviour owner exists only in Play mode, so the registration is
        /// not raised on destroy in Edit mode.
        /// </summary>
        /// <param name="cancellationTokenSource">Source to cancel.</param>
        /// <param name="component">Owner component.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void RegisterRaiseCancelOnDestroy(
            this CancellationTokenSource cancellationTokenSource, Component component)
        {
            Register(cancellationTokenSource, component, nameof(component));
        }

        /// <summary>
        /// Cancels the source when the GameObject is destroyed. Main thread only. The source is
        /// canceled immediately when the owner is already destroyed; a source disposed before the
        /// destroy is ignored. The destroy trigger exists only in Play mode, so the registration is
        /// not raised on destroy in Edit mode.
        /// </summary>
        /// <param name="cancellationTokenSource">Source to cancel.</param>
        /// <param name="gameObject">Owner GameObject.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void RegisterRaiseCancelOnDestroy(
            this CancellationTokenSource cancellationTokenSource, GameObject gameObject)
        {
            Register(cancellationTokenSource, gameObject, nameof(gameObject));
        }

        private static void Register(
            CancellationTokenSource cancellationTokenSource, UnityEngine.Object owner, string ownerName)
        {
            if (cancellationTokenSource == null)
            {
                throw new ArgumentNullException(nameof(cancellationTokenSource));
            }

            if (ReferenceEquals(owner, null))
            {
                throw new ArgumentNullException(ownerName);
            }

            CancellationToken token = owner is GameObject gameObject
                ? gameObject.GetCancellationTokenOnDestroy()
                : ((Component)owner).GetCancellationTokenOnDestroy();

            // Register without capturing ExecutionContext (as UniTask does); an already-canceled
            // token runs the callback inline.
            if (ExecutionContext.IsFlowSuppressed())
            {
                token.Register(s_cancelSource, cancellationTokenSource);
                return;
            }

            using (ExecutionContext.SuppressFlow())
            {
                token.Register(s_cancelSource, cancellationTokenSource);
            }
        }

        private static void CancelSource(object state)
        {
            try
            {
                ((CancellationTokenSource)state).Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The owner disposed the source first; there is nothing left to cancel.
            }
        }
    }
}
