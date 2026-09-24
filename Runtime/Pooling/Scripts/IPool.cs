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
}
