using Onity.DI;
using Onity.DOTS;
using Onity.Factory;
using Onity.Messaging;
using Onity.Pooling;
using UnityEngine;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Enemy tank behaviour that chases player and shoots pooled projectiles.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TankArenaEnemyController : MonoBehaviour, IPoolHooks
    {
        [Header("Scene References")]
        [Tooltip("Optional projectile spawn origin for this enemy.")]
        [SerializeField] private Transform m_shootOrigin;

        [Tooltip("Minimum distance to keep from player while chasing.")]
        [SerializeField] private float m_stopDistance = 1.35f;

        private TankArenaGameSettings m_settings;
        private IFactory<TankArenaProjectile> m_projectileFactory;
        private IPool<TankArenaProjectile> m_projectilePool;
        private IPublisher<TankArenaEnemyDestroyedMessage> m_enemyDestroyedPublisher;
        private Transform m_targetPlayer;
        private IPool<TankArenaEnemyController> m_ownerPool;
        private int m_currentHealth;
        private float m_remainingFireCooldown;
        private bool m_isActive;

        private void Update()
        {
            if (m_isActive == false || m_targetPlayer == null || m_settings == null)
            {
                return;
            }

            TickMovement();
            TickShooting();
        }

        /// <summary>
        /// Configures one enemy instance for active gameplay.
        /// </summary>
        /// <param name="targetPlayer">Player target transform.</param>
        /// <param name="ownerPool">Owner pool used for release.</param>
        public void Initialize(Transform targetPlayer, IPool<TankArenaEnemyController> ownerPool)
        {
            m_targetPlayer = targetPlayer;
            m_ownerPool = ownerPool;
            m_currentHealth = m_settings != null ? m_settings.EnemyHealth : 3;
            m_remainingFireCooldown = Random.Range(0f, 0.35f);
            m_isActive = true;
        }

        /// <summary>
        /// Applies incoming projectile damage.
        /// </summary>
        /// <param name="damage">Damage value.</param>
        public void ApplyDamage(int damage)
        {
            if (m_isActive == false || damage <= 0)
            {
                return;
            }

            m_currentHealth -= damage;

            if (m_currentHealth > 0)
            {
                return;
            }

            int scoreValue = m_settings != null ? m_settings.EnemyScoreValue : 1;
            m_enemyDestroyedPublisher?.Publish(new TankArenaEnemyDestroyedMessage(this, scoreValue));
            OnityDotsIntEventBridge.TryPublish(1);
            ReleaseToPool();
        }

        [Inject]
        private void Construct(
            TankArenaGameSettings settings,
            IFactory<TankArenaProjectile> projectileFactory,
            IPool<TankArenaProjectile> projectilePool,
            IPublisher<TankArenaEnemyDestroyedMessage> enemyDestroyedPublisher)
        {
            m_settings = settings;
            m_projectileFactory = projectileFactory;
            m_projectilePool = projectilePool;
            m_enemyDestroyedPublisher = enemyDestroyedPublisher;
        }

        /// <inheritdoc />
        public void OnPoolGet()
        {
            m_isActive = false;
            m_currentHealth = m_settings != null ? m_settings.EnemyHealth : 3;
            m_remainingFireCooldown = 0f;
        }

        /// <inheritdoc />
        public void OnPoolRelease()
        {
            m_targetPlayer = null;
            m_ownerPool = null;
            m_isActive = false;
            m_remainingFireCooldown = 0f;
            m_currentHealth = 0;
        }

        private void TickMovement()
        {
            Vector3 toPlayer = m_targetPlayer.position - transform.position;
            toPlayer.y = 0f;
            float distanceSquared = toPlayer.sqrMagnitude;

            if (distanceSquared <= 0.0001f)
            {
                return;
            }

            Vector3 direction = toPlayer.normalized;
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                m_settings.EnemyTurnSpeed * Time.deltaTime);

            if (distanceSquared <= m_stopDistance * m_stopDistance)
            {
                return;
            }

            transform.position += transform.forward * (m_settings.EnemyMoveSpeed * Time.deltaTime);
        }

        private void TickShooting()
        {
            m_remainingFireCooldown -= Time.deltaTime;

            if (m_remainingFireCooldown > 0f)
            {
                return;
            }

            if (m_projectileFactory == null || m_projectilePool == null)
            {
                return;
            }

            TankArenaProjectile projectile = m_projectileFactory.Create();

            if (projectile == null)
            {
                return;
            }

            Vector3 launchPosition =
                m_shootOrigin != null
                    ? m_shootOrigin.position
                    : transform.position + (transform.forward * 0.8f);
            Vector3 launchDirection =
                m_shootOrigin != null
                    ? m_shootOrigin.forward
                    : transform.forward;
            projectile.Launch(
                transform,
                m_projectilePool,
                launchPosition,
                launchDirection,
                false,
                m_settings.EnemyProjectileTargetMask,
                m_settings.EnemyProjectileSpeed,
                m_settings.ProjectileLifetimeSeconds,
                m_settings.ProjectileDamage,
                m_settings.ProjectileHitRadius);

            m_remainingFireCooldown = m_settings.EnemyFireCooldownSeconds;
        }

        private void ReleaseToPool()
        {
            if (m_isActive == false)
            {
                return;
            }

            IPool<TankArenaEnemyController> ownerPool = m_ownerPool;
            m_ownerPool = null;
            m_isActive = false;
            ownerPool?.Release(this);
        }
    }
}
