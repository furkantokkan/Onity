namespace Onity.Unity.Async
{
    /// <summary>
    /// Lifecycle state of an <see cref="OnityTask"/> or <see cref="OnityTask{T}"/>, as reported by
    /// the <c>Status</c> property of each.
    /// </summary>
    /// <remarks>
    /// The numeric values are fixed so they can be cast to
    /// <see cref="System.Threading.Tasks.Sources.ValueTaskSourceStatus"/>.
    /// </remarks>
    public enum OnityTaskStatus
    {
        /// <summary>The operation has not completed yet.</summary>
        Pending = 0,

        /// <summary>The operation completed successfully.</summary>
        Succeeded = 1,

        /// <summary>The operation completed with an exception other than cancellation.</summary>
        Faulted = 2,

        /// <summary>The operation completed as canceled.</summary>
        Canceled = 3
    }

    /// <summary>
    /// Convenience predicates for <see cref="OnityTaskStatus"/>.
    /// </summary>
    public static class OnityTaskStatusExtensions
    {
        /// <summary>
        /// Returns true when the status is anything other than <see cref="OnityTaskStatus.Pending"/>.
        /// </summary>
        /// <param name="status">Status to test.</param>
        /// <returns>True for a finished operation, whatever its outcome.</returns>
        public static bool IsCompleted(this OnityTaskStatus status)
        {
            return status != OnityTaskStatus.Pending;
        }

        /// <summary>
        /// Returns true when the status is <see cref="OnityTaskStatus.Succeeded"/>.
        /// </summary>
        /// <param name="status">Status to test.</param>
        /// <returns>True when the operation completed successfully.</returns>
        public static bool IsCompletedSuccessfully(this OnityTaskStatus status)
        {
            return status == OnityTaskStatus.Succeeded;
        }

        /// <summary>
        /// Returns true when the status is <see cref="OnityTaskStatus.Canceled"/>.
        /// </summary>
        /// <param name="status">Status to test.</param>
        /// <returns>True when the operation completed as canceled.</returns>
        public static bool IsCanceled(this OnityTaskStatus status)
        {
            return status == OnityTaskStatus.Canceled;
        }

        /// <summary>
        /// Returns true when the status is <see cref="OnityTaskStatus.Faulted"/>.
        /// </summary>
        /// <param name="status">Status to test.</param>
        /// <returns>True when the operation completed with an exception.</returns>
        public static bool IsFaulted(this OnityTaskStatus status)
        {
            return status == OnityTaskStatus.Faulted;
        }
    }
}
