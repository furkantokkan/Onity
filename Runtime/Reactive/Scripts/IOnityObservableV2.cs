using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Reactive v2 observable abstraction using <see cref="OnityObserver{T}" />.
    /// </summary>
    /// <typeparam name="T">Stream value type.</typeparam>
    internal interface IOnityObservableV2<T>
    {
        /// <summary>
        /// Subscribes an observer to the stream.
        /// </summary>
        /// <param name="observer">Observer instance.</param>
        /// <returns>Disposable subscription token.</returns>
        IDisposable Subscribe(OnityObserver<T> observer);
    }
}
