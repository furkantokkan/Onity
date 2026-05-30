using Onity.Reactive;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// Read-only score service contract for the Roll-a-Ball sample.
    /// </summary>
    public interface IRollABallScoreService
    {
        /// <summary>
        /// Current score value.
        /// </summary>
        IReadOnlyReactiveProperty<int> Score { get; }

        /// <summary>
        /// Total pickup count collected by the player.
        /// </summary>
        IReadOnlyReactiveProperty<int> CollectedCount { get; }

        /// <summary>
        /// Resets score and collection counters.
        /// </summary>
        void Reset();
    }
}
