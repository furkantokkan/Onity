using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>The writer an <see cref="OnityAsyncEnumerable.Create{T}"/> producer uses to publish items.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    public interface IOnityAsyncWriter<T>
    {
        /// <summary>Publishes one item to the waiting consumer and waits until the consumer requests the next one.</summary>
        /// <param name="value">Item to publish.</param>
        /// <returns>A task that completes when the consumer requests the next item. It completes as canceled when
        /// the enumeration token is canceled, the enumerator is disposed or the producer already finished, so a
        /// producer that awaits it unwinds through OperationCanceledException.</returns>
        /// <remarks>Await each call before writing again: a call while the previous one is still pending, or
        /// while no consumer move is waiting, throws <see cref="InvalidOperationException"/>. The writer may be
        /// called from any thread.</remarks>
        OnityTask YieldAsync(T value);
    }

    // Rendezvous between a producer and a pull consumer. At most one item is in flight: YieldAsync completes
    // the consumer's pending move and then parks the producer until the next move releases it. Cancellation
    // and cleanup release a parked producer, cancel the producer token and wait for the producer to finish.
    // No callback ever runs under the gate; completing a task can run its continuation inline.
    internal sealed class OnityCreateAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly Func<IOnityAsyncWriter<T>, CancellationToken, OnityTask> m_create;

        internal OnityCreateAsyncEnumerable(Func<IOnityAsyncWriter<T>, CancellationToken, OnityTask> create)
        {
            m_create = create;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_create, cancellationToken);
        }

        private sealed class Writer : IOnityAsyncWriter<T>
        {
            private readonly Enumerator m_owner;

            internal Writer(Enumerator owner)
            {
                m_owner = owner;
            }

            public OnityTask YieldAsync(T value)
            {
                return m_owner.Yield(value);
            }
        }

        private sealed class Enumerator : OnityAsyncEnumeratorBase<T>
        {
            private static readonly Action<object> s_cancel = state => ((Enumerator)state).OnTokenCanceled();

            private readonly object m_gate = new object();
            private readonly Func<IOnityAsyncWriter<T>, CancellationToken, OnityTask> m_create;
            private readonly CancellationToken m_token;
            private readonly Writer m_writer;
            private readonly Action<OnityAsyncStreamOutcome> m_onProducerFinished;
            private OnityAsyncStreamTokenLifetime m_lifetime;
            private CancellationTokenRegistration m_registration;
            private OnityTaskCompletionSource<bool> m_move;
            private OnityTaskCompletionSource m_yield;
            private OnityTaskCompletionSource m_finished;
            private OnityAsyncStreamOutcome m_producerOutcome;
            private T m_item;
            private bool m_started;
            private bool m_producerDone;
            private bool m_outcomeDelivered;
            private bool m_tokenCanceled;
            private bool m_closed;

            internal Enumerator(Func<IOnityAsyncWriter<T>, CancellationToken, OnityTask> create, CancellationToken token)
            {
                m_create = create;
                m_token = token;
                m_writer = new Writer(this);
                m_onProducerFinished = OnProducerFinished;
            }

            protected override OnityTask<bool> MoveCore()
            {
                OnityTaskCompletionSource<bool> move = null;
                OnityTaskCompletionSource resume = null;
                OnityAsyncStreamOutcome ready = default;
                bool hasReady = false;
                bool start = false;
                lock (m_gate)
                {
                    if (m_closed)
                    {
                        return OnityTask<bool>.FromResult(false);
                    }
                    if (m_tokenCanceled || m_token.IsCancellationRequested)
                    {
                        m_tokenCanceled = true;
                        ready = OnityStreamSourceOutcomes.Canceled(m_token);
                        hasReady = true;
                    }
                    else if (m_producerDone)
                    {
                        m_outcomeDelivered = true;
                        ready = m_producerOutcome;
                        hasReady = true;
                    }
                    else
                    {
                        move = new OnityTaskCompletionSource<bool>();
                        m_move = move;
                        if (!m_started)
                        {
                            m_started = true;
                            start = true;
                        }
                        else if (m_yield != null)
                        {
                            resume = m_yield;
                            m_yield = null;
                        }
                    }
                }
                if (hasReady)
                {
                    return OnityStreamSourceOutcomes.MoveTask(ready);
                }
                if (start)
                {
                    StartProducer();
                }
                if (resume != null)
                {
                    resume.TrySetResult();
                }
                return move.Task;
            }

            protected override bool ReadCurrentCore(out T current)
            {
                lock (m_gate)
                {
                    current = m_item;
                    m_item = default;
                    return true;
                }
            }

            private void StartProducer()
            {
                var lifetime = new OnityAsyncStreamTokenLifetime();
                lock (m_gate)
                {
                    m_lifetime = lifetime;
                }
                lifetime.Initialize(m_token);
                if (m_token.CanBeCanceled)
                {
                    CancellationTokenRegistration registration = OnityStreamSourceTokens.Register(m_token, s_cancel, this);
                    bool dispose;
                    lock (m_gate)
                    {
                        dispose = m_closed;
                        if (!dispose)
                        {
                            m_registration = registration;
                        }
                    }
                    if (dispose)
                    {
                        registration.Dispose();
                    }
                }
                OnityTask producer;
                try
                {
                    producer = m_create(m_writer, lifetime.Token);
                }
                catch (Exception exception)
                {
                    OnProducerFinished(OnityAsyncStreamOutcome.Failure(exception));
                    return;
                }
                if (producer.IsCompleted)
                {
                    OnProducerFinished(OnityAsyncStreamOutcome.Read(producer));
                }
                else
                {
                    new OnityAsyncStreamCleanupObserver(producer, m_onProducerFinished).Register();
                }
            }

            internal OnityTask Yield(T value)
            {
                OnityTaskCompletionSource<bool> move;
                OnityTaskCompletionSource yield;
                lock (m_gate)
                {
                    if (m_closed || m_tokenCanceled || m_producerDone)
                    {
                        return CanceledTask();
                    }
                    if (m_yield != null)
                    {
                        throw new InvalidOperationException(
                            "YieldAsync was called again before the previous YieldAsync task completed.");
                    }
                    if (m_move == null)
                    {
                        throw new InvalidOperationException("YieldAsync was called while no move is waiting.");
                    }
                    m_item = value;
                    move = m_move;
                    m_move = null;
                    yield = new OnityTaskCompletionSource();
                    m_yield = yield;
                }
                move.TrySetResult(true);
                return yield.Task;
            }

            // Called under the gate by the producer path; the lifetime exists once the producer has started.
            private OnityTask CanceledTask()
            {
                var source = new OnityTaskCompletionSource();
                source.TrySetCanceled(m_lifetime != null ? m_lifetime.Token : m_token);
                return source.Task;
            }

            private void OnProducerFinished(OnityAsyncStreamOutcome outcome)
            {
                OnityTaskCompletionSource<bool> move = null;
                OnityTaskCompletionSource finished;
                lock (m_gate)
                {
                    m_producerDone = true;
                    m_producerOutcome = outcome;
                    finished = m_finished;
                    if (m_move != null && !m_closed)
                    {
                        move = m_move;
                        m_move = null;
                        m_outcomeDelivered = true;
                    }
                }
                if (move != null)
                {
                    outcome.Publish(move, false);
                }
                finished?.TrySetResult();
            }

            // The enumeration token was canceled: cancel the waiting move and release a parked producer.
            private void OnTokenCanceled()
            {
                OnityTaskCompletionSource<bool> move;
                OnityTaskCompletionSource yield;
                lock (m_gate)
                {
                    m_tokenCanceled = true;
                    move = m_move;
                    m_move = null;
                    yield = m_yield;
                    m_yield = null;
                }
                move?.TrySetCanceled(m_token);
                yield?.TrySetCanceled(m_token);
            }

            protected override OnityTask CleanupCore()
            {
                OnityTaskCompletionSource<bool> move;
                OnityTaskCompletionSource yield;
                OnityAsyncStreamTokenLifetime lifetime;
                CancellationTokenRegistration registration;
                OnityTaskCompletionSource finished = null;
                lock (m_gate)
                {
                    m_closed = true;
                    move = m_move;
                    m_move = null;
                    yield = m_yield;
                    m_yield = null;
                    lifetime = m_lifetime;
                    registration = m_registration;
                    m_registration = default;
                    if (m_started && !m_producerDone)
                    {
                        m_finished ??= new OnityTaskCompletionSource();
                        finished = m_finished;
                    }
                }
                registration.Dispose();
                OnityTask quiescence = lifetime != null ? lifetime.Close() : OnityTask.Completed;
                yield?.TrySetCanceled(lifetime != null ? lifetime.Token : m_token);
                move?.TrySetResult(false);
                return Finish(quiescence, finished != null ? finished.Task : OnityTask.Completed);
            }

            // Waits for the producer to unwind. A producer fault that no move reported surfaces here as a cleanup
            // failure; cancellation and normal completion do not.
            private async OnityTask Finish(OnityTask quiescence, OnityTask producerFinished)
            {
                await quiescence;
                await producerFinished;
                Exception unobserved = null;
                lock (m_gate)
                {
                    if (m_producerDone && !m_outcomeDelivered && m_producerOutcome.Status == OnityTaskSourceStatus.Faulted
                        && !(m_producerOutcome.Fault is OperationCanceledException))
                    {
                        unobserved = m_producerOutcome.Fault;
                    }
                }
                if (unobserved != null)
                {
                    ExceptionDispatchInfo.Capture(unobserved).Throw();
                }
            }

            protected override void FinishCleanupCore()
            {
                OnityAsyncStreamTokenLifetime lifetime;
                lock (m_gate)
                {
                    lifetime = m_lifetime;
                    m_item = default;
                }
                lifetime?.Dispose();
            }
        }
    }
}
