using System;
using System.Threading.Tasks;
using Onity.Core;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Status, text and conversion members of <see cref="OnityTask"/>. They use only the public
    /// surface of the task, so they never claim a pooled source except where a member says it
    /// consumes the task.
    /// </summary>
    public readonly partial struct OnityTask
    {
        /// <summary>
        /// Gets the lifecycle state of the task. Reading it does not consume the task, but a pooled
        /// single-consumer task that was already consumed throws
        /// <see cref="InvalidOperationException"/>, as any other member does.
        /// </summary>
        public OnityTaskStatus Status
        {
            get
            {
                if (!IsCompleted)
                {
                    return OnityTaskStatus.Pending;
                }

                if (IsCompletedSuccessfully)
                {
                    return OnityTaskStatus.Succeeded;
                }

                return IsCanceled ? OnityTaskStatus.Canceled : OnityTaskStatus.Faulted;
            }
        }

        /// <summary>
        /// Returns the status in parentheses, for example <c>(Pending)</c>. A task that was already
        /// consumed reports <c>(Consumed)</c> instead of throwing.
        /// </summary>
        /// <returns>Status text.</returns>
        public override string ToString()
        {
            try
            {
                return "(" + Status + ")";
            }
            catch (InvalidOperationException)
            {
                return "(Consumed)";
            }
        }

        /// <summary>
        /// Converts the task to a typed task that completes with <see cref="Unit"/>. The original
        /// task is consumed by this call and must not be awaited again.
        /// </summary>
        /// <returns>
        /// A completed task when the original already succeeded; otherwise a single-consumer task
        /// that completes, faults or cancels as the original does.
        /// </returns>
        public OnityTask<Unit> AsUnitTask()
        {
            if (IsCompletedSuccessfully)
            {
                GetAwaiter().GetResult();
                return OnityTask<Unit>.FromResult(Unit.Default);
            }

            return ConvertToUnitAsync(this);
        }

        /// <summary>
        /// Converts the task to a <see cref="ValueTask"/>. The task is consumed by the conversion,
        /// which behaves as <see cref="OnityTaskValueTaskExtensions.AsValueTask(OnityTask)"/>.
        /// </summary>
        /// <param name="task">Task to convert.</param>
        /// <returns>Value task that completes as the task does.</returns>
        public static implicit operator ValueTask(OnityTask task)
        {
            return OnityTaskValueTaskExtensions.AsValueTask(task);
        }

        private static async OnityTask<Unit> ConvertToUnitAsync(OnityTask task)
        {
            await task;
            return Unit.Default;
        }
    }

    /// <summary>
    /// Status, text and conversion members of <see cref="OnityTask{T}"/>. They use only the public
    /// surface of the task, so they never claim a pooled source except where a member says it
    /// consumes the task.
    /// </summary>
    public readonly partial struct OnityTask<T>
    {
        /// <summary>
        /// Gets the lifecycle state of the task. Reading it does not consume the task, but a pooled
        /// single-consumer task that was already consumed throws
        /// <see cref="InvalidOperationException"/>, as any other member does.
        /// </summary>
        public OnityTaskStatus Status
        {
            get
            {
                if (!IsCompleted)
                {
                    return OnityTaskStatus.Pending;
                }

                if (IsCompletedSuccessfully)
                {
                    return OnityTaskStatus.Succeeded;
                }

                return IsCanceled ? OnityTaskStatus.Canceled : OnityTaskStatus.Faulted;
            }
        }

        /// <summary>
        /// Returns the status in parentheses, for example <c>(Pending)</c>. A task that was already
        /// consumed reports <c>(Consumed)</c> instead of throwing. The result is never printed,
        /// because reading it would consume a pooled task.
        /// </summary>
        /// <returns>Status text.</returns>
        public override string ToString()
        {
            try
            {
                return "(" + Status + ")";
            }
            catch (InvalidOperationException)
            {
                return "(Consumed)";
            }
        }

        /// <summary>
        /// Converts the task to an untyped task that discards the result. The original task is
        /// consumed by this call and must not be awaited again.
        /// </summary>
        /// <returns>
        /// A completed task when the original already succeeded; otherwise a single-consumer task
        /// that completes, faults or cancels as the original does.
        /// </returns>
        public OnityTask AsOnityTask()
        {
            if (IsCompletedSuccessfully)
            {
                GetAwaiter().GetResult();
                return OnityTask.CompletedTask;
            }

            return ConvertToUntypedAsync(this);
        }

        /// <summary>
        /// Converts the task to a <see cref="ValueTask{TResult}"/>. The task is consumed by the
        /// conversion, which behaves as
        /// <see cref="OnityTaskValueTaskExtensions.AsValueTask{T}(OnityTask{T})"/>.
        /// </summary>
        /// <param name="task">Task to convert.</param>
        /// <returns>Value task that completes as the task does.</returns>
        public static implicit operator ValueTask<T>(OnityTask<T> task)
        {
            return OnityTaskValueTaskExtensions.AsValueTask(task);
        }

        private static async OnityTask ConvertToUntypedAsync(OnityTask<T> task)
        {
            await task;
        }
    }
}
