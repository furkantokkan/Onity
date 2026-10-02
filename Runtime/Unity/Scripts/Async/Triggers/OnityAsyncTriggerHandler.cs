using System;
using System.Threading;

namespace Onity.Unity.Async.Triggers
{
    /// <summary>
    /// One-shot wait on a trigger (UniTask <c>IAsyncOneShotTrigger</c> parity).
    /// </summary>
    public interface IOnityAsyncOneShotTrigger
    {
        /// <summary>Waits for the next value raised by the trigger. Main thread only.</summary>
        /// <returns>A single-consumption task completed by the next raised value.</returns>
        OnityTask OneShotAsync();
    }

    /// <summary>
    /// Waiter registered on an <see cref="OnityAsyncTriggerBase{T}"/> (UniTask
    /// <c>AsyncTriggerHandler</c> parity). Each message method (for example
    /// <see cref="IOnityAsyncOnEnableHandler.OnEnableAsync"/>) starts a new wait that completes with
    /// the next value raised after the call; values raised while no wait is pending are skipped.
    /// A reusable handler from <c>GetOnXAsyncHandler()</c> stays registered until it is disposed,
    /// its token is canceled, or the trigger is destroyed; the one-shot form used by
    /// <c>OnXAsync()</c> unregisters itself after its first value.
    /// </summary>
    /// <typeparam name="T">Trigger payload type.</typeparam>
    /// <remarks>
    /// Thread affinity: main thread only, including canceling the handler's token. Continuations run
    /// inline inside the Unity message that raised the value. Each returned task can be awaited once.
    /// Destroying the trigger completes a pending wait as canceled. Deviations from UniTask 2.5.11,
    /// which leaves these waits pending forever: a wait started after the handler was disposed, its
    /// token was canceled, or its trigger was destroyed completes as canceled, and disposing the
    /// handler cancels its pending wait. Starting a new wait while one is pending abandons the older
    /// one, as in UniTask.
    /// </remarks>
    public sealed partial class OnityAsyncTriggerHandler<T> :
        IOnityAsyncOneShotTrigger,
        IOnityTriggerHandler<T>,
        IDisposable
    {
        private static readonly Action<object> s_cancellationCallback = OnCancellationRequested;

        private readonly OnityAsyncTriggerBase<T> m_trigger;
        private readonly CancellationToken m_cancellationToken;
        private readonly bool m_callOnce;
        private CancellationTokenRegistration m_registration;

        // The pending wait: an OnityAutoResetTaskCompletionSource (message without payload and
        // OneShotAsync) or an OnityAutoResetTaskCompletionSource<T> (message with a payload).
        private object m_pending;
        private bool m_isDisposed;

        internal OnityAsyncTriggerHandler(OnityAsyncTriggerBase<T> trigger, bool callOnce)
            : this(trigger, CancellationToken.None, callOnce)
        {
        }

        internal OnityAsyncTriggerHandler(
            OnityAsyncTriggerBase<T> trigger, CancellationToken cancellationToken, bool callOnce)
        {
            m_trigger = trigger;
            m_cancellationToken = cancellationToken;
            m_callOnce = callOnce;

            if (cancellationToken.IsCancellationRequested)
            {
                m_isDisposed = true;
                return;
            }

            trigger.AddHandler(this);

            if (!m_isDisposed && cancellationToken.CanBeCanceled)
            {
                m_registration = OnityTriggerCancellation.Register(cancellationToken, s_cancellationCallback, this);
            }
        }

        IOnityTriggerHandler<T> IOnityTriggerHandler<T>.Prev { get; set; }

        IOnityTriggerHandler<T> IOnityTriggerHandler<T>.Next { get; set; }

        /// <summary>
        /// Unregisters the handler from its trigger. A wait pending at this moment completes as
        /// canceled. Idempotent. Main thread only.
        /// </summary>
        public void Dispose()
        {
            if (!DisposeCore())
            {
                return;
            }

            CompleteCanceled(TakePending(), m_cancellationToken);
        }

        OnityTask IOnityAsyncOneShotTrigger.OneShotAsync()
        {
            return WaitAsync();
        }

        /// <summary>Starts a new wait, without the payload, for the next raised value.</summary>
        internal OnityTask WaitAsync()
        {
            // A previous wait that is still pending is abandoned (UniTask core.Reset parity).
            TakePending();

            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask task = source.Task;

            if (m_isDisposed)
            {
                source.TrySetCanceled(m_cancellationToken);
                return task;
            }

            m_pending = source;
            return task;
        }

        /// <summary>
        /// Starts a new wait that completes with the next raised payload. <typeparamref name="TResult"/>
        /// must be the handler's payload type <typeparamref name="T"/>; the message interfaces
        /// guarantee it, and a mismatch is a programming error.
        /// </summary>
        internal OnityTask<TResult> WaitValueAsync<TResult>()
        {
            if (typeof(TResult) != typeof(T))
            {
                throw new InvalidOperationException(
                    "The handler delivers " + typeof(T).Name + " values, not " + typeof(TResult).Name + ".");
            }

            // A previous wait that is still pending is abandoned (UniTask core.Reset parity).
            TakePending();

            OnityAutoResetTaskCompletionSource<TResult> source = OnityAutoResetTaskCompletionSource<TResult>.Create();
            OnityTask<TResult> task = source.Task;

            if (m_isDisposed)
            {
                source.TrySetCanceled(m_cancellationToken);
                return task;
            }

            m_pending = source;
            return task;
        }

        void IOnityTriggerHandler<T>.OnNext(T value)
        {
            object pending = TakePending();
            if (pending == null)
            {
                return;
            }

            if (m_callOnce)
            {
                DisposeCore();
            }

            CompleteResult(pending, value);
        }

        void IOnityTriggerHandler<T>.OnError(Exception exception)
        {
            m_isDisposed = true;
            m_registration.Dispose();
            CompleteFaulted(TakePending(), exception);
        }

        void IOnityTriggerHandler<T>.OnCompleted()
        {
            m_isDisposed = true;
            m_registration.Dispose();
            CompleteCanceled(TakePending(), CancellationToken.None);
        }

        void IOnityTriggerHandler<T>.OnCanceled(CancellationToken cancellationToken)
        {
            m_isDisposed = true;
            m_registration.Dispose();
            CompleteCanceled(TakePending(), cancellationToken);
        }

        private static void OnCancellationRequested(object state)
        {
            OnityAsyncTriggerHandler<T> self = (OnityAsyncTriggerHandler<T>)state;
            self.DisposeCore();
            CompleteCanceled(self.TakePending(), self.m_cancellationToken);
        }

        private static void CompleteResult(object pending, T value)
        {
            if (pending is OnityAutoResetTaskCompletionSource untyped)
            {
                untyped.TrySetResult();
                return;
            }

            ((OnityAutoResetTaskCompletionSource<T>)pending).TrySetResult(value);
        }

        private static void CompleteCanceled(object pending, CancellationToken cancellationToken)
        {
            if (pending == null)
            {
                return;
            }

            if (pending is OnityAutoResetTaskCompletionSource untyped)
            {
                untyped.TrySetCanceled(cancellationToken);
                return;
            }

            ((OnityAutoResetTaskCompletionSource<T>)pending).TrySetCanceled(cancellationToken);
        }

        private static void CompleteFaulted(object pending, Exception exception)
        {
            if (pending == null)
            {
                return;
            }

            if (pending is OnityAutoResetTaskCompletionSource untyped)
            {
                untyped.TrySetException(exception);
                return;
            }

            ((OnityAutoResetTaskCompletionSource<T>)pending).TrySetException(exception);
        }

        private bool DisposeCore()
        {
            if (m_isDisposed)
            {
                return false;
            }

            m_isDisposed = true;
            m_registration.Dispose();
            m_trigger.RemoveHandler(this);
            return true;
        }

        private object TakePending()
        {
            return Interlocked.Exchange(ref m_pending, null);
        }
    }

    /// <summary>Token registration without ExecutionContext capture (UniTask parity).</summary>
    internal static class OnityTriggerCancellation
    {
        internal static CancellationTokenRegistration Register(
            CancellationToken cancellationToken, Action<object> callback, object state)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return cancellationToken.Register(callback, state);
            }

            using (ExecutionContext.SuppressFlow())
            {
                return cancellationToken.Register(callback, state);
            }
        }
    }
}
