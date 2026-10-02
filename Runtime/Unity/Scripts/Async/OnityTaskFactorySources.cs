using System;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Maps the public <see cref="OnityTaskStatus"/> onto the internal source status.
    /// </summary>
    internal static class OnityTaskStatusMapping
    {
        /// <summary>
        /// Converts a public status to the status vocabulary of the task sources.
        /// </summary>
        /// <param name="status">Public status.</param>
        /// <returns>Matching source status.</returns>
        public static OnityTaskSourceStatus ToSourceStatus(OnityTaskStatus status)
        {
            switch (status)
            {
                case OnityTaskStatus.Succeeded:
                    return OnityTaskSourceStatus.Succeeded;
                case OnityTaskStatus.Faulted:
                    return OnityTaskSourceStatus.Faulted;
                case OnityTaskStatus.Canceled:
                    return OnityTaskSourceStatus.Canceled;
                default:
                    return OnityTaskSourceStatus.Pending;
            }
        }
    }

    /// <summary>
    /// Source behind <see cref="OnityTask.Defer(Func{OnityTask})"/> and its stateful form. The task
    /// factory runs once, when the status is first read, a continuation is registered, or a .NET
    /// bridge is requested. The created task is preserved, so every consumer observes the same
    /// outcome and the deferred task can be awaited any number of times.
    /// </summary>
    internal abstract class OnityDeferTaskSourceBase : IOnityTaskSource, IOnityMultiConsumerTaskSource
    {
        private const int k_version = 1;
        private const int k_creating = 1;
        private const int k_ready = 2;

        private readonly object m_gate = new object();
        private OnityTask m_task;
        private int m_phase;

        int IOnityTaskSource.Version => k_version;

        /// <summary>Runs the factory. Called at most once.</summary>
        protected abstract OnityTask CreateTask();

        OnityTaskSourceStatus IOnityTaskSource.GetStatus(int token)
        {
            ValidateToken(token);
            return OnityTaskStatusMapping.ToSourceStatus(Resolve().Status);
        }

        Task IOnityTaskSource.AsTask(int token)
        {
            ValidateToken(token);
            return Resolve().AsTask();
        }

        void IOnityTaskSource.OnCompleted(Action continuation, int token)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            ValidateToken(token);
            Resolve().GetAwaiter().UnsafeOnCompleted(continuation);
        }

        void IOnityTaskSource.GetResult(int token)
        {
            ValidateToken(token);
            Resolve().GetAwaiter().GetResult();
        }

        private OnityTask Resolve()
        {
            if (Volatile.Read(ref m_phase) == k_ready)
            {
                return m_task;
            }

            lock (m_gate)
            {
                if (m_phase == k_ready)
                {
                    return m_task;
                }

                if (m_phase == k_creating)
                {
                    throw new InvalidOperationException(
                        "The deferred task factory used the task it is creating.");
                }

                m_phase = k_creating;
                try
                {
                    m_task = CreateTask().Preserve();
                }
                catch (OperationCanceledException exception)
                {
                    m_task = OnityTask.FromCanceled(exception.CancellationToken);
                }
                catch (Exception exception)
                {
                    m_task = OnityTask.FromException(exception);
                }
                finally
                {
                    Volatile.Write(ref m_phase, k_ready);
                }

                return m_task;
            }
        }

        private static void ValidateToken(int token)
        {
            if (token != k_version)
            {
                throw new InvalidOperationException("The OnityTask source token is invalid.");
            }
        }
    }

    /// <summary>Deferred untyped task created by a plain factory.</summary>
    internal sealed class OnityDeferTaskSource : OnityDeferTaskSourceBase
    {
        private Func<OnityTask> m_factory;

        public OnityDeferTaskSource(Func<OnityTask> factory)
        {
            m_factory = factory;
        }

        protected override OnityTask CreateTask()
        {
            Func<OnityTask> factory = m_factory;
            m_factory = null;
            return factory();
        }
    }

    /// <summary>Deferred untyped task created by a factory that receives a state value.</summary>
    /// <typeparam name="TState">State type.</typeparam>
    internal sealed class OnityDeferStateTaskSource<TState> : OnityDeferTaskSourceBase
    {
        private Func<TState, OnityTask> m_factory;
        private TState m_argument;

        public OnityDeferStateTaskSource(TState argument, Func<TState, OnityTask> factory)
        {
            m_argument = argument;
            m_factory = factory;
        }

        protected override OnityTask CreateTask()
        {
            Func<TState, OnityTask> factory = m_factory;
            TState argument = m_argument;
            m_factory = null;
            m_argument = default;
            return factory(argument);
        }
    }

    /// <summary>
    /// Typed counterpart of <see cref="OnityDeferTaskSourceBase"/>.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    internal abstract class OnityDeferTaskSourceBase<T> : IOnityTaskSource<T>, IOnityMultiConsumerTaskSource
    {
        private const int k_version = 1;
        private const int k_creating = 1;
        private const int k_ready = 2;

        private readonly object m_gate = new object();
        private OnityTask<T> m_task;
        private int m_phase;

        int IOnityTaskSource<T>.Version => k_version;

        /// <summary>Runs the factory. Called at most once.</summary>
        protected abstract OnityTask<T> CreateTask();

        OnityTaskSourceStatus IOnityTaskSource<T>.GetStatus(int token)
        {
            ValidateToken(token);
            return OnityTaskStatusMapping.ToSourceStatus(Resolve().Status);
        }

        Task<T> IOnityTaskSource<T>.AsTask(int token)
        {
            ValidateToken(token);
            return Resolve().AsTask();
        }

        void IOnityTaskSource<T>.OnCompleted(Action continuation, int token)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            ValidateToken(token);
            Resolve().GetAwaiter().UnsafeOnCompleted(continuation);
        }

        T IOnityTaskSource<T>.GetResult(int token)
        {
            ValidateToken(token);
            return Resolve().GetAwaiter().GetResult();
        }

        private OnityTask<T> Resolve()
        {
            if (Volatile.Read(ref m_phase) == k_ready)
            {
                return m_task;
            }

            lock (m_gate)
            {
                if (m_phase == k_ready)
                {
                    return m_task;
                }

                if (m_phase == k_creating)
                {
                    throw new InvalidOperationException(
                        "The deferred task factory used the task it is creating.");
                }

                m_phase = k_creating;
                try
                {
                    m_task = CreateTask().Preserve();
                }
                catch (OperationCanceledException exception)
                {
                    m_task = OnityTask<T>.FromCanceled(exception.CancellationToken);
                }
                catch (Exception exception)
                {
                    m_task = OnityTask<T>.FromException(exception);
                }
                finally
                {
                    Volatile.Write(ref m_phase, k_ready);
                }

                return m_task;
            }
        }

        private static void ValidateToken(int token)
        {
            if (token != k_version)
            {
                throw new InvalidOperationException("The OnityTask source token is invalid.");
            }
        }
    }

    /// <summary>Deferred typed task created by a plain factory.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    internal sealed class OnityDeferResultTaskSource<T> : OnityDeferTaskSourceBase<T>
    {
        private Func<OnityTask<T>> m_factory;

        public OnityDeferResultTaskSource(Func<OnityTask<T>> factory)
        {
            m_factory = factory;
        }

        protected override OnityTask<T> CreateTask()
        {
            Func<OnityTask<T>> factory = m_factory;
            m_factory = null;
            return factory();
        }
    }

    /// <summary>Deferred typed task created by a factory that receives a state value.</summary>
    /// <typeparam name="TState">State type.</typeparam>
    /// <typeparam name="T">Result type.</typeparam>
    internal sealed class OnityDeferStateResultTaskSource<TState, T> : OnityDeferTaskSourceBase<T>
    {
        private Func<TState, OnityTask<T>> m_factory;
        private TState m_argument;

        public OnityDeferStateResultTaskSource(TState argument, Func<TState, OnityTask<T>> factory)
        {
            m_argument = argument;
            m_factory = factory;
        }

        protected override OnityTask<T> CreateTask()
        {
            Func<TState, OnityTask<T>> factory = m_factory;
            TState argument = m_argument;
            m_factory = null;
            m_argument = default;
            return factory(argument);
        }
    }

    /// <summary>
    /// Owns one never-completing untyped task. The only way it completes is the cancellation of the
    /// token it was created with, which registers exactly once and stops there.
    /// </summary>
    internal sealed class OnityNeverTaskSource
    {
        private static readonly Action<object> s_cancel = Cancel;

        private readonly OnityTaskCompletionSource m_source = new OnityTaskCompletionSource();
        private readonly CancellationToken m_token;

        private OnityNeverTaskSource(CancellationToken token)
        {
            m_token = token;
        }

        /// <summary>Creates the task for a token.</summary>
        /// <param name="token">Token whose cancellation completes the task.</param>
        /// <returns>A task that never completes unless the token is canceled.</returns>
        public static OnityTask Create(CancellationToken token)
        {
            OnityNeverTaskSource never = new OnityNeverTaskSource(token);
            if (token.CanBeCanceled)
            {
                // An already canceled token runs the callback inline, before the registration returns.
                token.RegisterWithoutCaptureExecutionContext(s_cancel, never);
            }

            return never.m_source.Task;
        }

        private static void Cancel(object state)
        {
            OnityNeverTaskSource never = (OnityNeverTaskSource)state;
            never.m_source.TrySetCanceled(never.m_token);
        }
    }

    /// <summary>
    /// Typed counterpart of <see cref="OnityNeverTaskSource"/>.
    /// </summary>
    /// <typeparam name="T">Result type of the task that never completes.</typeparam>
    internal sealed class OnityNeverTaskSource<T>
    {
        private static readonly Action<object> s_cancel = Cancel;

        private readonly OnityTaskCompletionSource<T> m_source = new OnityTaskCompletionSource<T>();
        private readonly CancellationToken m_token;

        private OnityNeverTaskSource(CancellationToken token)
        {
            m_token = token;
        }

        /// <summary>Creates the task for a token.</summary>
        /// <param name="token">Token whose cancellation completes the task.</param>
        /// <returns>A task that never completes unless the token is canceled.</returns>
        public static OnityTask<T> Create(CancellationToken token)
        {
            OnityNeverTaskSource<T> never = new OnityNeverTaskSource<T>(token);
            if (token.CanBeCanceled)
            {
                token.RegisterWithoutCaptureExecutionContext(s_cancel, never);
            }

            return never.m_source.Task;
        }

        private static void Cancel(object state)
        {
            OnityNeverTaskSource<T> never = (OnityNeverTaskSource<T>)state;
            never.m_source.TrySetCanceled(never.m_token);
        }
    }
}
