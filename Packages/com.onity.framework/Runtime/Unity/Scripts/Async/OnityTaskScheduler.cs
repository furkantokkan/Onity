using System;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Central policy for exceptions that no code observed: faults of fire-and-forget work such as
    /// <c>async OnityTaskVoid</c> methods and <c>Forget()</c> without a handler, faults that
    /// nothing awaited on a completion source, and exceptions thrown by <c>Forget</c> handlers.
    /// Onity has no task scheduler; this class only decides what happens to such an exception.
    /// </summary>
    /// <remarks>
    /// An unobserved <see cref="OperationCanceledException"/> is dropped unless
    /// <see cref="PropagateOperationCanceledException"/> is true, because cancellation is the normal
    /// end of fire-and-forget work. Any other exception raises <see cref="UnobservedTaskException"/>
    /// when it has a subscriber; otherwise it is written to the Unity console as
    /// <see cref="UnobservedExceptionWriteLogType"/> says. The settings are process-wide, are read
    /// at the moment an exception is published, and are not reset between Play Mode sessions when
    /// domain reload is disabled.
    /// </remarks>
    public static class OnityTaskScheduler
    {
        private static bool s_propagateOperationCanceledException;
        private static bool s_dispatchUnityMainThread = true;
        private static int s_unobservedExceptionWriteLogType = (int)LogType.Exception;

        /// <summary>
        /// Raised for each unobserved exception that is not filtered out. Subscribers run on Unity's
        /// main thread when <see cref="DispatchUnityMainThread"/> is true, in subscription order;
        /// an exception thrown by one subscriber is logged and does not stop the others.
        /// </summary>
        public static event Action<Exception> UnobservedTaskException;

        /// <summary>
        /// When true, an unobserved <see cref="OperationCanceledException"/> is published like any
        /// other exception. Defaults to false, so cancellation is dropped silently.
        /// </summary>
        public static bool PropagateOperationCanceledException
        {
            get => Volatile.Read(ref s_propagateOperationCanceledException);
            set => Volatile.Write(ref s_propagateOperationCanceledException, value);
        }

        /// <summary>
        /// The kind of console message written for an unobserved exception when
        /// <see cref="UnobservedTaskException"/> has no subscriber. Defaults to
        /// <see cref="LogType.Exception"/>, which logs the exception with its stack trace; the other
        /// values log the exception text with the matching Unity log call.
        /// </summary>
        public static LogType UnobservedExceptionWriteLogType
        {
            get => (LogType)Volatile.Read(ref s_unobservedExceptionWriteLogType);
            set => Volatile.Write(ref s_unobservedExceptionWriteLogType, (int)value);
        }

        /// <summary>
        /// When true, <see cref="UnobservedTaskException"/> subscribers always run on Unity's main
        /// thread: an exception published from another thread is queued there. Defaults to true.
        /// Console logging is not dispatched; Unity's logging is thread-safe.
        /// </summary>
        public static bool DispatchUnityMainThread
        {
            get => Volatile.Read(ref s_dispatchUnityMainThread);
            set => Volatile.Write(ref s_dispatchUnityMainThread, value);
        }

        /// <summary>
        /// Publishes an unobserved exception: drops a filtered cancellation, raises the event when it
        /// has a subscriber, and otherwise writes the console message. Safe from any thread, including
        /// a finalizer: it never throws.
        /// </summary>
        /// <param name="exception">Exception that no code observed; null is ignored.</param>
        internal static void PublishUnobservedException(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            if (exception is OperationCanceledException && !PropagateOperationCanceledException)
            {
                return;
            }

            try
            {
                Publish(exception);
            }
            catch (Exception)
            {
                // This is the last resort for a fault, and callers include finalizers, which must
                // never throw. Unity logging or the main-thread queue can fail during shutdown.
                WriteToConsole(exception);
            }
        }

        private static void Publish(Exception exception)
        {
            Action<Exception> handlers = UnobservedTaskException;
            if (handlers == null)
            {
                WriteLog(exception);
                return;
            }

            if (!DispatchUnityMainThread)
            {
                Raise(handlers, exception);
                return;
            }

            // The public main-thread switch reports whether the caller already is on the main thread
            // and otherwise queues the continuation there.
            var awaiter = OnityTask.SwitchToMainThread().GetAwaiter();
            if (awaiter.IsCompleted)
            {
                Raise(handlers, exception);
                return;
            }

            awaiter.UnsafeOnCompleted(new MainThreadPublication(handlers, exception).Run);
        }

        private static void Raise(Action<Exception> handlers, Exception exception)
        {
            Delegate[] subscribers = handlers.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((Action<Exception>)subscribers[i])(exception);
                }
                catch (Exception subscriberException)
                {
                    LogSubscriberFailure(subscriberException);
                }
            }
        }

        private static void LogSubscriberFailure(Exception exception)
        {
            try
            {
                Debug.LogException(exception);
            }
            catch (Exception)
            {
                // A failing log call must not stop the remaining subscribers.
            }
        }

        private static void WriteToConsole(Exception exception)
        {
            try
            {
                Console.Error.WriteLine("Unobserved OnityTask exception: " + exception);
            }
            catch (Exception)
            {
                // Nothing is left to report to, and a reporter must not throw.
            }
        }

        private static void WriteLog(Exception exception)
        {
            LogType logType = UnobservedExceptionWriteLogType;
            if (logType == LogType.Exception)
            {
                Debug.LogException(exception);
                return;
            }

            string message = "Unobserved OnityTask exception: " + exception;
            switch (logType)
            {
                case LogType.Error:
                    Debug.LogError(message);
                    break;
                case LogType.Assert:
                    Debug.LogAssertion(message);
                    break;
                case LogType.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogType.Log:
                    Debug.Log(message);
                    break;
            }
        }

        private sealed class MainThreadPublication
        {
            private readonly Action<Exception> m_handlers;
            private readonly Exception m_exception;

            public MainThreadPublication(Action<Exception> handlers, Exception exception)
            {
                m_handlers = handlers;
                m_exception = exception;
            }

            public void Run()
            {
                Raise(m_handlers, m_exception);
            }
        }
    }
}
