using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onity.DI;
using Onity.Factory;
using Onity.Messaging;
using Onity.Pooling;
using Onity.Unity.Async;
using Onity.Unity.Physics;
using UnityEngine;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Async enemy wave spawner using pooled factories and non-alloc spawn checks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TankArenaEnemySpawner : MonoBehaviour
    {
        [Header("Scene References")]
        [Tooltip("Player transform targeted by spawned enemies.")]
        [SerializeField] private Transform m_playerTransform;

        [Tooltip("Optional spawn center override. Uses this transform when set.")]
        [SerializeField] private Transform m_spawnCenter;

        [Tooltip("Optional parent transform for active enemy instances.")]
        [SerializeField] private Transform m_runtimeEnemyRoot;

        [Header("Spawn Validation")]
        [Tooltip("How many random attempts are made for one valid spawn position.")]
        [SerializeField] private int m_maxSpawnAttempts = 8;

        [Tooltip("Spawn Y offset used for enemy root position.")]
        [SerializeField] private float m_spawnHeight = 0.5f;

        private readonly List<TankArenaEnemyController> m_activeEnemies = new List<TankArenaEnemyController>(64);
        private readonly Collider[] m_spawnOverlapBuffer = new Collider[8];
        private System.Random m_spawnRandom;

        private TankArenaGameSettings m_settings;
        private ITankArenaGameStateService m_gameStateService;
        private IFactory<TankArenaEnemyController> m_enemyFactory;
        private IPool<TankArenaEnemyController> m_enemyPool;
        private IPublisher<TankArenaEnemySpawnedMessage> m_enemySpawnedPublisher;
        private IPublisher<TankArenaWaveStartedMessage> m_waveStartedPublisher;
        private ISubscriber<TankArenaEnemyDestroyedMessage> m_enemyDestroyedSubscriber;
        private ISubscriber<TankArenaRestartRequestedMessage> m_restartSubscriber;
        private TankArenaPlayerController m_playerController;
        private CancellationTokenSource m_spawnCancellationTokenSource;
        private IDisposable m_enemyDestroyedSubscription;
        private IDisposable m_restartSubscription;
        private int m_sessionSeed;
        private int m_startWaveOffset;
        private int m_enemyBonusPerWave;
        private bool m_hasSessionConfig;

        /// <summary>
        /// Raised when a new wave begins.
        /// </summary>
        public event Action<int> WaveStarted;

        private void Awake()
        {
            if (m_playerTransform == null && m_playerController != null)
            {
                m_playerTransform = m_playerController.transform;
            }
        }

        private void OnEnable()
        {
            if (m_enemyDestroyedSubscriber != null)
            {
                m_enemyDestroyedSubscription = m_enemyDestroyedSubscriber.Subscribe(OnEnemyDestroyed);
            }

            if (m_restartSubscriber != null)
            {
                m_restartSubscription = m_restartSubscriber.Subscribe(_ => ResetArena());
            }
        }

        private void OnDisable()
        {
            m_enemyDestroyedSubscription?.Dispose();
            m_enemyDestroyedSubscription = null;
            m_restartSubscription?.Dispose();
            m_restartSubscription = null;
            StopSpawning();
        }

        private void OnDestroy()
        {
            StopSpawning();
            ReleaseAllActiveEnemies();
        }

        /// <summary>
        /// Starts asynchronous wave spawning loop.
        /// </summary>
        public void BeginSpawning()
        {
            if (m_settings == null || m_enemyFactory == null || m_enemyPool == null)
            {
                return;
            }

            if (m_playerTransform == null && m_playerController != null)
            {
                m_playerTransform = m_playerController.transform;
            }

#if ONITY_ENTITIES
            if (m_hasSessionConfig == false
                && Onity.DOTS.OnityDotsSessionBridge.TryGetSessionState(
                    out int sessionSeed,
                    out int startingWave,
                    out int enemyBonusPerWave,
                    out _,
                    out _))
            {
                ApplySessionConfig(sessionSeed, startingWave, enemyBonusPerWave);
            }
#endif

            EnsureSessionRandom();
            StopSpawning();
            m_spawnCancellationTokenSource = new CancellationTokenSource();
            _ = RunSpawnLoopAsync(m_spawnCancellationTokenSource.Token);
        }

        /// <summary>
        /// Stops ongoing spawn loop.
        /// </summary>
        public void StopSpawning()
        {
            if (m_spawnCancellationTokenSource == null)
            {
                return;
            }

            m_spawnCancellationTokenSource.Cancel();
            m_spawnCancellationTokenSource.Dispose();
            m_spawnCancellationTokenSource = null;
        }

        /// <summary>
        /// Clears active enemies and restarts wave progression.
        /// </summary>
        public void ResetArena()
        {
            StopSpawning();
            ReleaseAllActiveEnemies();
            Onity.DOTS.OnityDotsIntEventBridge.TryResetAccumulator();
            m_playerController?.ResetPlayerState();
            BeginSpawning();
        }

        /// <summary>
        /// Applies deterministic session settings for wave progression and spawn randomness.
        /// </summary>
        /// <param name="sessionSeed">Session seed value.</param>
        /// <param name="startingWave">Starting wave index.</param>
        /// <param name="enemyBonusPerWave">Additional enemies added per wave.</param>
        public void ApplySessionConfig(int sessionSeed, int startingWave, int enemyBonusPerWave)
        {
            m_sessionSeed = sessionSeed;
            m_startWaveOffset = Mathf.Max(1, startingWave) - 1;
            m_enemyBonusPerWave = Mathf.Max(0, enemyBonusPerWave);
            m_hasSessionConfig = true;
            m_spawnRandom = new System.Random(m_sessionSeed);
        }

        [Inject]
        private void Construct(
            TankArenaGameSettings settings,
            ITankArenaGameStateService gameStateService,
            IFactory<TankArenaEnemyController> enemyFactory,
            IPool<TankArenaEnemyController> enemyPool,
            IPublisher<TankArenaEnemySpawnedMessage> enemySpawnedPublisher,
            IPublisher<TankArenaWaveStartedMessage> waveStartedPublisher,
            ISubscriber<TankArenaEnemyDestroyedMessage> enemyDestroyedSubscriber,
            ISubscriber<TankArenaRestartRequestedMessage> restartSubscriber,
            TankArenaPlayerController playerController)
        {
            m_settings = settings;
            m_gameStateService = gameStateService;
            m_enemyFactory = enemyFactory;
            m_enemyPool = enemyPool;
            m_enemySpawnedPublisher = enemySpawnedPublisher;
            m_waveStartedPublisher = waveStartedPublisher;
            m_enemyDestroyedSubscriber = enemyDestroyedSubscriber;
            m_restartSubscriber = restartSubscriber;
            m_playerController = playerController;

            if (m_playerTransform == null && m_playerController != null)
            {
                m_playerTransform = m_playerController.transform;
            }
        }

        private async Task RunSpawnLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await OnityAsync.DelayAsync(m_settings.FirstWaveDelaySeconds, cancellationToken: cancellationToken);

                int waveIndex = 0;

                if (m_hasSessionConfig)
                {
                    waveIndex = m_startWaveOffset;
                }

                while (cancellationToken.IsCancellationRequested == false)
                {
                    if (m_gameStateService != null && m_gameStateService.IsGameOver.Value)
                    {
                        await OnityAsync.NextFrameAsync(cancellationToken);
                        continue;
                    }

                    waveIndex++;
                    WaveStarted?.Invoke(waveIndex);
                    m_waveStartedPublisher?.Publish(new TankArenaWaveStartedMessage(waveIndex));

                    int spawnCount =
                        m_settings.InitialEnemyCount +
                        ((waveIndex - 1) * m_settings.AdditionalEnemiesPerWave) +
                        m_enemyBonusPerWave;

                    for (int i = 0; i < spawnCount; i++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        SpawnEnemy();

                        if (m_settings.EnemySpawnIntervalSeconds > 0f)
                        {
                            await OnityAsync.DelayAsync(
                                m_settings.EnemySpawnIntervalSeconds,
                                cancellationToken: cancellationToken);
                        }
                    }

                    if (m_settings.WaveIntervalSeconds > 0f)
                    {
                        await OnityAsync.DelayAsync(
                            m_settings.WaveIntervalSeconds,
                            cancellationToken: cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void SpawnEnemy()
        {
            if (m_enemyFactory == null || m_enemyPool == null)
            {
                return;
            }

            TankArenaEnemyController enemy = m_enemyFactory.Create();

            if (enemy == null)
            {
                return;
            }

            Vector3 spawnPosition = ResolveSpawnPosition();
            enemy.transform.position = spawnPosition;

            if (m_runtimeEnemyRoot != null)
            {
                enemy.transform.SetParent(m_runtimeEnemyRoot, true);
            }

            enemy.Initialize(m_playerTransform, m_enemyPool);
            m_activeEnemies.Add(enemy);
            m_enemySpawnedPublisher?.Publish(new TankArenaEnemySpawnedMessage(enemy));
        }

        private Vector3 ResolveSpawnPosition()
        {
            Vector3 center = m_spawnCenter != null ? m_spawnCenter.position : transform.position;
            Vector2 halfExtents = m_settings != null ? m_settings.ArenaHalfExtents : new Vector2(12f, 9f);
            float spawnCheckRadius = m_settings != null ? m_settings.SpawnCheckRadius : 0.8f;
            int spawnBlockMask = m_settings != null ? m_settings.SpawnBlockMask : UnityEngine.Physics.AllLayers;

            for (int attempt = 0; attempt < Mathf.Max(1, m_maxSpawnAttempts); attempt++)
            {
                float x = NextRandomRange(-halfExtents.x, halfExtents.x);
                float z = NextRandomRange(-halfExtents.y, halfExtents.y);
                Vector3 candidate = center + new Vector3(x, m_spawnHeight, z);
                Vector3 queryPosition = candidate + (Vector3.up * (spawnCheckRadius + 0.05f));
                int hitCount = OnityNonAllocPhysics.OverlapSphere(
                    queryPosition,
                    spawnCheckRadius,
                    m_spawnOverlapBuffer,
                    spawnBlockMask,
                    QueryTriggerInteraction.Ignore);

                if (hitCount == 0)
                {
                    return candidate;
                }
            }

            return center + new Vector3(0f, m_spawnHeight, 0f);
        }

        private void EnsureSessionRandom()
        {
            if (m_spawnRandom != null)
            {
                return;
            }

            if (m_hasSessionConfig == false)
            {
                m_sessionSeed = unchecked((int)DateTime.UtcNow.Ticks);
            }

            m_spawnRandom = new System.Random(m_sessionSeed);
        }

        private float NextRandomRange(float min, float max)
        {
            EnsureSessionRandom();
            double normalized = m_spawnRandom.NextDouble();
            return (float)(min + ((max - min) * normalized));
        }

        private void OnEnemyDestroyed(TankArenaEnemyDestroyedMessage message)
        {
            TankArenaEnemyController destroyedEnemy = message.Enemy;

            if (destroyedEnemy == null)
            {
                return;
            }

            for (int i = m_activeEnemies.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(m_activeEnemies[i], destroyedEnemy))
                {
                    m_activeEnemies.RemoveAt(i);
                    break;
                }
            }
        }

        private void ReleaseAllActiveEnemies()
        {
            if (m_enemyPool == null)
            {
                m_activeEnemies.Clear();
                return;
            }

            for (int i = m_activeEnemies.Count - 1; i >= 0; i--)
            {
                TankArenaEnemyController enemy = m_activeEnemies[i];

                if (enemy == null)
                {
                    continue;
                }

                m_enemyPool.Release(enemy);
            }

            m_activeEnemies.Clear();
        }
    }
}
