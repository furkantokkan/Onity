using System;
using Onity.DI;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Binds the scene service on a scope, usually the <c>ProjectContext</c>.
    /// </summary>
    public static class OnitySceneServiceBindingExtensions
    {
        /// <summary>
        /// Creates one <see cref="OnitySceneService" /> and binds it as <see cref="IOnitySceneService" /> and as
        /// itself. The scope owns it and disposes it when it ends; scene scopes resolve the same instance from it.
        /// Call it once, from a <c>ProjectContext</c> installer, because scene changes outlive every scene scope.
        /// </summary>
        /// <param name="container">The <c>ProjectContext</c> container.</param>
        /// <param name="profile">
        /// Routes Single requests through its Loading scene when it asks to; null loads every scene directly.
        /// </param>
        /// <param name="defaultCover">The cover of requests that ask for the default; null for none.</param>
        /// <param name="preloader">
        /// The preloader the game prepares scenes with, so requests that prefer a prepared scene can start it; null
        /// loads those scenes normally. The preloader stays caller-owned.
        /// </param>
        /// <param name="revealPolicy">When a loaded scene may show; null uses the default policy.</param>
        /// <returns>The bound service.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="container" /> is null.</exception>
        public static OnitySceneService BindSceneService(
            this OnityContainer container,
            OnitySceneFlowProfile profile = null,
            IOnitySceneCover defaultCover = null,
            OnityScenePreloader preloader = null,
            OnitySceneRevealPolicy revealPolicy = null)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            OnitySceneService service = new OnitySceneService(profile, defaultCover, preloader, revealPolicy);

            // Register the disposal before binding, so a bind that fails cannot leave the service behind.
            service.AddTo(container);
            container.BindInstance<IOnitySceneService>(service);
            container.BindInstance(service);
            return service;
        }
    }
}
