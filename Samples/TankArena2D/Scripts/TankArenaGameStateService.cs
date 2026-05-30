using System;
using Onity.Messaging;
using Onity.Reactive;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Reactive game state service driven by Tank Arena message channels.
    /// </summary>
    public sealed class TankArenaGameStateService : ITankArenaGameStateService, IDisposable
    {
        private readonly ReactiveProperty<int> m_score;
        private readonly ReactiveProperty<int> m_health;
        private readonly ReactiveProperty<int> m_activeEnemyCount;
        private readonly ReactiveProperty<int> m_currentWave;
        private readonly ReactiveProperty<bool> m_isGameOver;
        private readonly CompositeDisposable m_disposables;
        private readonly int m_startHealth;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes game state subscriptions.
        /// </summary>
        /// <param name="settings">Sample settings.</param>
        /// <param name="enemySpawnedSubscriber">Enemy spawn channel subscriber.</param>
        /// <param name="enemyDestroyedSubscriber">Enemy destroy channel subscriber.</param>
        /// <param name="playerDamagedSubscriber">Player damage channel subscriber.</param>
        /// <param name="waveStartedSubscriber">Wave start channel subscriber.</param>
        /// <param name="restartRequestedSubscriber">Restart request channel subscriber.</param>
        public TankArenaGameStateService(
            TankArenaGameSettings settings,
            ISubscriber<TankArenaEnemySpawnedMessage> enemySpawnedSubscriber,
            ISubscriber<TankArenaEnemyDestroyedMessage> enemyDestroyedSubscriber,
            ISubscriber<TankArenaPlayerDamagedMessage> playerDamagedSubscriber,
            ISubscriber<TankArenaWaveStartedMessage> waveStartedSubscriber,
            ISubscriber<TankArenaRestartRequestedMessage> restartRequestedSubscriber)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            m_startHealth = settings.PlayerStartHealth;
            m_score = new ReactiveProperty<int>(0);
            m_health = new ReactiveProperty<int>(m_startHealth);
            m_activeEnemyCount = new ReactiveProperty<int>(0);
            m_currentWave = new ReactiveProperty<int>(0);
            m_isGameOver = new ReactiveProperty<bool>(false);
            m_disposables = new CompositeDisposable();
            m_isDisposed = false;

            enemySpawnedSubscriber.Subscribe(OnEnemySpawned).AddTo(m_disposables);
            enemyDestroyedSubscriber.Subscribe(OnEnemyDestroyed).AddTo(m_disposables);
            playerDamagedSubscriber.Subscribe(OnPlayerDamaged).AddTo(m_disposables);
            waveStartedSubscriber.Subscribe(OnWaveStarted).AddTo(m_disposables);
            restartRequestedSubscriber.Subscribe(_ => Reset()).AddTo(m_disposables);
        }

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<int> Score => m_score;

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<int> Health => m_health;

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<int> ActiveEnemyCount => m_activeEnemyCount;

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<int> CurrentWave => m_currentWave;

        /// <inheritdoc />
        public IReadOnlyReactiveProperty<bool> IsGameOver => m_isGameOver;

        /// <summary>
        /// Resets gameplay state values to defaults.
        /// </summary>
        public void Reset()
        {
            ThrowIfDisposed();
            m_score.Value = 0;
            m_health.Value = m_startHealth;
            m_activeEnemyCount.Value = 0;
            m_currentWave.Value = 0;
            m_isGameOver.Value = false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_disposables.Dispose();
            m_score.Dispose();
            m_health.Dispose();
            m_activeEnemyCount.Dispose();
            m_currentWave.Dispose();
            m_isGameOver.Dispose();
        }

        private void OnEnemySpawned(TankArenaEnemySpawnedMessage message)
        {
            if (m_isGameOver.Value)
            {
                return;
            }

            m_activeEnemyCount.Value = m_activeEnemyCount.Value + 1;
        }

        private void OnEnemyDestroyed(TankArenaEnemyDestroyedMessage message)
        {
            if (m_activeEnemyCount.Value > 0)
            {
                m_activeEnemyCount.Value = m_activeEnemyCount.Value - 1;
            }

            if (m_isGameOver.Value)
            {
                return;
            }

            int clampedScoreValue = message.ScoreValue > 0 ? message.ScoreValue : 1;
            m_score.Value = m_score.Value + clampedScoreValue;
        }

        private void OnPlayerDamaged(TankArenaPlayerDamagedMessage message)
        {
            if (m_isGameOver.Value || message.Damage <= 0)
            {
                return;
            }

            int nextHealth = m_health.Value - message.Damage;

            if (nextHealth < 0)
            {
                nextHealth = 0;
            }

            m_health.Value = nextHealth;

            if (nextHealth <= 0)
            {
                m_isGameOver.Value = true;
            }
        }

        private void OnWaveStarted(TankArenaWaveStartedMessage message)
        {
            if (message.WaveIndex <= 0)
            {
                return;
            }

            m_currentWave.Value = message.WaveIndex;
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(nameof(TankArenaGameStateService));
            }
        }
    }
}
