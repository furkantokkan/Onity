using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Task factories: lambda-friendly creation, deferred creation, never-completing tasks and
    /// lazily started shared tasks. None of these members requires Unity's main thread by itself;
    /// threading follows the task that the supplied factory creates.
    /// </summary>
    public readonly partial struct OnityTask
    {
        /// <summary>
        /// Creates a faulted typed task without naming the generic struct.
        /// </summary>
        /// <typeparam name="T">Result type of the task.</typeparam>
        /// <param name="exception">Failure exception.</param>
        /// <returns>A faulted task, as <see cref="OnityTask{T}.FromException(Exception)"/> creates.</returns>
        public static OnityTask<T> FromException<T>(Exception exception)
        {
            return OnityTask<T>.FromException(exception);
        }

        /// <summary>
        /// Creates a canceled typed task without naming the generic struct.
        /// </summary>
        /// <typeparam name="T">Result type of the task.</typeparam>
        /// <param name="cancellationToken">Cancellation token reported by the task.</param>
        /// <returns>A canceled task, as <see cref="OnityTask{T}.FromCanceled(CancellationToken)"/> creates.</returns>
        public static OnityTask<T> FromCanceled<T>(CancellationToken cancellationToken)
        {
            return OnityTask<T>.FromCanceled(cancellationToken);
        }

        /// <summary>
        /// Runs a factory immediately and returns its task. It exists to write an async lambda as an
        /// expression, for example <c>OnityTask.Create(async () =&gt; { ... })</c>.
        /// An exception thrown synchronously by the factory propagates to the caller.
        /// </summary>
        /// <param name="factory">Factory that starts the work.</param>
        /// <returns>The task the factory returned.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityTask Create(Func<OnityTask> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return factory();
        }

        /// <summary>
        /// Runs a factory immediately with a cancellation token and returns its task.
        /// An exception thrown synchronously by the factory propagates to the caller.
        /// </summary>
        /// <param name="factory">Factory that starts the work.</param>
        /// <param name="cancellationToken">Token passed to the factory.</param>
        /// <returns>The task the factory returned.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityTask Create(
            Func<CancellationToken, OnityTask> factory,
            CancellationToken cancellationToken)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return factory(cancellationToken);
        }

        /// <summary>
        /// Runs a factory immediately with a state argument and returns its task, so a static
        /// method or a non-capturing lambda can start the work without a closure.
        /// An exception thrown synchronously by the factory propagates to the caller.
        /// </summary>
        /// <typeparam name="T">State type.</typeparam>
        /// <param name="state">State passed to the factory.</param>
        /// <param name="factory">Factory that starts the work.</param>
        /// <returns>The task the factory returned.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityTask Create<T>(T state, Func<T, OnityTask> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return factory(state);
        }

        /// <summary>
        /// Runs a factory immediately and returns its typed task. It exists to write an async lambda
        /// as an expression, for example <c>OnityTask.Create(async () =&gt; { ...; return value; })</c>.
        /// An exception thrown synchronously by the factory propagates to the caller.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="factory">Factory that starts the work.</param>
        /// <returns>The task the factory returned.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityTask<T> Create<T>(Func<OnityTask<T>> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return factory();
        }

        /// <summary>
        /// Returns a task whose factory runs only when the task is first used: when its status is
        /// read, a continuation is registered, it is awaited or it is bridged to a .NET task.
        /// </summary>
        /// <remarks>
        /// The factory runs once, on the thread of that first use. The created task is preserved, so
        /// the deferred task is shareable: any number of consumers can await it and observe the same
        /// outcome. A factory that throws produces a faulted task (a canceled one for an
        /// <see cref="OperationCanceledException"/>) instead of throwing from the first use.
        /// </remarks>
        /// <param name="factory">Factory that starts the work.</param>
        /// <returns>A shareable task that has not started yet.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityTask Defer(Func<OnityTask> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return new OnityTask(new OnityDeferTaskSource(factory));
        }

        /// <summary>
        /// Returns a typed task whose factory runs only when the task is first used. See
        /// <see cref="Defer(Func{OnityTask})"/> for the sharing and failure rules.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="factory">Factory that starts the work.</param>
        /// <returns>A shareable task that has not started yet.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityTask<T> Defer<T>(Func<OnityTask<T>> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return new OnityTask<T>(new OnityDeferResultTaskSource<T>(factory));
        }

        /// <summary>
        /// Returns a task whose factory runs only when the task is first used, receiving a state
        /// argument so the factory needs no closure. See <see cref="Defer(Func{OnityTask})"/> for the
        /// sharing and failure rules.
        /// </summary>
        /// <typeparam name="TState">State type.</typeparam>
        /// <param name="state">State passed to the factory.</param>
        /// <param name="factory">Factory that starts the work.</param>
        /// <returns>A shareable task that has not started yet.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityTask Defer<TState>(TState state, Func<TState, OnityTask> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return new OnityTask(new OnityDeferStateTaskSource<TState>(state, factory));
        }

        /// <summary>
        /// Returns a typed task whose factory runs only when the task is first used, receiving a state
        /// argument so the factory needs no closure. See <see cref="Defer(Func{OnityTask})"/> for the
        /// sharing and failure rules.
        /// </summary>
        /// <typeparam name="TState">State type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="state">State passed to the factory.</param>
        /// <param name="factory">Factory that starts the work.</param>
        /// <returns>A shareable task that has not started yet.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityTask<TResult> Defer<TState, TResult>(
            TState state,
            Func<TState, OnityTask<TResult>> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return new OnityTask<TResult>(new OnityDeferStateResultTaskSource<TState, TResult>(state, factory));
        }

        /// <summary>
        /// Returns a task that never completes, except that it completes as canceled when the
        /// token is canceled.
        /// </summary>
        /// <remarks>
        /// The task is shareable. The token registration is held until the token is canceled or
        /// collected with its source, so pass a token that has a defined end.
        /// </remarks>
        /// <param name="cancellationToken">Token whose cancellation completes the task.</param>
        /// <returns>A task that stays pending until the token is canceled.</returns>
        public static OnityTask Never(CancellationToken cancellationToken)
        {
            return OnityNeverTaskSource.Create(cancellationToken);
        }

        /// <summary>
        /// Returns a typed task that never completes, except that it completes as canceled when the
        /// token is canceled. See <see cref="Never(CancellationToken)"/> for the lifetime rules.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="cancellationToken">Token whose cancellation completes the task.</param>
        /// <returns>A task that stays pending until the token is canceled.</returns>
        public static OnityTask<T> Never<T>(CancellationToken cancellationToken)
        {
            return OnityNeverTaskSource<T>.Create(cancellationToken);
        }

        /// <summary>
        /// Creates a lazily started, shared task. The factory runs on the first access of
        /// <see cref="OnityAsyncLazy.Task"/> or the first await, and every awaiter observes the same
        /// outcome.
        /// </summary>
        /// <param name="factory">Factory that starts the work.</param>
        /// <returns>The lazy wrapper.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityAsyncLazy Lazy(Func<OnityTask> factory)
        {
            return new OnityAsyncLazy(factory);
        }

        /// <summary>
        /// Creates a lazily started, shared typed task. The factory runs on the first access of
        /// <see cref="OnityAsyncLazy{T}.Task"/> or the first await, and every awaiter observes the
        /// same outcome.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="factory">Factory that starts the work.</param>
        /// <returns>The lazy wrapper.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static OnityAsyncLazy<T> Lazy<T>(Func<OnityTask<T>> factory)
        {
            return new OnityAsyncLazy<T>(factory);
        }
    }
}
