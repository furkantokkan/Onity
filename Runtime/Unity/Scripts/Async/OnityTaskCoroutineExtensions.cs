using System;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Conversions between <see cref="OnityTask"/> and Unity coroutines. Every member needs Unity's
    /// main thread unless its remarks say otherwise.
    /// </summary>
    public static class OnityTaskCoroutineExtensions
    {
        private static readonly Action<object> s_cancelOnDestroy = CancelOnDestroy;

        // WaitForSeconds exposes no duration, but Unity keeps its private field for native code
        // (the type carries RequiredByNativeCode), so a managed-stripping build keeps it too.
        private static readonly FieldInfo s_waitForSecondsField = typeof(WaitForSeconds).GetField(
            "m_Seconds", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Returns a Unity coroutine enumerator that runs until the task completes.
        /// </summary>
        /// <remarks>
        /// The task is consumed when the enumerator starts, at its first <c>MoveNext</c>. A failure,
        /// including cancellation, goes to the handler; without a handler it is rethrown from
        /// <c>MoveNext</c>, which makes Unity log it and end the coroutine.
        /// </remarks>
        /// <param name="task">Task to wait for.</param>
        /// <param name="exceptionHandler">Optional failure callback, called on the completing thread.</param>
        /// <returns>A coroutine enumerator that completes with the task.</returns>
        public static IEnumerator ToCoroutine(this OnityTask task, Action<Exception> exceptionHandler = null)
        {
            return new OnityTaskCoroutineEnumerator(task, exceptionHandler);
        }

        /// <summary>
        /// Returns a Unity coroutine enumerator that runs until the typed task completes and hands over
        /// its result.
        /// </summary>
        /// <remarks>
        /// The task is consumed when the enumerator starts. The result goes to the handler, and the
        /// enumerator's <c>Current</c> holds it afterwards, boxed. A failure, including cancellation,
        /// goes to the exception handler; without one it is rethrown from <c>MoveNext</c>.
        /// </remarks>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="task">Task to wait for.</param>
        /// <param name="resultHandler">Optional result callback, called on the completing thread.</param>
        /// <param name="exceptionHandler">Optional failure callback, called on the completing thread.</param>
        /// <returns>A coroutine enumerator that completes with the task.</returns>
        public static IEnumerator ToCoroutine<T>(
            this OnityTask<T> task,
            Action<T> resultHandler = null,
            Action<Exception> exceptionHandler = null)
        {
            return new OnityTaskCoroutineEnumerator<T>(task, resultHandler, exceptionHandler);
        }

        /// <summary>
        /// Enables <c>await</c> on a coroutine enumerator. The enumerator is advanced at once and then
        /// once per Update.
        /// </summary>
        /// <remarks>
        /// See <see cref="ToOnityTask(IEnumerator, OnityPlayerLoopTiming, CancellationToken)"/> for
        /// which yield instructions are supported and how.
        /// </remarks>
        /// <typeparam name="T">Enumerator type.</typeparam>
        /// <param name="enumerator">Coroutine enumerator.</param>
        /// <returns>Awaiter that completes when the enumerator is exhausted.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="enumerator"/> is null.</exception>
        public static OnityTaskAwaiter GetAwaiter<T>(this T enumerator)
            where T : IEnumerator
        {
            IEnumerator coroutine = enumerator;
            if (coroutine == null)
            {
                throw new ArgumentNullException(nameof(enumerator));
            }

            return RunEnumeratorAsync(coroutine, OnityPlayerLoopTiming.Update, default).GetAwaiter();
        }

        /// <summary>
        /// Awaits a coroutine enumerator that can be canceled. See
        /// <see cref="ToOnityTask(IEnumerator, OnityPlayerLoopTiming, CancellationToken)"/>.
        /// </summary>
        /// <param name="enumerator">Coroutine enumerator.</param>
        /// <param name="cancellationToken">Cancellation published on Unity's main thread.</param>
        /// <returns>A task that completes when the enumerator is exhausted.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="enumerator"/> is null.</exception>
        public static OnityTask WithCancellation(this IEnumerator enumerator, CancellationToken cancellationToken)
        {
            if (enumerator == null)
            {
                throw new ArgumentNullException(nameof(enumerator));
            }

            return RunEnumeratorAsync(enumerator, OnityPlayerLoopTiming.Update, cancellationToken);
        }

        /// <summary>
        /// Converts a coroutine enumerator to a task by advancing it from the PlayerLoop, without a
        /// Unity coroutine host.
        /// </summary>
        /// <remarks>
        /// The enumerator is advanced synchronously at the call and then once per drain of
        /// <paramref name="timing"/>. <c>null</c>, <see cref="CustomYieldInstruction"/> values
        /// (polled each drain), <see cref="AsyncOperation"/> values (polled until done) and nested
        /// enumerators are supported; <see cref="WaitForSeconds"/> waits for scaled time, and
        /// <see cref="WaitForFixedUpdate"/> and <see cref="WaitForEndOfFrame"/> wait for those phases.
        /// Any other yielded value logs a warning and waits one drain: use
        /// <see cref="ToOnityTask(IEnumerator, MonoBehaviour)"/> to run such a coroutine as Unity does.
        /// The duration of a <see cref="WaitForSeconds"/> is read from its private field, which Unity
        /// keeps for native code; when it is missing the wait degrades to one drain with a warning.
        /// Cancellation is checked at every step and completes the task as canceled; the enumerator is
        /// then left where it stopped.
        /// </remarks>
        /// <param name="enumerator">Coroutine enumerator.</param>
        /// <param name="timing">PlayerLoop phase that advances the enumerator.</param>
        /// <param name="cancellationToken">Cancellation published on Unity's main thread.</param>
        /// <returns>A task that completes when the enumerator is exhausted.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="enumerator"/> is null.</exception>
        public static OnityTask ToOnityTask(
            this IEnumerator enumerator,
            OnityPlayerLoopTiming timing = OnityPlayerLoopTiming.Update,
            CancellationToken cancellationToken = default)
        {
            if (enumerator == null)
            {
                throw new ArgumentNullException(nameof(enumerator));
            }

            return RunEnumeratorAsync(enumerator, timing, cancellationToken);
        }

        /// <summary>
        /// Converts a coroutine enumerator to a task by running it as a real Unity coroutine on a
        /// MonoBehaviour, so every yield instruction behaves exactly as Unity defines it.
        /// </summary>
        /// <remarks>
        /// The task completes when the coroutine finishes, including when it ends with an exception that
        /// Unity logs. It completes as canceled when the runner is destroyed. Deactivating the runner's
        /// GameObject stops its coroutines without completing the task, so keep the runner active. The
        /// task is single-consumer. Main thread only.
        /// </remarks>
        /// <param name="enumerator">Coroutine enumerator.</param>
        /// <param name="coroutineRunner">MonoBehaviour that runs the coroutine.</param>
        /// <returns>A task that completes when the coroutine finishes.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static OnityTask ToOnityTask(this IEnumerator enumerator, MonoBehaviour coroutineRunner)
        {
            if (enumerator == null)
            {
                throw new ArgumentNullException(nameof(enumerator));
            }

            if (ReferenceEquals(coroutineRunner, null))
            {
                throw new ArgumentNullException(nameof(coroutineRunner));
            }

            if (coroutineRunner == null)
            {
                // A destroyed runner can never run the coroutine.
                return OnityTask.FromCanceled(new CancellationToken(true));
            }

            if (!coroutineRunner.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "The coroutine runner must be active in the hierarchy: Unity cannot start a coroutine on an inactive GameObject.");
            }

            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            CancellationToken destroyed = coroutineRunner.GetCancellationTokenOnDestroy();
            CancellationTokenRegistration registration = destroyed.RegisterWithoutCaptureExecutionContext(
                s_cancelOnDestroy,
                new DestroyCancellation(source, destroyed));
            try
            {
                coroutineRunner.StartCoroutine(RunOnRunner(enumerator, coroutineRunner, source, registration));
            }
            catch
            {
                registration.Dispose();
                throw;
            }

            return source.Task;
        }

        /// <summary>
        /// Runs an async method that is canceled when the MonoBehaviour is destroyed, and returns its
        /// task.
        /// </summary>
        /// <param name="monoBehaviour">Owner whose destruction cancels the token.</param>
        /// <param name="asyncCoroutine">Async method that receives the destroy token.</param>
        /// <returns>The task the method returned.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static OnityTask StartAsyncCoroutine(
            this MonoBehaviour monoBehaviour,
            Func<CancellationToken, OnityTask> asyncCoroutine)
        {
            if (ReferenceEquals(monoBehaviour, null))
            {
                throw new ArgumentNullException(nameof(monoBehaviour));
            }

            if (asyncCoroutine == null)
            {
                throw new ArgumentNullException(nameof(asyncCoroutine));
            }

            return asyncCoroutine(monoBehaviour.GetCancellationTokenOnDestroy());
        }

        private sealed class DestroyCancellation
        {
            public readonly OnityAutoResetTaskCompletionSource Source;
            public readonly CancellationToken Token;

            public DestroyCancellation(OnityAutoResetTaskCompletionSource source, CancellationToken token)
            {
                Source = source;
                Token = token;
            }
        }

        private static void CancelOnDestroy(object state)
        {
            DestroyCancellation destroyed = (DestroyCancellation)state;
            destroyed.Source.TrySetCanceled(destroyed.Token);
        }

        private static IEnumerator RunOnRunner(
            IEnumerator inner,
            MonoBehaviour runner,
            OnityAutoResetTaskCompletionSource source,
            CancellationTokenRegistration registration)
        {
            yield return runner.StartCoroutine(inner);
            registration.Dispose();
            source.TrySetResult();
        }

        private static async OnityTask RunEnumeratorAsync(
            IEnumerator enumerator,
            OnityPlayerLoopTiming timing,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!enumerator.MoveNext())
                {
                    return;
                }

                await WaitForYieldedAsync(enumerator.Current, timing, cancellationToken);
            }
        }

        private static async OnityTask WaitForYieldedAsync(
            object current,
            OnityPlayerLoopTiming timing,
            CancellationToken cancellationToken)
        {
            if (current == null)
            {
                await OnityTask.Yield(timing, cancellationToken);
            }
            else if (current is CustomYieldInstruction customYield)
            {
                while (customYield.keepWaiting)
                {
                    await OnityTask.Yield(timing, cancellationToken);
                }
            }
            else if (current is AsyncOperation operation)
            {
                while (!operation.isDone)
                {
                    await OnityTask.Yield(timing, cancellationToken);
                }
            }
            else if (current is WaitForSeconds waitForSeconds)
            {
                await WaitForSecondsAsync(waitForSeconds, timing, cancellationToken);
            }
            else if (current is WaitForFixedUpdate)
            {
                await OnityTask.NextFixedFrame(cancellationToken);
            }
            else if (current is WaitForEndOfFrame)
            {
                await OnityTask.WaitForEndOfFrame(cancellationToken);
            }
            else if (current is IEnumerator nested)
            {
                await RunEnumeratorAsync(nested, timing, cancellationToken);
            }
            else
            {
                Debug.LogWarning(
                    "yield " + current.GetType().Name
                    + " is not supported by IEnumerator.ToOnityTask(); use ToOnityTask(MonoBehaviour coroutineRunner) instead.");
                await OnityTask.Yield(timing, cancellationToken);
            }
        }

        private static async OnityTask WaitForSecondsAsync(
            WaitForSeconds waitForSeconds,
            OnityPlayerLoopTiming timing,
            CancellationToken cancellationToken)
        {
            if (s_waitForSecondsField == null)
            {
                Debug.LogWarning(
                    "WaitForSeconds duration is unavailable (UnityEngine.WaitForSeconds.m_Seconds was not found, for example"
                    + " because it was stripped); waiting one frame instead.");
                await OnityTask.Yield(timing, cancellationToken);
                return;
            }

            float seconds = (float)s_waitForSecondsField.GetValue(waitForSeconds);

            // A zero wait still takes a frame in Unity.
            if (seconds <= 0f)
            {
                await OnityTask.Yield(timing, cancellationToken);
                return;
            }

            await OnityTask.Delay(seconds, false, cancellationToken);
        }
    }

    /// <summary>
    /// Enumerator that starts awaiting an untyped task at its first <c>MoveNext</c> and keeps a
    /// coroutine alive until the task completes.
    /// </summary>
    internal sealed class OnityTaskCoroutineEnumerator : IEnumerator
    {
        private readonly OnityTask m_task;
        private readonly Action<Exception> m_exceptionHandler;

        private ExceptionDispatchInfo m_exception;
        private volatile bool m_completed;
        private bool m_started;

        public OnityTaskCoroutineEnumerator(OnityTask task, Action<Exception> exceptionHandler)
        {
            m_task = task;
            m_exceptionHandler = exceptionHandler;
        }

        public object Current => null;

        public bool MoveNext()
        {
            if (!m_started)
            {
                m_started = true;
                RunAsync().Forget();
            }

            // The completion flag is read first: the failure is stored before the flag is raised.
            bool completed = m_completed;
            ExceptionDispatchInfo exception = Volatile.Read(ref m_exception);
            if (exception != null)
            {
                exception.Throw();
            }

            return !completed;
        }

        void IEnumerator.Reset()
        {
        }

        private async OnityTaskVoid RunAsync()
        {
            try
            {
                await m_task;
            }
            catch (Exception exception)
            {
                if (m_exceptionHandler != null)
                {
                    m_exceptionHandler(exception);
                }
                else
                {
                    Volatile.Write(ref m_exception, ExceptionDispatchInfo.Capture(exception));
                }
            }
            finally
            {
                m_completed = true;
            }
        }
    }

    /// <summary>
    /// Typed counterpart of <see cref="OnityTaskCoroutineEnumerator"/> that also keeps the result.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    internal sealed class OnityTaskCoroutineEnumerator<T> : IEnumerator
    {
        private readonly OnityTask<T> m_task;
        private readonly Action<T> m_resultHandler;
        private readonly Action<Exception> m_exceptionHandler;

        private ExceptionDispatchInfo m_exception;
        private object m_current;
        private volatile bool m_completed;
        private bool m_started;

        public OnityTaskCoroutineEnumerator(
            OnityTask<T> task,
            Action<T> resultHandler,
            Action<Exception> exceptionHandler)
        {
            m_task = task;
            m_resultHandler = resultHandler;
            m_exceptionHandler = exceptionHandler;
        }

        public object Current => Volatile.Read(ref m_current);

        public bool MoveNext()
        {
            if (!m_started)
            {
                m_started = true;
                RunAsync().Forget();
            }

            bool completed = m_completed;
            ExceptionDispatchInfo exception = Volatile.Read(ref m_exception);
            if (exception != null)
            {
                exception.Throw();
            }

            return !completed;
        }

        void IEnumerator.Reset()
        {
        }

        private async OnityTaskVoid RunAsync()
        {
            try
            {
                T result = await m_task;
                Volatile.Write(ref m_current, result);
                m_resultHandler?.Invoke(result);
            }
            catch (Exception exception)
            {
                if (m_exceptionHandler != null)
                {
                    m_exceptionHandler(exception);
                }
                else
                {
                    Volatile.Write(ref m_exception, ExceptionDispatchInfo.Capture(exception));
                }
            }
            finally
            {
                m_completed = true;
            }
        }
    }
}
