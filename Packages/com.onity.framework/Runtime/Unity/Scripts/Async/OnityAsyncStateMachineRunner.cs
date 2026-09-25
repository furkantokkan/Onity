using System;
using System.Collections.Generic;
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
    internal static class OnityAsyncExecutionContext
    {
        /// <summary>
        /// Captures the current execution context, including <see cref="AsyncLocal{T}"/> values,
        /// without copying the thread's synchronization context. The public
        /// <see cref="ExecutionContext.Capture"/> copies whatever synchronization context the
        /// thread currently holds, so the context is detached for the duration of the capture.
        /// Returns null when flow is suppressed.
        /// </summary>
        /// <returns>The captured context, or null when flow is suppressed.</returns>
        public static ExecutionContext Capture()
        {
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
    }

    /// <summary>
    /// Pooled state machine runner for suspended untyped async methods. The state machine is held
    /// by value, one cached delegate resumes it, and the runner is the method's native task source.
    /// </summary>
    /// <typeparam name="TStateMachine">Compiler-generated state machine type.</typeparam>
    internal sealed class OnityAsyncStateMachineRunner<TStateMachine> :
        OnityTaskSourceBase, IOnityAsyncStateMachineRunner
        where TStateMachine : IAsyncStateMachine
    {
        private const int k_maxPoolSize = 128;

        private static readonly Stack<OnityAsyncStateMachineRunner<TStateMachine>> s_pool =
            new Stack<OnityAsyncStateMachineRunner<TStateMachine>>(8);
        private static readonly ContextCallback s_moveNextInContext = MoveNextInContext;

        private readonly Action m_moveNext;
#if ENABLE_IL2CPP
        private readonly Action m_returnToPool;
#endif
        private TStateMachine m_stateMachine;
        private ExecutionContext m_executionContext;
        private SynchronizationContext m_resumeContext;

        private OnityAsyncStateMachineRunner()
        {
            m_moveNext = MoveNext;
#if ENABLE_IL2CPP
            m_returnToPool = ReturnToPool;
#endif
        }

        public Action MoveNextAction => m_moveNext;

        public OnityTask Task => new OnityTask((IOnityTaskSource)this);

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
            OnityAsyncStateMachineRunner<TStateMachine> runner;
            lock (s_pool)
            {
                runner = s_pool.Count > 0
                    ? s_pool.Pop()
                    : new OnityAsyncStateMachineRunner<TStateMachine>();
            }

            runner.Reset(CancellationToken.None);
            runnerField = runner;
            runner.m_stateMachine = stateMachine;
        }

        public void SetResult()
        {
            TrySetResult();
        }

        public void SetException(Exception exception)
        {
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
            m_stateMachine = default;
            m_executionContext = null;
            m_resumeContext = null;
            lock (s_pool)
            {
                if (s_pool.Count < k_maxPoolSize)
                {
                    s_pool.Push(this);
                }
            }
        }

        private void MoveNext()
        {
            ExecutionContext context = m_executionContext;
            if (context == null)
            {
                m_stateMachine.MoveNext();
                return;
            }

            m_executionContext = null;
            m_resumeContext = SynchronizationContext.Current;
            try
            {
                ExecutionContext.Run(context, s_moveNextInContext, this);
            }
            finally
            {
                context.Dispose();
            }
        }

        private static void MoveNextInContext(object state)
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
        OnityTaskSourceBase<T>, IOnityAsyncStateMachineRunner<T>
        where TStateMachine : IAsyncStateMachine
    {
        private const int k_maxPoolSize = 128;

        private static readonly Stack<OnityAsyncStateMachineRunner<TStateMachine, T>> s_pool =
            new Stack<OnityAsyncStateMachineRunner<TStateMachine, T>>(8);
        private static readonly ContextCallback s_moveNextInContext = MoveNextInContext;

        private readonly Action m_moveNext;
#if ENABLE_IL2CPP
        private readonly Action m_returnToPool;
#endif
        private TStateMachine m_stateMachine;
        private ExecutionContext m_executionContext;
        private SynchronizationContext m_resumeContext;

        private OnityAsyncStateMachineRunner()
        {
            m_moveNext = MoveNext;
#if ENABLE_IL2CPP
            m_returnToPool = ReturnToPool;
#endif
        }

        public Action MoveNextAction => m_moveNext;

        public OnityTask<T> Task => new OnityTask<T>((IOnityTaskSource<T>)this);

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
            OnityAsyncStateMachineRunner<TStateMachine, T> runner;
            lock (s_pool)
            {
                runner = s_pool.Count > 0
                    ? s_pool.Pop()
                    : new OnityAsyncStateMachineRunner<TStateMachine, T>();
            }

            runner.Reset(CancellationToken.None);
            runnerField = runner;
            runner.m_stateMachine = stateMachine;
        }

        public void SetResult(T result)
        {
            TrySetResult(result);
        }

        public void SetException(Exception exception)
        {
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
            m_stateMachine = default;
            m_executionContext = null;
            m_resumeContext = null;
            lock (s_pool)
            {
                if (s_pool.Count < k_maxPoolSize)
                {
                    s_pool.Push(this);
                }
            }
        }

        private void MoveNext()
        {
            ExecutionContext context = m_executionContext;
            if (context == null)
            {
                m_stateMachine.MoveNext();
                return;
            }

            m_executionContext = null;
            m_resumeContext = SynchronizationContext.Current;
            try
            {
                ExecutionContext.Run(context, s_moveNextInContext, this);
            }
            finally
            {
                context.Dispose();
            }
        }

        private static void MoveNextInContext(object state)
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
