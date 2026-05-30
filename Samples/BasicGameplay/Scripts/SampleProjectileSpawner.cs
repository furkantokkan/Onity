using Onity.DI;
using Onity.Factory;
using Onity.Pooling;
using UnityEngine;

namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// Spawns projectiles through Onity factory and pool integrations.
    /// </summary>
    public sealed class SampleProjectileSpawner : MonoBehaviour
    {
        [Header("Spawn Setup")]
        [Tooltip("Optional spawn origin. Uses this transform if null.")]
        [SerializeField] private Transform m_spawnPoint;

        [Tooltip("Launch speed used for spawned projectiles.")]
        [SerializeField] private float m_launchSpeed = 14f;

        [Tooltip("Lifetime used for spawned projectiles.")]
        [SerializeField] private float m_lifetimeSeconds = 2.2f;

        [Inject]
        private IFactory<SampleProjectile> m_projectileFactory = null;

        [Inject]
        private IPool<SampleProjectile> m_projectilePool = null;

        /// <summary>
        /// Spawns one projectile.
        /// </summary>
        public void SpawnProjectile()
        {
            SampleProjectile projectile = m_projectileFactory.Create();
            Transform spawnTransform = m_spawnPoint != null ? m_spawnPoint : transform;

            projectile.transform.SetPositionAndRotation(spawnTransform.position, spawnTransform.rotation);
            projectile.Launch(spawnTransform.forward, m_launchSpeed, m_lifetimeSeconds, m_projectilePool);
        }
    }
}
