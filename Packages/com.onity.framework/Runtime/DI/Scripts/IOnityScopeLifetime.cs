using System.Threading;

namespace Onity.DI
{
    /// <summary>
    /// The lifetime of one DI scope. <see cref="OnityContainer" /> implements it, and every Unity
    /// context binds it to its own container, so a service that injects
    /// <see cref="IOnityScopeLifetime" /> receives the scope that owns it.
    /// </summary>
    /// <remarks>
    /// <see cref="Token" /> is canceled when the scope is disposed, before the scope disposes its
    /// child scopes and owned instances. Pass it to async work that must stop with the scope, or
    /// tie an <see cref="System.IDisposable" /> to the scope with
    /// <see cref="OnityScopeLifetimeExtensions.AddTo{TDisposable}" />.
    /// </remarks>
    public interface IOnityScopeLifetime
    {
        /// <summary>
        /// Gets a token that is canceled when the scope is disposed. A disposed scope returns a
        /// canceled token.
        /// </summary>
        CancellationToken Token { get; }

        /// <summary>
        /// Gets whether the scope has been disposed.
        /// </summary>
        bool IsDisposed { get; }
    }
}
