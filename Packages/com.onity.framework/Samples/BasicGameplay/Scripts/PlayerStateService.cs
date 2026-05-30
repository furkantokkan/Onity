using System;
using Onity.Messaging;
using Onity.Reactive;

namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// Player state service that reacts to damage messages.
    /// </summary>
    public sealed class PlayerStateService : IPlayerStateService, IDisposable
    {
        private const int k_defaultHealth = 100;

        private readonly ReactiveProperty<int> m_health;
        private readonly IDisposable m_damageSubscription;
        private readonly int m_startHealth;

        /// <summary>
        /// Initializes player state service.
        /// </summary>
        /// <param name="damageSubscriber">Damage message subscriber.</param>
        public PlayerStateService(ISubscriber<PlayerDamagedMessage> damageSubscriber)
        {
            m_startHealth = k_defaultHealth;
            m_health = new ReactiveProperty<int>(m_startHealth);
            m_damageSubscription = damageSubscriber.Subscribe(OnPlayerDamaged);
        }

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<int> Health => m_health;

        /// <inheritdoc />
        public void ResetHealth()
        {
            m_health.Value = m_startHealth;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_damageSubscription.Dispose();
            m_health.Dispose();
        }

        private void OnPlayerDamaged(PlayerDamagedMessage message)
        {
            if (message.Amount <= 0)
            {
                return;
            }

            int nextHealth = m_health.Value - message.Amount;

            if (nextHealth < 0)
            {
                nextHealth = 0;
            }

            m_health.Value = nextHealth;
        }
    }
}
