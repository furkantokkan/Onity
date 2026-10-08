using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Execution-context helpers shared by the native builders and task bridges.
    /// </summary>
    /// <remarks>
    /// The class library's own async method builder captures with the internal
    /// <c>ExecutionContext.FastCapture</c>, which ignores the synchronization context and returns a
    /// shared default context when nothing needs to flow, and resumes with the internal
    /// <c>RunInternal(context, callback, state, preserveSyncCtx: true)</c>, which keeps the resuming
    /// thread's synchronization context. Both are <c>FriendAccessAllowed</c> members of the .NET
    /// Framework reference source that Unity's Mono compiles. They are bound through reflection and
    /// proven with a probe run once; when either is missing or behaves differently, the public
    /// <see cref="ExecutionContext.Capture"/> and <see cref="ExecutionContext.Run"/> path below is
    /// used instead, which allocates a context per suspension and re-installs the synchronization
    /// context inside the callback. Managed code stripping keeps both members because this class
    /// references the class library's own async method builder, whose completion path calls them.
    /// </remarks>
    internal static class OnityAsyncExecutionContext
    {
        private static readonly Func<ExecutionContext> s_fastCapture;
        private static readonly Action<ExecutionContext, ContextCallback, object, bool> s_runPreservingContext;
        private static readonly Func<Task, Task> s_classLibraryBuilderAnchor;

        static OnityAsyncExecutionContext()
        {
            // The state machine of this async Task method references
            // AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted, which reaches FastCapture and
            // RunInternal, so the linker cannot strip them while this class is in the build.
            s_classLibraryBuilderAnchor = AwaitThroughClassLibraryBuilderAsync;

            Func<ExecutionContext> fastCapture = null;
            Action<ExecutionContext, ContextCallback, object, bool> runPreservingContext = null;
            try
            {
                Type type = typeof(ExecutionContext);
                MethodInfo capture = type.GetMethod(
                    "FastCapture", BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                MethodInfo run = type.GetMethod(
                    "RunInternal",
                    BindingFlags.Static | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(ExecutionContext), typeof(ContextCallback), typeof(object), typeof(bool) },
                    null);
                if (capture != null && run != null
                    && capture.ReturnType == typeof(ExecutionContext)
                    && !capture.IsGenericMethod && !run.IsGenericMethod)
                {
                    fastCapture = (Func<ExecutionContext>)Delegate.CreateDelegate(
                        typeof(Func<ExecutionContext>), capture, false);
                    runPreservingContext = (Action<ExecutionContext, ContextCallback, object, bool>)Delegate.CreateDelegate(
                        typeof(Action<ExecutionContext, ContextCallback, object, bool>), run, false);
                }

                if (fastCapture == null || runPreservingContext == null || !Probe(fastCapture, runPreservingContext))
                {
                    fastCapture = null;
                    runPreservingContext = null;
                }
            }
            catch (Exception)
            {
                fastCapture = null;
                runPreservingContext = null;
            }

            s_fastCapture = fastCapture;
            s_runPreservingContext = runPreservingContext;
            if (fastCapture == null)
            {
                ReportFallback();
            }
        }

        /// <summary>
        /// True when the class library's internal capture and run pair was bound and proven on
        /// this runtime; false means the public, allocating path is in use.
        /// </summary>
        public static bool IsFastPathAvailable => s_fastCapture != null;

        /// <summary>
        /// Captures the current execution context, including <see cref="AsyncLocal{T}"/> values,
        /// without copying the thread's synchronization context. Returns null when flow is
        /// suppressed. On the fast path a context that carries nothing is the shared default
        /// instance; on the public path the capture is detached from the synchronization context
        /// for its duration because <see cref="ExecutionContext.Capture"/> would copy it.
        /// </summary>
        /// <returns>The captured context, or null when flow is suppressed.</returns>
        public static ExecutionContext Capture()
        {
            Func<ExecutionContext> fastCapture = s_fastCapture;
            if (fastCapture != null)
            {
                return fastCapture();
            }

            SynchronizationContext current = SynchronizationContext.Current;
            if (current == null)
            {
                return ExecutionContext.Capture();
            }

            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                return ExecutionContext.Capture();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(current);
            }
        }

        /// <summary>
        /// Runs the callback inside the captured context while keeping the calling thread's
        /// synchronization context, and returns false when only the public path is available.
        /// The public path installs the captured context without a synchronization context, so
        /// its caller must re-install the thread's context inside the callback.
        /// </summary>
        /// <param name="context">A context returned by <see cref="Capture"/>.</param>
        /// <param name="callback">Callback to run.</param>
        /// <param name="state">Callback state.</param>
        /// <returns>True when the callback ran on the fast path.</returns>
        public static bool TryRunPreservingContext(ExecutionContext context, ContextCallback callback, object state)
        {
            Action<ExecutionContext, ContextCallback, object, bool> run = s_runPreservingContext;
            if (run == null)
            {
                return false;
            }

            run(context, callback, state, true);
            return true;
        }

        /// <summary>
        /// Creates a task completion source without capturing the caller's execution context,
        /// so a bridge created on Unity's main thread does not copy its synchronization context.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <returns>A completion source that runs its continuations asynchronously.</returns>
        public static TaskCompletionSource<T> CreateTaskBridge<T>()
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            AsyncFlowControl flowControl = ExecutionContext.SuppressFlow();
            try
            {
                return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            finally
            {
                flowControl.Undo();
            }
        }

        /// <summary>
        /// Never invoked; its state machine keeps the class library's builder path reachable.
        /// </summary>
        private static async Task AwaitThroughClassLibraryBuilderAsync(Task task)
        {
            await task;
        }

        /// <summary>
        /// Logs once, in players only, that the public path is in use, since that is the
        /// allocation profile a developer would otherwise attribute to the package.
        /// </summary>
        private static void ReportFallback()
        {
#if !UNITY_EDITOR
            try
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning(
                        "OnityTask: ExecutionContext.FastCapture or RunInternal is unavailable on this "
                        + "runtime, so async OnityTask methods flow their context through the public "
                        + "ExecutionContext API, which allocates per suspension. Managed code stripping "
                        + "or a different class library can cause this; see the OnityTask guide.");
                }
            }
            catch (Exception)
            {
                // Logging is a courtesy; the public path is fully functional.
            }
