// Fallback for the immediate runner return (PERF-7): compiled only with ONITY_RUNNER_RETURN_GATE,
// which restores the IL2CPP return-on-unwind handshake of PERF-4. Remove after the final attempt passes.
#if ONITY_RUNNER_RETURN_GATE
using System.Runtime.CompilerServices;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Decides which party pools a state machine runner when its result is consumed while a
    /// <c>MoveNext</c> of the same rental may still be on a stack. One <see cref="int"/> word holds
    /// the number of <c>MoveNext</c> frames in its low bits and a return-pending bit: a release that
    /// finds no frame pools at once, otherwise it sets the bit and the last frame to unwind pools.
    /// Exactly one party is told to pool, and the word is zero again when it does.
    /// </summary>
    /// <remarks>
    /// The protocol relies on two facts of a rental: the release happens once, after the result was
    /// claimed, and no <c>MoveNext</c> of the rental can start after the method completed, so a
    /// word that reads zero at release means no frame of this rental is on any stack.
    /// </remarks>
    internal static class OnityRunnerReturnGate
    {
        /// <summary>
        /// Bit set by a release that found a <c>MoveNext</c> frame on a stack.
        /// </summary>
        internal const int k_returnPending = 0x40000000;

        /// <summary>
        /// Counts a <c>MoveNext</c> frame that is about to run the state machine.
        /// </summary>
        /// <param name="state">The runner's frame and pending word.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnterFrame(ref int state)
        {
            Interlocked.Increment(ref state);
        }

        /// <summary>
        /// Removes a <c>MoveNext</c> frame that has unwound.
        /// </summary>
        /// <param name="state">The runner's frame and pending word.</param>
        /// <returns>True when this was the last frame of a released rental and the caller must pool the runner now.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ExitFrame(ref int state)
        {
            return Interlocked.Decrement(ref state) == k_returnPending
                && Interlocked.CompareExchange(ref state, 0, k_returnPending) == k_returnPending;
        }

        /// <summary>
        /// Requests the return of a released rental.
        /// </summary>
        /// <param name="state">The runner's frame and pending word.</param>
        /// <returns>True when no frame is on a stack and the caller must pool the runner now; false when the last frame to unwind pools it.</returns>
        public static bool RequestReturn(ref int state)
        {
            while (true)
            {
                int observed = Volatile.Read(ref state);
                if (observed == 0)
                {
                    return true;
                }

                if (Interlocked.CompareExchange(ref state, observed | k_returnPending, observed) == observed)
                {
                    return false;
                }
            }
        }
    }
}
#endif
