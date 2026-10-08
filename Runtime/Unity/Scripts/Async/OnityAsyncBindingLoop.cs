using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Shared loop behind the <c>BindTo</c> overloads of the UI Toolkit and uGUI adapters: it consumes
    /// an async sequence and applies each item to a target (UniTask <c>UnityBindingExtensions</c>
    /// parity).
    /// </summary>
    /// <remarks>
    /// Thread affinity: starts on the calling thread (the main thread for Unity targets) and resumes on
    /// whichever thread produces the next item. A fault from the sequence (not a cancellation) is retried
    /// once by creating a new enumerator when <c>rebindOnError</c> is set, and a second consecutive
    /// fault, or a fault from the apply callback, is reported through the unobserved-exception path.
    /// Cancellation and normal completion end the loop silently. The enumerator is always disposed.
    /// </remarks>
    internal static class OnityAsyncBindingLoop
    {
        /// <summary>Runs the loop; the returned task completes when the binding ends.</summary>
        internal static async OnityTask RunAsync<TSource, TTarget>(
            IOnityAsyncEnumerable<TSource> source,
            TTarget target,
            Action<TTarget, TSource> apply,
            CancellationToken cancellationToken,
            bool rebindOnError)
        {
            bool repeat = false;

            while (true)
            {
                bool rebind = false;
                IOnityAsyncEnumerator<TSource> enumerator = source.GetAsyncEnumerator(cancellationToken);

                try
                {
                    while (true)
                    {
                        bool moved;

                        try
                        {
                            moved = await enumerator.MoveNextAsync();
                            repeat = false;
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception) when (rebindOnError && !repeat)
                        {
                            repeat = true;
                            rebind = true;
                            break;
                        }

                        if (!moved)
                        {
                            return;
                        }

                        apply(target, enumerator.Current);
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync();
                }

                if (!rebind)
                {
                    return;
                }
            }
        }

        /// <summary>Starts the loop without waiting for it.</summary>
        internal static void Start<TSource, TTarget>(
            IOnityAsyncEnumerable<TSource> source,
            TTarget target,
            Action<TTarget, TSource> apply,
            CancellationToken cancellationToken,
            bool rebindOnError)
        {
            RunAsync(source, target, apply, cancellationToken, rebindOnError).Forget();
        }
    }
}
