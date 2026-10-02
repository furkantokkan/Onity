using System;
using System.Threading;

namespace Onity.Unity.Async
{
    // Cancels one subscription. The enumeration owns the token source: it is disposed when the
    // enumeration ends before a cancel was requested; otherwise the cancel path leaves it to the GC
    // because a concurrent Cancel call may still be running.
    internal sealed class OnitySubscription : IDisposable
    {
        private readonly object m_gate = new object();
        private readonly CancellationTokenSource m_source = new CancellationTokenSource();
        private readonly CancellationToken m_token;
        private bool m_canceled;
        private bool m_released;

        internal OnitySubscription()
        {
            m_token = m_source.Token;
        }

        internal CancellationToken Token => m_token;

        public void Dispose()
        {
            lock (m_gate)
            {
                if (m_canceled || m_released)
                {
                    return;
                }
                m_canceled = true;
            }
            m_source.Cancel();
        }

        internal void Release()
        {
            lock (m_gate)
            {
                if (m_released)
                {
                    return;
                }
                m_released = true;
                if (m_canceled)
                {
                    return;
                }
            }
            m_source.Dispose();
        }
    }

    // Normalized item handler: exactly one of the shapes is set. Fire-and-forget shapes return a
    // completed task; their faults are reported by OnityTaskVoid itself.
    internal readonly struct OnitySubscribeHandler<T>
    {
        private readonly Action<T> m_action;
        private readonly Func<T, OnityTaskVoid> m_void;
        private readonly Func<T, CancellationToken, OnityTaskVoid> m_voidWithToken;
        private readonly Func<T, OnityTask> m_await;
        private readonly Func<T, CancellationToken, OnityTask> m_awaitWithToken;

        private OnitySubscribeHandler(Action<T> action, Func<T, OnityTaskVoid> voidHandler,
            Func<T, CancellationToken, OnityTaskVoid> voidWithToken, Func<T, OnityTask> awaiting,
            Func<T, CancellationToken, OnityTask> awaitingWithToken)
        {
            m_action = action;
            m_void = voidHandler;
            m_voidWithToken = voidWithToken;
            m_await = awaiting;
            m_awaitWithToken = awaitingWithToken;
        }

        internal static OnitySubscribeHandler<T> FromAction(Action<T> action) =>
            new OnitySubscribeHandler<T>(action, null, null, null, null);

        internal static OnitySubscribeHandler<T> FromVoid(Func<T, OnityTaskVoid> handler) =>
            new OnitySubscribeHandler<T>(null, handler, null, null, null);

        internal static OnitySubscribeHandler<T> FromVoidWithToken(Func<T, CancellationToken, OnityTaskVoid> handler) =>
            new OnitySubscribeHandler<T>(null, null, handler, null, null);

        internal static OnitySubscribeHandler<T> FromAwait(Func<T, OnityTask> handler) =>
            new OnitySubscribeHandler<T>(null, null, null, handler, null);

        internal static OnitySubscribeHandler<T> FromAwaitWithToken(Func<T, CancellationToken, OnityTask> handler) =>
            new OnitySubscribeHandler<T>(null, null, null, null, handler);

        internal OnityTask Invoke(T item, CancellationToken token)
        {
            if (m_action != null)
            {
                m_action(item);
                return OnityTask.Completed;
            }
            if (m_void != null)
            {
                m_void(item).Forget();
                return OnityTask.Completed;
            }
            if (m_voidWithToken != null)
            {
                m_voidWithToken(item, token).Forget();
                return OnityTask.Completed;
            }
            return m_await != null ? m_await(item) : m_awaitWithToken(item, token);
        }
    }

    internal static class OnityAsyncSubscribeCore
    {
        internal static IDisposable Start<T>(IOnityAsyncEnumerable<T> source, OnitySubscribeHandler<T> handler,
            Action<Exception> onError, Action onCompleted)
        {
            var subscription = new OnitySubscription();
            Run(source, handler, onError, onCompleted, subscription.Token, subscription).Forget();
            return subscription;
        }

        internal static void Start<T>(IOnityAsyncEnumerable<T> source, OnitySubscribeHandler<T> handler,
            Action<Exception> onError, Action onCompleted, CancellationToken cancellationToken)
        {
            Run(source, handler, onError, onCompleted, cancellationToken, null).Forget();
        }

        // The loop starts on the calling thread and runs until its first suspension. Upstream cleanup is
        // awaited on every exit and its failure replaces the normal end; the terminal callback runs last.
        private static async OnityTaskVoid Run<T>(IOnityAsyncEnumerable<T> source, OnitySubscribeHandler<T> handler,
            Action<Exception> onError, Action onCompleted, CancellationToken token, OnitySubscription owner)
        {
            IOnityAsyncEnumerator<T> enumerator = null;
            Exception failure = null;
            try
            {
                enumerator = OnityLinqCore.Acquire(source, token);
                while (await enumerator.MoveNextAsync())
                {
                    try
                    {
                        await handler.Invoke(enumerator.Current, token);
                    }
                    catch (Exception exception)
                    {
                        ReportUnobserved(exception);
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (enumerator != null)
            {
                try
                {
                    await enumerator.DisposeAsync();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }
            owner?.Release();
            Finish(failure, onError, onCompleted);
        }

        private static void Finish(Exception failure, Action<Exception> onError, Action onCompleted)
        {
            if (failure == null)
            {
                if (onCompleted != null)
                {
                    try
                    {
                        onCompleted();
                    }
                    catch (Exception exception)
                    {
                        ReportUnobserved(exception);
                    }
                }
                return;
            }
            if (failure is OperationCanceledException)
            {
                return;
            }
            if (onError == null)
            {
                ReportUnobserved(failure);
                return;
            }
            try
            {
                onError(failure);
            }
            catch (Exception exception)
            {
                ReportUnobserved(exception);
            }
        }

        // One funnel for failures nobody can observe, so the unobserved-exception policy changes in one place.
        private static void ReportUnobserved(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return;
            }
            OnityTaskForgetObserver.Report(exception, null);
        }
    }
}
