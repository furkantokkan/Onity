namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Published when player receives damage.
    /// </summary>
    public readonly struct TankArenaPlayerDamagedMessage
    {
        /// <summary>
        /// Incoming damage value.
        /// </summary>
        public int Damage { get; }

        /// <summary>
        /// Initializes a player damage message.
        /// </summary>
        /// <param name="damage">Damage value.</param>
        public TankArenaPlayerDamagedMessage(int damage)
        {
            Damage = damage;
        }
    }
}
