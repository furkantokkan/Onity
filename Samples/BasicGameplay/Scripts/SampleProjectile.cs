using Onity.Pooling;
using UnityEngine;

namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// Pooled projectile sample component.
    /// </summary>
    public sealed class SampleProjectile : MonoBehaviour, IPoolHooks
    {
        [Header("Projectile Defaults")]
        [Tooltip("Default projectile speed.")]
        [SerializeField] private float m_defaultSpeed = 12f;

        [Tooltip("Default projectile lifetime in seconds.")]
        [SerializeField] private float m_defaultLifetime = 2f;

        private IPool<SampleProjectile> m_ownerPool;
        private Vector3 m_direction;
        private float m_speed;
        private float m_remainingLifetime;
        private bool m_isActive;

        private void OnDisable()
        {
            m_isActive = false;
        }

        private void Update()
        {
            if (m_isActive == false)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            transform.position += m_direction * (m_speed * deltaTime);
            m_remainingLifetime -= deltaTime;

            if (m_remainingLifetime > 0f)
            {
                return;
            }

            IPool<SampleProjectile> ownerPool = m_ownerPool;
            m_ownerPool = null;
            m_isActive = false;
            ownerPool?.Release(this);
        }

        /// <summary>
        /// Configures and launches the projectile instance.
        /// </summary>
        /// <param name="direction">World direction vector.</param>
        /// <param name="speed">Override speed.</param>
        /// <param name="lifetime">Override lifetime.</param>
        /// <param name="ownerPool">Pool used for release.</param>
        public void Launch(Vector3 direction, float speed, float lifetime, IPool<SampleProjectile> ownerPool)
        {
            m_ownerPool = ownerPool;
            m_direction = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
            m_speed = speed > 0f ? speed : m_defaultSpeed;
            m_remainingLifetime = lifetime > 0f ? lifetime : m_defaultLifetime;
            m_isActive = true;
        }

        /// <inheritdoc />
        public void OnPoolGet()
        {
        }

        /// <inheritdoc />
        public void OnPoolRelease()
        {
            m_ownerPool = null;
            m_isActive = false;
        }
    }
}
