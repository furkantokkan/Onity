using System;
using Onity.Messaging;
using UnityEngine;

namespace OnityShowcase
{
    /// <summary>
    /// Thin view for a single coin. It owns no game logic: when clicked it forwards a
    /// <see cref="CoinCollectedMessage"/> to the injected publisher and removes itself. Scoring is
    /// decided by <see cref="ScoreService"/> listening on the same channel — the coin neither
    /// knows nor references the score. Created at runtime by <see cref="CoinSpawnerBehaviour"/>,
    /// so its dependencies are passed via <see cref="Configure"/> rather than container injection.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class CoinBehaviour : MonoBehaviour
    {
        private IPublisher<CoinCollectedMessage> m_collectedPublisher;
        private int m_scoreValue;
        private bool m_isCollected;

        /// <summary>
        /// Supplies the coin with its publish channel and score value at spawn time.
        /// </summary>
        /// <param name="collectedPublisher">Publisher for the coin-collected channel.</param>
        /// <param name="scoreValue">Score value this coin awards when collected.</param>
        public void Configure(IPublisher<CoinCollectedMessage> collectedPublisher, int scoreValue)
        {
            m_collectedPublisher = collectedPublisher ?? throw new ArgumentNullException(nameof(collectedPublisher));
            m_scoreValue = scoreValue;
        }

        /// <summary>
        /// Collects the coin on click and publishes the collected event.
        /// </summary>
        private void OnMouseDown()
        {
            Collect();
        }

        private void Collect()
        {
            if (m_isCollected || m_collectedPublisher == null)
            {
                return;
            }

            m_isCollected = true;
            m_collectedPublisher.Publish(new CoinCollectedMessage(m_scoreValue));
            Destroy(gameObject);
        }
    }
}
