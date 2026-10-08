using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityFirstAsyncEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        internal OnityFirstAsyncEnumerator(IOnityAsyncEnumerable<T> source, CancellationToken token)
            : base(source, token)
        {
        }

        protected override bool EndAfterItem => true;

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            return true;
        }

        protected override bool ReadEndCore(out T current)
        {
            throw new InvalidOperationException("The asynchronous stream contains no items.");
        }
    }

    internal sealed class OnityArrayAsyncEnumerator<T> : OnitySourceAsyncEnumerator<T, T[]>
    {
        private readonly List<T> m_items = new List<T>();

        internal OnityArrayAsyncEnumerator(IOnityAsyncEnumerable<T> source, CancellationToken token)
            : base(source, token)
        {
        }

        protected override bool ReadCurrentCore(out T[] current)
        {
            m_items.Add(UpstreamCurrent);
            current = null;
            return false;
        }

        protected override bool ReadEndCore(out T[] current)
        {
            current = m_items.ToArray();
            m_items.Clear();
            return true;
        }
    }

    internal static class OnityAsyncEnumerableConsumers
    {
        internal static OnityTask<T> Read<T>(OnityAsyncEnumeratorBase<T> enumerator)
        {
            return new Consumer<T>(enumerator).Start();
        }

        private sealed class Consumer<T>
        {
            private readonly object m_gate = new object();
            private readonly OnityAsyncEnumeratorBase<T> m_enumerator;
            private readonly Action<OnityAsyncStreamOutcome> m_onMove;
            private readonly Action<OnityAsyncStreamOutcome> m_onCleanup;
            private OnityTaskCompletionSource<T> m_output;
            private OnityAsyncStreamOutcome m_outcome;
            private T m_result;
            private bool m_completed;

            internal Consumer(OnityAsyncEnumeratorBase<T> enumerator)
            {
                m_enumerator = enumerator;
                m_onMove = OnMove;
                m_onCleanup = OnCleanup;
            }

            internal OnityTask<T> Start()
            {
                try
                {
                    OnityTask<bool> task = m_enumerator.MoveNextAsync();
                    if (task.IsCompleted)
                    {
                        OnMove(OnityAsyncStreamOutcome.Read(task));
                    }
                    else
                    {
                        new OnityAsyncStreamMoveObserver(task, m_onMove).Register();
                    }
                }
                catch (Exception exception)
                {
                    OnMove(OnityAsyncStreamOutcome.Failure(exception));
                }

                OnityAsyncStreamOutcome outcome;
                T result;
                lock (m_gate)
                {
                    if (!m_completed)
                    {
                        m_output ??= new OnityTaskCompletionSource<T>();
                        return m_output.Task;
                    }
                    if (m_output != null)
                    {
                        return m_output.Task;
                    }
                    outcome = m_outcome;
                    result = m_result;
                }
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    return OnityTask<T>.FromResult(result);
                }
                var output = new OnityTaskCompletionSource<T>();
                outcome.Publish(output, result);
                return output.Task;
            }

            private void OnMove(OnityAsyncStreamOutcome outcome)
            {
                T result = default;
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    try
                    {
                        if (!outcome.Value)
                        {
                            throw new InvalidOperationException("The terminal consumer did not produce a result.");
                        }
                        result = m_enumerator.Current;
                    }
                    catch (Exception exception)
                    {
                        outcome = OnityAsyncStreamOutcome.Failure(exception);
                    }
                }
                lock (m_gate)
                {
                    m_result = result;
                    m_outcome = outcome;
                }
                try
                {
                    OnityTask cleanup = m_enumerator.DisposeAsync();
                    if (cleanup.IsCompleted)
                    {
                        OnCleanup(OnityAsyncStreamOutcome.Read(cleanup));
                    }
                    else
                    {
                        new OnityAsyncStreamCleanupObserver(cleanup, m_onCleanup).Register();
                    }
                }
                catch (Exception exception)
                {
                    OnCleanup(OnityAsyncStreamOutcome.Failure(exception));
                }
            }

            private void OnCleanup(OnityAsyncStreamOutcome cleanup)
            {
                OnityTaskCompletionSource<T> output;
                OnityAsyncStreamOutcome outcome;
                T result;
                lock (m_gate)
                {
                    if (cleanup.Status != OnityTaskSourceStatus.Succeeded)
                    {
                        m_outcome = cleanup;
                    }
                    m_completed = true;
                    output = m_output;
                    outcome = m_outcome;
                    result = m_result;
                }
                if (output != null)
                {
                    outcome.Publish(output, result);
                }
            }
        }
    }
}
