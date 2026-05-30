namespace Onity.Samples.RollABall
{
    /// <summary>
    /// Message sent when a Roll-a-Ball pickup is collected.
    /// </summary>
    public readonly struct RollABallPickupCollectedMessage
    {
        /// <summary>
        /// Initializes message data.
        /// </summary>
        /// <param name="pickupId">Unique pickup identifier.</param>
        /// <param name="points">Awarded score points.</param>
        public RollABallPickupCollectedMessage(int pickupId, int points)
        {
            PickupId = pickupId;
            Points = points;
        }

        /// <summary>
        /// Collected pickup identifier.
        /// </summary>
        public int PickupId { get; }

        /// <summary>
        /// Score points for this pickup.
        /// </summary>
        public int Points { get; }
    }
}
