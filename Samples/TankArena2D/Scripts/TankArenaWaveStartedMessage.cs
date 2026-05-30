namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Published when a new wave starts spawning.
    /// </summary>
    public readonly struct TankArenaWaveStartedMessage
    {
        /// <summary>
        /// 1-based wave index.
        /// </summary>
        public int WaveIndex { get; }

        /// <summary>
        /// Initializes a wave start message.
        /// </summary>
        /// <param name="waveIndex">1-based wave index.</param>
        public TankArenaWaveStartedMessage(int waveIndex)
        {
            WaveIndex = waveIndex;
        }
    }
}
