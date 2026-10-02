namespace Onity.Unity.Async
{
    /// <summary>
    /// Clock of a timed PlayerLoop wait or timer; the equivalent of UniTask's <c>DelayType</c>.
    /// </summary>
    public enum OnityDelayType
    {
        /// <summary>Accumulates <c>Time.deltaTime</c> at each drain of the timing, from the frame after
        /// the wait starts; pauses while <c>Time.timeScale</c> is zero.</summary>
        DeltaTime = 0,

        /// <summary>Accumulates <c>Time.unscaledDeltaTime</c> at each drain of the timing, from the
        /// frame after the wait starts.</summary>
        UnscaledDeltaTime = 1,

        /// <summary>Measures real time with <see cref="System.Diagnostics.Stopwatch"/>, checked at each
        /// drain of the timing.</summary>
        Realtime = 2
    }
}
