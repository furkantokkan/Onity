namespace OnityShowcase
{
    /// <summary>
    /// Transient notification published whenever the player collects a single coin.
    /// Modeled as a message (fire-and-forget, fan-out to 0..N listeners) rather than
    /// reactive state, because late subscribers do not need the past collections.
    /// </summary>
    public readonly struct CoinCollectedMessage
    {
        /// <summary>
        /// Score value awarded for the collected coin.
        /// </summary>
        public readonly int ScoreValue;

        /// <summary>
        /// Initializes a coin-collected message.
        /// </summary>
        /// <param name="scoreValue">Score value awarded for the collected coin.</param>
        public CoinCollectedMessage(int scoreValue)
        {
            ScoreValue = scoreValue;
        }
    }
}
