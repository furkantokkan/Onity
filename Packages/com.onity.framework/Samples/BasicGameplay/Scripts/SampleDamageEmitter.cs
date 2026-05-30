using Onity.DI;
#if ONITY_ENTITIES
using Onity.DOTS;
#endif
using Onity.Messaging;
using UnityEngine;

namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// Publishes damage messages to demonstrate message bus usage.
    /// </summary>
    public sealed class SampleDamageEmitter : MonoBehaviour
    {
        [Header("Damage Setup")]
        [Tooltip("Damage amount published per button action.")]
        [SerializeField] private int m_damageAmount = 10;

        [Inject]
        private IPublisher<PlayerDamagedMessage> m_damagePublisher = null;

        /// <summary>
        /// Publishes one damage message.
        /// </summary>
        public void PublishDamage()
        {
            if (m_damageAmount <= 0)
            {
                return;
            }

            m_damagePublisher.Publish(new PlayerDamagedMessage(m_damageAmount));

#if ONITY_ENTITIES
            OnityDotsIntEventBridge.TryPublish(m_damageAmount);
#endif
        }
    }
}
