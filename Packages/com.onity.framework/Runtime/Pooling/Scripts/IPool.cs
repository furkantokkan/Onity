using System;

namespace Onity.Pooling
{
    /// <summary>
    /// Common pool abstraction.
    /// </summary>
    /// <typeparam name="T">Pooled item type.</typeparam>
    public interface IPool<T>
    {
        /// <summary>
        /// Gets an item from the pool.
        /// </summary>
        /// <returns>Pooled instance.</returns>
        T Get();

        /// <summary>
        /// Returns an item to the pool.
        /// </summary>
        /// <param name="item">Pooled instance.</param>
        void Release(T item);

        /// <summary>
        /// Clears pool state.
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// Gets pooled items with runtime parameters applied before activation or get hooks.
    /// </summary>
    /// <typeparam name="T">Pooled item type.</typeparam>
    public interface IParameterizedPool<T> : IPool<T>
    {
        /// <summary>Gets an item after applying one runtime parameter.</summary>
        /// <typeparam name="TParam">Parameter type.</typeparam>
        /// <param name="param">Runtime parameter.</param>
        /// <param name="initialize">Sets the item's state before get hooks run.</param>
        /// <returns>Configured pooled item.</returns>
        T Get<TParam>(TParam param, Action<T, TParam> initialize);

        /// <summary>Gets an item after applying two runtime parameters.</summary>
        /// <typeparam name="TParam1">First parameter type.</typeparam>
        /// <typeparam name="TParam2">Second parameter type.</typeparam>
        /// <param name="param1">First runtime parameter.</param>
        /// <param name="param2">Second runtime parameter.</param>
        /// <param name="initialize">Sets the item's state before get hooks run.</param>
        /// <returns>Configured pooled item.</returns>
        T Get<TParam1, TParam2>(
            TParam1 param1, TParam2 param2, Action<T, TParam1, TParam2> initialize);
    }
}
