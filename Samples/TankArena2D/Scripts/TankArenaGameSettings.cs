using UnityEngine;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// ScriptableObject configuration for Tank Arena sample gameplay.
    /// </summary>
    [CreateAssetMenu(fileName = "OnityTankArenaSettings", menuName = "Onity/Samples/Tank Arena Settings")]
    public sealed class TankArenaGameSettings : ScriptableObject
    {
        [Header("Player")]
        [Tooltip("Starting health value for the player.")]
        [SerializeField] private int m_playerStartHealth = 100;

        [Tooltip("Movement speed for the player tank.")]
        [SerializeField] private float m_playerMoveSpeed = 7f;

        [Tooltip("Turn speed in degrees per second for the player tank.")]
        [SerializeField] private float m_playerTurnSpeed = 300f;

        [Tooltip("Projectile speed used by the player tank.")]
        [SerializeField] private float m_playerProjectileSpeed = 17f;

        [Tooltip("Seconds between player shots.")]
        [SerializeField] private float m_playerFireCooldownSeconds = 0.18f;

        [Header("Enemy")]
        [Tooltip("Move speed for enemy tanks.")]
        [SerializeField] private float m_enemyMoveSpeed = 4f;

        [Tooltip("Turn speed in degrees per second for enemy tanks.")]
        [SerializeField] private float m_enemyTurnSpeed = 220f;

        [Tooltip("Starting health for each enemy tank.")]
        [SerializeField] private int m_enemyHealth = 3;

        [Tooltip("Score granted when one enemy is destroyed.")]
        [SerializeField] private int m_enemyScoreValue = 10;

        [Tooltip("Projectile speed used by enemy tanks.")]
        [SerializeField] private float m_enemyProjectileSpeed = 11f;

        [Tooltip("Seconds between enemy shots.")]
        [SerializeField] private float m_enemyFireCooldownSeconds = 1.15f;

        [Header("Projectile")]
        [Tooltip("Lifetime of spawned projectiles in seconds.")]
        [SerializeField] private float m_projectileLifetimeSeconds = 3f;

        [Tooltip("Hit radius used by non-alloc overlap checks.")]
        [SerializeField] private float m_projectileHitRadius = 0.45f;

        [Tooltip("Damage done by one projectile hit.")]
        [SerializeField] private int m_projectileDamage = 1;

        [Tooltip("Layer mask hit by player projectiles.")]
        [SerializeField] private LayerMask m_playerProjectileTargetMask = ~0;

        [Tooltip("Layer mask hit by enemy projectiles.")]
        [SerializeField] private LayerMask m_enemyProjectileTargetMask = ~0;

        [Header("Waves")]
        [Tooltip("Delay before the first wave starts.")]
        [SerializeField] private float m_firstWaveDelaySeconds = 0.45f;

        [Tooltip("Pause after one wave is fully spawned.")]
        [SerializeField] private float m_waveIntervalSeconds = 2.2f;

        [Tooltip("Delay between individual enemy spawns.")]
        [SerializeField] private float m_enemySpawnIntervalSeconds = 0.22f;

        [Tooltip("Enemy count in wave 1.")]
        [SerializeField] private int m_initialEnemyCount = 3;

        [Tooltip("Additional enemy count added per wave.")]
        [SerializeField] private int m_additionalEnemiesPerWave = 1;

        [Header("Arena")]
        [Tooltip("Half size of the rectangular spawn/play area.")]
        [SerializeField] private Vector2 m_arenaHalfExtents = new Vector2(16f, 11f);

        [Tooltip("Radius used for spawn overlap validation.")]
        [SerializeField] private float m_spawnCheckRadius = 0.8f;

        [Tooltip("Layer mask that blocks enemy spawn points.")]
        [SerializeField] private LayerMask m_spawnBlockMask = ~0;

        /// <summary>
        /// Starting player health.
        /// </summary>
        public int PlayerStartHealth => Mathf.Max(1, m_playerStartHealth);

        /// <summary>
        /// Player move speed.
        /// </summary>
        public float PlayerMoveSpeed => Mathf.Max(0.1f, m_playerMoveSpeed);

        /// <summary>
        /// Player turn speed.
        /// </summary>
        public float PlayerTurnSpeed => Mathf.Max(1f, m_playerTurnSpeed);

        /// <summary>
        /// Player projectile speed.
        /// </summary>
        public float PlayerProjectileSpeed => Mathf.Max(0.1f, m_playerProjectileSpeed);

        /// <summary>
        /// Player fire cooldown in seconds.
        /// </summary>
        public float PlayerFireCooldownSeconds => Mathf.Max(0.02f, m_playerFireCooldownSeconds);

        /// <summary>
        /// Enemy move speed.
        /// </summary>
        public float EnemyMoveSpeed => Mathf.Max(0.1f, m_enemyMoveSpeed);

        /// <summary>
        /// Enemy turn speed.
        /// </summary>
        public float EnemyTurnSpeed => Mathf.Max(1f, m_enemyTurnSpeed);

        /// <summary>
        /// Enemy health value.
        /// </summary>
        public int EnemyHealth => Mathf.Max(1, m_enemyHealth);

        /// <summary>
        /// Score value for enemy kill.
        /// </summary>
        public int EnemyScoreValue => Mathf.Max(1, m_enemyScoreValue);

        /// <summary>
        /// Enemy projectile speed.
        /// </summary>
        public float EnemyProjectileSpeed => Mathf.Max(0.1f, m_enemyProjectileSpeed);

        /// <summary>
        /// Enemy fire cooldown.
        /// </summary>
        public float EnemyFireCooldownSeconds => Mathf.Max(0.1f, m_enemyFireCooldownSeconds);

        /// <summary>
        /// Projectile lifetime.
        /// </summary>
        public float ProjectileLifetimeSeconds => Mathf.Max(0.1f, m_projectileLifetimeSeconds);

        /// <summary>
        /// Projectile hit radius.
        /// </summary>
        public float ProjectileHitRadius => Mathf.Max(0.05f, m_projectileHitRadius);

        /// <summary>
        /// Projectile hit damage.
        /// </summary>
        public int ProjectileDamage => Mathf.Max(1, m_projectileDamage);

        /// <summary>
        /// Player projectile target mask.
        /// </summary>
        public LayerMask PlayerProjectileTargetMask => m_playerProjectileTargetMask;

        /// <summary>
        /// Enemy projectile target mask.
        /// </summary>
        public LayerMask EnemyProjectileTargetMask => m_enemyProjectileTargetMask;

        /// <summary>
        /// First wave delay in seconds.
        /// </summary>
        public float FirstWaveDelaySeconds => Mathf.Max(0f, m_firstWaveDelaySeconds);

        /// <summary>
        /// Delay after each wave spawn pass.
        /// </summary>
        public float WaveIntervalSeconds => Mathf.Max(0f, m_waveIntervalSeconds);

        /// <summary>
        /// Delay between enemy spawns.
        /// </summary>
        public float EnemySpawnIntervalSeconds => Mathf.Max(0f, m_enemySpawnIntervalSeconds);

        /// <summary>
        /// Initial enemy count.
        /// </summary>
        public int InitialEnemyCount => Mathf.Max(1, m_initialEnemyCount);

        /// <summary>
        /// Additional enemies per wave.
        /// </summary>
        public int AdditionalEnemiesPerWave => Mathf.Max(0, m_additionalEnemiesPerWave);

        /// <summary>
        /// Arena half extents.
        /// </summary>
        public Vector2 ArenaHalfExtents => new Vector2(
            Mathf.Max(2f, m_arenaHalfExtents.x),
            Mathf.Max(2f, m_arenaHalfExtents.y));

        /// <summary>
        /// Spawn overlap check radius.
        /// </summary>
        public float SpawnCheckRadius => Mathf.Max(0.1f, m_spawnCheckRadius);

        /// <summary>
        /// Blocking layer mask for spawn checks.
        /// </summary>
        public LayerMask SpawnBlockMask => m_spawnBlockMask;
    }
}
