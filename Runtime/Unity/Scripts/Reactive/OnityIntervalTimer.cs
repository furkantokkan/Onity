using System;

namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Repeating interval timer that emits interval events while running.
    /// </summary>
    public sealed class OnityIntervalTimer : OnityTimer
    {
        private float m_intervalSeconds;
        private float m_elapsedSinceLastIntervalSeconds;
        private float m_totalElapsedSeconds;
        private int m_tickCount;

        /// <summary>
        /// Invoked for each elapsed interval and provides cumulative tick count.
        /// </summary>
        public event Action<int> IntervalElapsed;

        /// <summary>
        /// Initializes a repeating interval timer.
        /// </summary>
        /// <param name="intervalSeconds">Interval duration in seconds.</param>
        /// <param name="useUnscaledTime">Use unscaled time in automatic update mode.</param>
        /// <param name="autoUpdate">Register timer to automatic update runner.</param>
        public OnityIntervalTimer(
            float intervalSeconds,
            bool useUnscaledTime = false,
            bool autoUpdate = true)
            : base(useUnscaledTime, autoUpdate)
        {
            ValidateInterval(intervalSeconds);
            m_intervalSeconds = intervalSeconds;
            m_elapsedSinceLastIntervalSeconds = 0f;
            m_totalElapsedSeconds = 0f;
            m_tickCount = 0;
        }

        /// <summary>
        /// Interval duration in seconds.
        /// </summary>
        public float IntervalSeconds => m_intervalSeconds;

        /// <summary>
        /// Total number of emitted interval ticks.
        /// </summary>
        public int TickCount => m_tickCount;

        /// <inheritdoc />
        public override bool IsFinished => false;

        /// <inheritdoc />
        public override float CurrentTimeSeconds => m_totalElapsedSeconds;

        /// <inheritdoc />
        public override float Progress01 => 0f;

        /// <inheritdoc />
        public override void Reset()
        {
            EnsureNotDisposed();
            m_elapsedSinceLastIntervalSeconds = 0f;
            m_totalElapsedSeconds = 0f;
            m_tickCount = 0;
        }

        /// <summary>
        /// Resets timer with a new interval.
        /// </summary>
        /// <param name="intervalSeconds">Interval duration in seconds.</param>
        public void Reset(float intervalSeconds)
        {
            EnsureNotDisposed();
            ValidateInterval(intervalSeconds);
            m_intervalSeconds = intervalSeconds;
            Reset();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();
            IntervalElapsed = null;
        }

        /// <inheritdoc />
        protected override void TickCore(float deltaTimeSeconds)
        {
            m_elapsedSinceLastIntervalSeconds += deltaTimeSeconds;
            m_totalElapsedSeconds += deltaTimeSeconds;

            while (m_elapsedSinceLastIntervalSeconds >= m_intervalSeconds)
            {
                m_elapsedSinceLastIntervalSeconds -= m_intervalSeconds;
                m_tickCount++;
                IntervalElapsed?.Invoke(m_tickCount);
            }
        }

        private static void ValidateInterval(float intervalSeconds)
        {
            if (intervalSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            }
        }
    }
}
