namespace Onity.Core
{
    /// <summary>
    /// Value-less payload type used by reactive and messaging APIs.
    /// </summary>
    public readonly struct Unit
    {
        /// <summary>
        /// Shared unit value.
        /// </summary>
        public static Unit Default => default;
    }
}
