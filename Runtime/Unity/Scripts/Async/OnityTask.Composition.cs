using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Onity.Unity.Async
{
    public readonly partial struct OnityTask
    {
        /// <summary>
        /// Completes after every task in a sequence completes. The sequence is enumerated once into an
        /// array, which then follows <see cref="WhenAll(OnityTask[])"/>, including its fault,
        /// cancellation, duplicate-input and task tracker behavior.
        /// </summary>
        /// <param name="tasks">Tasks to await. Each single-consumer task is consumed once.</param>
        /// <returns>A task that completes after every input.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        public static OnityTask WhenAll(IEnumerable<OnityTask> tasks)
        {
            return WhenAll(OnityTaskComposition.ToArray(tasks, nameof(tasks)));
        }

        /// <summary>
        /// Awaits every typed task in a sequence and returns the results in sequence order. The
        /// sequence is enumerated once into an array, which then follows
        /// <see cref="WhenAll{T}(OnityTask{T}[])"/>, including its fault, cancellation,
        /// duplicate-input and task tracker behavior.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="tasks">Tasks to await. Each single-consumer task is consumed once.</param>
        /// <returns>The results in sequence order; an empty sequence returns an empty array.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        public static OnityTask<T[]> WhenAll<T>(IEnumerable<OnityTask<T>> tasks)
        {
            return WhenAll(OnityTaskComposition.ToArray(tasks, nameof(tasks)));
        }

        /// <summary>
        /// Completes with the sequence index of the first observed terminal task. The sequence is
        /// enumerated once into an array, which then follows <see cref="WhenAny(OnityTask[])"/>:
        /// every input is observed and losers keep running without cancellation.
        /// </summary>
        /// <param name="tasks">Nonempty task sequence.</param>
        /// <returns>A single-consumer task with the winner index, or its failure or cancellation.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The sequence is empty or repeats a single-consumer task.
        /// </exception>
        public static OnityTask<int> WhenAny(IEnumerable<OnityTask> tasks)
        {
            return WhenAny(OnityTaskComposition.ToArray(tasks, nameof(tasks)));
        }

        /// <summary>
        /// Completes with the sequence index and result of the first observed terminal task. The
        /// sequence is enumerated once into an array, which then follows
        /// <see cref="WhenAny{T}(OnityTask{T}[])"/>: every input is observed and losers keep running
        /// without cancellation.
        /// </summary>
        /// <typeparam name="T">Shared input result type.</typeparam>
        /// <param name="tasks">Nonempty task sequence.</param>
        /// <returns>
        /// A single-consumer task with the winner index and result, or its failure or cancellation.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The sequence is empty or repeats a single-consumer task.
        /// </exception>
        public static OnityTask<(int winnerIndex, T result)> WhenAny<T>(IEnumerable<OnityTask<T>> tasks)
        {
            return WhenAny(OnityTaskComposition.ToArray(tasks, nameof(tasks)));
        }

        /// <summary>
        /// Completes when either the typed left task or the untyped right task completes first.
        /// Both inputs are consumed; the loser keeps running, is not canceled, and is observed when it
        /// completes. The winner's fault or cancellation is propagated instead of a result.
        /// </summary>
        /// <remarks>
        /// The left input is registered first, so it wins when both inputs are already complete.
        /// Publication runs on the completing input's thread and does not capture Unity's
        /// <see cref="System.Threading.SynchronizationContext"/>; await <c>AsTask()</c> from the Unity
        /// context to resume there. An input that cannot be observed, such as a stale pooled task,
        /// becomes that input's fault. The returned native task is single-consumer and its source is
        /// pooled.
        /// </remarks>
        /// <typeparam name="T">Result type of the left task.</typeparam>
        /// <param name="leftTask">Typed input whose result is returned when it wins.</param>
        /// <param name="rightTask">Untyped input.</param>
        /// <returns>
        /// A single-consumer task with <c>(true, leftResult)</c> when the left task wins, or
        /// <c>(false, default)</c> when the right task wins.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Both inputs refer to the same single-consumer native operation.
        /// </exception>
        public static OnityTask<(bool hasResultLeft, T result)> WhenAny<T>(
            OnityTask<T> leftTask,
            OnityTask rightTask)
        {
            if (leftTask.TryGetWhenAnyIdentity(out OnityWhenAnyInputIdentity left)
                && rightTask.TryGetWhenAnyIdentity(out OnityWhenAnyInputIdentity right)
                && left.Equals(right))
            {
                OnityTaskComposition.ThrowDuplicateInput(OnityTaskComposition.k_whenAny, nameof(rightTask));
            }

            OnityWhenAnyLeftRightTaskSource<T> source = OnityWhenAnyLeftRightTaskSource<T>.Rent(leftTask, rightTask);
            OnityTask<(bool hasResultLeft, T result)> output = new OnityTask<(bool hasResultLeft, T result)>(source);
            source.Start();
            return output;
        }
    }

    /// <summary>
    /// Argument handling shared by the task composition overloads. Composition sources are pooled up to
    /// <see cref="OnityTask.SourcePoolCapacity"/>.
    /// </summary>
    internal static class OnityTaskComposition
    {
        internal const string k_whenAll = "WhenAll";
        internal const string k_whenAny = "WhenAny";
        internal const string k_whenEach = "WhenEach";

        private const int k_initialBufferLength = 4;
        private const int k_pairwiseValidationLimit = 16;

        /// <summary>One when the task wraps a single-consumer native source, otherwise zero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountSingleConsumer<T>(in OnityTask<T> task)
        {
            return task.HasSingleConsumerSource ? 1 : 0;
        }

        /// <summary>
        /// Probes for a successful completion without throwing: a task whose token is no longer valid
        /// reports false and is then reported as a registration fault by the pending path.
        /// </summary>
        internal static bool IsCompletedSuccessfully<T>(in OnityTask<T> task)
        {
            try
            {
                return task.IsCompletedSuccessfully;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the index of the first input that repeats an earlier single-consumer source and
        /// token, or -1. The comparison is quadratic, so callers use it only when at least two inputs
        /// are single-consumer native tasks.
        /// </summary>
        internal static int FindDuplicateInput(IOnityCompositionInputs inputs, int count)
        {
            for (int i = 1; i < count; i++)
            {
                if (!inputs.TryGetInputIdentity(i, out OnityWhenAnyInputIdentity identity))
                {
                    continue;
                }

                for (int j = 0; j < i; j++)
                {
                    if (inputs.TryGetInputIdentity(j, out OnityWhenAnyInputIdentity previous)
                        && identity.Equals(previous))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Throws when an array repeats a single-consumer source and token. Small arrays compare
        /// pairwise; larger ones use a set.
        /// </summary>
        internal static void ValidateDistinctInputs<T>(OnityTask<T>[] inputs, string operation, string parameterName)
        {
            HashSet<OnityWhenAnyInputIdentity> seen = null;
            for (int i = 0; i < inputs.Length; i++)
            {
                if (!inputs[i].TryGetWhenAnyIdentity(out OnityWhenAnyInputIdentity identity))
                {
                    continue;
                }

                if (inputs.Length > k_pairwiseValidationLimit)
                {
                    if (seen == null)
                    {
                        seen = new HashSet<OnityWhenAnyInputIdentity>();
                    }

                    if (!seen.Add(identity))
                    {
                        ThrowDuplicateInput(operation, parameterName);
                    }

                    continue;
                }

                for (int j = 0; j < i; j++)
                {
                    if (inputs[j].TryGetWhenAnyIdentity(out OnityWhenAnyInputIdentity previous)
                        && identity.Equals(previous))
                    {
                        ThrowDuplicateInput(operation, parameterName);
                    }
                }
            }
        }

        /// <summary>Throws for a repeated input of a fixed-arity overload, naming its parameter.</summary>
        internal static void ThrowDuplicateInput(string operation, int index)
        {
            ThrowDuplicateInput(operation, "task" + (index + 1));
        }

        internal static void ThrowDuplicateInput(string operation, string parameterName)
        {
            throw new ArgumentException(
                "A single-consumer OnityTask cannot be passed to " + operation + " twice.",
                parameterName);
        }

        /// <summary>
        /// Enumerates a sequence once. An array is returned as is; other sequences are copied into a
        /// new exact-length array.
        /// </summary>
        internal static T[] ToArray<T>(IEnumerable<T> source, string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (source is T[] array)
            {
                return array;
            }

            return CopyToArray(source);
        }

        /// <summary>
        /// Enumerates a sequence once into an array that the caller owns: arrays are copied, so later
        /// writes to the caller's array do not change the snapshot.
        /// </summary>
        internal static T[] ToOwnedArray<T>(IEnumerable<T> source, string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (source is T[] array)
            {
                if (array.Length == 0)
                {
                    return Array.Empty<T>();
                }

                T[] copy = new T[array.Length];
                Array.Copy(array, copy, array.Length);
                return copy;
            }

            return CopyToArray(source);
        }

        private static T[] CopyToArray<T>(IEnumerable<T> source)
        {
            if (source is ICollection<T> collection)
            {
                int count = collection.Count;
                if (count == 0)
                {
                    return Array.Empty<T>();
                }

                T[] copy = new T[count];
                collection.CopyTo(copy, 0);
                return copy;
            }

            T[] buffer = Array.Empty<T>();
            int length = 0;
            foreach (T item in source)
            {
                if (length == buffer.Length)
                {
                    Array.Resize(ref buffer, length == 0 ? k_initialBufferLength : length * 2);
                }

                buffer[length++] = item;
            }

            if (length != buffer.Length)
            {
                Array.Resize(ref buffer, length);
            }

            return buffer;
        }
    }
}
