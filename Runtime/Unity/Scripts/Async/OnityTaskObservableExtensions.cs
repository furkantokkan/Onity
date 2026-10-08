using System;
using System.Collections.Generic;
using System.Threading;
using Onity.Core;
using Onity.Reactive;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Bridges between <see cref="OnityTask"/> and <see cref="IOnityObservable{T}"/>.
    /// </summary>
    public static class OnityTaskObservableExtensions
    {
        /// <summary>
        /// Exposes a typed task as an observable that emits its result and completes, or reports its
        /// fault or cancellation as an error.
        /// </summary>
        /// <remarks>
        /// The task is consumed immediately by an observer owned by the returned observable, which
        /// stays pending for as long as the task does. Every subscriber, including one that subscribes
        /// after the task completed, receives the same outcome. A subscriber that uses a lifecycle
        /// observer gets <c>OnNext</c> then <c>OnCompleted</c>, or <c>OnError</c> for a fault or
        /// cancellation; a plain callback subscriber gets the value, and an error is routed to
        /// <see cref="OnityObservableExceptionHandler"/>. Notifications run on the thread that
        /// completes the task.
        /// </remarks>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="task">Task to observe; it is consumed.</param>
        /// <returns>An observable of the task's result.</returns>
        public static IOnityObservable<T> ToObservable<T>(this OnityTask<T> task)
        {
            return new OnityTaskObservable<T>(task);
        }

        /// <summary>
        /// Exposes an untyped task as an observable of <see cref="Unit"/> that emits once when the
        /// task succeeds. See <see cref="ToObservable{T}(OnityTask{T})"/> for the delivery rules.
        /// </summary>
        /// <param name="task">Task to observe; it is consumed.</param>
        /// <returns>An observable that emits <see cref="Unit"/> and completes.</returns>
        public static IOnityObservable<Unit> ToObservable(this OnityTask task)
        {
            return new OnityTaskObservable<Unit>(task.AsUnitTask());
        }

        /// <summary>
        /// Converts an observable to a task that completes with the last value when the source
        /// completes, with the semantics of UniTask's <c>ToUniTask</c> default mode. Use
        /// <see cref="ToOnityTask{T}(IOnityObservable{T}, bool, CancellationToken)"/> with
        /// <c>useFirstValue</c> to complete with the first value instead.
        /// </summary>
        /// <remarks>
        /// This overload and the one with <c>useFirstValue</c> replace a single signature with
        /// <c>bool useFirstValue = false</c>: that form makes <c>unitStream.ToOnityTask()</c> ambiguous
        /// with the first-value overload for <see cref="Unit"/> streams. A <see cref="Unit"/> stream
        /// converted with no <c>useFirstValue</c> argument therefore keeps binding to
        /// <see cref="OnityTaskBridgeExtensions.ToOnityTask(IOnityObservable{Unit}, CancellationToken)"/>,
        /// which completes with the first value. See the overload with <c>useFirstValue</c> for the
        /// completion, error and cancellation rules.
        /// </remarks>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Token that cancels the wait.</param>
        /// <returns>A task that completes with the last value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        public static OnityTask<T> ToOnityTask<T>(
            this IOnityObservable<T> source,
            CancellationToken cancellationToken = default)
        {
            return source.ToOnityTask(false, cancellationToken);
        }

        /// <summary>
        /// Converts an observable to a task, with the semantics of UniTask's <c>ToUniTask</c>: the task
        /// completes with the last value when the source completes, or with the first value when
        /// <paramref name="useFirstValue"/> is true.
        /// </summary>
        /// <remarks>
        /// Completion is the source's <c>OnCompleted</c> notification on a lifecycle observer. Hot
        /// streams that never signal completion, such as a <see cref="Subject{T}"/> or a Unity frame
        /// stream, keep the last-value mode pending until the token is canceled, so use
        /// <paramref name="useFirstValue"/> or a token for them. A source that completes without a
        /// value, or in first-value mode without having produced one, faults the task with an
        /// <see cref="InvalidOperationException"/>. A source error faults the task. Canceling the token
        /// cancels the task and disposes the subscription, which is also disposed as soon as the task
        /// completes. The returned task is a native shareable task, and an already canceled token
        /// returns a canceled task without subscribing.
        /// </remarks>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="useFirstValue">True to complete with the first value instead of the last.</param>
        /// <param name="cancellationToken">Token that cancels the wait.</param>
        /// <returns>A task that completes with the selected value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        public static OnityTask<T> ToOnityTask<T>(
            this IOnityObservable<T> source,
            bool useFirstValue,
            CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<T>.FromCanceled(cancellationToken);
            }

            return OnityObservableToTaskObserver<T>.Start(source, useFirstValue, cancellationToken);
        }
    }

    /// <summary>
    /// Lifecycle observer that turns a source's notifications into one task outcome. It owns the
    /// subscription and the cancellation registration, and releases both at the first terminal
    /// event: a value in first-value mode, completion, an error, or cancellation.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    internal sealed class OnityObservableToTaskObserver<T> : OnityObserver<T>
    {
        private static readonly Action<object> s_cancel = CancelFromToken;

        private readonly OnityTaskCompletionSource<T> m_source = new OnityTaskCompletionSource<T>();
        private readonly object m_gate = new object();
        private readonly bool m_useFirstValue;
        private readonly CancellationToken m_cancellationToken;

        private CancellationTokenRegistration m_registration;
        private IDisposable m_subscription;
        private int m_completed;
        private bool m_detached;
        private bool m_hasValue;
        private T m_latest;

        private OnityObservableToTaskObserver(bool useFirstValue, CancellationToken cancellationToken)
        {
            m_useFirstValue = useFirstValue;
            m_cancellationToken = cancellationToken;
        }

        public static OnityTask<T> Start(
            IOnityObservable<T> source,
            bool useFirstValue,
            CancellationToken cancellationToken)
        {
            OnityObservableToTaskObserver<T> observer =
                new OnityObservableToTaskObserver<T>(useFirstValue, cancellationToken);
            if (cancellationToken.CanBeCanceled)
            {
                observer.m_registration =
                    cancellationToken.RegisterWithoutCaptureExecutionContext(s_cancel, observer);
            }

            try
            {
                observer.Attach(source.Subscribe(observer));
            }
            catch (Exception exception)
            {
                observer.Fail(exception);
            }

            return observer.m_source.Task;
        }

        protected override void OnNextCore(T value)
        {
            if (m_useFirstValue)
            {
                if (TryComplete())
                {
                    m_source.TrySetResult(value);
                    Detach();
                }

                return;
            }

            lock (m_gate)
            {
                m_hasValue = true;
                m_latest = value;
            }
        }

        protected override void OnErrorCore(Exception exception)
        {
            Fail(exception);
        }

        protected override void OnCompletedCore(OnityResult result)
        {
            if (!TryComplete())
            {
                return;
            }

            if (result.IsFailure)
            {
                m_source.TrySetException(result.Exception);
            }
            else
            {
                bool hasValue;
                T latest;
                lock (m_gate)
                {
                    hasValue = m_hasValue;
                    latest = m_latest;
                }

                if (hasValue)
                {
                    m_source.TrySetResult(latest);
                }
                else
                {
                    m_source.TrySetException(new InvalidOperationException("Sequence has no elements"));
                }
            }

            Detach();
        }

        private static void CancelFromToken(object state)
        {
            ((OnityObservableToTaskObserver<T>)state).Cancel();
        }

        private void Cancel()
        {
            if (TryComplete())
            {
                m_source.TrySetCanceled(m_cancellationToken);
                Detach();
            }
        }

        private void Fail(Exception exception)
        {
            if (TryComplete())
            {
                m_source.TrySetException(exception);
                Detach();
            }
        }

        private bool TryComplete()
        {
            return Interlocked.Exchange(ref m_completed, 1) == 0;
        }

        private void Attach(IDisposable subscription)
        {
            bool dispose;
            lock (m_gate)
            {
                dispose = m_detached;
                if (!dispose)
                {
                    m_subscription = subscription;
                }
            }

            if (dispose)
            {
                subscription.Dispose();
            }
        }

        private void Detach()
        {
            IDisposable subscription;
            lock (m_gate)
            {
                m_detached = true;
                subscription = m_subscription;
                m_subscription = null;
            }

            m_registration.Dispose();
            subscription?.Dispose();
        }
    }

    /// <summary>
    /// Observable over one task. A single pump owned by the observable awaits the task once and
    /// then replays the outcome to every subscriber, so late subscribers are served too.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    internal sealed class OnityTaskObservable<T> : IOnityObservable<T>
    {
        private const int k_pending = 0;
        private const int k_succeeded = 1;
        private const int k_faulted = 2;

        private readonly object m_gate = new object();
        private readonly List<OnityObserver<T>> m_observers = new List<OnityObserver<T>>();

        private int m_state;
        private T m_value;
        private Exception m_error;

        public OnityTaskObservable(OnityTask<T> task)
        {
            PumpAsync(task).Forget();
        }

        public IDisposable Subscribe(Observer<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            return Subscribe(new DelegateObserver(observer));
        }

        public IDisposable Subscribe(OnityObserver<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            int trackingId = OnityObservableTracker.RegisterSubscription(this, observer);
            int state;
            T value;
            Exception error;
            lock (m_gate)
            {
                state = m_state;
                value = m_value;
                error = m_error;
                if (state == k_pending)
                {
                    m_observers.Add(observer);
                }
            }

            if (state == k_pending)
            {
                return new Subscription(this, observer, trackingId);
            }

            Notify(observer, state, value, error);
            OnityObservableTracker.CompleteSubscription(trackingId);
            return DisposableAction.Empty;
        }

        private async OnityTaskVoid PumpAsync(OnityTask<T> task)
        {
            T value;
            try
            {
                value = await task;
            }
            catch (Exception exception)
            {
                Complete(k_faulted, default, exception);
                return;
            }

            Complete(k_succeeded, value, null);
        }

        private void Complete(int state, T value, Exception error)
        {
            OnityObserver<T>[] observers;
            lock (m_gate)
            {
                m_state = state;
                m_value = value;
                m_error = error;
                observers = m_observers.ToArray();
                m_observers.Clear();
            }

            for (int i = 0; i < observers.Length; i++)
            {
                Notify(observers[i], state, value, error);
            }
        }

        private void Remove(OnityObserver<T> observer)
        {
            lock (m_gate)
            {
                m_observers.Remove(observer);
            }
        }

        private static void Notify(OnityObserver<T> observer, int state, T value, Exception error)
        {
            try
            {
                if (state == k_succeeded)
                {
                    observer.OnNext(value);
                    observer.OnCompleted();
                }
                else
                {
                    observer.OnError(error);
                }
            }
            catch (Exception exception)
            {
                OnityObservableExceptionHandler.Publish(exception);
            }

            try
            {
                observer.Dispose();
            }
            catch (Exception exception)
            {
                OnityObservableExceptionHandler.Publish(exception);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly OnityTaskObservable<T> m_owner;
            private readonly OnityObserver<T> m_observer;
            private readonly int m_trackingId;
            private int m_disposed;

            public Subscription(OnityTaskObservable<T> owner, OnityObserver<T> observer, int trackingId)
            {
                m_owner = owner;
                m_observer = observer;
                m_trackingId = trackingId;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }

                m_owner.Remove(m_observer);
                m_observer.OnCompleted();
                m_observer.Dispose();
                OnityObservableTracker.CompleteSubscription(m_trackingId);
            }
        }

        private sealed class DelegateObserver : OnityObserver<T>
        {
            private readonly Observer<T> m_callback;

            public DelegateObserver(Observer<T> callback)
            {
                m_callback = callback;
            }

            protected override void OnNextCore(T value)
            {
                m_callback(value);
            }

            protected override void OnErrorCore(Exception exception)
            {
                OnityObservableExceptionHandler.Publish(exception);
            }
        }
    }
}
