using System;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>Binds asynchronous streams to objects: every item is applied to a target until the stream
    /// ends, the binding is canceled, or an error repeats (UniTask <c>BindTo</c> parity).</summary>
    /// <remarks>Bindings are fire-and-forget. Each item is applied on the thread that completed the move,
    /// which is the Unity main thread for PlayerLoop-driven sources; start bindings on the main thread.
    /// Cancellation, including a canceled destroy token, ends a binding silently. An exception thrown by the
    /// bind action, a disposal failure, or a stream error that is not rebound is reported like an unobserved
    /// <c>OnityTaskVoid</c> fault (logged with <c>Debug.LogException</c>).</remarks>
    public static class OnityAsyncBindingExtensions
    {
        /// <summary>Applies every item to a behaviour until the stream ends or the behaviour is destroyed.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TObject">Behaviour type.</typeparam>
        /// <param name="source">Stream to bind.</param>
        /// <param name="monoBehaviour">Target behaviour; its destroy token ends the binding.</param>
        /// <param name="bindAction">Applies one item to the target.</param>
        /// <param name="rebindOnError">When true, a stream error re-acquires the stream once; an error that
        /// follows without a successful move in between is reported.</param>
        /// <exception cref="ArgumentNullException">Source, behaviour or action is null.</exception>
        public static void BindTo<TSource, TObject>(this IOnityAsyncEnumerable<TSource> source, TObject monoBehaviour,
            Action<TObject, TSource> bindAction, bool rebindOnError = true)
            where TObject : MonoBehaviour
        {
            OnityLinqCore.NotNull(source, nameof(source));
            if (ReferenceEquals(monoBehaviour, null))
            {
                throw new ArgumentNullException(nameof(monoBehaviour));
            }
            OnityLinqCore.NotNull(bindAction, nameof(bindAction));
            CancellationToken token = monoBehaviour.GetCancellationTokenOnDestroy();
            BindCore(source, monoBehaviour, bindAction, token, rebindOnError).Forget();
        }

        /// <summary>Applies every item to a target until the stream ends or the token is canceled.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TObject">Target type.</typeparam>
        /// <param name="source">Stream to bind.</param>
        /// <param name="bindTarget">Target handed to the action.</param>
        /// <param name="bindAction">Applies one item to the target.</param>
        /// <param name="cancellationToken">Enumeration token; cancellation ends the binding silently.</param>
        /// <param name="rebindOnError">When true, a stream error re-acquires the stream once; an error that
        /// follows without a successful move in between is reported.</param>
        /// <exception cref="ArgumentNullException">Source or action is null.</exception>
        public static void BindTo<TSource, TObject>(this IOnityAsyncEnumerable<TSource> source, TObject bindTarget,
            Action<TObject, TSource> bindAction, CancellationToken cancellationToken, bool rebindOnError = true)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(bindAction, nameof(bindAction));
            BindCore(source, bindTarget, bindAction, cancellationToken, rebindOnError).Forget();
        }

        private static async OnityTaskVoid BindCore<TSource, TObject>(IOnityAsyncEnumerable<TSource> source,
            TObject target, Action<TObject, TSource> bindAction, CancellationToken cancellationToken,
            bool rebindOnError)
        {
            bool repeated = false;
            while (true)
            {
                IOnityAsyncEnumerator<TSource> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
                bool rebind = false;
                try
                {
                    while (true)
                    {
                        bool moved;
                        try
                        {
                            moved = await enumerator.MoveNextAsync();
                            repeated = false;
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception) when (rebindOnError && !repeated)
                        {
                            repeated = true;
                            rebind = true;
                            break;
                        }
                        if (!moved)
                        {
                            return;
                        }
                        bindAction(target, enumerator.Current);
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
    }
}
