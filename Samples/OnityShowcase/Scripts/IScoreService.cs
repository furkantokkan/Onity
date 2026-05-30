using Onity.Reactive;

namespace OnityShowcase
{
    /// <summary>
    /// Holds the current round score as observable reactive state.
    /// Score is the canonical "current value a fresh subscriber must see", so it is
    /// a <see cref="ReactiveProperty{T}"/> rather than a message stream.
    /// </summary>
    public interface IScoreService
    {
        /// <summary>
        /// Current score. Subscribing emits the current value immediately, then each change.
        /// </summary>
        IReadOnlyReactiveProperty<int> Score { get; }

        /// <summary>
        /// Resets the score to zero for a new round.
        /// </summary>
        void Reset();
    }
}
