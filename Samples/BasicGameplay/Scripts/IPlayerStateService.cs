using Onity.Reactive;

namespace Onity.Samples.BasicGameplay
{
    /// <summary>
    /// Exposes player state values for gameplay and UI.
    /// </summary>
    public interface IPlayerStateService
    {
        /// <summary>
        /// Current health stream.
        /// </summary>
        IReadOnlyReactiveProperty<int> Health { get; }

        /// <summary>
        /// Sets health back to the configured starting value.
        /// </summary>
        void ResetHealth();
    }
}
