namespace OnityShowcase
{
    /// <summary>
    /// Plain tuning values for the Coin Rush loop. Bound as a single instance via
    /// <c>BindInstance</c> so services stay free of magic numbers and remain testable.
    /// Kept engine-free (no ScriptableObject) so it can be constructed directly in tests.
    /// </summary>
    public sealed class ShowcaseSettings
    {
        /// <summary>
        /// Round length in seconds before game-over is published.
        /// </summary>
        public float RoundDurationSeconds { get; }

        /// <summary>
        /// Score awarded per collected coin.
        /// </summary>
        public int CoinScoreValue { get; }

        /// <summary>
        /// Average seconds between coin spawns.
        /// </summary>
        public float SpawnIntervalSeconds { get; }

        /// <summary>
        /// Half-extent of the square play area coins spawn within (world units).
        /// </summary>
        public float SpawnAreaHalfSize { get; }

        /// <summary>
        /// Initializes showcase settings with sensible defaults for a short demo round.
        /// </summary>
        /// <param name="roundDurationSeconds">Round length in seconds.</param>
        /// <param name="coinScoreValue">Score per collected coin.</param>
        /// <param name="spawnIntervalSeconds">Average seconds between spawns.</param>
        /// <param name="spawnAreaHalfSize">Half-extent of the spawn area in world units.</param>
        public ShowcaseSettings(
            float roundDurationSeconds = 30f,
            int coinScoreValue = 10,
            float spawnIntervalSeconds = 1.25f,
            float spawnAreaHalfSize = 4f)
        {
            RoundDurationSeconds = roundDurationSeconds;
            CoinScoreValue = coinScoreValue;
            SpawnIntervalSeconds = spawnIntervalSeconds;
            SpawnAreaHalfSize = spawnAreaHalfSize;
        }
    }
}
