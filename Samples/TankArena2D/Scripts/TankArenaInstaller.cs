using Onity.DI;
using Onity.Unity.Async;
using Onity.Unity.Installers;
using Onity.Unity.Messaging;
using UnityEngine;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Registers Tank Arena sample bindings and startup workflow.
    /// </summary>
    public sealed class TankArenaInstaller : MonoInstaller
    {
        [Header("Config")]
        [Tooltip("Gameplay settings used by this sample.")]
        [SerializeField] private TankArenaGameSettings m_settings;

        [Header("Scene References")]
        [Tooltip("Spawner controller started after async build callbacks.")]
        [SerializeField] private TankArenaEnemySpawner m_enemySpawner;

        [Tooltip("Player controller instance bound as context singleton.")]
        [SerializeField] private TankArenaPlayerController m_playerController;

        [Header("Pool Setup")]
        [Tooltip("Enemy prefab used by pooled factory binding.")]
        [SerializeField] private TankArenaEnemyController m_enemyPrefab;

        [Tooltip("Projectile prefab used by pooled factory binding.")]
        [SerializeField] private TankArenaProjectile m_projectilePrefab;

        [Tooltip("Optional parent transform for enemy pooled instances.")]
        [SerializeField] private Transform m_enemyPoolRoot;

        [Tooltip("Optional parent transform for projectile pooled instances.")]
        [SerializeField] private Transform m_projectilePoolRoot;

        [Tooltip("Initial pooled enemy capacity.")]
        [SerializeField] private int m_enemyPoolCapacity = 20;

        [Tooltip("Maximum pooled enemy capacity.")]
        [SerializeField] private int m_enemyPoolMaxSize = 128;

        [Tooltip("Initial pooled projectile capacity.")]
        [SerializeField] private int m_projectilePoolCapacity = 64;

        [Tooltip("Maximum pooled projectile capacity.")]
        [SerializeField] private int m_projectilePoolMaxSize = 512;

        /// <inheritdoc />
        public override void InstallBindings(OnityContainer container)
        {
            if (m_settings == null)
            {
                Debug.LogError("TankArenaInstaller requires a TankArenaGameSettings asset.", this);
                return;
            }

            if (m_enemySpawner == null)
            {
                Debug.LogError("TankArenaInstaller requires a TankArenaEnemySpawner reference.", this);
                return;
            }

            if (m_playerController == null)
            {
                Debug.LogError("TankArenaInstaller requires a TankArenaPlayerController reference.", this);
                return;
            }

            if (m_enemyPrefab == null)
            {
                Debug.LogError("TankArenaInstaller requires an enemy prefab.", this);
                return;
            }

            if (m_projectilePrefab == null)
            {
                Debug.LogError("TankArenaInstaller requires a projectile prefab.", this);
                return;
            }

            container.BindScriptableObject<TankArenaGameSettings>(m_settings);
            container.BindInstance(m_enemySpawner);
            container.BindInstance(m_playerController);

            container.BindMessageChannel<TankArenaEnemySpawnedMessage>();
            container.BindMessageChannel<TankArenaEnemyDestroyedMessage>();
            container.BindMessageChannel<TankArenaPlayerDamagedMessage>();
            container.BindMessageChannel<TankArenaWaveStartedMessage>();
            container.BindMessageChannel<TankArenaRestartRequestedMessage>();

            container.BindInterfacesAndSelfTo<TankArenaGameStateService>().AsSingle().NonLazy();
            container.Bind<TankArenaHudPresenter>().AsTransient();
            container.BindUiResolverBridge();

            container.BindPooledFactory(
                m_enemyPrefab,
                m_enemyPoolRoot,
                Mathf.Max(1, m_enemyPoolCapacity),
                Mathf.Max(8, m_enemyPoolMaxSize));
            container.BindPooledFactory(
                m_projectilePrefab,
                m_projectilePoolRoot,
                Mathf.Max(4, m_projectilePoolCapacity),
                Mathf.Max(16, m_projectilePoolMaxSize));

            container.RegisterBuildCallbackAsync(
                async (_, cancellationToken) =>
                {
                    await OnityAsync.NextFrameAsync(cancellationToken);
                    m_enemySpawner.BeginSpawning();
                });
        }
    }
}
