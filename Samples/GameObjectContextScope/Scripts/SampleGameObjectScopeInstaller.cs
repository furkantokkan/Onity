using Onity.DI;
using Onity.Unity.Installers;

namespace Onity.Samples.GameObjectContextScope
{
    /// <summary>
    /// Installer that registers a per-GameObjectContext scoped counter service.
    /// </summary>
    public sealed class SampleGameObjectScopeInstaller : MonoInstaller
    {
        /// <inheritdoc />
        public override void InstallBindings(OnityContainer container)
        {
            container.Bind<ScopedCounterService>().AsSingle();
        }
    }
}
