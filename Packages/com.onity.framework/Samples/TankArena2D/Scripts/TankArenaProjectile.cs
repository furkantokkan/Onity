using Onity.Pooling;
using Onity.Unity.Physics;
using UnityEngine;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Pooled projectile that uses non-alloc overlap checks for hit detection.
    /// </summary>
    public sealed class TankArenaProjectile : MonoBehaviour, IPoolHooks
    {
        [Header("Defaults")]
        [Tooltip("Fallback speed used when launch speed is not provided.")]
        [SerializeField] private float m_defaultSpeed = 14f;

        [Tooltip("Fallback lifetime used when launch lifetime is not provided.")]
        [SerializeField] private float m_defaultLifetimeSeconds = 2.8f;

        [Tooltip("Fallback hit radius used when launch radius is not provided.")]
        [SerializeField] private float m_defaultHitRadius = 0.45f;

        [Tooltip("Maximum hit colliders checked per frame.")]
        [SerializeField] private int m_maxHitBufferSize = 8;

        private Collider[] m_hitBuffer;
        private IPool<TankArenaProjectile> m_ownerPool;
        private Transform m_ownerTransform;
        private Vector3 m_direction;
        private int m_damage;
        private int m_targetMask;
        private float m_speed;
        private float m_hitRadius;
        private float m_remainingLifetimeSeconds;
        private bool m_isFromPlayer;
        private bool m_isActive;

        private void Awake()
        {
            int bufferSize = Mathf.Max(2, m_maxHitBufferSize);
            m_hitBuffer = new Collider[bufferSize];
            m_isActive = false;
        }

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
            m_remainingLifetimeSeconds -= deltaTime;

            if (ProbeHits())
            {
                return;
            }

            if (m_remainingLifetimeSeconds <= 0f)
            {
                ReleaseToPool();
            }
        }

        /// <summary>
        /// Configures and launches the projectile.
        /// </summary>
        /// <param name="ownerTransform">Owner transform ignored by hit checks.</param>
        /// <param name="ownerPool">Pool used for release.</param>
        /// <param name="position">Spawn position.</param>
        /// <param name="direction">Launch direction.</param>
        /// <param name="isFromPlayer">True when fired by player.</param>
        /// <param name="targetMask">Layer mask used for overlap checks.</param>
        /// <param name="speed">Launch speed.</param>
        /// <param name="lifetimeSeconds">Projectile lifetime in seconds.</param>
        /// <param name="damage">Hit damage.</param>
        /// <param name="hitRadius">Overlap hit radius.</param>
        public void Launch(
            Transform ownerTransform,
            IPool<TankArenaProjectile> ownerPool,
            Vector3 position,
            Vector3 direction,
            bool isFromPlayer,
            int targetMask,
            float speed,
            float lifetimeSeconds,
            int damage,
            float hitRadius)
        {
            m_ownerTransform = ownerTransform;
            m_ownerPool = ownerPool;
            m_isFromPlayer = isFromPlayer;
            m_targetMask = targetMask;
            m_speed = speed > 0f ? speed : m_defaultSpeed;
            m_remainingLifetimeSeconds = lifetimeSeconds > 0f ? lifetimeSeconds : m_defaultLifetimeSeconds;
            m_damage = damage > 0 ? damage : 1;
            m_hitRadius = hitRadius > 0f ? hitRadius : m_defaultHitRadius;
            m_direction = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
            m_isActive = true;
            transform.position = position;
            transform.forward = m_direction;
        }

        /// <inheritdoc />
        public void OnPoolGet()
        {
            m_isActive = false;
        }

        /// <inheritdoc />
        public void OnPoolRelease()
        {
            m_ownerTransform = null;
            m_ownerPool = null;
            m_isActive = false;
            m_isFromPlayer = false;
            m_targetMask = 0;
            m_speed = 0f;
            m_remainingLifetimeSeconds = 0f;
            m_damage = 0;
            m_hitRadius = 0f;
            m_direction = Vector3.zero;
        }

        private bool ProbeHits()
        {
            int hitCount = OnityNonAllocPhysics.OverlapSphere(
                transform.position,
                m_hitRadius,
                m_hitBuffer,
                m_targetMask,
                QueryTriggerInteraction.Ignore);

            if (hitCount <= 0)
            {
                return false;
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = m_hitBuffer[i];

                if (hitCollider == null)
                {
                    continue;
                }

                Transform hitTransform = hitCollider.transform;

                if (m_ownerTransform != null && hitTransform.IsChildOf(m_ownerTransform))
                {
                    continue;
                }

                if (m_isFromPlayer)
                {
                    if (hitCollider.TryGetComponent(out TankArenaEnemyController enemy))
                    {
                        enemy.ApplyDamage(m_damage);
                        ReleaseToPool();
                        return true;
                    }

                    continue;
                }

                if (hitCollider.TryGetComponent(out TankArenaPlayerController player))
                {
                    player.ApplyDamage(m_damage);
                    ReleaseToPool();
                    return true;
                }
            }

            return false;
        }

        private void ReleaseToPool()
        {
            if (m_isActive == false)
            {
                return;
            }

            IPool<TankArenaProjectile> ownerPool = m_ownerPool;
            m_ownerPool = null;
            m_isActive = false;
            ownerPool?.Release(this);
        }
    }
}
