using System;
using UnityEngine;

namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Countdown timer that completes when remaining time reaches zero.
    /// </summary>
    public sealed class OnityCountdownTimer : OnityTimer
    {
        private float m_durationSeconds;
        private float m_currentTimeSeconds;

        /// <summary>
        /// Initializes a countdown timer.
        /// </summary>
        /// <param name="durationSeconds">Countdown duration in seconds.</param>
        /// <param name="useUnscaledTime">Use unscaled time in automatic update mode.</param>
        /// <param name="autoUpdate">Register timer to automatic update runner.</param>
        public OnityCountdownTimer(
            float durationSeconds,
            bool useUnscaledTime = false,
            bool autoUpdate = true)
            : base(useUnscaledTime, autoUpdate)
        {
            ValidateDuration(durationSeconds);
            m_durationSeconds = durationSeconds;
            m_currentTimeSeconds = durationSeconds;
        }

        /// <summary>
        /// Current countdown duration in seconds.
        /// </summary>
        public float DurationSeconds => m_durationSeconds;

        /// <inheritdoc />
        public override bool IsFinished => m_currentTimeSeconds <= 0f;

        /// <inheritdoc />
        public override float CurrentTimeSeconds => m_currentTimeSeconds;

        /// <inheritdoc />
        public override float Progress01
        {
            get
            {
                if (m_durationSeconds <= 0f)
                {
                    return 1f;
                }

                return Mathf.Clamp01(1f - (m_currentTimeSeconds / m_durationSeconds));
            }
        }

        /// <inheritdoc />
        public override void Reset()
        {
            EnsureNotDisposed();
            m_currentTimeSeconds = m_durationSeconds;
        }

        /// <summary>
        /// Resets timer with a new duration.
        /// </summary>
        /// <param name="durationSeconds">Countdown duration in seconds.</param>
        public void Reset(float durationSeconds)
        {
            EnsureNotDisposed();
            ValidateDuration(durationSeconds);
            m_durationSeconds = durationSeconds;
            Reset();
        }

        /// <inheritdoc />
        protected override void TickCore(float deltaTimeSeconds)
        {
            m_currentTimeSeconds -= deltaTimeSeconds;

            if (m_currentTimeSeconds < 0f)
            {
                m_currentTimeSeconds = 0f;
            }
        }

        private static void ValidateDuration(float durationSeconds)
        {
            if (durationSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            }
        }
    }
}
