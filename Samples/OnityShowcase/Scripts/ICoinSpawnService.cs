namespace OnityShowcase
{
    /// <summary>
    /// Decides when and where the next coin should appear. This is a command/query service
    /// with a single owner and a synchronous result, so it is a plain injected interface
    /// (not a message and not reactive state). The host advances it each frame via
    /// <see cref="TryGetNextSpawn"/>; all timing and placement math stays engine-free.
    /// </summary>
    public interface ICoinSpawnService
    {
        /// <summary>
        /// Advances the spawn timer and, when an interval elapses, returns a planar spawn
        /// position (x, z) inside the configured play area.
        /// </summary>
        /// <param name="deltaSeconds">Elapsed seconds since the previous call.</param>
        /// <param name="x">Spawn x coordinate when the result is true.</param>
        /// <param name="z">Spawn z coordinate when the result is true.</param>
        /// <returns>True when a coin should be spawned this frame; otherwise false.</returns>
        bool TryGetNextSpawn(float deltaSeconds, out float x, out float z);
    }
}
