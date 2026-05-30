using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Abstracts time-based waits for reactive operators.
    /// </summary>
    public abstract class OnityTimeProvider
    {
        private static readonly OnityTimeProvider s_system = new OnitySystemTimeProvider();

        /// <summary>
        /// Default system time provider.
        /// </summary>
        public static OnityTimeProvider System => s_system;

        /// <summary>
        /// Delays for the given duration.
        /// </summary>
        /// <param name="delay">Delay duration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Delay task.</returns>
        public abstract Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Abstracts frame-based ticks for reactive operators.
    /// </summary>
    public abstract class OnityFrameProvider
    {
        /// <summary>
        /// Human-readable provider name.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Emits once per provider frame.
        /// </summary>
        /// <returns>Frame tick observable.</returns>
        public abstract IOnityObservable<Unit> EveryFrame();
    }

    internal sealed class OnitySystemTimeProvider : OnityTimeProvider
    {
        public override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (delay <= TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }

            return Task.Delay(delay, cancellationToken);
        }
    }
}
