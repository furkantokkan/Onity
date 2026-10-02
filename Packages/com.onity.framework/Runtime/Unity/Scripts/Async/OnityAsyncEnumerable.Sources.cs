using System;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityNeverAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        internal static readonly OnityNeverAsyncEnumerable<T> Instance = new OnityNeverAsyncEnumerable<T>();

        private OnityNeverAsyncEnumerable()
        {
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(cancellationToken);
        }

        private sealed class Enumerator : OnityAsyncEnumeratorBase<T>
        {
            private readonly CancellationToken m_token;
            private OnityStreamPendingMove m_pending;

            internal Enumerator(CancellationToken token)
            {
                m_token = token;
            }

            protected override OnityTask<bool> MoveCore()
            {
                if (IsClosing)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (m_token.IsCancellationRequested)
                {
                    return OnityTask<bool>.FromCanceled(m_token);
                }
                var pending = new OnityStreamPendingMove(m_token);
                m_pending = pending;
                pending.Start();
                return pending.Task;
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = default;
                return false;
            }

            protected override OnityTask CleanupCore()
            {
                m_pending?.Close();
                return OnityTask.Completed;
            }

            protected override void FinishCleanupCore()
            {
                m_pending = null;
            }
        }
    }

    internal sealed class OnityThrowAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly Exception m_exception;

        internal OnityThrowAsyncEnumerable(Exception exception)
        {
            m_exception = exception;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_exception, cancellationToken);
        }

        private sealed class Enumerator : OnityAsyncEnumeratorBase<T>
        {
            private readonly Exception m_exception;
            private readonly CancellationToken m_token;

            internal Enumerator(Exception exception, CancellationToken token)
            {
                m_exception = exception;
                m_token = token;
            }

            protected override OnityTask<bool> MoveCore()
            {
                if (m_token.IsCancellationRequested)
                {
                    return OnityTask<bool>.FromCanceled(m_token);
                }
                return OnityTask<bool>.FromException(m_exception);
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = default;
                return false;
            }
        }
    }

    internal sealed class OnityRepeatAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly T m_element;
        private readonly int m_count;

        internal OnityRepeatAsyncEnumerable(T element, int count)
        {
            m_element = element;
            m_count = count;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_element, m_count, cancellationToken);
        }

        private sealed class Enumerator : OnityAsyncEnumeratorBase<T>
        {
            private readonly T m_element;
            private readonly CancellationToken m_token;
            private int m_remaining;

            internal Enumerator(T element, int count, CancellationToken token)
            {
                m_element = element;
                m_remaining = count;
                m_token = token;
            }

            protected override OnityTask<bool> MoveCore()
            {
                if (m_remaining == 0)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (m_token.IsCancellationRequested)
                {
                    return OnityTask<bool>.FromCanceled(m_token);
                }
                m_remaining--;
                return OnityTask<bool>.FromResult(true);
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = m_element;
                return true;
            }
        }
    }

    public static partial class OnityAsyncEnumerable
    {
        /// <summary>Creates a stream that never yields an item and never ends.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <returns>A reusable description. Every enumeration waits until its token is canceled, which cancels
        /// the pending move, or until it is disposed, which ends the move with false; a token that cannot be
        /// canceled waits until disposal.</returns>
        public static IOnityAsyncEnumerable<T> Never<T>()
        {
            return OnityNeverAsyncEnumerable<T>.Instance;
        }

        /// <summary>Creates a stream whose first move faults.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="exception">Exception that faults the first move.</param>
        /// <returns>A reusable description; an already canceled token cancels the move instead, and every
        /// enumeration faults with the same exception instance.</returns>
        /// <exception cref="ArgumentNullException">Exception is null.</exception>
        public static IOnityAsyncEnumerable<T> Throw<T>(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }
            return new OnityThrowAsyncEnumerable<T>(exception);
        }

        /// <summary>Creates a stream that yields one element a number of times.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="element">Element to yield.</param>
        /// <param name="count">Nonnegative number of repetitions.</param>
        /// <returns>A reusable description; zero count is empty. A canceled token cancels the next move unless
        /// every repetition was already yielded.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Count is negative.</exception>
        public static IOnityAsyncEnumerable<T> Repeat<T>(T element, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            return count == 0 ? Empty<T>() : new OnityRepeatAsyncEnumerable<T>(element, count);
        }

        /// <summary>Creates a stream fed by a producer function that writes items through a rendezvous writer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="create">Producer, started lazily on the first move of each enumeration with the writer and
        /// a cooperative token. The stream ends when the returned task completes successfully and faults when it
        /// faults.</param>
        /// <returns>A reusable description; every enumeration runs its own producer.</returns>
        /// <remarks>Back-pressure: <see cref="IOnityAsyncWriter{T}.YieldAsync"/> publishes one item and completes
        /// only when the consumer requests the next one, so the producer never runs ahead. Canceling the
        /// enumeration token or disposing the enumerator cancels the producer token and completes a pending
        /// <c>YieldAsync</c> as canceled, which unwinds a producer parked in it. Cleanup, and with it the
        /// terminal outcome of a canceled or failed enumeration, waits for the producer to finish, so a producer
        /// that ignores cancellation and never reaches <c>YieldAsync</c> retains cleanup. A producer fault that
        /// no move observed is reported by disposal. The producer starts on the thread that calls the first move
        /// and continues on whichever thread resumes it; no implicit thread hop is added. Each enumeration
        /// allocates a completion source per request and a token source.</remarks>
        /// <exception cref="ArgumentNullException">Create is null.</exception>
        public static IOnityAsyncEnumerable<T> Create<T>(Func<IOnityAsyncWriter<T>, CancellationToken, OnityTask> create)
        {
            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }
            return new OnityCreateAsyncEnumerable<T>(create);
        }
    }
}
