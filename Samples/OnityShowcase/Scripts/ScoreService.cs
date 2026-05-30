using System;
using Onity.Messaging;
using Onity.Reactive;

namespace OnityShowcase
{
    /// <summary>
    /// Reactive score service. Listens to <see cref="CoinCollectedMessage"/> on the broker
    /// (Events pillar) and accumulates the awarded value into a <see cref="ReactiveProperty{T}"/>
    /// (Reactive pillar). It is plain, engine-free, and constructor-injected (DI pillar),
    /// so it can be unit-tested with a bare <c>MessageBroker</c> and no Unity scene.
    /// </summary>
    public sealed class ScoreService : IScoreService, IDisposable
    {
        private readonly ReactiveProperty<int> m_score;
        private readonly IDisposable m_subscription;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes the score service and subscribes to the coin-collected channel.
        /// </summary>
        /// <param name="coinCollected">Subscriber for the coin-collected message channel.</param>
        public ScoreService(ISubscriber<CoinCollectedMessage> coinCollected)
        {
            if (coinCollected == null)
            {
                throw new ArgumentNullException(nameof(coinCollected));
            }

            m_score = new ReactiveProperty<int>(0);
            m_subscription = coinCollected.Subscribe(OnCoinCollected);
        }

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<int> Score => m_score;

        /// <inheritdoc />
        public void Reset()
        {
            m_score.Value = 0;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_subscription.Dispose();
            m_score.Dispose();
        }

        private void OnCoinCollected(CoinCollectedMessage message)
        {
            int awarded = message.ScoreValue > 0 ? message.ScoreValue : 1;
            m_score.Value = m_score.Value + awarded;
        }
    }
}
