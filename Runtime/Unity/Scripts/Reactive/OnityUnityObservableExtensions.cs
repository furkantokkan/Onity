using System;
using Onity.Core;
using Onity.Reactive;

namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Unity-specific reactive operators.
    /// </summary>
    public static class OnityUnityObservableExtensions
    {
        /// <summary>
        /// Delays each source value by the provided duration.
        /// </summary>
        /// <typeparam name="T">Source value type.</typeparam>
        /// <param name="source">Source observable.</param>
        /// <param name="delaySeconds">Delay duration in seconds.</param>
        /// <param name="useUnscaledTime">Use unscaled time for delay timers.</param>
        /// <returns>Delayed observable stream.</returns>
        public static IOnityObservable<T> Delay<T>(
            this IOnityObservable<T> source,
            float delaySeconds,
            bool useUnscaledTime = false)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (delaySeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(delaySeconds));
            }

            if (delaySeconds <= 0f)
            {
                return source;
            }

            return new OnityObservable<T>(
                observer =>
                {
                    CompositeDisposable disposables = new CompositeDisposable();
                    IDisposable sourceSubscription =
                        source.Subscribe(
                            value =>
                            {
                                OnityCountdownTimer timer =
                                    new OnityCountdownTimer(delaySeconds, useUnscaledTime);

                                IDisposable timerLifetime = null;

                                void HandleTimerCompleted()
                                {
                                    observer(value);

                                    if (timerLifetime != null)
                                    {
                                        disposables.Remove(timerLifetime);
                                    }
                                }

                                timer.Completed += HandleTimerCompleted;
                                timerLifetime =
                                    new DisposableAction(
                                        () =>
                                        {
                                            timer.Completed -= HandleTimerCompleted;
                                            timer.Dispose();
                                        });

                                disposables.Add(timerLifetime);
                                timer.Start();
                            });

                    disposables.Add(sourceSubscription);
                    return disposables;
                });
        }

        /// <summary>
        /// Re-posts each source value onto the Unity main-thread Update loop.
        /// Emission is deferred to the next <see cref="OnityFrameProviders.Update" /> tick rather
        /// than forwarded synchronously. This is the required hop after <c>SelectAwait</c> and
        /// <c>WhereAwait</c>, which resume their continuations off the Unity main thread, so any
        /// downstream observer that touches Unity API must run after this operator.
        /// </summary>
        /// <typeparam name="T">Source value type.</typeparam>
        /// <param name="source">Source observable.</param>
        /// <returns>Observable that emits source values on the Unity Update loop.</returns>
        public static IOnityObservable<T> ObserveOnMainThread<T>(this IOnityObservable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return source.ObserveOn(OnityFrameProviders.Update);
        }

        /// <summary>
        /// Re-posts each source value onto the provided Unity frame loop.
        /// Emission is deferred to the next <paramref name="frameProvider" /> tick rather than
        /// forwarded synchronously, which moves emission back onto the Unity main thread. Use this
        /// overload to select a specific loop phase such as <c>FixedUpdate</c> or <c>LateUpdate</c>.
        /// </summary>
        /// <typeparam name="T">Source value type.</typeparam>
        /// <param name="source">Source observable.</param>
        /// <param name="frameProvider">Unity frame provider used to schedule emission.</param>
        /// <returns>Observable that emits source values on the selected Unity frame loop.</returns>
        public static IOnityObservable<T> ObserveOnMainThread<T>(
            this IOnityObservable<T> source,
            OnityUnityFrameProvider frameProvider)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (frameProvider == null)
            {
                throw new ArgumentNullException(nameof(frameProvider));
            }

            return source.ObserveOn(frameProvider);
        }
    }
}
