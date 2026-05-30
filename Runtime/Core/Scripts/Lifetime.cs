namespace Onity.Core
{
    /// <summary>
    /// Defines the creation strategy for a registered dependency.
    /// </summary>
    public enum Lifetime
    {
        /// <summary>
        /// A single instance is created and reused for all resolves in the same container.
        /// </summary>
        Singleton = 0,

        /// <summary>
        /// A new instance is created for each resolve.
        /// </summary>
        Transient = 1
    }
}
