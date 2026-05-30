using Onity.Factory;
using Onity.Pooling;

namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// Factory implementation that retrieves projectiles from a pool.
    /// </summary>
    public sealed class SampleProjectileFactory : IFactory<SampleProjectile>
    {
        private readonly IPool<SampleProjectile> m_projectilePool;

        /// <summary>
        /// Initializes the projectile factory.
        /// </summary>
        /// <param name="projectilePool">Projectile pool.</param>
        public SampleProjectileFactory(IPool<SampleProjectile> projectilePool)
        {
            m_projectilePool = projectilePool;
        }

        /// <inheritdoc />
        public SampleProjectile Create()
        {
            return m_projectilePool.Get();
        }
    }
}
