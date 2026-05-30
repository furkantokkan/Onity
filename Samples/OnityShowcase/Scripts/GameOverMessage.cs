namespace OnityShowcase
{
    /// <summary>
    /// Transient notification published exactly once when the countdown reaches zero.
    /// Carries the final score so listeners do not have to re-query a service.
    /// </summary>
    public readonly struct GameOverMessage
    {
        /// <summary>
        /// Final score at the moment the round ended.
        /// </summary>
        public readonly int FinalScore;

        /// <summary>
        /// Initializes a game-over message.
        /// </summary>
        /// <param name="finalScore">Final score at the moment the round ended.</param>
        public GameOverMessage(int finalScore)
        {
            FinalScore = finalScore;
        }
    }
}
