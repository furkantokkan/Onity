using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
    internal sealed class OnityEnumerableAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IEnumerable<T> m_source;

        internal OnityEnumerableAsyncEnumerable(IEnumerable<T> source)
        {
            m_source = source;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_source, cancellationToken);
        }

        private sealed class Enumerator : OnityAsyncEnumeratorBase<T>
        {
            private readonly IEnumerable<T> m_source;
            private readonly CancellationToken m_token;
            private IEnumerator<T> m_enumerator;

            internal Enumerator(IEnumerable<T> source, CancellationToken token)
            {
                m_source = source;
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
                if (m_enumerator == null)
                {
                    m_enumerator = m_source.GetEnumerator();
                    if (m_enumerator == null)
                    {
                        throw new InvalidOperationException("The sequence returned a null enumerator.");
                    }
                }
                return OnityTask<bool>.FromResult(m_enumerator.MoveNext());
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = m_enumerator.Current;
                return true;
            }

            protected override OnityTask CleanupCore()
            {
                m_enumerator?.Dispose();
                return OnityTask.Completed;
            }

            protected override void FinishCleanupCore()
            {
                m_enumerator = null;
            }
        }
    }

    // A single-item stream that awaits one task on the first move. Subclasses supply the task lazily so an
    // unused description never consumes it. The enumeration token and disposal release a pending move, so a
    // slow task never retains a canceled or disposed enumeration; a late task outcome is observed and dropped.
    internal abstract class OnitySingleResultAsyncEnumerator<T> : OnityAsyncEnumeratorBase<T>
    {
        private readonly CancellationToken m_token;
        private OnityStreamPendingMove m_pending;
        private T m_value;

        protected OnitySingleResultAsyncEnumerator(CancellationToken token)
        {
            m_token = token;
        }

        protected override bool EndAfterItem => true;

        protected abstract OnityTask<T> StartTask();

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
            OnityTask<T> task = StartTask();
            if (task.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome = OnityAwaitStreamObserver<T>.Read(task, out T value);
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    m_value = value;
                    outcome.Value = true;
                }
                return OnityStreamSourceOutcomes.MoveTask(outcome);
            }
            var pending = new OnityStreamPendingMove(m_token);
            m_pending = pending;
            pending.Start();
            new OnityAwaitStreamObserver<T>(task, (outcome, value) => OnTaskCompleted(pending, outcome, value)).Register();
            return pending.Task;
        }

        private void OnTaskCompleted(OnityStreamPendingMove pending, OnityAsyncStreamOutcome outcome, T value)
        {
            if (outcome.Status == OnityTaskSourceStatus.Succeeded)
            {
                m_value = value;
                outcome.Value = true;
            }
            pending.Complete(outcome);
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = m_value;
            m_value = default;
            return true;
        }

        protected override OnityTask CleanupCore()
        {
            m_pending?.Close();
            return OnityTask.Completed;
        }

        protected override void FinishCleanupCore()
        {
            m_pending = null;
            m_value = default;
        }
    }

    internal sealed class OnityTaskResultAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly OnityTask<T> m_task;

        internal OnityTaskResultAsyncEnumerable(OnityTask<T> task)
        {
            m_task = task;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_task, cancellationToken);
        }

        private sealed class Enumerator : OnitySingleResultAsyncEnumerator<T>
        {
            private readonly OnityTask<T> m_task;

            internal Enumerator(OnityTask<T> task, CancellationToken token) : base(token)
            {
                m_task = task;
            }

            protected override OnityTask<T> StartTask() => m_task;
        }
    }

    internal sealed class OnityUnitTaskAsyncEnumerable : IOnityAsyncEnumerable<Onity.Core.Unit>
    {
        private readonly OnityTask m_task;

        internal OnityUnitTaskAsyncEnumerable(OnityTask task)
        {
            m_task = task;
        }

        public IOnityAsyncEnumerator<Onity.Core.Unit> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_task, cancellationToken);
        }

        private sealed class Enumerator : OnitySingleResultAsyncEnumerator<Onity.Core.Unit>
        {
            private readonly OnityTask m_task;

            internal Enumerator(OnityTask task, CancellationToken token) : base(token)
            {
                m_task = task;
            }

            // Adapts the untyped task to a typed one without a thread hop; the outcome keeps its status.
            protected override OnityTask<Onity.Core.Unit> StartTask()
            {
                if (m_task.IsCompleted)
                {
                    OnityAsyncStreamOutcome outcome = OnityAsyncStreamOutcome.Read(m_task);
                    if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                    {
                        return OnityTask<Onity.Core.Unit>.FromResult(Onity.Core.Unit.Default);
                    }
                    var failed = new OnityTaskCompletionSource<Onity.Core.Unit>();
                    outcome.Publish(failed, Onity.Core.Unit.Default);
                    return failed.Task;
                }
                var source = new OnityTaskCompletionSource<Onity.Core.Unit>();
                new OnityAsyncStreamCleanupObserver(m_task,
                    outcome => outcome.Publish(source, Onity.Core.Unit.Default)).Register();
                return source.Task;
            }
        }
    }

    public static partial class OnityAsyncEnumerableExtensions
    {
        /// <summary>Returns the stream itself; narrows a derived stream type to the stream interface.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Stream description.</param>
        /// <returns>The same instance.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> AsOnityAsyncEnumerable<T>(this IOnityAsyncEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return source;
        }

        /// <summary>Exposes a synchronous sequence as a native asynchronous stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Sequence to enumerate.</param>
        /// <returns>An inert reusable description; every enumeration calls <c>GetEnumerator</c> on its first move
        /// and disposes the enumerator during cleanup.</returns>
        /// <remarks>Every move completes synchronously, so a consumer that does not suspend drains the sequence on
        /// its own stack. The enumeration token is checked before each item and a canceled token cancels the move.
        /// Exceptions from <c>GetEnumerator</c>, <c>MoveNext</c> and <c>Dispose</c> follow the usual
        /// stream fault and cleanup rules. The sequence is enumerated on the calling thread; no thread hop is
        /// added.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> ToOnityAsyncEnumerable<T>(this IEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return new OnityEnumerableAsyncEnumerable<T>(source);
        }

        /// <summary>Exposes the result of a task as a single-item native asynchronous stream.</summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="source">Task whose result becomes the only item.</param>
        /// <returns>An inert reusable description; the first move awaits the task, a faulted or canceled task
        /// faults or cancels the move, and the stream ends after the item.</returns>
        /// <remarks>A canceled enumeration token or disposal releases a pending move without waiting for the
        /// task; the task itself is never canceled and a late outcome is dropped. A pre-canceled token cancels
        /// the first move before the task is observed. No thread hop is added: the continuation runs where
        /// the task completes.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> ToOnityAsyncEnumerable<T>(this Task<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return new OnityTaskResultAsyncEnumerable<T>(OnityTask<T>.FromTask(source));
        }

        /// <summary>Exposes the result of an Onity task as a single-item native asynchronous stream.</summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="source">Task whose result becomes the only item.</param>
        /// <returns>An inert reusable description with the semantics of the <see cref="Task{TResult}"/>
        /// overload.</returns>
        /// <remarks>A pooled native task has a single consumer, so such a description supports one enumeration;
        /// call <c>Preserve()</c> first when several enumerations are needed. The task is consumed by the first
        /// move that is not pre-canceled, not by creating the description.</remarks>
        public static IOnityAsyncEnumerable<T> ToOnityAsyncEnumerable<T>(this OnityTask<T> source)
        {
            return new OnityTaskResultAsyncEnumerable<T>(source);
        }

        /// <summary>Exposes the completion of an Onity task as a single-item native asynchronous stream.</summary>
        /// <param name="source">Task whose successful completion yields one unit item.</param>
        /// <returns>An inert description of <see cref="Onity.Core.Unit"/> items with the semantics of the typed
        /// overload; a faulted or canceled task faults or cancels the move.</returns>
        /// <remarks>A pooled native task has a single consumer, so such a description supports one enumeration;
        /// call <c>Preserve()</c> first when several enumerations are needed.</remarks>
        public static IOnityAsyncEnumerable<Onity.Core.Unit> ToOnityAsyncEnumerable(this OnityTask source)
        {
            return new OnityUnitTaskAsyncEnumerable(source);
        }
    }
}
