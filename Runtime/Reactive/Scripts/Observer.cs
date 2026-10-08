namespace Onity.Reactive
{
    /// <summary>
    /// Callback signature for reactive streams.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="value">Next value.</param>
    public delegate void Observer<T>(T value);
}
