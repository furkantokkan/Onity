namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Published when one enemy is destroyed.
    /// </summary>
    public readonly struct TankArenaEnemyDestroyedMessage
    {
        /// <summary>
        /// Destroyed enemy instance.
        /// </summary>
        public TankArenaEnemyController Enemy { get; }

        /// <summary>
        /// Score value awarded for this enemy.
        /// </summary>
        public int ScoreValue { get; }

        /// <summary>
        /// Initializes a destroy message.
        /// </summary>
        /// <param name="enemy">Destroyed enemy instance.</param>
        /// <param name="scoreValue">Awarded score value.</param>
        public TankArenaEnemyDestroyedMessage(TankArenaEnemyController enemy, int scoreValue)
        {
            Enemy = enemy;
            ScoreValue = scoreValue;
        }
    }
}
