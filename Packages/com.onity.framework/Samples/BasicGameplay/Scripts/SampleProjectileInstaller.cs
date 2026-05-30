using Onity.DI;
using Onity.Unity.Installers;
using UnityEngine;

namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// Registers prefab pooling and factory services for projectiles.
    /// </summary>
    public sealed class SampleProjectileInstaller : MonoInstaller
    {
        [Header("Pool Setup")]
        [Tooltip("Projectile prefab used by the pool.")]
        [SerializeField] private SampleProjectile m_projectilePrefab;

        [Tooltip("Optional parent transform for pooled instances.")]
        [SerializeField] private Transform m_poolRoot;

        [Tooltip("Initial capacity for the projectile pool.")]
        [SerializeField] private int m_defaultCapacity = 16;

        [Tooltip("Maximum capacity for the projectile pool.")]
        [SerializeField] private int m_maxSize = 128;

        /// <inheritdoc />
        public override void InstallBindings(OnityContainer container)
        {
            if (m_projectilePrefab == null)
            {
                Debug.LogError("SampleProjectileInstaller requires a projectile prefab.", this);
                return;
            }

            container.BindPooledFactory(
                m_projectilePrefab,
                m_poolRoot,
                m_defaultCapacity,
                m_maxSize);
        }
    }
}
