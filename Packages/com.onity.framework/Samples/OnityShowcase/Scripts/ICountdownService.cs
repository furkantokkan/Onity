using Onity.Reactive;

namespace OnityShowcase
{
    /// <summary>
    /// Drives the round countdown. Time remaining is reactive state; reaching zero is a
    /// one-shot transient event, so it is published as a <see cref="GameOverMessage"/>.
    /// The service is engine-free: a host advances it by calling <see cref="Tick"/> with a
    /// delta time, which keeps all timing logic testable without Unity.
    /// </summary>
    public interface ICountdownService
    {
        /// <summary>
        /// Seconds left in the round. Subscribing emits the current value first, then each change.
        /// </summary>
        IReadOnlyReactiveProperty<float> TimeRemaining { get; }

        /// <summary>
        /// True once the countdown has reached zero and game-over has been published.
        /// </summary>
        IReadOnlyReactiveProperty<bool> IsFinished { get; }

        /// <summary>
        /// Emits true when time remaining drops below the warning threshold and false when it
        /// recovers. Derived from <see cref="TimeRemaining"/> with reactive operators
        /// (<c>Select</c> + <c>DistinctUntilChanged</c>), so consumers get only edge changes.
        /// </summary>
        IOnityObservable<bool> LowTimeWarning { get; }

        /// <summary>
        /// Advances the countdown by the given elapsed seconds. Publishes
        /// <see cref="GameOverMessage"/> exactly once when the timer first hits zero.
        /// </summary>
        /// <param name="deltaSeconds">Elapsed seconds since the previous tick.</param>
        void Tick(float deltaSeconds);

        /// <summary>
        /// Restarts the countdown to its configured duration for a new round.
        /// </summary>
        void Restart();
    }
}
