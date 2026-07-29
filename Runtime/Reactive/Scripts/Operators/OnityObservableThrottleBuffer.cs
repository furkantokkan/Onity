using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Leading-edge throttle and value buffering operators for Onity observables.
    /// </summary>
    public static partial class OnityObservableExtensions
    {
        /// <summary>
        /// Emits the first source value immediately, then ignores values until <paramref name="interval" /> elapses.
        /// </summary>
        /// <remarks>
        /// This is the leading-edge counterpart to <see cref="ThrottleLast{T}" />: the first value of each window is
        /// forwarded right away and subsequent values are dropped until the cool-down period completes.
        /// </remarks>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="interval">Cool-down duration after each emitted value.</param>
        /// <param name="provider">Optional time provider.</param>
        /// <returns>Observable that forwards the leading value of each cool-down window.</returns>
        public static IOnityObservable<T> Throttle<T>(
            this IOnityObservable<T> source,
            TimeSpan interval,
            OnityTimeProvider provider = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            OnityTimeProvider resolvedProvider = provider ?? OnityTimeProvider.System;

            return new OnityObservable<T>(
                observer =>
                {
                    object gate = new object();
                    CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
                    bool isCoolingDown = false;

                    IDisposable sourceSubscription = source.Subscribe(
                        value =>
                        {
                            bool shouldEmit = false;

                            lock (gate)
                            {
                                if (isCoolingDown == false)
                                {
                                    isCoolingDown = true;
                                    shouldEmit = true;
                                }
                            }

                            if (shouldEmit == false)
                            {
                                return;
                            }

                            observer(value);
                            _ = CoolDownAsync();
                        });

                    async Task CoolDownAsync()
                    {
                        try
                        {
                            await resolvedProvider.DelayAsync(interval, lifetimeCancellation.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception exception)
                        {
                            OnityObservableExceptionHandler.Publish(exception);
                            return;
                        }

                        lock (gate)
                        {
                            isCoolingDown = false;
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
        /// Buffers source values and emits a list every <paramref name="count" /> values.
        /// </summary>
        /// <remarks>
        /// A pooled buffer is filled and emitted once it reaches <paramref name="count" /> values, then cleared for the
        /// next window. No allocation occurs per source value beyond the emitted list itself.
        /// </remarks>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="count">Number of values gathered per emitted list.</param>
        /// <returns>Observable that emits a read-only list every <paramref name="count" /> source values.</returns>
        public static IOnityObservable<IReadOnlyList<T>> Buffer<T>(this IOnityObservable<T> source, int count)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return new OnityObservable<IReadOnlyList<T>>(
                observer =>
                {
                    List<T> buffer = new List<T>(count);

                    return source.Subscribe(
                        value =>
                        {
                            buffer.Add(value);

                            if (buffer.Count < count)
                            {
                                return;
                            }

                            List<T> emitted = buffer;
                            buffer = new List<T>(count);
                            observer(emitted);
                        });
                });
        }

        /// <summary>
        /// Buffers source values and emits the buffered list on each <paramref name="timeSpan" /> window.
        /// </summary>
        /// <remarks>
        /// Values arriving within a window are gathered into a pooled buffer. When the window elapses the buffered
        /// values are emitted as a list and a fresh buffer begins the next window. Empty windows emit nothing.
        /// </remarks>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="timeSpan">Window duration between emissions.</param>
        /// <param name="provider">Optional time provider.</param>
        /// <returns>Observable that emits buffered values once per time window.</returns>
        public static IOnityObservable<IReadOnlyList<T>> Buffer<T>(
            this IOnityObservable<T> source,
            TimeSpan timeSpan,
            OnityTimeProvider provider = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (timeSpan <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeSpan));
            }

            OnityTimeProvider resolvedProvider = provider ?? OnityTimeProvider.System;

            return new OnityObservable<IReadOnlyList<T>>(
                observer =>
                {
                    object gate = new object();
                    CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
                    List<T> buffer = new List<T>();

                    IDisposable sourceSubscription = source.Subscribe(
                        value =>
                        {
                            lock (gate)
                            {
                                buffer.Add(value);
                            }
                        });

                    _ = PumpAsync();

                    async Task PumpAsync()
                    {
                        while (lifetimeCancellation.IsCancellationRequested == false)
                        {
                            try
                            {
                                await resolvedProvider.DelayAsync(timeSpan, lifetimeCancellation.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                            catch (Exception exception)
                            {
                                OnityObservableExceptionHandler.Publish(exception);
                                return;
                            }

                            List<T> emitted = null;

                            lock (gate)
                            {
                                if (buffer.Count > 0)
                                {
                                    emitted = buffer;
                                    buffer = new List<T>();
                                }
                            }

                            if (emitted != null)
                            {
                                observer(emitted);
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
    }
}