#endif
        }

        /// <summary>
        /// Proves the bound pair once: the capture must succeed and the run must invoke the
        /// callback with the calling thread's synchronization context still installed.
        /// </summary>
        private static bool Probe(
            Func<ExecutionContext> fastCapture,
            Action<ExecutionContext, ContextCallback, object, bool> runPreservingContext)
        {
            ExecutionContext context = fastCapture();
            if (context == null)
            {
                // Flow is suppressed on the initializing thread; the pair cannot be proven here.
                return false;
            }

            ProbeState probe = new ProbeState { Expected = SynchronizationContext.Current };
            runPreservingContext(context, ProbeCallback, probe, true);
            return probe.Ran && ReferenceEquals(probe.Observed, probe.Expected);
        }

        private static void ProbeCallback(object state)
        {
            ProbeState probe = (ProbeState)state;
            probe.Ran = true;
            probe.Observed = SynchronizationContext.Current;
        }

        private sealed class ProbeState
        {
            public SynchronizationContext Expected;
            public SynchronizationContext Observed;
            public bool Ran;
        }
    }

    /// <summary>
    /// Intrusive pool node: the runner stores the link to the next pooled runner itself.
    /// </summary>
    /// <typeparam name="T">Runner type.</typeparam>
    internal interface IOnityPooledRunner<T> where T : class
    {
        ref T NextPooled { get; }
    }

    /// <summary>
    /// Array slot of <see cref="OnityRunnerPool{T}"/>. Storing into a struct field avoids the array
    /// covariance check of a reference-type element, and clearing with <c>default</c> compiles to
    /// <c>initobj</c>, which needs no GC write barrier.
    /// </summary>
    /// <typeparam name="T">Pooled type.</typeparam>
    internal struct OnityPoolSlot<T> where T : class
    {
        internal T Value;
    }

    /// <summary>
    /// Pool guarded by one compare-and-swap gate instead of a monitor. A rent or return that finds
    /// the gate taken does not wait: the rent allocates and the return lets the object be collected,
    /// so the uncontended path is one compare-and-swap plus the releasing volatile write. Entries live
    /// in an array that grows on demand up to the capacity passed to <see cref="TryPush"/>; a pop
    /// performs no write barrier and no interface call, a push one write barrier.
    /// </summary>
    /// <typeparam name="T">Pooled type. <see cref="IOnityPooledRunner{T}"/> stays part of the
    /// constraint for source compatibility; the pool no longer links entries through it.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    internal struct OnityRunnerPool<T> where T : class, IOnityPooledRunner<T>
    {
        private const int k_initialSlots = 16;

        private int m_gate;
        private int m_count;
        private OnityPoolSlot<T>[] m_slots;

        public bool TryPop(out T runner)
        {
            if (Interlocked.CompareExchange(ref m_gate, 1, 0) == 0)
            {
                int count = m_count;
                if (count > 0)
                {
                    count--;
                    OnityPoolSlot<T>[] slots = m_slots;
                    runner = slots[count].Value;
                    slots[count] = default;
                    m_count = count;
                    Volatile.Write(ref m_gate, 0);
                    return true;
                }

                Volatile.Write(ref m_gate, 0);
            }

            runner = null;
            return false;
        }

        public bool TryPush(T runner, int capacity)
        {
            if (Interlocked.CompareExchange(ref m_gate, 1, 0) == 0)
            {
                int count = m_count;
                if (count < capacity)
                {
                    OnityPoolSlot<T>[] slots = m_slots;
                    if (slots == null || count == slots.Length)
                    {
                        slots = Grow(slots, count, capacity);
                        m_slots = slots;
                    }

                    slots[count].Value = runner;
                    m_count = count + 1;
                    Volatile.Write(ref m_gate, 0);
                    return true;
                }

                Volatile.Write(ref m_gate, 0);
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static OnityPoolSlot<T>[] Grow(OnityPoolSlot<T>[] slots, int count, int capacity)
        {
            int length = slots == null ? k_initialSlots : slots.Length * 2;
            if (length > capacity)
            {
                length = capacity;
            }

            if (length <= count)
            {
                length = count + 1;
            }

            OnityPoolSlot<T>[] grown = new OnityPoolSlot<T>[length];
            if (slots != null)
            {
                Array.Copy(slots, grown, count);
            }

            return grown;
        }
    }

    /// <summary>
    /// Base of the runners behind suspended untyped async methods. The builder reaches the cached
    /// resume delegate, the task and completion through this class with field reads and non-virtual
    /// calls instead of interface dispatch.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    internal abstract class OnityRunnerBase : OnityTaskSourceBase
    {
        /// <summary>Cached delegate that resumes the state machine; assigned once by the subclass.</summary>
        internal Action m_moveNext;

        /// <summary>Context captured at the last suspension when execution-context flow is on.</summary>
        internal ExecutionContext m_executionContext;

        internal OnityTask Task
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new OnityTask(this, Version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CaptureExecutionContext()
        {
            m_executionContext = OnityAsyncExecutionContext.Capture();
        }

        /// <summary>
        /// Completes the method. Only the method's own state machine completes its runner, so one
        /// compare-and-swap publishes the result; no field is touched once it is visible.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetResult()
        {
            TrySetOwnedResult();
        }

        internal void SetException(Exception exception)
        {
            // A method that faults after capturing, for example when its awaiter rejected the
            // registration, never resumes; drop the captured context now rather than at pool return.
            if (m_executionContext != null)
            {
                m_executionContext = null;
            }

            if (exception is OperationCanceledException canceled)
            {
                TrySetCanceled(canceled);
            }
            else
            {
                TrySetException(exception);
            }
        }
    }

    /// <summary>
    /// Base of the runners behind suspended typed async methods; see <see cref="OnityRunnerBase"/>.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    internal abstract class OnityRunnerBase<T> : OnityTaskSourceBase<T>
    {
        /// <summary>Cached delegate that resumes the state machine; assigned once by the subclass.</summary>
        internal Action m_moveNext;

        /// <summary>Context captured at the last suspension when execution-context flow is on.</summary>
        internal ExecutionContext m_executionContext;

        internal OnityTask<T> Task
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new OnityTask<T>(this, Version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CaptureExecutionContext()
        {
            m_executionContext = OnityAsyncExecutionContext.Capture();
        }

        /// <summary>
        /// Completes the method with its result. Only the method's own state machine completes its
        /// runner, so one compare-and-swap publishes it; no field is touched once it is visible.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetResult(T result)
        {
            TrySetOwnedResult(result);
        }

        internal void SetException(Exception exception)
        {
            if (m_executionContext != null)
            {
                m_executionContext = null;
            }

            if (exception is OperationCanceledException canceled)
            {
                TrySetCanceled(canceled);
            }
            else
            {
                TrySetException(exception);
            }
        }
    }

    /// <summary>
    /// Pooled state machine runner for suspended untyped async methods. The state machine is held
    /// by value, one cached delegate resumes it, and the runner is the method's native task source.
    /// </summary>
    /// <remarks>
    /// The runner returns to its pool as soon as its task is consumed, on every backend, even while a
    /// MoveNext of the same rental is still unwinding on this thread or a worker. That is safe because
    /// the generated code runs MoveNext directly on the state machine field without copying it back,
    /// the compiler-generated MoveNext ends with the builder's SetResult or SetException, and the
    /// completion publishes its status as its last access to the runner. Define
    /// <c>ONITY_RUNNER_RETURN_GATE</c> to restore the IL2CPP return-on-unwind gate.
    /// </remarks>
    /// <typeparam name="TStateMachine">Compiler-generated state machine type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    internal sealed class OnityAsyncStateMachineRunner<TStateMachine> :
        OnityRunnerBase, IOnityPooledRunner<OnityAsyncStateMachineRunner<TStateMachine>>
        where TStateMachine : IAsyncStateMachine
    {
        private static OnityRunnerPool<OnityAsyncStateMachineRunner<TStateMachine>> s_pool;
        private static readonly ContextCallback s_moveNextInContext = MoveNextInContext;
        private static readonly ContextCallback s_moveNextRestoringContext = MoveNextRestoringContext;

#if ONITY_RUNNER_RETURN_GATE && ENABLE_IL2CPP
        // MoveNext frames of the current rental plus the return-pending bit; see OnityRunnerReturnGate.
        private int m_moveNextDepth;
#endif
        private TStateMachine m_stateMachine;
        private SynchronizationContext m_resumeContext;
        private OnityAsyncStateMachineRunner<TStateMachine> m_nextPooled;

        private OnityAsyncStateMachineRunner()
        {
            m_moveNext = MoveNext;
        }

        public ref OnityAsyncStateMachineRunner<TStateMachine> NextPooled => ref m_nextPooled;

        /// <summary>
        /// Rents a runner, binds it to the builder field inside the state machine, and then copies
        /// the state machine so the copy already references its runner.
        /// </summary>
        /// <param name="stateMachine">The caller's state machine.</param>
        /// <param name="runnerField">The builder's runner field inside that state machine.</param>
        /// <returns>The bound runner.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OnityRunnerBase Rent(
            ref TStateMachine stateMachine,
            ref OnityRunnerBase runnerField)
        {
            if (!s_pool.TryPop(out OnityAsyncStateMachineRunner<TStateMachine> runner))
            {
                runner = new OnityAsyncStateMachineRunner<TStateMachine>();
            }

            // The runner was retired before it was pooled, or was never published, so no other
            // thread can hold a valid token; the unsynchronized reset publishes the new version.
            runner.ResetRetired();
            runnerField = runner;
            runner.m_stateMachine = stateMachine;
            return runner;
        }

        protected override void ReleaseSource()
        {
            // The base retired the version in the compare-and-swap that released the source.
#if ONITY_RUNNER_RETURN_GATE && ENABLE_IL2CPP
            if (OnityRunnerReturnGate.RequestReturn(ref m_moveNextDepth))
            {
                ReturnToPool();
            }
#else
            ReturnToPool();
#endif
        }

        private void ReturnToPool()
        {
            // initobj: clearing the state machine needs no write barrier.
            m_stateMachine = default;
            // Both are usually cleared already; skipping the store skips its write barrier.
            if (m_executionContext != null)
            {
                m_executionContext = null;
            }

            if (m_resumeContext != null)
            {
                m_resumeContext = null;
            }

            s_pool.TryPush(this, OnityTaskSettings.s_runnerPoolCapacity);
        }

        private void MoveNext()
        {
#if ONITY_RUNNER_RETURN_GATE && ENABLE_IL2CPP
            OnityRunnerReturnGate.EnterFrame(ref m_moveNextDepth);
            try
            {
                MoveNextCore();
            }
            finally
            {
                if (OnityRunnerReturnGate.ExitFrame(ref m_moveNextDepth))
                {
                    ReturnToPool();
                }
            }
        }

        private void MoveNextCore()
        {
#endif
            ExecutionContext context = m_executionContext;
            if (context == null)
            {
                m_stateMachine.MoveNext();
                return;
            }

            MoveNextWithContext(context);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void MoveNextWithContext(ExecutionContext context)
        {
            m_executionContext = null;
            if (OnityAsyncExecutionContext.TryRunPreservingContext(context, s_moveNextInContext, this))
            {
                return;
            }

            m_resumeContext = SynchronizationContext.Current;
            try
            {
                ExecutionContext.Run(context, s_moveNextRestoringContext, this);
            }
            finally
            {
                context.Dispose();
            }
        }

        private static void MoveNextInContext(object state)
        {
            ((OnityAsyncStateMachineRunner<TStateMachine>)state).m_stateMachine.MoveNext();
        }

        private static void MoveNextRestoringContext(object state)
        {
            OnityAsyncStateMachineRunner<TStateMachine> runner =
                (OnityAsyncStateMachineRunner<TStateMachine>)state;
            SynchronizationContext resumeContext = runner.m_resumeContext;
            runner.m_resumeContext = null;

            // The public Run installs the captured context without the resuming thread's
            // synchronization context; restore it before user code observes Current.
            if (!ReferenceEquals(SynchronizationContext.Current, resumeContext))
            {
                SynchronizationContext.SetSynchronizationContext(resumeContext);
            }

            runner.m_stateMachine.MoveNext();
        }
    }

    /// <summary>
    /// Pooled state machine runner for suspended typed async methods; see
    /// <see cref="OnityAsyncStateMachineRunner{TStateMachine}"/>.
    /// </summary>
    /// <typeparam name="TStateMachine">Compiler-generated state machine type.</typeparam>
    /// <typeparam name="T">Result type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    internal sealed class OnityAsyncStateMachineRunner<TStateMachine, T> :
        OnityRunnerBase<T>, IOnityPooledRunner<OnityAsyncStateMachineRunner<TStateMachine, T>>
        where TStateMachine : IAsyncStateMachine
    {
        private static OnityRunnerPool<OnityAsyncStateMachineRunner<TStateMachine, T>> s_pool;
        private static readonly ContextCallback s_moveNextInContext = MoveNextInContext;
        private static readonly ContextCallback s_moveNextRestoringContext = MoveNextRestoringContext;

#if ONITY_RUNNER_RETURN_GATE && ENABLE_IL2CPP
        // MoveNext frames of the current rental plus the return-pending bit; see OnityRunnerReturnGate.
        private int m_moveNextDepth;
#endif
        private TStateMachine m_stateMachine;
        private SynchronizationContext m_resumeContext;
        private OnityAsyncStateMachineRunner<TStateMachine, T> m_nextPooled;

        private OnityAsyncStateMachineRunner()
        {
            m_moveNext = MoveNext;
        }

        public ref OnityAsyncStateMachineRunner<TStateMachine, T> NextPooled => ref m_nextPooled;

        /// <summary>
        /// Rents a runner, binds it to the builder field inside the state machine, and then copies
        /// the state machine so the copy already references its runner.
        /// </summary>
        /// <param name="stateMachine">The caller's state machine.</param>
        /// <param name="runnerField">The builder's runner field inside that state machine.</param>
        /// <returns>The bound runner.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OnityRunnerBase<T> Rent(
            ref TStateMachine stateMachine,
            ref OnityRunnerBase<T> runnerField)
        {
            if (!s_pool.TryPop(out OnityAsyncStateMachineRunner<TStateMachine, T> runner))
            {
                runner = new OnityAsyncStateMachineRunner<TStateMachine, T>();
            }

            runner.ResetRetired();
            runnerField = runner;
            runner.m_stateMachine = stateMachine;
            return runner;
        }

        protected override void ReleaseSource()
        {
            // The base retired the version in the compare-and-swap that released the source.
#if ONITY_RUNNER_RETURN_GATE && ENABLE_IL2CPP
            if (OnityRunnerReturnGate.RequestReturn(ref m_moveNextDepth))
            {
                ReturnToPool();
            }
#else
            ReturnToPool();
#endif
        }

        private void ReturnToPool()
        {
            // initobj: clearing the state machine needs no write barrier.
            m_stateMachine = default;
            if (m_executionContext != null)
            {
                m_executionContext = null;
            }

            if (m_resumeContext != null)
            {
                m_resumeContext = null;
            }

            s_pool.TryPush(this, OnityTaskSettings.s_runnerPoolCapacity);
        }

        private void MoveNext()
        {
#if ONITY_RUNNER_RETURN_GATE && ENABLE_IL2CPP
            OnityRunnerReturnGate.EnterFrame(ref m_moveNextDepth);
            try
            {
                MoveNextCore();
            }
            finally
            {
                if (OnityRunnerReturnGate.ExitFrame(ref m_moveNextDepth))
                {
                    ReturnToPool();
                }
            }
        }

        private void MoveNextCore()
        {
#endif
            ExecutionContext context = m_executionContext;
            if (context == null)
            {
                m_stateMachine.MoveNext();
                return;
            }

            MoveNextWithContext(context);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void MoveNextWithContext(ExecutionContext context)
        {
            m_executionContext = null;
            if (OnityAsyncExecutionContext.TryRunPreservingContext(context, s_moveNextInContext, this))
            {
                return;
            }

            m_resumeContext = SynchronizationContext.Current;
            try
            {
                ExecutionContext.Run(context, s_moveNextRestoringContext, this);
            }
            finally
            {
                context.Dispose();
            }
        }

        private static void MoveNextInContext(object state)
        {
            ((OnityAsyncStateMachineRunner<TStateMachine, T>)state).m_stateMachine.MoveNext();
        }

        private static void MoveNextRestoringContext(object state)
        {
            OnityAsyncStateMachineRunner<TStateMachine, T> runner =
                (OnityAsyncStateMachineRunner<TStateMachine, T>)state;
            SynchronizationContext resumeContext = runner.m_resumeContext;
            runner.m_resumeContext = null;

            if (!ReferenceEquals(SynchronizationContext.Current, resumeContext))
            {
                SynchronizationContext.SetSynchronizationContext(resumeContext);
            }

            runner.m_stateMachine.MoveNext();
        }
    }

    /// <summary>
    /// Observes a single-consumer native task for <c>Forget</c> without creating a .NET task bridge.
    /// </summary>
    internal sealed class OnityTaskForgetObserver
    {
        private readonly IOnityTaskSource m_source;
        private readonly int m_token;
        private readonly Action<Exception> m_exceptionHandler;
        private readonly int m_trackingId;

        private OnityTaskForgetObserver(
            IOnityTaskSource source, int token, Action<Exception> exceptionHandler, int trackingId)
        {
            m_source = source;
            m_token = token;
            m_exceptionHandler = exceptionHandler;
            m_trackingId = trackingId;
        }

        /// <summary>
        /// Registers the observer as the source's single native consumer. While task tracking is on, the
        /// task is also recorded as a native tracker row, without a .NET task bridge.
        /// </summary>
        /// <param name="source">Single-consumer source.</param>
        /// <param name="token">Token of the task value.</param>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public static void Observe(IOnityTaskSource source, int token, Action<Exception> exceptionHandler)
        {
            int trackingId = OnityTaskTracker.IsEnabled ? OnityTaskTracker.BeginNative("OnityTaskExtensions.Forget") : 0;
            OnityTaskForgetObserver observer = new OnityTaskForgetObserver(source, token, exceptionHandler, trackingId);
            source.OnCompleted(observer.Complete, token);
        }

        private void Complete()
        {
            try
            {
                m_source.GetResult(m_token);
                if (m_trackingId != 0)
                {
                    OnityTaskTracker.CompleteNative(m_trackingId, null);
                }
            }
            catch (Exception exception)
            {
                if (m_trackingId != 0)
                {
                    OnityTaskTracker.CompleteNative(m_trackingId, exception);
                }

                Report(exception, m_exceptionHandler);
            }
        }

        /// <summary>
        /// Delivers a fault of a forgotten task to its handler, or publishes it as unobserved through
        /// <see cref="OnityTaskScheduler"/>, which drops <see cref="OperationCanceledException"/> unless
        /// <see cref="OnityTaskScheduler.PropagateOperationCanceledException"/> is set. A handler that
        /// throws is published the same way.
        /// </summary>
        /// <param name="exception">Fault of the forgotten task.</param>
        /// <param name="exceptionHandler">Optional handler.</param>
        internal static void Report(Exception exception, Action<Exception> exceptionHandler)
        {
            if (exceptionHandler == null)
            {
                OnityTaskScheduler.PublishUnobservedException(exception);
                return;
            }

            try
            {
                exceptionHandler(exception);
            }
            catch (Exception handlerException)
            {
                OnityTaskScheduler.PublishUnobservedException(handlerException);
            }
        }
    }

    /// <summary>
    /// Observes a single-consumer typed native task for <c>Forget</c> without a .NET task bridge.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    internal sealed class OnityTaskForgetObserver<T>
    {
        private readonly IOnityTaskSource<T> m_source;
        private readonly int m_token;
        private readonly Action<Exception> m_exceptionHandler;
        private readonly int m_trackingId;

        private OnityTaskForgetObserver(
            IOnityTaskSource<T> source, int token, Action<Exception> exceptionHandler, int trackingId)
        {
            m_source = source;
            m_token = token;
            m_exceptionHandler = exceptionHandler;
            m_trackingId = trackingId;
        }

        /// <summary>
        /// Registers the observer as the source's single native consumer; see
        /// <see cref="OnityTaskForgetObserver.Observe"/>.
        /// </summary>
        /// <param name="source">Single-consumer source.</param>
        /// <param name="token">Token of the task value.</param>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public static void Observe(IOnityTaskSource<T> source, int token, Action<Exception> exceptionHandler)
        {
            int trackingId = OnityTaskTracker.IsEnabled ? OnityTaskTracker.BeginNative("OnityTaskExtensions.Forget<T>") : 0;
            OnityTaskForgetObserver<T> observer = new OnityTaskForgetObserver<T>(source, token, exceptionHandler, trackingId);
            source.OnCompleted(observer.Complete, token);
        }

        private void Complete()
        {
            try
            {
                m_source.GetResult(m_token);
                if (m_trackingId != 0)
                {
                    OnityTaskTracker.CompleteNative(m_trackingId, null);
                }
            }
            catch (Exception exception)
            {
                if (m_trackingId != 0)
                {
                    OnityTaskTracker.CompleteNative(m_trackingId, exception);
                }

                OnityTaskForgetObserver.Report(exception, m_exceptionHandler);
            }
        }
    }
}
