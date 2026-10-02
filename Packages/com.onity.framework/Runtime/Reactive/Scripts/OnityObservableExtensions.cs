using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Core reactive operators for Onity observables.
    /// </summary>
    public static partial class OnityObservableExtensions
    {
        /// <summary>
        /// Filters source values using the provided predicate.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="predicate">Filter condition.</param>
        /// <returns>Filtered observable stream.</returns>
        public static IOnityObservable<T> Where<T>(this IOnityObservable<T> source, Predicate<T> predicate)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return new OnityWhereObservable<T>(source, predicate);
        }

        /// <summary>
        /// Projects source values into a new result stream.
        /// </summary>
        /// <typeparam name="TSource">Source value type.</typeparam>
        /// <typeparam name="TResult">Projected value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="selector">Projection function.</param>
        /// <returns>Projected observable stream.</returns>
        public static IOnityObservable<TResult> Select<TSource, TResult>(
            this IOnityObservable<TSource> source,
            Func<TSource, TResult> selector)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            // Where followed by Select runs as one operator with the same call order.
            if (source is OnityWhereObservable<TSource> whereObservable)
            {
                return new OnityWhereSelectObservable<TSource, TResult>(
                    whereObservable.Source,
                    whereObservable.Predicate,
                    selector);
            }

            return new OnitySelectObservable<TSource, TResult>(source, selector);
        }

        /// <summary>
        /// Suppresses consecutive duplicate values.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="comparer">Optional equality comparer.</param>
        /// <returns>Distinct-by-consecutive observable stream.</returns>
        public static IOnityObservable<T> DistinctUntilChanged<T>(
            this IOnityObservable<T> source,
            IEqualityComparer<T> comparer = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            IEqualityComparer<T> equalityComparer = comparer ?? EqualityComparer<T>.Default;

            return new OnityDistinctUntilChangedObservable<T>(source, equalityComparer);
        }

        /// <summary>
        /// Skips the first <paramref name="count" /> source values and forwards the rest.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="count">Number of values to skip.</param>
        /// <returns>Observable that drops the leading <paramref name="count" /> values.</returns>
        public static IOnityObservable<T> Skip<T>(this IOnityObservable<T> source, int count)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 0)
            {
                return source;
            }

            return new OnitySkipObservable<T>(source, count);
        }

        /// <summary>
        /// Skips source values while <paramref name="predicate" /> returns true.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="predicate">Skip condition.</param>
        /// <returns>Observable that forwards values once <paramref name="predicate" /> first returns false.</returns>
        public static IOnityObservable<T> SkipWhile<T>(this IOnityObservable<T> source, Predicate<T> predicate)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return new OnitySkipWhileObservable<T>(source, predicate);
        }

        /// <summary>
        /// Forwards the first <paramref name="count" /> source values then disposes the upstream subscription.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="count">Maximum number of values to forward.</param>
        /// <returns>Observable that completes after <paramref name="count" /> values.</returns>
        public static IOnityObservable<T> Take<T>(this IOnityObservable<T> source, int count)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 0)
            {
                return OnityObservable.Empty<T>();
            }

            return new OnityTakeObservable<T>(source, count);
        }

        /// <summary>
        /// Forwards source values while <paramref name="predicate" /> returns true.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="predicate">Continuation condition.</param>
        /// <returns>Observable that completes once <paramref name="predicate" /> first returns false.</returns>
        public static IOnityObservable<T> TakeWhile<T>(this IOnityObservable<T> source, Predicate<T> predicate)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return new OnityTakeWhileObservable<T>(source, predicate);
        }

        /// <summary>
        /// Emits <paramref name="initialValue" /> before subscribing to <paramref name="source" />.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="initialValue">Prefix value emitted before source values.</param>
        /// <returns>Observable that emits <paramref name="initialValue" /> followed by source values.</returns>
        public static IOnityObservable<T> StartWith<T>(this IOnityObservable<T> source, T initialValue)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new OnityStartWithObservable<T>(source, initialValue);
        }

        /// <summary>
        /// Subscribes to an observable using an <see cref="Action{T}" /> callback.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="onNext">Callback invoked for each value.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable Subscribe<T>(this IOnityObservable<T> source, Action<T> onNext)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (onNext == null)
            {
                throw new ArgumentNullException(nameof(onNext));
            }

            return OnityNodeSources.SubscribeAction(source, onNext);
        }

        /// <summary>
        /// Subscribes with value, error, and completion callbacks.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="onNext">Value callback.</param>
        /// <param name="onError">Error callback.</param>
        /// <param name="onCompleted">Completion callback.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable Subscribe<T>(
            this IOnityObservable<T> source,
            Action<T> onNext,
            Action<Exception> onError,
            Action<OnityResult> onCompleted)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (onNext == null)
            {
                throw new ArgumentNullException(nameof(onNext));
            }

            return source.Subscribe(new AnonymousOnityObserver<T>(onNext, onError, onCompleted));
        }

        /// <summary>
        /// Stops forwarding source values when cancellation token is canceled.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cancellation-aware stream.</returns>
        public static IOnityObservable<T> TakeUntilCancellation<T>(
            this IOnityObservable<T> source,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityObservable.Empty<T>();
            }

            return new OnityObservable<T>(
                observer =>
                {
                    IDisposable sourceSubscription = source.Subscribe(observer);
                    CancellationTokenRegistration registration =
                        cancellationToken.Register(sourceSubscription.Dispose);

                    return new DisposableAction(
                        () =>
                        {
                            registration.Dispose();
                            sourceSubscription.Dispose();
                        });
                });
        }

        /// <summary>
        /// Returns a task that completes with the first source value.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task completed by first source value.</returns>
        public static Task<T> FirstAsync<T>(
            this IOnityObservable<T> source,
            CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            TaskCompletionSource<T> completionSource =
                new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            IDisposable sourceSubscription = null;
            CancellationTokenRegistration cancellationRegistration = default;
            bool shouldDisposeAfterSubscribe = false;

            sourceSubscription = source.Subscribe(
                value =>
                {
                    if (completionSource.TrySetResult(value) == false)
                    {
                        return;
                    }

                    cancellationRegistration.Dispose();

                    if (sourceSubscription == null)
                    {
                        shouldDisposeAfterSubscribe = true;
                        return;
                    }

                    sourceSubscription.Dispose();
                });

            if (shouldDisposeAfterSubscribe)
            {
                sourceSubscription.Dispose();
            }

            if (completionSource.Task.IsCompleted)
            {
                return completionSource.Task;
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration =
                    cancellationToken.Register(
                        () =>
                        {
                            if (completionSource.TrySetCanceled(cancellationToken))
                            {
                                sourceSubscription.Dispose();
                            }
                        });
            }

            return completionSource.Task;
        }

        /// <summary>
        /// Returns a task that completes when the source emits one unit value.
        /// </summary>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static async Task ToTask(
            this IOnityObservable<Unit> source,
            CancellationToken cancellationToken = default)
        {
            await FirstAsync(source, cancellationToken);
        }
    }
}
