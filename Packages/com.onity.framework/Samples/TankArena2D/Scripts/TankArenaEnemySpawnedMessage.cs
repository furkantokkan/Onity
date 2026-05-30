namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Published when one enemy is spawned into the arena.
    /// </summary>
    public readonly struct TankArenaEnemySpawnedMessage
    {
        /// <summary>
        /// Spawned enemy instance.
        /// </summary>
        public TankArenaEnemyController Enemy { get; }

        /// <summary>
        /// Initializes a spawn message.
        /// </summary>
        /// <param name="enemy">Spawned enemy instance.</param>
        public TankArenaEnemySpawnedMessage(TankArenaEnemyController enemy)
        {
            Enemy = enemy;
        }
    }
}
