using System;

namespace OnityShowcase
{
    /// <summary>
    /// Engine-free coin spawn planner. Accumulates elapsed time and, once per configured
    /// interval, produces a random position inside the square play area. The random source is
    /// injected as a <see cref="Func{TResult}"/> returning values in [0,1), so tests can feed a
    /// deterministic sequence while gameplay passes a Unity-backed random. Demonstrates the DI
    /// pillar: a single-owner service resolved by constructor injection with no Unity dependency.
    /// </summary>
    public sealed class CoinSpawnService : ICoinSpawnService
    {
        private readonly float m_interval;
        private readonly float m_halfSize;
        private readonly Func<float> m_random01;
        private float m_accumulator;

        /// <summary>
        /// Initializes the spawn service from settings and a random source.
        /// </summary>
        /// <param name="settings">Round tuning values.</param>
        /// <param name="random01">Function returning a value in [0,1); seam for deterministic tests.</param>
        public CoinSpawnService(ShowcaseSettings settings, Func<float> random01)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            m_random01 = random01 ?? throw new ArgumentNullException(nameof(random01));
            m_interval = settings.SpawnIntervalSeconds > 0f ? settings.SpawnIntervalSeconds : 1f;
            m_halfSize = settings.SpawnAreaHalfSize > 0f ? settings.SpawnAreaHalfSize : 1f;
        }

        /// <inheritdoc />
        public bool TryGetNextSpawn(float deltaSeconds, out float x, out float z)
        {
            x = 0f;
            z = 0f;

            if (deltaSeconds > 0f)
            {
                m_accumulator += deltaSeconds;
            }

            if (m_accumulator < m_interval)
            {
                return false;
            }

            m_accumulator -= m_interval;
            x = ToAxis(m_random01());
            z = ToAxis(m_random01());
            return true;
        }

        private float ToAxis(float unit)
        {
            float clamped = unit < 0f ? 0f : unit > 1f ? 1f : unit;
            return (clamped * 2f - 1f) * m_halfSize;
        }
    }
}
