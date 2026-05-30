using System;
using Onity.Messaging;
using Onity.Reactive;

namespace OnityShowcase
{
    /// <summary>
    /// Reactive countdown service. Exposes remaining time as <see cref="ReactiveProperty{T}"/>
    /// state (Reactive pillar), is fed elapsed time through <see cref="Tick"/> by its host, and
    /// publishes a single <see cref="GameOverMessage"/> on the broker when it reaches zero
    /// (Events pillar). It depends only on injected abstractions (DI pillar) and on no Unity type,
    /// so a unit test can advance it deterministically and assert the game-over publish.
    /// </summary>
    public sealed class CountdownService : ICountdownService, IDisposable
    {
        private readonly IPublisher<GameOverMessage> m_gameOverPublisher;
        private readonly IScoreService m_scoreService;
        private readonly float m_roundDuration;
        private readonly ReactiveProperty<float> m_timeRemaining;
        private readonly ReactiveProperty<bool> m_isFinished;
        private readonly IOnityObservable<bool> m_lowTimeWarning;
        private bool m_isDisposed;

        private const float k_lowTimeThresholdSeconds = 5f;

        /// <summary>
        /// Initializes the countdown service from settings and broker bindings.
        /// </summary>
        /// <param name="settings">Round tuning values.</param>
        /// <param name="gameOverPublisher">Publisher for the game-over channel.</param>
        /// <param name="scoreService">Score service, queried for the final score on game-over.</param>
        public CountdownService(
            ShowcaseSettings settings,
            IPublisher<GameOverMessage> gameOverPublisher,
            IScoreService scoreService)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            m_gameOverPublisher = gameOverPublisher ?? throw new ArgumentNullException(nameof(gameOverPublisher));
            m_scoreService = scoreService ?? throw new ArgumentNullException(nameof(scoreService));
            m_roundDuration = settings.RoundDurationSeconds > 0f ? settings.RoundDurationSeconds : 1f;
            m_timeRemaining = new ReactiveProperty<float>(m_roundDuration);
            m_isFinished = new ReactiveProperty<bool>(false);

            // Reactive operator pipeline over the time stream: map to "is low" then collapse
            // consecutive duplicates so subscribers only see the warning toggle on/off.
            m_lowTimeWarning = m_timeRemaining
                .Select(seconds => seconds <= k_lowTimeThresholdSeconds)
                .DistinctUntilChanged();
        }

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<float> TimeRemaining => m_timeRemaining;

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<bool> IsFinished => m_isFinished;

        /// <inheritdoc />
        public IOnityObservable<bool> LowTimeWarning => m_lowTimeWarning;

        /// <inheritdoc />
        public void Tick(float deltaSeconds)
        {
            if (m_isFinished.Value || deltaSeconds <= 0f)
            {
                return;
            }

            float next = m_timeRemaining.Value - deltaSeconds;

            if (next <= 0f)
            {
                m_timeRemaining.Value = 0f;
                m_isFinished.Value = true;
                m_gameOverPublisher.Publish(new GameOverMessage(m_scoreService.Score.Value));
                return;
            }

            m_timeRemaining.Value = next;
        }

        /// <inheritdoc />
        public void Restart()
        {
            m_isFinished.Value = false;
            m_timeRemaining.Value = m_roundDuration;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_timeRemaining.Dispose();
            m_isFinished.Dispose();
        }
    }
}
