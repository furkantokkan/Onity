using System;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Reusable waiter for a click-style event without a payload (UniTask
    /// <c>IAsyncClickEventHandler</c> parity). Each <see cref="OnClickAsync"/> call starts a new wait
    /// for the next invocation; dispose the handler to stop listening.
    /// </summary>
    public interface IOnityAsyncClickEventHandler : IDisposable
    {
        /// <summary>Waits for the next click. Main thread only.</summary>
        /// <returns>A single-consumption task; canceled when the handler is disposed or its token is canceled.</returns>
        OnityTask OnClickAsync();
    }

    /// <summary>
    /// Reusable waiter for a value-changed event (UniTask <c>IAsyncValueChangedEventHandler</c> parity).
    /// </summary>
    /// <typeparam name="T">Changed value type.</typeparam>
    public interface IOnityAsyncValueChangedEventHandler<T> : IDisposable
    {
        /// <summary>Waits for the next value change. Main thread only.</summary>
        /// <returns>A single-consumption task completed with the new value; canceled when the handler is disposed or its token is canceled.</returns>
        OnityTask<T> OnValueChangedAsync();
    }

    /// <summary>
    /// Reusable waiter for an end-edit event (UniTask <c>IAsyncEndEditEventHandler</c> parity).
    /// </summary>
    /// <typeparam name="T">Edited value type.</typeparam>
    public interface IOnityAsyncEndEditEventHandler<T> : IDisposable
    {
        /// <summary>Waits for the next end of an edit. Main thread only.</summary>
        /// <returns>A single-consumption task completed with the final value; canceled when the handler is disposed or its token is canceled.</returns>
        OnityTask<T> OnEndEditAsync();
    }

    /// <summary>
    /// Reusable waiter for an end-of-text-selection event (UniTask
    /// <c>IAsyncEndTextSelectionEventHandler</c> parity).
    /// </summary>
    /// <typeparam name="T">Event payload type.</typeparam>
    public interface IOnityAsyncEndTextSelectionEventHandler<T> : IDisposable
    {
        /// <summary>Waits for the next end of a text selection. Main thread only.</summary>
        /// <returns>A single-consumption task completed with the payload; canceled when the handler is disposed or its token is canceled.</returns>
        OnityTask<T> OnEndTextSelectionAsync();
    }

    /// <summary>
    /// Reusable waiter for a text-selection event (UniTask <c>IAsyncTextSelectionEventHandler</c> parity).
    /// </summary>
    /// <typeparam name="T">Event payload type.</typeparam>
    public interface IOnityAsyncTextSelectionEventHandler<T> : IDisposable
    {
        /// <summary>Waits for the next text selection. Main thread only.</summary>
        /// <returns>A single-consumption task completed with the payload; canceled when the handler is disposed or its token is canceled.</returns>
        OnityTask<T> OnTextSelectionAsync();
    }

    /// <summary>
    /// Reusable waiter for a deselect event (UniTask <c>IAsyncDeselectEventHandler</c> parity).
    /// </summary>
    /// <typeparam name="T">Event payload type.</typeparam>
    public interface IOnityAsyncDeselectEventHandler<T> : IDisposable
    {
        /// <summary>Waits for the next deselection. Main thread only.</summary>
        /// <returns>A single-consumption task completed with the payload; canceled when the handler is disposed or its token is canceled.</returns>
        OnityTask<T> OnDeselectAsync();
    }

    /// <summary>
    /// Reusable waiter for a select event (UniTask <c>IAsyncSelectEventHandler</c> parity).
    /// </summary>
    /// <typeparam name="T">Event payload type.</typeparam>
    public interface IOnityAsyncSelectEventHandler<T> : IDisposable
    {
        /// <summary>Waits for the next selection. Main thread only.</summary>
        /// <returns>A single-consumption task completed with the payload; canceled when the handler is disposed or its token is canceled.</returns>
        OnityTask<T> OnSelectAsync();
    }

    /// <summary>
    /// Reusable waiter for a submit event (UniTask <c>IAsyncSubmitEventHandler</c> parity).
    /// </summary>
    /// <typeparam name="T">Event payload type.</typeparam>
    public interface IOnityAsyncSubmitEventHandler<T> : IDisposable
    {
        /// <summary>Waits for the next submit. Main thread only.</summary>
        /// <returns>A single-consumption task completed with the payload; canceled when the handler is disposed or its token is canceled.</returns>
        OnityTask<T> OnSubmitAsync();
    }
}
