using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Return type of fire-and-forget async methods, the counterpart of <c>async void</c> that is
    /// bound to Onity's pooled runner instead of the synchronization context.
    /// The method starts synchronously on the calling thread and runs until its first suspension.
    /// It has no awaitable result: an exception that escapes the method is published through
    /// <see cref="OnityTaskScheduler"/>, which raises <see cref="OnityTaskScheduler.UnobservedTaskException"/>
    /// or, without a subscriber, logs it (with <see cref="UnityEngine.Debug.LogException(Exception)"/> by
    /// default). An <see cref="OperationCanceledException"/> is ignored unless
    /// <see cref="OnityTaskScheduler.PropagateOperationCanceledException"/> is set.
    /// Main-thread affinity: the method runs on the thread that calls it and resumes on whichever
    /// thread completes its awaited operation; an await on a PlayerLoop-driven task resumes on the
    /// Unity main thread, so call it from the main thread when the method touches Unity objects.
    /// </summary>
    [AsyncMethodBuilder(typeof(OnityTaskVoidMethodBuilder))]
    public readonly struct OnityTaskVoid
    {
        /// <summary>
        /// Does nothing. The async method already started and its faults are already reported, so
        /// this exists only to make the discard explicit and silence unawaited-call warnings.
        /// </summary>
        public void Forget()
        {
        }
    }

    /// <summary>
    /// Async method builder for async methods returning <see cref="OnityTaskVoid"/>.
    /// It wraps <see cref="OnityTaskMethodBuilder"/>, so a suspended method uses the same pooled
    /// runner as <c>async OnityTask</c>; when the method finishes, the builder consumes that
    /// single-consumer task itself so the runner returns to its pool and a fault is reported once.
    /// A method that never suspends allocates nothing. Mutable by design: it lives inside the
    /// compiler-generated state machine and must not be copied.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct OnityTaskVoidMethodBuilder
    {
        private OnityTaskMethodBuilder m_builder;

        /// <summary>
        /// Creates a method builder.
        /// </summary>
        /// <returns>Created builder.</returns>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OnityTaskVoidMethodBuilder Create()
        {
            return default;
        }

        /// <summary>
        /// Gets the value the async method returns, which carries no state.
        /// </summary>
        public OnityTaskVoid Task
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return default;
            }
        }

        /// <summary>
        /// Starts the async state machine on the calling thread.
        /// </summary>
        /// <typeparam name="TStateMachine">State machine type.</typeparam>
        /// <param name="stateMachine">State machine.</param>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            m_builder.Start(ref stateMachine);
        }

        /// <summary>
        /// Part of the builder contract that is never invoked; the runner stores the state machine
        /// by value instead of boxing it.
        /// </summary>
        /// <param name="stateMachine">State machine.</param>
        [DebuggerHidden]
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            m_builder.SetStateMachine(stateMachine);
        }

        /// <summary>
        /// Schedules a continuation for a safe awaiter.
        /// </summary>
        /// <typeparam name="TAwaiter">Awaiter type.</typeparam>
        /// <typeparam name="TStateMachine">State machine type.</typeparam>
        /// <param name="awaiter">Awaiter.</param>
        /// <param name="stateMachine">State machine.</param>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            m_builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>
        /// Schedules a continuation for a critical awaiter.
        /// </summary>
        /// <typeparam name="TAwaiter">Awaiter type.</typeparam>
        /// <typeparam name="TStateMachine">State machine type.</typeparam>
        /// <param name="awaiter">Awaiter.</param>
        /// <param name="stateMachine">State machine.</param>
        [DebuggerHidden]
        [SecuritySafeCritical]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            m_builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>
        /// Completes the async method successfully and returns its runner to the pool.
        /// </summary>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResult()
        {
            m_builder.SetResult();
            Consume();
        }

        /// <summary>
        /// Completes the async method with an exception, publishes it once through
        /// <see cref="OnityTaskScheduler"/>, and returns the runner to the pool. An
        /// <see cref="OperationCanceledException"/> is ignored unless
        /// <see cref="OnityTaskScheduler.PropagateOperationCanceledException"/> is set.
        /// </summary>
        /// <param name="exception">Failure exception.</param>
        [DebuggerHidden]
        public void SetException(Exception exception)
        {
            m_builder.SetException(exception);
            Consume();
        }

        [DebuggerHidden]
        private void Consume()
        {
            try
            {
                m_builder.Task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                // Cancellation is the normal end of a fire-and-forget method: the scheduler drops it
                // unless PropagateOperationCanceledException asks for it.
                OnityTaskScheduler.PublishUnobservedException(exception);
            }
        }
    }
}
