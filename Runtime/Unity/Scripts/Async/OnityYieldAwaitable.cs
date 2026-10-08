using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Awaitable returned by <see cref="OnityTask.Yield()"/>: resumes at the next drain of an Onity
    /// PlayerLoop node, relative to the await. It holds only the timing, so creating, awaiting and
    /// storing it in a state machine perform no reference store and rent nothing; the equivalent of
    /// UniTask's <c>YieldAwaitable</c>. Session exit cancels a pending yield.
    /// </summary>
    public readonly struct OnityYieldAwaitable
    {
        private readonly OnityPlayerLoopTiming m_timing;

        internal OnityYieldAwaitable(OnityPlayerLoopTiming timing)
        {
            m_timing = timing;
        }

        /// <summary>Converts the yield to a task that completes at the next drain of its timing.</summary>
        /// <param name="awaitable">Yield awaitable.</param>
        public static implicit operator OnityTask(OnityYieldAwaitable awaitable)
        {
            return awaitable.ToOnityTask();
        }

        /// <summary>Returns the awaiter.</summary>
        /// <returns>Yield awaiter.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OnityYieldAwaiter GetAwaiter()
        {
            return new OnityYieldAwaiter(m_timing, OnityTaskPlayerLoop.s_epoch);
        }

        /// <summary>
        /// Converts the yield to a stateless task that completes at the next drain of its timing after
        /// this call. The task allocates nothing and may be awaited by any number of consumers.
        /// </summary>
        /// <returns>Yield task.</returns>
        public OnityTask ToOnityTask()
        {
            return OnityTaskPlayerLoop.CreateYieldTask(m_timing);
        }

        /// <summary>Converts the yield to a .NET task.</summary>
        /// <returns>Task that completes at the next drain.</returns>
        public Task AsTask()
        {
            return ToOnityTask().AsTask();
        }

        /// <summary>Returns the stateless yield task; it is already shareable.</summary>
        /// <returns>Yield task.</returns>
        public OnityTask Preserve()
        {
            return ToOnityTask();
        }

        /// <summary>Does nothing: a yield has no outcome to observe.</summary>
        public void Forget()
        {
        }
    }

    /// <summary>Awaiter of <see cref="OnityYieldAwaitable"/>; holds no reference.</summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    public readonly struct OnityYieldAwaiter : ICriticalNotifyCompletion
    {
        private readonly OnityPlayerLoopTiming m_timing;
        private readonly int m_epoch;

        internal OnityYieldAwaiter(OnityPlayerLoopTiming timing, int epoch)
        {
            m_timing = timing;
            m_epoch = epoch;
        }

        /// <summary>Always false: a yield always suspends.</summary>
        public bool IsCompleted => false;

        /// <summary>
        /// Completes the await. Throws <see cref="OperationCanceledException"/> when the session that
        /// created the awaiter ended before the drain, or the repair failure that retired it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetResult()
        {
            if (m_epoch != OnityTaskPlayerLoop.s_epoch)
            {
                OnityTaskPlayerLoop.ThrowRetired();
            }
        }

        /// <summary>Queues the continuation for the next drain of the timing.</summary>
        /// <param name="continuation">Continuation callback.</param>
        public void OnCompleted(Action continuation)
        {
            OnityTaskPlayerLoop.EnqueueYield(m_timing, continuation);
        }

        /// <summary>Queues the continuation for the next drain of the timing.</summary>
        /// <param name="continuation">Continuation callback.</param>
        public void UnsafeOnCompleted(Action continuation)
        {
            OnityTaskPlayerLoop.EnqueueYield(m_timing, continuation);
        }
    }
}
