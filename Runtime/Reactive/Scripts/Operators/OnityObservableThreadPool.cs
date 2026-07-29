using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Thread-pool scheduling operators for Onity observables.
    /// </summary>
    public static partial class OnityObservableExtensions
    {
        /// <summary>
        /// Re-posts each source value onto a .NET thread-pool worker while preserving source order.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <returns>Observable that emits source values from a thread-pool worker.</returns>
        /// <remarks>
        /// Downstream observers run off the Unity main thread. Use
        /// <c>ObserveOnMainThread()</c> from <c>Onity.Unity.Reactive</c> before touching
        /// UnityEngine APIs.
        /// </remarks>
        public static IOnityObservable<T> ObserveOnThreadPool<T>(this IOnityObservable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new OnityObservable<T>(
                observer =>
                {
                    object gate = new object();
                    Queue<T> pendingValues = new Queue<T>(8);
                    bool isWorkerScheduled = false;
                    bool isDisposed = false;

                    void ScheduleWorker()
                    {
                        ThreadPool.QueueUserWorkItem(_ => DrainQueue());
                    }

                    void DrainQueue()
                    {
                        while (true)
                        {
                            T value;

                            lock (gate)
                            {
                                if (isDisposed)
                                {
                                    pendingValues.Clear();
                                    isWorkerScheduled = false;
                                    return;
                                }

                                if (pendingValues.Count == 0)
                                {
                                    isWorkerScheduled = false;
                                    return;
                                }

                                value = pendingValues.Dequeue();
                            }

                            try
                            {
                                observer(value);
                            }
                            catch (Exception exception)
                            {
                                OnityObservableExceptionHandler.Publish(exception);
                            }
                        }
                    }

                    IDisposable sourceSubscription =
                        source.Subscribe(
                            value =>
                            {
                                bool shouldSchedule;

                                lock (gate)
                                {
                                    if (isDisposed)
                                    {
                                        return;
                                    }

                                    pendingValues.Enqueue(value);
                                    shouldSchedule = isWorkerScheduled == false;

                                    if (shouldSchedule)
                                    {
                                        isWorkerScheduled = true;
                                    }
                                }

                                if (shouldSchedule)
                                {
                                    ScheduleWorker();
                                }
                            });

                    return new DisposableAction(
                        () =>
                        {
                            lock (gate)
                            {
                                if (isDisposed)
                                {
                                    return;
                                }

                                isDisposed = true;
                                pendingValues.Clear();
                            }

                            sourceSubscription.Dispose();
                        });
                });
        }

        /// <summary>
        /// Projects each source value on the .NET thread pool.
        /// </summary>
        /// <typeparam name="TSource">Source value type.</typeparam>
        /// <typeparam name="TResult">Projected value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="selector">CPU-bound projection function.</param>
        /// <param name="maxConcurrency">
        /// Maximum concurrent selector executions. Pass <c>0</c> to use the processor count.
        /// </param>
        /// <returns>Observable that emits selector results from thread-pool workers.</returns>
        /// <remarks>
        /// Results are emitted as worker tasks complete when <paramref name="maxConcurrency" />
        /// is greater than one. Use <paramref name="maxConcurrency" /> = 1 when source order
        /// must be preserved. Downstream observers run off the Unity main thread.
        /// </remarks>
        public static IOnityObservable<TResult> SelectOnThreadPool<TSource, TResult>(
            this IOnityObservable<TSource> source,
            Func<TSource, TResult> selector,
            int maxConcurrency = 0)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            return source.SelectOnThreadPool((value, _) => selector(value), maxConcurrency);
        }

        /// <summary>
        /// Projects each source value on the .NET thread pool with cancellation-aware selectors.
        /// </summary>
        /// <typeparam name="TSource">Source value type.</typeparam>
        /// <typeparam name="TResult">Projected value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="selector">CPU-bound projection function.</param>
        /// <param name="maxConcurrency">
        /// Maximum concurrent selector executions. Pass <c>0</c> to use the processor count.
        /// </param>
        /// <returns>Observable that emits selector results from thread-pool workers.</returns>
        /// <remarks>
        /// Results are emitted as worker tasks complete when <paramref name="maxConcurrency" />
        /// is greater than one. Use <paramref name="maxConcurrency" /> = 1 when source order
        /// must be preserved. Downstream observers run off the Unity main thread.
        /// </remarks>
        public static IOnityObservable<TResult> SelectOnThreadPool<TSource, TResult>(
            this IOnityObservable<TSource> source,
            Func<TSource, CancellationToken, TResult> selector,
            int maxConcurrency = 0)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            int resolvedMaxConcurrency = ResolveMaxConcurrency(maxConcurrency);

            if (resolvedMaxConcurrency == 1)
            {
                return SelectSequentialOnThreadPool(source, selector);
            }

            return new OnityObservable<TResult>(
                observer =>
                {
                    object observerGate = new object();
                    CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
                    SemaphoreSlim workerSlots =
                        new SemaphoreSlim(resolvedMaxConcurrency, resolvedMaxConcurrency);
                    int isDisposed = 0;
                    int activeWorkItemCount = 0;

                    void DisposeWorkerResourcesWhenIdle()
                    {
                        if (Volatile.Read(ref isDisposed) == 0)
                        {
                            return;
                        }

                        if (Volatile.Read(ref activeWorkItemCount) != 0)
                        {
                            return;
                        }

                        workerSlots.Dispose();
                        lifetimeCancellation.Dispose();
                    }

                    async Task RunSelectorAsync(TSource value)
                    {
                        bool slotAcquired = false;

                        try
                        {
                            CancellationToken cancellationToken = lifetimeCancellation.Token;
                            await workerSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                            slotAcquired = true;

                            TResult result =
                                await Task.Run(
                                        () => selector(value, cancellationToken),
                                        cancellationToken)
                                    .ConfigureAwait(false);

                            if (cancellationToken.IsCancellationRequested)
                            {
                                return;
                            }

                            lock (observerGate)
                            {
                                if (cancellationToken.IsCancellationRequested == false)
                                {
                                    observer(result);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                            when (lifetimeCancellation.IsCancellationRequested)
                        {
                        }
                        catch (Exception exception)
                        {
                            OnityObservableExceptionHandler.Publish(exception);
                        }
                        finally
                        {
                            if (slotAcquired)
                            {
                                workerSlots.Release();
                            }

                            Interlocked.Decrement(ref activeWorkItemCount);
                            DisposeWorkerResourcesWhenIdle();
                        }
                    }

                    IDisposable sourceSubscription =
                        source.Subscribe(
                            value =>
                            {
                                if (Volatile.Read(ref isDisposed) != 0)
                                {
                                    return;
                                }

                                Interlocked.Increment(ref activeWorkItemCount);

                                if (Volatile.Read(ref isDisposed) != 0)
                                {
                                    Interlocked.Decrement(ref activeWorkItemCount);
                                    DisposeWorkerResourcesWhenIdle();
                                    return;
                                }

                                _ = RunSelectorAsync(value);
                            });

                    return new DisposableAction(
                        () =>
                        {
                            if (Interlocked.Exchange(ref isDisposed, 1) != 0)
                            {
                                return;
                            }

                            sourceSubscription.Dispose();
                            lifetimeCancellation.Cancel();
                            DisposeWorkerResourcesWhenIdle();
                        });
                });
        }

        private static IOnityObservable<TResult> SelectSequentialOnThreadPool<TSource, TResult>(
            IOnityObservable<TSource> source,
            Func<TSource, CancellationToken, TResult> selector)
        {
            return new OnityObservable<TResult>(
                observer =>
                {
                    object gate = new object();
                    Queue<TSource> pendingValues = new Queue<TSource>(8);
                    CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
                    bool isWorkerScheduled = false;
                    bool isDisposed = false;
                    int activeWorkerCount = 0;

                    void DisposeResourcesWhenIdle()
                    {
                        if (Volatile.Read(ref isDisposed) == false)
                        {
                            return;
                        }

                        if (Volatile.Read(ref activeWorkerCount) != 0)
                        {
                            return;
                        }

                        lifetimeCancellation.Dispose();
                    }

                    void ScheduleWorker()
                    {
                        Interlocked.Increment(ref activeWorkerCount);
                        ThreadPool.QueueUserWorkItem(_ => DrainQueue());
                    }

                    void DrainQueue()
                    {
                        try
                        {
                            while (true)
                            {
                                TSource value;

                                lock (gate)
                                {
                                    if (isDisposed)
                                    {
                                        pendingValues.Clear();
                                        isWorkerScheduled = false;
                                        return;
                                    }

                                    if (pendingValues.Count == 0)
                                    {
                                        isWorkerScheduled = false;
                                        return;
                                    }

                                    value = pendingValues.Dequeue();
                                }

                                try
                                {
                                    CancellationToken cancellationToken = lifetimeCancellation.Token;
                                    TResult result = selector(value, cancellationToken);

                                    if (cancellationToken.IsCancellationRequested == false)
                                    {
                                        observer(result);
                                    }
                                }
                                catch (OperationCanceledException)
                                    when (lifetimeCancellation.IsCancellationRequested)
                                {
                                    return;
                                }
                                catch (Exception exception)
                                {
                                    OnityObservableExceptionHandler.Publish(exception);
                                }
                            }
                        }
                        finally
                        {
                            Interlocked.Decrement(ref activeWorkerCount);
                            DisposeResourcesWhenIdle();
                        }
                    }

                    IDisposable sourceSubscription =
                        source.Subscribe(
                            value =>
                            {
                                bool shouldSchedule;

                                lock (gate)
                                {
                                    if (isDisposed)
                                    {
                                        return;
                                    }

                                    pendingValues.Enqueue(value);
                                    shouldSchedule = isWorkerScheduled == false;

                                    if (shouldSchedule)
                                    {
                                        isWorkerScheduled = true;
                                    }
                                }

                                if (shouldSchedule)
                                {
                                    ScheduleWorker();
                                }
                            });

                    return new DisposableAction(
                        () =>
                        {
                            lock (gate)
                            {
                                if (isDisposed)
                                {
                                    return;
                                }

                                isDisposed = true;
                                pendingValues.Clear();
                            }

                            sourceSubscription.Dispose();
                            lifetimeCancellation.Cancel();
                            DisposeResourcesWhenIdle();
                        });
                });
        }

        private static int ResolveMaxConcurrency(int maxConcurrency)
        {
            if (maxConcurrency < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            }

            if (maxConcurrency == 0)
            {
                return Math.Max(1, Environment.ProcessorCount);
            }

            return maxConcurrency;
        }
    }
}
