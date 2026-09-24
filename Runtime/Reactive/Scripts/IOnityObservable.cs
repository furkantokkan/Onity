using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Lightweight observable abstraction used by Onity reactive operators.
    /// </summary>
    /// <typeparam name="T">Stream value type.</typeparam>
    public interface IOnityObservable<T>
    {
        /// <summary>
        /// Subscribes to value notifications.
        /// </summary>
        /// <param name="observer">Observer callback.</param>
        /// <returns>Disposable subscription token.</returns>
        IDisposable Subscribe(Observer<T> observer);

        /// <summary>
        /// Subscribes with lifecycle callbacks.
        /// </summary>
        /// <param name="observer">Lifecycle observer.</param>
        /// <returns>Disposable subscription token.</returns>
        IDisposable Subscribe(OnityObserver<T> observer);
    }
}
