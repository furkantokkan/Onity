using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// A shared, lazily started <see cref="OnityTask"/>. The factory runs once, on the first access
    /// of <see cref="Task"/> or the first await, and every consumer observes the same outcome. The
    /// task can be awaited any number of times, from any thread.
    /// </summary>
    /// <remarks>
    /// The factory runs on the thread of that first access, under a lock, so it must not wait for
    /// another thread that reads the same lazy value. A factory that throws completes the shared
    /// task as faulted instead of throwing from the first access. Cancellation of the factory's task
    /// completes the shared task as canceled.
    /// </remarks>
    public sealed class OnityAsyncLazy
    {
        private readonly OnityTaskCompletionSource m_completion = new OnityTaskCompletionSource();
        private readonly object m_gate;

        private Func<OnityTask> m_factory;
        private OnityTaskAwaiter m_awaiter;
        private Action m_onCompleted;
        private bool m_creating;
        private bool m_initialized;

        /// <summary>
        /// Creates a lazy task that starts when it is first used.
        /// </summary>
        /// <param name="taskFactory">Factory that starts the work.</param>
        /// <exception cref="ArgumentNullException"><paramref name="taskFactory"/> is null.</exception>
        public OnityAsyncLazy(Func<OnityTask> taskFactory)
        {
            m_factory = taskFactory ?? throw new ArgumentNullException(nameof(taskFactory));
            m_gate = new object();
        }

        /// <summary>
        /// Wraps a task that is already running. The task is consumed by this call.
        /// </summary>
        /// <param name="task">Running task to share.</param>
        internal OnityAsyncLazy(OnityTask task)
        {
            m_initialized = true;
            Attach(task);
        }

        /// <summary>
        /// Gets the shared task, starting the factory on the first access. The task can be awaited
        /// any number of times.
        /// </summary>
        public OnityTask Task
        {
            get
            {
                EnsureInitialized();
                return m_completion.Task;
            }
        }

        /// <summary>
        /// Returns the awaiter of <see cref="Task"/>, starting the factory on the first await.
        /// </summary>
        /// <returns>Awaiter of the shared task.</returns>
        public OnityTaskAwaiter GetAwaiter()
        {
            return Task.GetAwaiter();
        }

        private void EnsureInitialized()
        {
            if (Volatile.Read(ref m_initialized))
            {
                return;
            }

            lock (m_gate)
            {
                if (Volatile.Read(ref m_initialized))
                {
                    return;
                }

                if (m_creating)
                {
                    throw new InvalidOperationException("The lazy factory used the lazy value it is creating.");
                }

                m_creating = true;
                Func<OnityTask> factory = m_factory;
                m_factory = null;
                try
                {
                    Attach(factory());
                }
                catch (Exception exception)
                {
                    m_completion.TrySetException(exception);
                }
                finally
                {
                    m_creating = false;
                    Volatile.Write(ref m_initialized, true);
                }
            }
        }

        private void Attach(OnityTask task)
        {
            OnityTaskAwaiter awaiter = task.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                Complete(awaiter);
                return;
            }

            m_awaiter = awaiter;
            m_onCompleted = OnTaskCompleted;
            awaiter.UnsafeOnCompleted(m_onCompleted);
        }

        private void OnTaskCompleted()
        {
            OnityTaskAwaiter awaiter = m_awaiter;
            m_awaiter = default;
            m_onCompleted = null;
            Complete(awaiter);
        }

        private void Complete(OnityTaskAwaiter awaiter)
        {
            try
            {
                awaiter.GetResult();
                m_completion.TrySetResult();
            }
            catch (Exception exception)
            {
                m_completion.TrySetException(exception);
            }
        }
    }

    /// <summary>
    /// A shared, lazily started <see cref="OnityTask{T}"/>. The factory runs once, on the first
    /// access of <see cref="Task"/> or the first await, and every consumer observes the same
    /// outcome. The task can be awaited any number of times, from any thread.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <remarks>
    /// The factory runs on the thread of that first access, under a lock, so it must not wait for
    /// another thread that reads the same lazy value. A factory that throws completes the shared
    /// task as faulted instead of throwing from the first access. Cancellation of the factory's task
    /// completes the shared task as canceled.
    /// </remarks>
    public sealed class OnityAsyncLazy<T>
    {
        private readonly OnityTaskCompletionSource<T> m_completion = new OnityTaskCompletionSource<T>();
        private readonly object m_gate;

        private Func<OnityTask<T>> m_factory;
        private OnityTaskAwaiter<T> m_awaiter;
        private Action m_onCompleted;
        private bool m_creating;
        private bool m_initialized;

        /// <summary>
        /// Creates a lazy task that starts when it is first used.
        /// </summary>
        /// <param name="taskFactory">Factory that starts the work.</param>
        /// <exception cref="ArgumentNullException"><paramref name="taskFactory"/> is null.</exception>
        public OnityAsyncLazy(Func<OnityTask<T>> taskFactory)
        {
            m_factory = taskFactory ?? throw new ArgumentNullException(nameof(taskFactory));
            m_gate = new object();
        }

        /// <summary>
        /// Wraps a task that is already running. The task is consumed by this call.
        /// </summary>
        /// <param name="task">Running task to share.</param>
        internal OnityAsyncLazy(OnityTask<T> task)
        {
            m_initialized = true;
            Attach(task);
        }

        /// <summary>
        /// Gets the shared task, starting the factory on the first access. The task can be awaited
        /// any number of times.
        /// </summary>
        public OnityTask<T> Task
        {
            get
            {
                EnsureInitialized();
                return m_completion.Task;
            }
        }

        /// <summary>
        /// Returns the awaiter of <see cref="Task"/>, starting the factory on the first await.
        /// </summary>
        /// <returns>Awaiter of the shared task.</returns>
        public OnityTaskAwaiter<T> GetAwaiter()
        {
            return Task.GetAwaiter();
        }

        private void EnsureInitialized()
        {
            if (Volatile.Read(ref m_initialized))
            {
                return;
            }

            lock (m_gate)
            {
                if (Volatile.Read(ref m_initialized))
                {
                    return;
                }

                if (m_creating)
                {
                    throw new InvalidOperationException("The lazy factory used the lazy value it is creating.");
                }

                m_creating = true;
                Func<OnityTask<T>> factory = m_factory;
                m_factory = null;
                try
                {
                    Attach(factory());
                }
                catch (Exception exception)
                {
                    m_completion.TrySetException(exception);
                }
                finally
                {
                    m_creating = false;
                    Volatile.Write(ref m_initialized, true);
                }
            }
        }

        private void Attach(OnityTask<T> task)
        {
            OnityTaskAwaiter<T> awaiter = task.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                Complete(awaiter);
                return;
            }

            m_awaiter = awaiter;
            m_onCompleted = OnTaskCompleted;
            awaiter.UnsafeOnCompleted(m_onCompleted);
        }

        private void OnTaskCompleted()
        {
            OnityTaskAwaiter<T> awaiter = m_awaiter;
            m_awaiter = default;
            m_onCompleted = null;
            Complete(awaiter);
        }

        private void Complete(OnityTaskAwaiter<T> awaiter)
        {
            try
            {
                m_completion.TrySetResult(awaiter.GetResult());
            }
            catch (Exception exception)
            {
                m_completion.TrySetException(exception);
            }
        }
    }
}
