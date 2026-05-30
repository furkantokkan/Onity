using System;
using Onity.Messaging;
using Onity.Reactive;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// Reactive score state that updates from pickup collection messages.
    /// </summary>
    public sealed class RollABallScoreService : IRollABallScoreService, IDisposable
    {
        private readonly ReactiveProperty<int> m_score;
        private readonly ReactiveProperty<int> m_collectedCount;
        private readonly IDisposable m_pickupCollectedSubscription;

        /// <summary>
        /// Initializes score service and subscribes to pickup collection messages.
        /// </summary>
        /// <param name="pickupSubscriber">Pickup message subscriber.</param>
        public RollABallScoreService(ISubscriber<RollABallPickupCollectedMessage> pickupSubscriber)
        {
            m_score = new ReactiveProperty<int>(0);
            m_collectedCount = new ReactiveProperty<int>(0);
            m_pickupCollectedSubscription = pickupSubscriber.Subscribe(OnPickupCollected);
        }

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<int> Score => m_score;

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<int> CollectedCount => m_collectedCount;

        /// <inheritdoc />
        public void Reset()
        {
            m_score.Value = 0;
            m_collectedCount.Value = 0;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_pickupCollectedSubscription.Dispose();
            m_score.Dispose();
            m_collectedCount.Dispose();
        }

        private void OnPickupCollected(RollABallPickupCollectedMessage message)
        {
            if (message.Points <= 0)
            {
                return;
            }

            m_score.Value += message.Points;
            m_collectedCount.Value++;
        }
    }
}
