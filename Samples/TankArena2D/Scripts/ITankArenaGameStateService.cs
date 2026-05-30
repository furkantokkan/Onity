using Onity.Reactive;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Exposes reactive game state values for Tank Arena sample.
    /// </summary>
    public interface ITankArenaGameStateService
    {
        /// <summary>
        /// Current score stream.
        /// </summary>
        IReadOnlyReactiveProperty<int> Score { get; }

        /// <summary>
        /// Current player health stream.
        /// </summary>
        IReadOnlyReactiveProperty<int> Health { get; }

        /// <summary>
        /// Current active enemy count stream.
        /// </summary>
        IReadOnlyReactiveProperty<int> ActiveEnemyCount { get; }

        /// <summary>
        /// Current wave index stream.
        /// </summary>
        IReadOnlyReactiveProperty<int> CurrentWave { get; }

        /// <summary>
        /// True when game over state is reached.
        /// </summary>
        IReadOnlyReactiveProperty<bool> IsGameOver { get; }
    }
}
