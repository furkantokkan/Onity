using Onity.Messaging;
using Onity.Pooling;
#if ONITY_ENTITIES
using Onity.DOTS;
#endif
using UnityEngine;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// Pooled pickup component for Roll-a-Ball sample.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class RollABallPickup : MonoBehaviour, IPoolHooks
    {
        private const string k_playerTag = "Player";

        private IPublisher<RollABallPickupCollectedMessage> m_pickupPublisher;
        private IPool<RollABallPickup> m_ownerPool;
        private int m_pickupId;
        private int m_points;
        private float m_rotationSpeed;
        private bool m_isCollected;

        /// <summary>
        /// Current unique pickup identifier.
        /// </summary>
        public int PickupId => m_pickupId;

        private void Awake()
        {
            Collider collider = GetComponent<Collider>();
            collider.isTrigger = true;
        }

        private void Update()
        {
            if (m_isCollected)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            transform.Rotate(0f, m_rotationSpeed * deltaTime, 0f, Space.World);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_isCollected)
            {
                return;
            }

            if (other.CompareTag(k_playerTag) == false)
            {
                return;
            }

            m_isCollected = true;
            m_pickupPublisher?.Publish(new RollABallPickupCollectedMessage(m_pickupId, m_points));

#if ONITY_ENTITIES
            OnityDotsIntEventBridge.TryPublish(m_points);
#endif

            m_ownerPool?.Release(this);
        }

        /// <summary>
        /// Configures pickup dependencies and runtime values.
        /// </summary>
        /// <param name="pickupId">Unique pickup id.</param>
        /// <param name="points">Score points on collection.</param>
        /// <param name="rotationSpeed">Visual spin speed.</param>
        /// <param name="pickupPublisher">Pickup event publisher.</param>
        /// <param name="ownerPool">Owner pool for release.</param>
        public void Configure(
            int pickupId,
            int points,
            float rotationSpeed,
            IPublisher<RollABallPickupCollectedMessage> pickupPublisher,
            IPool<RollABallPickup> ownerPool)
        {
            m_pickupId = pickupId;
            m_points = points;
            m_rotationSpeed = rotationSpeed;
            m_pickupPublisher = pickupPublisher;
            m_ownerPool = ownerPool;
            m_isCollected = false;
        }

        /// <inheritdoc />
        public void OnPoolGet()
        {
            m_isCollected = false;
        }

        /// <inheritdoc />
        public void OnPoolRelease()
        {
            m_isCollected = true;
            m_pickupPublisher = null;
            m_ownerPool = null;
        }
    }
}
