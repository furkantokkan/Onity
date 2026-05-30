namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Stopwatch timer that accumulates elapsed time while running.
    /// </summary>
    public sealed class OnityStopwatchTimer : OnityTimer
    {
        private float m_currentTimeSeconds;

        /// <summary>
        /// Initializes a stopwatch timer.
        /// </summary>
        /// <param name="useUnscaledTime">Use unscaled time in automatic update mode.</param>
        /// <param name="autoUpdate">Register timer to automatic update runner.</param>
        public OnityStopwatchTimer(bool useUnscaledTime = false, bool autoUpdate = true)
            : base(useUnscaledTime, autoUpdate)
        {
            m_currentTimeSeconds = 0f;
        }

        /// <inheritdoc />
        public override bool IsFinished => false;

        /// <inheritdoc />
        public override float CurrentTimeSeconds => m_currentTimeSeconds;

        /// <inheritdoc />
        public override float Progress01 => 0f;

        /// <inheritdoc />
        public override void Reset()
        {
            EnsureNotDisposed();
            m_currentTimeSeconds = 0f;
        }

        /// <inheritdoc />
        protected override void TickCore(float deltaTimeSeconds)
        {
            m_currentTimeSeconds += deltaTimeSeconds;
        }
    }
}
