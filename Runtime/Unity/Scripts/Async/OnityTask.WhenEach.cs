using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    public readonly partial struct OnityTask
    {
        /// <summary>
        /// Streams the outcome of every task in completion order. Each item carries the task's result,
        /// or its fault, or an <see cref="OperationCanceledException"/> with the token of a canceled
        /// task, so one failed input does not end the stream.
        /// </summary>
        /// <remarks>
        /// The tasks are snapshotted when this method runs. Creating the enumerator observes every
        /// task once in argument order, so already completed tasks are yielded first in argument order;
        /// the stream ends after the last task. The returned sequence can be enumerated once.
        /// Disposing the enumerator early does not cancel the tasks: their later outcomes are still
        /// observed and then discarded. The enumeration token cancels only waiting for the next item.
        /// Items are published on the thread that completes each task, without a main-thread hop. The
        /// stream, its enumerator, an unbounded channel and one observer per task are allocated.
        /// </remarks>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="tasks">Tasks to observe. Each single-consumer task is consumed once.</param>
        /// <returns>A single-use sequence with one item per task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        /// <exception cref="ArgumentException">The array repeats a single-consumer task.</exception>
        public static IOnityAsyncEnumerable<OnityWhenEachResult<T>> WhenEach<T>(params OnityTask<T>[] tasks)
        {
            return OnityWhenEachEnumerable<T>.Create(OnityTaskComposition.ToOwnedArray(tasks, nameof(tasks)));
        }

        /// <summary>
        /// Streams the outcome of every task in a sequence in completion order. The sequence is
        /// enumerated once, then behaves as <see cref="WhenEach{T}(OnityTask{T}[])"/>.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="tasks">Tasks to observe. Each single-consumer task is consumed once.</param>
        /// <returns>A single-use sequence with one item per task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        /// <exception cref="ArgumentException">The sequence repeats a single-consumer task.</exception>
        public static IOnityAsyncEnumerable<OnityWhenEachResult<T>> WhenEach<T>(IEnumerable<OnityTask<T>> tasks)
        {
            return OnityWhenEachEnumerable<T>.Create(OnityTaskComposition.ToOwnedArray(tasks, nameof(tasks)));
        }
    }

    /// <summary>
    /// Single-use stream of <c>OnityTask.WhenEach</c>: the first enumerator takes the task snapshot.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    internal sealed class OnityWhenEachEnumerable<T> : IOnityAsyncEnumerable<OnityWhenEachResult<T>>
    {
        private OnityTask<T>[] m_inputs;

        private OnityWhenEachEnumerable(OnityTask<T>[] inputs)
        {
            m_inputs = inputs;
        }

        internal static OnityWhenEachEnumerable<T> Create(OnityTask<T>[] inputs)
        {
            OnityTaskComposition.ValidateDistinctInputs(inputs, OnityTaskComposition.k_whenEach, "tasks");
            return new OnityWhenEachEnumerable<T>(inputs);
        }

        public IOnityAsyncEnumerator<OnityWhenEachResult<T>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            OnityTask<T>[] inputs = Interlocked.Exchange(ref m_inputs, null);
            if (inputs == null)
            {
                throw new InvalidOperationException(
                    "WhenEach can be enumerated only once because it consumes its input tasks.");
            }

            OnityWhenEachEnumerator<T> enumerator = new OnityWhenEachEnumerator<T>(inputs.Length, cancellationToken);
            enumerator.Start(inputs);
            return enumerator;
        }
    }

    /// <summary>
    /// Observes every input once and writes each outcome to an unbounded channel whose reader
    /// enumerator supplies the move, cancellation and disposal behavior. The channel completes after
    /// the last outcome, or when the enumerator is disposed, which discards later outcomes.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    internal sealed class OnityWhenEachEnumerator<T> : IOnityAsyncEnumerator<OnityWhenEachResult<T>>
    {
        private readonly OnityChannel<OnityWhenEachResult<T>> m_channel;
        private readonly IOnityAsyncEnumerator<OnityWhenEachResult<T>> m_reader;
        private int m_remaining;

        internal OnityWhenEachEnumerator(int count, CancellationToken cancellationToken)
        {
            m_channel = OnityChannel.CreateUnbounded<OnityWhenEachResult<T>>();
            m_reader = m_channel.Reader.ReadAllAsync().GetAsyncEnumerator(cancellationToken);
            m_remaining = count;
        }

        public OnityWhenEachResult<T> Current => m_reader.Current;

        public OnityTask<bool> MoveNextAsync()
        {
            return m_reader.MoveNextAsync();
        }

        public OnityTask DisposeAsync()
        {
            OnityTask disposal = m_reader.DisposeAsync();
            m_channel.Writer.TryComplete();
            return disposal;
        }

        /// <summary>Registers an observer on every input in argument order.</summary>
        internal void Start(OnityTask<T>[] inputs)
        {
            if (inputs.Length == 0)
            {
                m_channel.Writer.TryComplete();
                return;
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                new InputObserver(this, inputs[i]).Register();
            }
        }

        private void Publish(OnityWhenEachResult<T> item)
        {
            // A write after disposal is rejected by the closed channel and the item is dropped.
            m_channel.Writer.TryWrite(item);
            if (Interlocked.Decrement(ref m_remaining) == 0)
            {
                m_channel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// Observes one input once. A registration failure is reported as that input's outcome; a
        /// callback that a custom source accepted before throwing still consumes the input later.
        /// </summary>
        private sealed class InputObserver
        {
            private readonly OnityWhenEachEnumerator<T> m_owner;
            private readonly Action m_observe;
            private OnityTask<T> m_input;
            private int m_observed;
            private int m_reported;

            internal InputObserver(OnityWhenEachEnumerator<T> owner, OnityTask<T> input)
            {
                m_owner = owner;
                m_input = input;
                m_observe = Observe;
            }

            internal void Register()
            {
                // The callback can run on another thread and clear the field during registration.
                OnityTask<T> input = m_input;
                try
                {
                    input.RegisterWhenAnyObserver(m_observe);
                }
                catch (Exception exception)
                {
                    Report(new OnityWhenEachResult<T>(exception));
                }
            }

            private void Observe()
            {
                if (Interlocked.Exchange(ref m_observed, 1) != 0)
                {
                    return;
                }

                OnityWhenEachResult<T> item;
                try
                {
                    OnityTaskSourceStatus status = m_input.ReadWhenAnyOutcome(
                        out T result, out Exception fault, out CancellationToken cancellationToken);
                    if (fault != null)
                    {
                        item = new OnityWhenEachResult<T>(fault);
                    }
                    else if (status == OnityTaskSourceStatus.Canceled)
                    {
                        item = new OnityWhenEachResult<T>(new OperationCanceledException(cancellationToken));
                    }
                    else if (status == OnityTaskSourceStatus.Succeeded)
                    {
                        item = new OnityWhenEachResult<T>(result);
                    }
                    else
                    {
                        item = new OnityWhenEachResult<T>(
                            new InvalidOperationException("WhenEach input did not provide a terminal outcome."));
                    }
                }
                catch (Exception exception)
                {
                    item = new OnityWhenEachResult<T>(exception);
                }

                m_input = default;
                Report(item);
            }

            private void Report(OnityWhenEachResult<T> item)
            {
                if (Interlocked.Exchange(ref m_reported, 1) == 0)
                {
                    m_owner.Publish(item);
                }
            }
        }
    }
}
