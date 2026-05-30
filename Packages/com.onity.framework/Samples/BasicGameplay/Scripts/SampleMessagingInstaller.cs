using Onity.DI;
using Onity.Unity.Installers;
using Onity.Unity.Messaging;

namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// Registers messaging and state services for the basic gameplay sample.
    /// </summary>
    public sealed class SampleMessagingInstaller : MonoInstaller
    {
        /// <inheritdoc />
        public override void InstallBindings(OnityContainer container)
        {
            container.BindMessageChannel<PlayerDamagedMessage>();
            container.BindInterfacesAndSelfTo<PlayerStateService>().AsSingle();
        }
    }
}
