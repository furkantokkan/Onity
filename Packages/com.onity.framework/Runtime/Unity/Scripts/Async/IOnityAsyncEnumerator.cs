namespace Onity.Unity.Async
{
    /// <summary>An asynchronous enumeration with one outstanding move at a time.</summary>
    /// <typeparam name="T">Covariant item type.</typeparam>
    public interface IOnityAsyncEnumerator<out T>
    {
        /// <summary>Gets the item after a successful move, until the next move or disposal.</summary>
        /// <exception cref="System.InvalidOperationException">No current item is valid.</exception>
        T Current { get; }

        /// <summary>Advances once, returning true for an item and false for normal termination.</summary>
        /// <returns>The move outcome; faults and cancellation remain terminal until disposal.</returns>
        /// <exception cref="System.InvalidOperationException">Another move is outstanding.</exception>
        OnityTask<bool> MoveNextAsync();

        /// <summary>Closes acceptance and waits for owned cleanup exactly once.</summary>
        /// <returns>Shared cleanup completion. An uncommitted move ends false when disposal wins.</returns>
        /// <remarks>Native cleanup may settle an outstanding move. Cleanup failure is reported
        /// by this task and does not replace a disposal-winning false move result.</remarks>
        OnityTask DisposeAsync();
    }
}
