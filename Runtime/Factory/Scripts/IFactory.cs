namespace Onity.Factory
{
    /// <summary>
    /// Non-parameterized factory contract.
    /// </summary>
    /// <typeparam name="TValue">Produced value type.</typeparam>
    public interface IFactory<out TValue>
    {
        /// <summary>
        /// Creates a value.
        /// </summary>
        /// <returns>Created value instance.</returns>
        TValue Create();
    }

    /// <summary>
    /// Single-parameter factory contract.
    /// </summary>
    /// <typeparam name="TParam">Input parameter type.</typeparam>
    /// <typeparam name="TValue">Produced value type.</typeparam>
    public interface IFactory<in TParam, out TValue>
    {
        /// <summary>
        /// Creates a value using one input parameter.
        /// </summary>
        /// <param name="param">Input value.</param>
        /// <returns>Created value instance.</returns>
        TValue Create(TParam param);
    }

    /// <summary>
    /// Two-parameter factory contract.
    /// </summary>
    /// <typeparam name="TParam1">First input parameter type.</typeparam>
    /// <typeparam name="TParam2">Second input parameter type.</typeparam>
    /// <typeparam name="TValue">Produced value type.</typeparam>
    public interface IFactory<in TParam1, in TParam2, out TValue>
    {
        /// <summary>
        /// Creates a value using two input parameters.
        /// </summary>
        /// <param name="param1">First input value.</param>
        /// <param name="param2">Second input value.</param>
        /// <returns>Created value instance.</returns>
        TValue Create(TParam1 param1, TParam2 param2);
    }
}
