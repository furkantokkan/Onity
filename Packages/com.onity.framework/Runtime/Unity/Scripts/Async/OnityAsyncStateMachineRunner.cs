using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Pooled runner behind a suspended <c>async OnityTask</c> method. Untyped builders bind to it
    /// on the first suspension.
    /// </summary>
    internal interface IOnityAsyncStateMachineRunner
    {
        Action MoveNextAction { get; }

        OnityTask Task { get; }

        void SetResult();

        void SetException(Exception exception);

        void CaptureExecutionContext();
    }

    /// <summary>
    /// Pooled runner behind a suspended <c>async OnityTask&lt;T&gt;</c> method.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    internal interface IOnityAsyncStateMachineRunner<T>
    {
        Action MoveNextAction { get; }

        OnityTask<T> Task { get; }

        void SetResult(T result);

        void SetException(Exception exception);

        void CaptureExecutionContext();
    }

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
    /// Runner pool guarded by one compare-and-swap gate instead of a monitor. A rent or return that
    /// finds the gate taken does not wait: the rent allocates and the return lets the runner be
    /// collected, which keeps the uncontended path to two interlocked operations.
    /// </summary>
    /// <typeparam name="T">Runner type.</typeparam>
    internal struct OnityRunnerPool<T> where T : class, IOnityPooledRunner<T>
    {
        private int m_gate;
        private int m_size;
        private T m_head;

        public bool TryPop(out T runner)
        {
            if (Interlocked.CompareExchange(ref m_gate, 1, 0) == 0)
            {
                T head = m_head;
                if (head != null)
                {
                    ref T next = ref head.NextPooled;
                    m_head = next;
                    next = null;
                    m_size--;
                    Volatile.Write(ref m_gate, 0);
                    runner = head;
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
                if (m_size < capacity)
                {
                    runner.NextPooled = m_head;
                    m_head = runner;
                    m_size++;
                    Volatile.Write(ref m_gate, 0);
                    return true;
                }

                Volatile.Write(ref m_gate, 0);
            }

            return false;
        }
    }

    /// <summary>
    /// Pooled state machine runner for suspended untyped async methods. The state machine is held
    /// by value, one cached delegate resumes it, and the runner is the method's native task source.
    /// </summary>
    /// <typeparam name="TStateMachine">Compiler-generated state machine type.</typeparam>
    internal sealed class OnityAsyncStateMachineRunner<TStateMachine> :
        OnityTaskSourceBase, IOnityAsyncStateMachineRunner, IOnityPooledRunner<OnityAsyncStateMachineRunner<TStateMachine>>
        where TStateMachine : IAsyncStateMachine
    {
        private static OnityRunnerPool<OnityAsyncStateMachineRunner<TStateMachine>> s_pool;
        private static readonly ContextCallback s_moveNextInContext = MoveNextInContext;
        private static readonly ContextCallback s_moveNextRestoringContext = MoveNextRestoringContext;

        private readonly Action m_moveNext;
#if ENABLE_IL2CPP
        private readonly Action m_returnToPool;
        private int m_moveNextDepth;
#endif
        private TStateMachine m_stateMachine;
        private ExecutionContext m_executionContext;
        private SynchronizationContext m_resumeContext;
        private OnityAsyncStateMachineRunner<TStateMachine> m_nextPooled;

        private OnityAsyncStateMachineRunner()
        {
            m_moveNext = MoveNext;
#if ENABLE_IL2CPP
            m_returnToPool = ReturnToPool;
#endif
        }

        public Action MoveNextAction => m_moveNext;

        public OnityTask Task => new OnityTask((IOnityTaskSource)this);

        public ref OnityAsyncStateMachineRunner<TStateMachine> NextPooled => ref m_nextPooled;

        /// <summary>
        /// Rents a runner, binds it to the builder field inside the state machine, and then copies
        /// the state machine so the copy already references its runner.
        /// </summary>
        /// <param name="stateMachine">The caller's state machine.</param>
        /// <param name="runnerField">The builder's runner field inside that state machine.</param>
        public static void Rent(
            ref TStateMachine stateMachine,
            ref IOnityAsyncStateMachineRunner runnerField)
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
        }

        public void SetResult()
        {
            TrySetResult();
        }

        public void SetException(Exception exception)
        {
            // A method that faults after capturing, for example when its awaiter rejected the
            // registration, never resumes; drop the captured context now rather than at pool return.
            m_executionContext = null;
            if (exception is OperationCanceledException canceled)
            {
                TrySetCanceled(canceled);
            }
            else
            {
                TrySetException(exception);
            }
        }

        public void CaptureExecutionContext()
        {
            m_executionContext = OnityAsyncExecutionContext.Capture();
        }

        protected override void ReleaseSource()
        {
            InvalidateVersion();
#if ENABLE_IL2CPP
            // IL2CPP may copy a struct back after a method call on it, so the state machine is
            // cleared and the runner pooled only after the producing MoveNext has unwound.
            OnityTaskMainThreadDispatcher.Enqueue(m_returnToPool, 0);
#else
            ReturnToPool();
#endif
        }

        private void ReturnToPool()
        {
#if ENABLE_IL2CPP
            // A worker that completed the method may still be unwinding its MoveNext call; wait
            // for a later drain so the copy-back cannot overwrite the next rental.
            if (Volatile.Read(ref m_moveNextDepth) != 0)
            {
                OnityTaskMainThreadDispatcher.Enqueue(m_returnToPool, 0);
                return;
            }
#endif
            m_stateMachine = default;
            m_executionContext = null;
            m_resumeContext = null;
            s_pool.TryPush(this, OnityTask.RunnerPoolCapacity);
        }

        private void MoveNext()
        {
#if ENABLE_IL2CPP
            Interlocked.Increment(ref m_moveNextDepth);
            try
            {
                MoveNextCore();
            }
            finally
            {
                Interlocked.Decrement(ref m_moveNextDepth);
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
    /// Pooled state machine runner for suspended typed async methods.
    /// </summary>
    /// <typeparam name="TStateMachine">Compiler-generated state machine type.</typeparam>
    /// <typeparam name="T">Result type.</typeparam>
    internal sealed class OnityAsyncStateMachineRunner<TStateMachine, T> :
        OnityTaskSourceBase<T>, IOnityAsyncStateMachineRunner<T>, IOnityPooledRunner<OnityAsyncStateMachineRunner<TStateMachine, T>>
        where TStateMachine : IAsyncStateMachine
    {
        private static OnityRunnerPool<OnityAsyncStateMachineRunner<TStateMachine, T>> s_pool;
        private static readonly ContextCallback s_moveNextInContext = MoveNextInContext;
        private static readonly ContextCallback s_moveNextRestoringContext = MoveNextRestoringContext;

        private readonly Action m_moveNext;
#if ENABLE_IL2CPP
        private readonly Action m_returnToPool;
        private int m_moveNextDepth;
#endif
        private TStateMachine m_stateMachine;
        private ExecutionContext m_executionContext;
        private SynchronizationContext m_resumeContext;
        private OnityAsyncStateMachineRunner<TStateMachine, T> m_nextPooled;

        private OnityAsyncStateMachineRunner()
        {
            m_moveNext = MoveNext;
#if ENABLE_IL2CPP
            m_returnToPool = ReturnToPool;
#endif
        }

        public Action MoveNextAction => m_moveNext;

        public OnityTask<T> Task => new OnityTask<T>((IOnityTaskSource<T>)this);

        public ref OnityAsyncStateMachineRunner<TStateMachine, T> NextPooled => ref m_nextPooled;

        /// <summary>
        /// Rents a runner, binds it to the builder field inside the state machine, and then copies
        /// the state machine so the copy already references its runner.
        /// </summary>
        /// <param name="stateMachine">The caller's state machine.</param>
        /// <param name="runnerField">The builder's runner field inside that state machine.</param>
        public static void Rent(
            ref TStateMachine stateMachine,
            ref IOnityAsyncStateMachineRunner<T> runnerField)
        {
            if (!s_pool.TryPop(out OnityAsyncStateMachineRunner<TStateMachine, T> runner))
            {
                runner = new OnityAsyncStateMachineRunner<TStateMachine, T>();
            }

            runner.ResetRetired();
            runnerField = runner;
            runner.m_stateMachine = stateMachine;
        }

        public void SetResult(T result)
        {
            TrySetResult(result);
        }

        public void SetException(Exception exception)
        {
            // A method that faults after capturing, for example when its awaiter rejected the
            // registration, never resumes; drop the captured context now rather than at pool return.
            m_executionContext = null;
            if (exception is OperationCanceledException canceled)
            {
                TrySetCanceled(canceled);
            }
            else
            {
                TrySetException(exception);
            }
        }

        public void CaptureExecutionContext()
        {
            m_executionContext = OnityAsyncExecutionContext.Capture();
        }

        protected override void ReleaseSource()
        {
            InvalidateVersion();
#if ENABLE_IL2CPP
            OnityTaskMainThreadDispatcher.Enqueue(m_returnToPool, 0);
#else
            ReturnToPool();
#endif
        }

        private void ReturnToPool()
        {
#if ENABLE_IL2CPP
            // A worker that completed the method may still be unwinding its MoveNext call; wait
            // for a later drain so the copy-back cannot overwrite the next rental.
            if (Volatile.Read(ref m_moveNextDepth) != 0)
            {
                OnityTaskMainThreadDispatcher.Enqueue(m_returnToPool, 0);
                return;
            }
#endif
            m_stateMachine = default;
            m_executionContext = null;
            m_resumeContext = null;
            s_pool.TryPush(this, OnityTask.RunnerPoolCapacity);
        }

        private void MoveNext()
        {
#if ENABLE_IL2CPP
            Interlocked.Increment(ref m_moveNextDepth);
            try
            {
                MoveNextCore();
            }
            finally
            {
                Interlocked.Decrement(ref m_moveNextDepth);
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

        private OnityTaskForgetObserver(IOnityTaskSource source, int token, Action<Exception> exceptionHandler)
        {
            m_source = source;
            m_token = token;
            m_exceptionHandler = exceptionHandler;
        }

        /// <summary>
        /// Registers the observer as the source's single native consumer.
        /// </summary>
        /// <param name="source">Single-consumer source.</param>
        /// <param name="token">Token of the task value.</param>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public static void Observe(IOnityTaskSource source, int token, Action<Exception> exceptionHandler)
        {
            OnityTaskForgetObserver observer = new OnityTaskForgetObserver(source, token, exceptionHandler);
            source.OnCompleted(observer.Complete, token);
        }

        private void Complete()
        {
            try
            {
                m_source.GetResult(m_token);
            }
            catch (Exception exception)
            {
                Report(exception, m_exceptionHandler);
            }
        }

        internal static void Report(Exception exception, Action<Exception> exceptionHandler)
        {
            if (exceptionHandler == null)
            {
                Debug.LogException(exception);
                return;
            }

            try
            {
                exceptionHandler(exception);
            }
            catch (Exception handlerException)
            {
                Debug.LogException(handlerException);
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

        private OnityTaskForgetObserver(IOnityTaskSource<T> source, int token, Action<Exception> exceptionHandler)
        {
            m_source = source;
            m_token = token;
            m_exceptionHandler = exceptionHandler;
        }

        /// <summary>
        /// Registers the observer as the source's single native consumer.
        /// </summary>
        /// <param name="source">Single-consumer source.</param>
        /// <param name="token">Token of the task value.</param>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public static void Observe(IOnityTaskSource<T> source, int token, Action<Exception> exceptionHandler)
        {
            OnityTaskForgetObserver<T> observer = new OnityTaskForgetObserver<T>(source, token, exceptionHandler);
            source.OnCompleted(observer.Complete, token);
        }

        private void Complete()
        {
            try
            {
                m_source.GetResult(m_token);
            }
            catch (Exception exception)
            {
                OnityTaskForgetObserver.Report(exception, m_exceptionHandler);
            }
        }
    }
}
