using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Read-only reactive property abstraction.
    /// </summary>
    /// <typeparam name="T">Property value type.</typeparam>
    public interface IReadOnlyReactiveProperty<T>
    {
        /// <summary>
        /// Current value.
        /// </summary>
        T Value { get; }

        /// <summary>
        /// Subscribes to value updates.
        /// </summary>
        /// <param name="observer">Observer callback.</param>
        /// <param name="emitCurrentValue">Emit current value immediately if true.</param>
        /// <returns>Disposable subscription token.</returns>
        IDisposable Subscribe(Observer<T> observer, bool emitCurrentValue = true);
    }
}
