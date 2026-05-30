using Onity.DI;
using UnityEngine;

namespace OnityShowcase
{
    /// <summary>
    /// Minimal installer base for this showcase. The full Onity package ships
    /// <c>Onity.Unity.Installers.MonoInstaller</c> with the same shape, but the Example Game
    /// embeds only the engine-free cores (com.onity.di / com.onity.reactive / com.onity.messaging),
    /// so the sample owns this tiny equivalent. An installer declares bindings; it does not
    /// resolve or build — the <see cref="OnityShowcaseContext"/> drives that lifecycle.
    /// </summary>
    public abstract class OnityMonoInstaller : MonoBehaviour
    {
        /// <summary>
        /// Registers this installer's bindings into the container before it is built.
        /// </summary>
        /// <param name="container">Container to register bindings into.</param>
        public abstract void InstallBindings(OnityContainer container);
    }
}
