using System;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Bridge adapters between legacy and reactive v2 observable contracts.
    /// </summary>
    internal static class OnityObservableAdapters
    {
        /// <summary>
        /// Adapts a legacy observable to reactive v2.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Legacy source stream.</param>
        /// <returns>V2 stream wrapper.</returns>
        internal static IOnityObservableV2<T> ToV2<T>(this IOnityObservable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new OnityObservableV2<T>(
                observer =>
                {
                    IDisposable subscription = source.Subscribe(observer.OnNext);

                    return new DisposableAction(
                        () =>
                        {
                            subscription.Dispose();
                            observer.OnCompleted();
                        });
                });
        }

        /// <summary>
        /// Adapts a reactive v2 observable to legacy observer delegate shape.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">V2 source stream.</param>
        /// <returns>Legacy stream wrapper.</returns>
        internal static IOnityObservable<T> ToLegacy<T>(this IOnityObservableV2<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new OnityObservable<T>(
                observer =>
                {
                    AnonymousOnityObserver<T> adapter =
                        new AnonymousOnityObserver<T>(
                            onNext: value => observer(value),
                            onError: _ => { },
                            onCompleted: _ => { });

                    return source.Subscribe(adapter);
                });
        }
    }
}
