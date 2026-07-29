using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Reactive v2 operators and subscribe helpers.
    /// </summary>
    internal static class OnityObservableAsyncCoreExtensions
    {
        /// <summary>
        /// Subscribes with value callback only.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="onNext">Value callback.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable Subscribe<T>(this IOnityObservableV2<T> source, Action<T> onNext)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (onNext == null)
            {
                throw new ArgumentNullException(nameof(onNext));
            }

            return source.Subscribe(new AnonymousOnityObserver<T>(onNext));
        }

        /// <summary>
        /// Subscribes with full v2 lifecycle callbacks.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="onNext">Value callback.</param>
        /// <param name="onError">Error callback.</param>
        /// <param name="onCompleted">Completion callback.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable Subscribe<T>(
            this IOnityObservableV2<T> source,
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
        /// Stops the source when cancellation token is canceled.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cancellation-aware stream.</returns>
        public static IOnityObservableV2<T> TakeUntil<T>(
            this IOnityObservableV2<T> source,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityObservableV2.Empty<T>();
            }

            return new OnityObservableV2<T>(
                observer =>
                {
                    int isTerminated = 0;
                    IDisposable sourceSubscription = source.Subscribe(
                        new AnonymousOnityObserver<T>(
                            observer.OnNext,
                            exception =>
                            {
                                if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                                {
                                    return;
                                }

                                observer.OnError(exception);
                            },
                            result =>
                            {
                                if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                                {
                                    return;
                                }

                                observer.OnCompleted(result);
                            }));

                    CancellationTokenRegistration cancellationRegistration =
                        cancellationToken.Register(
                            () =>
                            {
                                if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                                {
                                    return;
                                }

                                sourceSubscription.Dispose();
                                observer.OnCompleted();
                            });

                    return new DisposableAction(
                        () =>
                        {
                            cancellationRegistration.Dispose();
                            sourceSubscription.Dispose();
                        });
                });
        }

        /// <summary>
        /// Stops the source when provided task completes.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="untilTask">Task used as stop signal.</param>
        /// <returns>Task-aware stream.</returns>
        public static IOnityObservableV2<T> TakeUntil<T>(
            this IOnityObservableV2<T> source,
            Task untilTask)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (untilTask == null)
            {
                throw new ArgumentNullException(nameof(untilTask));
            }

            if (untilTask.IsCompleted)
            {
                return OnityObservableV2.Empty<T>();
            }

            return new OnityObservableV2<T>(
                observer =>
                {
                    int isTerminated = 0;
                    IDisposable sourceSubscription = source.Subscribe(
                        new AnonymousOnityObserver<T>(
                            observer.OnNext,
                            exception =>
                            {
                                if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                                {
                                    return;
                                }

                                observer.OnError(exception);
                            },
                            result =>
                            {
                                if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                                {
                                    return;
                                }

                                observer.OnCompleted(result);
                            }));

                    untilTask.ContinueWith(
                        completedTask =>
                        {
                            if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                            {
                                return;
                            }

                            sourceSubscription.Dispose();

                            if (completedTask.IsFaulted && completedTask.Exception != null)
                            {
                                observer.OnError(completedTask.Exception.GetBaseException());
                                return;
                            }

                            observer.OnCompleted();
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);

                    return sourceSubscription;
                });
        }

        /// <summary>
        /// Emits latest source value after silence window.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="dueTime">Silence duration.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <returns>Debounced stream.</returns>
        public static IOnityObservableV2<T> Debounce<T>(
            this IOnityObservableV2<T> source,
            TimeSpan dueTime,
            OnityTimeProvider timeProvider = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (dueTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(dueTime));
            }

            if (dueTime == TimeSpan.Zero)
            {
                return source;
            }

            OnityTimeProvider resolvedTimeProvider = timeProvider ?? OnityTimeProvider.System;

            return new OnityObservableV2<T>(
                observer =>
                {
                    object gate = new object();
                    CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
                    long version = 0;
                    bool hasLatestValue = false;
                    bool isSourceCompleted = false;
                    T latestValue = default;
                    OnityResult completionResult = OnityResult.Success();
                    int isTerminated = 0;

                    void TerminateWithError(Exception exception)
                    {
                        if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                        {
                            return;
                        }

                        lifetimeCancellation.Cancel();
                        observer.OnError(exception);
                    }

                    void TerminateWithCompletion(OnityResult result)
                    {
                        if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                        {
                            return;
                        }

                        lifetimeCancellation.Cancel();
                        observer.OnCompleted(result);
                    }

                    IDisposable sourceSubscription = source.Subscribe(
                        new AnonymousOnityObserver<T>(
                            value =>
                            {
                                long expectedVersion;

                                lock (gate)
                                {
                                    latestValue = value;
                                    hasLatestValue = true;
                                    version++;
                                    expectedVersion = version;
                                }

                                _ = EmitLatestAfterDelayAsync(expectedVersion);
                            },
                            TerminateWithError,
                            result =>
                            {
                                long expectedVersion;
                                bool completeImmediately;

                                lock (gate)
                                {
                                    isSourceCompleted = true;
                                    completionResult = result;
                                    completeImmediately = hasLatestValue == false;
                                    version++;
                                    expectedVersion = version;
                                }

                                if (completeImmediately)
                                {
                                    TerminateWithCompletion(result);
                                    return;
                                }

                                _ = EmitLatestAfterDelayAsync(expectedVersion);
                            }));

                    async Task EmitLatestAfterDelayAsync(long expectedVersion)
                    {
                        try
                        {
                            await resolvedTimeProvider.DelayAsync(dueTime, lifetimeCancellation.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception exception)
                        {
                            TerminateWithError(exception);
                            return;
                        }

                        bool shouldEmit = false;
                        bool shouldComplete = false;
                        T emittedValue = default;
                        OnityResult result = OnityResult.Success();

                        lock (gate)
                        {
                            if (expectedVersion != version || hasLatestValue == false)
                            {
                                return;
                            }

                            emittedValue = latestValue;
                            hasLatestValue = false;
                            shouldEmit = true;

                            if (isSourceCompleted)
                            {
                                shouldComplete = true;
                                result = completionResult;
                            }
                        }

                        if (shouldEmit)
                        {
                            observer.OnNext(emittedValue);
                        }

                        if (shouldComplete)
                        {
                            TerminateWithCompletion(result);
                        }
                    }

                    return new DisposableAction(
                        () =>
                        {
                            lifetimeCancellation.Cancel();
                            sourceSubscription.Dispose();
                            lifetimeCancellation.Dispose();
                        });
                });
        }

        /// <summary>
        /// Emits latest source value at fixed sampling interval while values are flowing.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="interval">Sampling interval.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <returns>Sampled stream.</returns>
        public static IOnityObservableV2<T> ThrottleLast<T>(
            this IOnityObservableV2<T> source,
            TimeSpan interval,
            OnityTimeProvider timeProvider = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            OnityTimeProvider resolvedTimeProvider = timeProvider ?? OnityTimeProvider.System;

            return new OnityObservableV2<T>(
                observer =>
                {
                    object gate = new object();
                    CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
                    bool hasLatestValue = false;
                    bool isPumpRunning = false;
                    bool isSourceCompleted = false;
                    T latestValue = default;
                    OnityResult completionResult = OnityResult.Success();
                    int isTerminated = 0;

                    void TerminateWithError(Exception exception)
                    {
                        if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                        {
                            return;
                        }

                        lifetimeCancellation.Cancel();
                        observer.OnError(exception);
                    }

                    void TerminateWithCompletion(OnityResult result)
                    {
                        if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                        {
                            return;
                        }

                        lifetimeCancellation.Cancel();
                        observer.OnCompleted(result);
                    }

                    IDisposable sourceSubscription = source.Subscribe(
                        new AnonymousOnityObserver<T>(
                            value =>
                            {
                                bool shouldStartPump = false;

                                lock (gate)
                                {
                                    latestValue = value;
                                    hasLatestValue = true;

                                    if (isPumpRunning == false)
                                    {
                                        isPumpRunning = true;
                                        shouldStartPump = true;
                                    }
                                }

                                if (shouldStartPump)
                                {
                                    _ = PumpAsync();
                                }
                            },
                            TerminateWithError,
                            result =>
                            {
                                bool completeImmediately = false;

                                lock (gate)
                                {
                                    isSourceCompleted = true;
                                    completionResult = result;

                                    if (isPumpRunning == false && hasLatestValue == false)
                                    {
                                        completeImmediately = true;
                                    }
                                }

                                if (completeImmediately)
                                {
                                    TerminateWithCompletion(result);
                                }
                            }));

                    async Task PumpAsync()
                    {
                        while (lifetimeCancellation.IsCancellationRequested == false)
                        {
                            try
                            {
                                await resolvedTimeProvider.DelayAsync(interval, lifetimeCancellation.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                            catch (Exception exception)
                            {
                                TerminateWithError(exception);
                                return;
                            }

                            bool shouldEmit = false;
                            bool shouldContinue = false;
                            bool shouldComplete = false;
                            T emittedValue = default;
                            OnityResult result = OnityResult.Success();

                            lock (gate)
                            {
                                if (hasLatestValue)
                                {
                                    emittedValue = latestValue;
                                    hasLatestValue = false;
                                    shouldEmit = true;
                                }

                                if (hasLatestValue)
                                {
                                    shouldContinue = true;
                                }
                                else
                                {
                                    isPumpRunning = false;
                                }

                                if (isPumpRunning == false && isSourceCompleted)
                                {
                                    shouldComplete = true;
                                    result = completionResult;
                                }
                            }

                            if (shouldEmit)
                            {
                                observer.OnNext(emittedValue);
                            }

                            if (shouldComplete)
                            {
                                TerminateWithCompletion(result);
                                return;
                            }

                            if (shouldContinue == false)
                            {
                                return;
                            }
                        }
                    }

                    return new DisposableAction(
                        () =>
                        {
                            lifetimeCancellation.Cancel();
                            sourceSubscription.Dispose();
                            lifetimeCancellation.Dispose();
                        });
                });
        }

        /// <summary>
        /// Projects values using asynchronous selector with sequential execution.
        /// </summary>
        /// <typeparam name="TSource">Source value type.</typeparam>
        /// <typeparam name="TResult">Result value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="selector">Async projection callback.</param>
        /// <returns>Projected stream.</returns>
        public static IOnityObservableV2<TResult> SelectAwait<TSource, TResult>(
            this IOnityObservableV2<TSource> source,
            Func<TSource, CancellationToken, ValueTask<TResult>> selector)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            return new OnityObservableV2<TResult>(
                observer =>
                {
                    CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
                    object gate = new object();
                    Queue<TSource> pendingValues = new Queue<TSource>(8);
                    OnityResult completionResult = OnityResult.Success();
                    bool isProcessing = false;
                    bool isSourceCompleted = false;
                    int isTerminated = 0;
                    IDisposable sourceSubscription = null;

                    void TerminateWithError(Exception exception)
                    {
                        if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                        {
                            return;
                        }

                        lifetimeCancellation.Cancel();
                        sourceSubscription?.Dispose();
                        observer.OnError(exception);
                    }

                    void TerminateWithCompletion()
                    {
                        if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                        {
                            return;
                        }

                        lifetimeCancellation.Cancel();
                        sourceSubscription?.Dispose();
                        observer.OnCompleted(completionResult);
                    }

                    void TryCompleteWhenIdle()
                    {
                        bool shouldComplete = false;

                        lock (gate)
                        {
                            if (isSourceCompleted && isProcessing == false && pendingValues.Count == 0)
                            {
                                shouldComplete = true;
                            }
                        }

                        if (shouldComplete)
                        {
                            TerminateWithCompletion();
                        }
                    }

                    void EnqueueValue(TSource value)
                    {
                        bool shouldStartProcessor = false;

                        lock (gate)
                        {
                            pendingValues.Enqueue(value);

                            if (isProcessing == false)
                            {
                                isProcessing = true;
                                shouldStartProcessor = true;
                            }
                        }

                        if (shouldStartProcessor)
                        {
                            _ = ProcessQueueAsync();
                        }
                    }

                    sourceSubscription = source.Subscribe(
                        new AnonymousOnityObserver<TSource>(
                            EnqueueValue,
                            exception =>
                            {
                                completionResult = OnityResult.Failure(exception);
                                lock (gate)
                                {
                                    isSourceCompleted = true;
                                }

                                TerminateWithError(exception);
                            },
                            result =>
                            {
                                completionResult = result;
                                lock (gate)
                                {
                                    isSourceCompleted = true;
                                }

                                TryCompleteWhenIdle();
                            }));

                    async Task ProcessQueueAsync()
                    {
                        while (true)
                        {
                            TSource value;

                            lock (gate)
                            {
                                if (pendingValues.Count == 0)
                                {
                                    isProcessing = false;
                                    break;
                                }

                                value = pendingValues.Dequeue();
                            }

                            try
                            {
                                TResult projectedValue =
                                    await Task.Run(
                                            async () =>
                                                await selector(value, lifetimeCancellation.Token)
                                                    .ConfigureAwait(false),
                                            lifetimeCancellation.Token)
                                        .ConfigureAwait(false);

                                if (lifetimeCancellation.IsCancellationRequested == false)
                                {
                                    observer.OnNext(projectedValue);
                                }
                            }
                            catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
                            {
                                break;
                            }
                            catch (Exception exception)
                            {
                                completionResult = OnityResult.Failure(exception);

                                lock (gate)
                                {
                                    isSourceCompleted = true;
                                }

                                TerminateWithError(exception);
                                return;
                            }
                        }

                        TryCompleteWhenIdle();
                    }

                    return new DisposableAction(
                        () =>
                        {
                            lifetimeCancellation.Cancel();
                            sourceSubscription?.Dispose();
                            lifetimeCancellation.Dispose();
                        });
                });
        }

        /// <summary>
        /// Filters values using asynchronous predicate with sequential execution.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="predicate">Async predicate callback.</param>
        /// <returns>Filtered stream.</returns>
        public static IOnityObservableV2<T> WhereAwait<T>(
            this IOnityObservableV2<T> source,
            Func<T, CancellationToken, ValueTask<bool>> predicate)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return new OnityObservableV2<T>(
                observer =>
                {
                    CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
                    object gate = new object();
                    Queue<T> pendingValues = new Queue<T>(8);
                    OnityResult completionResult = OnityResult.Success();
                    bool isProcessing = false;
                    bool isSourceCompleted = false;
                    int isTerminated = 0;
                    IDisposable sourceSubscription = null;

                    void TerminateWithError(Exception exception)
                    {
                        if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                        {
                            return;
                        }

                        lifetimeCancellation.Cancel();
                        sourceSubscription?.Dispose();
                        observer.OnError(exception);
                    }

                    void TerminateWithCompletion()
                    {
                        if (Interlocked.Exchange(ref isTerminated, 1) == 1)
                        {
                            return;
                        }

                        lifetimeCancellation.Cancel();
                        sourceSubscription?.Dispose();
                        observer.OnCompleted(completionResult);
                    }

                    void TryCompleteWhenIdle()
                    {
                        bool shouldComplete = false;

                        lock (gate)
                        {
                            if (isSourceCompleted && isProcessing == false && pendingValues.Count == 0)
                            {
                                shouldComplete = true;
                            }
                        }

                        if (shouldComplete)
                        {
                            TerminateWithCompletion();
                        }
                    }

                    void EnqueueValue(T value)
                    {
                        bool shouldStartProcessor = false;

                        lock (gate)
                        {
                            pendingValues.Enqueue(value);

                            if (isProcessing == false)
                            {
                                isProcessing = true;
                                shouldStartProcessor = true;
                            }
                        }

                        if (shouldStartProcessor)
                        {
                            _ = ProcessQueueAsync();
                        }
                    }

                    sourceSubscription = source.Subscribe(
                        new AnonymousOnityObserver<T>(
                            EnqueueValue,
                            exception =>
                            {
                                completionResult = OnityResult.Failure(exception);
                                lock (gate)
                                {
                                    isSourceCompleted = true;
                                }

                                TerminateWithError(exception);
                            },
                            result =>
                            {
                                completionResult = result;
                                lock (gate)
                                {
                                    isSourceCompleted = true;
                                }

                                TryCompleteWhenIdle();
                            }));

                    async Task ProcessQueueAsync()
                    {
                        while (true)
                        {
                            T value;

                            lock (gate)
                            {
                                if (pendingValues.Count == 0)
                                {
                                    isProcessing = false;
                                    break;
                                }

                                value = pendingValues.Dequeue();
                            }

                            try
                            {
                                bool passed =
                                    await Task.Run(
                                            async () =>
                                                await predicate(value, lifetimeCancellation.Token)
                                                    .ConfigureAwait(false),
                                            lifetimeCancellation.Token)
                                        .ConfigureAwait(false);

                                if (passed && lifetimeCancellation.IsCancellationRequested == false)
                                {
                                    observer.OnNext(value);
                                }
                            }
                            catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
                            {
                                break;
                            }
                            catch (Exception exception)
                            {
                                completionResult = OnityResult.Failure(exception);

                                lock (gate)
                                {
                                    isSourceCompleted = true;
                                }

                                TerminateWithError(exception);
                                return;
                            }
                        }

                        TryCompleteWhenIdle();
                    }

                    return new DisposableAction(
                        () =>
                        {
                            lifetimeCancellation.Cancel();
                            sourceSubscription?.Dispose();
                            lifetimeCancellation.Dispose();
                        });
                });
        }

        /// <summary>
        /// Returns a task completed by the first received value.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task completed by first value.</returns>
        public static Task<T> FirstAsync<T>(
            this IOnityObservableV2<T> source,
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
                new AnonymousOnityObserver<T>(
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
                    },
                    exception =>
                    {
                        if (completionSource.TrySetException(exception))
                        {
                            cancellationRegistration.Dispose();
                        }
                    },
                    result =>
                    {
                        if (result.IsFailure)
                        {
                            completionSource.TrySetException(result.Exception);
                            return;
                        }

                        completionSource.TrySetException(
                            new InvalidOperationException("Sequence completed before producing a value."));
                    }));

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
        /// Returns a task that completes when source emits one unit value.
        /// </summary>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static async Task ToTask(
            this IOnityObservableV2<Unit> source,
            CancellationToken cancellationToken = default)
        {
            await source.FirstAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Legacy API wrappers for v2 async/time operators.
    /// </summary>
    public static class OnityObservableAsyncExtensions
    {
        /// <summary>
        /// Stops source when cancellation token is canceled.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cancellation-aware source stream.</returns>
        public static IOnityObservable<T> TakeUntil<T>(
            this IOnityObservable<T> source,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return source.ToV2().TakeUntil(cancellationToken).ToLegacy();
        }

        /// <summary>
        /// Stops source when provided task completes.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="untilTask">Task used as stop signal.</param>
        /// <returns>Task-aware source stream.</returns>
        public static IOnityObservable<T> TakeUntil<T>(
            this IOnityObservable<T> source,
            Task untilTask)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return source.ToV2().TakeUntil(untilTask).ToLegacy();
        }

        /// <summary>
        /// Emits latest source value after silence window.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="dueTime">Silence duration.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <returns>Debounced source stream.</returns>
        public static IOnityObservable<T> Debounce<T>(
            this IOnityObservable<T> source,
            TimeSpan dueTime,
            OnityTimeProvider timeProvider = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return source.ToV2().Debounce(dueTime, timeProvider).ToLegacy();
        }

        /// <summary>
        /// Emits latest source value at fixed sampling interval while values are flowing.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="interval">Sampling interval.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <returns>Sampled source stream.</returns>
        public static IOnityObservable<T> ThrottleLast<T>(
            this IOnityObservable<T> source,
            TimeSpan interval,
            OnityTimeProvider timeProvider = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return source.ToV2().ThrottleLast(interval, timeProvider).ToLegacy();
        }

        /// <summary>
        /// Projects values using asynchronous selector with sequential execution.
        /// </summary>
        /// <typeparam name="TSource">Source value type.</typeparam>
        /// <typeparam name="TResult">Result value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="selector">Async projection callback.</param>
        /// <returns>Projected source stream.</returns>
        public static IOnityObservable<TResult> SelectAwait<TSource, TResult>(
            this IOnityObservable<TSource> source,
            Func<TSource, CancellationToken, ValueTask<TResult>> selector)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return source.ToV2().SelectAwait(selector).ToLegacy();
        }

        /// <summary>
        /// Filters values using asynchronous predicate with sequential execution.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="predicate">Async predicate callback.</param>
        /// <returns>Filtered source stream.</returns>
        public static IOnityObservable<T> WhereAwait<T>(
            this IOnityObservable<T> source,
            Func<T, CancellationToken, ValueTask<bool>> predicate)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return source.ToV2().WhereAwait(predicate).ToLegacy();
        }
    }
}
