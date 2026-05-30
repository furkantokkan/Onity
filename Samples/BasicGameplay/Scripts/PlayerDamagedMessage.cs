namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// Message payload representing incoming damage for the player.
    /// </summary>
    public readonly struct PlayerDamagedMessage
    {
        /// <summary>
        /// Initializes a new damage message.
        /// </summary>
        /// <param name="amount">Damage amount.</param>
        public PlayerDamagedMessage(int amount)
        {
            Amount = amount;
        }

        /// <summary>
        /// Damage value.
        /// </summary>
        public int Amount { get; }
    }
}
