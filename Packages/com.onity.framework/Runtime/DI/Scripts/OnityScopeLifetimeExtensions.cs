using System;
using System.Threading;

namespace Onity.DI
{
    /// <summary>
    /// Ties disposables to the lifetime of a DI scope.
    /// </summary>
    public static class OnityScopeLifetimeExtensions
    {
        private static readonly Action<object> s_disposeState = DisposeState;

        /// <summary>
        /// Disposes <paramref name="disposable" /> when <paramref name="scope" /> ends. The scope
        /// cancels its <see cref="IOnityScopeLifetime.Token" /> before it disposes its child scopes
        /// and owned instances, so the disposable is released first. A scope that has already ended
        /// disposes it immediately.
        /// </summary>
        /// <typeparam name="TDisposable">Disposable type, kept for fluent chaining.</typeparam>
        /// <param name="disposable">Disposable to release with the scope.</param>
        /// <param name="scope">Owning scope, for example an injected <see cref="IOnityScopeLifetime" />
        /// or an <see cref="OnityContainer" />.</param>
        /// <returns>The same disposable.</returns>
        /// <remarks>
        /// The scope keeps the disposable until it ends, so dispose short-lived subscriptions yourself.
        /// An exception thrown by the disposable surfaces from the scope's <c>Dispose</c>. A scope
        /// whose token can never be canceled never disposes it.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="disposable" /> or
        /// <paramref name="scope" /> is null.</exception>
        public static TDisposable AddTo<TDisposable>(this TDisposable disposable, IOnityScopeLifetime scope)
            where TDisposable : IDisposable
        {
            if (disposable == null)
            {
                throw new ArgumentNullException(nameof(disposable));
            }

            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            CancellationToken token = scope.Token;

            if (scope.IsDisposed || token.IsCancellationRequested)
            {
                disposable.Dispose();
                return disposable;
            }

            RegisterWithoutContext(token, s_disposeState, disposable);
            return disposable;
        }

        // Registers without capturing the caller's ExecutionContext: the callback runs when the scope
        // ends, which is unrelated to the context that happened to register it.
        internal static CancellationTokenRegistration RegisterWithoutContext(
            CancellationToken token,
            Action<object> callback,
            object state)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return token.Register(callback, state, false);
            }

            AsyncFlowControl flow = ExecutionContext.SuppressFlow();

            try
            {
                return token.Register(callback, state, false);
            }
            finally
            {
                flow.Undo();
            }
        }

        private static void DisposeState(object state)
        {
            ((IDisposable)state).Dispose();
        }
    }
}
