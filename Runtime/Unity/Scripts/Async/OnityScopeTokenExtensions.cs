using System;
using System.Threading;
using Onity.Unity.Contexts;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Scope-bound cancellation for Unity components. Every member requires Unity's main thread.
    /// </summary>
    public static class OnityScopeTokenExtensions
    {
        /// <summary>
        /// Gets the lifetime token of the scope that owns <paramref name="component" />: the nearest
        /// <see cref="OnityContext" /> on the component's GameObject or one of its parents (including
        /// inactive ones). Without such a context, the component's destroy token is returned.
        /// </summary>
        /// <param name="component">Component whose owning scope is looked up.</param>
        /// <returns>The scope's <see cref="OnityContext.LifetimeToken" />, the destroy token without
        /// a context, or a canceled token for a destroyed component.</returns>
        /// <remarks>
        /// The scope token is canceled when the context is destroyed, before the scope's services
        /// are disposed, so work started with it stops together with the services it uses. The
        /// lookup walks the transform hierarchy; cache the token instead of calling this every frame.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="component" /> is null.</exception>
        public static CancellationToken GetScopeCancellationToken(this Component component)
        {
            if (ReferenceEquals(component, null))
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (component == null)
            {
                return new CancellationToken(true);
            }

            OnityContext context = component as OnityContext;

            if (context == null)
            {
                context = component.GetComponentInParent<OnityContext>(true);
            }

            return context != null ? context.LifetimeToken : component.GetCancellationTokenOnDestroy();
        }
    }
}
