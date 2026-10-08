namespace Onity.Pooling
{
    /// <summary>
    /// Optional lifecycle callbacks for pooled components.
    /// </summary>
    public interface IPoolHooks
    {
        /// <summary>
        /// Called after object is fetched from pool.
        /// </summary>
        void OnPoolGet();

        /// <summary>
        /// Called before object is returned to pool.
        /// </summary>
        void OnPoolRelease();
    }
}
