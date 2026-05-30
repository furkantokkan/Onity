namespace Onity.Samples.GameObjectContextScope
{
    /// <summary>
    /// Simple scoped state service used to demonstrate GameObjectContext isolation.
    /// </summary>
    public sealed class ScopedCounterService
    {
        private int m_value;

        /// <summary>
        /// Current counter value.
        /// </summary>
        public int Value => m_value;

        /// <summary>
        /// Increments the counter and returns the next value.
        /// </summary>
        /// <returns>Incremented value.</returns>
        public int Increment()
        {
            m_value++;
            return m_value;
        }
    }
}
